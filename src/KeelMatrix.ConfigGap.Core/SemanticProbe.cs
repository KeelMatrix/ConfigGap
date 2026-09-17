using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

namespace KeelMatrix.ConfigGap.Probe;

public sealed class SemanticProbe
{
    public static Task<IReadOnlyList<ObservedAccess>> AnalyzeAsync(
        string solutionPath,
        string repositoryRoot,
        CancellationToken cancellationToken = default) =>
        AnalyzeSolutionAsync(solutionPath, repositoryRoot, repositoryRoot, selectedProjectPath: null, cancellationToken);

    public static async Task<IReadOnlyList<ObservedAccess>> AnalyzeProjectAsync(
        string projectPath,
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath) ?? throw new InvalidOperationException("The project path has no directory.");
        var temporarySolution = Path.Combine(projectDirectory, ".configgap-evaluation.sln");
        File.WriteAllText(temporarySolution, CreateSingleProjectSolution(Path.GetFileName(projectPath)));
        try
        {
            return await AnalyzeSolutionAsync(
                temporarySolution,
                repositoryRoot,
                repositoryRoot,
                Path.GetFullPath(projectPath),
                cancellationToken);
        }
        finally
        {
            if (File.Exists(temporarySolution))
            {
                File.Delete(temporarySolution);
            }
        }
    }

    private static async Task<IReadOnlyList<ObservedAccess>> AnalyzeSolutionAsync(
        string solutionPath,
        string repositoryRoot,
        string msbuildRoot,
        string? selectedProjectPath,
        CancellationToken cancellationToken)
    {
        RegisterMsBuild(msbuildRoot);

        var workspaceDiagnostics = new List<string>();
        using var workspace = MSBuildWorkspace.Create();
        workspace.WorkspaceFailed += (_, args) =>
        {
            if (args.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure &&
                !IsKnownBenignWorkspaceDiagnostic(args.Diagnostic.Message))
            {
                workspaceDiagnostics.Add(args.Diagnostic.Message);
            }
        };

        Solution solution;
        try
        {
            solution = await workspace.OpenSolutionAsync(solutionPath, progress: null, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"CONFIGGAP_WORKSPACE_LOAD_FAILURE: could not load '{Path.GetFileName(solutionPath)}'. " +
                "Restore the selected project and verify that its SDK and project assets are available.",
                exception);
        }

        ThrowIfWorkspaceFailed(workspaceDiagnostics);
        var selectedProjectFound = selectedProjectPath is null;
        var observations = new List<ObservedAccess>();
        foreach (var project in solution.Projects
            .Where(project => selectedProjectPath is null ||
                string.Equals(Path.GetFullPath(project.FilePath ?? string.Empty), selectedProjectPath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(project => project.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            selectedProjectFound = true;
            ThrowIfWorkspaceFailed(workspaceDiagnostics, project.Name);
            Compilation? compilation;
            try
            {
                compilation = await project.GetCompilationAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"CONFIGGAP_COMPILATION_LOAD_FAILURE: could not build the compilation for project '{project.Name}'. " +
                    "Restore the project and verify its SDK, project references, and assets.",
                    exception);
            }

            if (compilation is null)
            {
                throw new InvalidOperationException(
                    $"CONFIGGAP_COMPILATION_LOAD_FAILURE: Roslyn did not produce a compilation for project '{project.Name}'. " +
                    "Restore the project and verify its SDK, project references, and assets.");
            }

            var compilationErrors = compilation.GetDiagnostics(cancellationToken)
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Take(5)
                .Select(diagnostic => $"{diagnostic.Id}: {diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}")
                .ToArray();
            if (compilationErrors.Length > 0)
            {
                throw new InvalidOperationException(
                    $"CONFIGGAP_COMPILATION_LOAD_FAILURE: project '{project.Name}' has compilation errors. " +
                    "Restore the project and fix the project/source errors before analysis. " +
                    string.Join(" | ", compilationErrors));
            }

            foreach (var document in project.Documents.OrderBy(document => document.FilePath, StringComparer.OrdinalIgnoreCase))
            {
                if (document.FilePath is null)
                {
                    continue;
                }

                var tree = await document.GetSyntaxTreeAsync(cancellationToken);
                if (tree is null)
                {
                    continue;
                }

                var model = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync(cancellationToken);
                foreach (var elementAccess in root.DescendantNodes().OfType<ElementAccessExpressionSyntax>())
                {
                    if (IsNestedKeyExpression(elementAccess, model, cancellationToken))
                    {
                        continue;
                    }

                    var receiverType = model.GetTypeInfo(elementAccess.Expression, cancellationToken).Type;
                    if (!IsConfigurationType(receiverType))
                    {
                        continue;
                    }

                    var argument = elementAccess.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                    if (argument is null)
                    {
                        continue;
                    }

                    foreach (var resolution in ResolveConfigurationKey(
                        elementAccess.Expression,
                        argument,
                        model,
                        compilation,
                        cancellationToken))
                    {
                        observations.Add(CreateObservation(
                            repositoryRoot,
                            document.FilePath,
                            elementAccess,
                            "indexer",
                            resolution));
                    }
                }

                foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var symbol = model.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
                    var methodName = symbol?.Name ??
                        (invocation.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.Text;
                    if (methodName is null || (symbol is null && methodName != "BindConfiguration"))
                    {
                        continue;
                    }

                    var receiver = GetReceiver(invocation);
                    if (methodName is "GetValue" or "GetSection" or "GetRequiredSection")
                    {
                        if (receiver is null || !IsConfigurationType(model.GetTypeInfo(receiver, cancellationToken).Type))
                        {
                            continue;
                        }

                        var consumerName = GetKnownConsumerName(invocation, model, cancellationToken);
                        if (consumerName == "Configure")
                        {
                            foreach (var section in ResolveConfigurationPath(invocation, model, compilation, cancellationToken))
                            {
                                observations.Add(CreateObservation(
                                    repositoryRoot,
                                    document.FilePath,
                                    GetAccessLocation(GetKnownConsumerInvocation(invocation, model, cancellationToken) ?? invocation),
                                    "options-bind",
                                    new StringResolution(section.Value, section.Kind)));
                            }
                        }

                        if (consumerName is not null)
                        {
                            continue;
                        }

                        if (methodName is "GetSection" or "GetRequiredSection" &&
                            IsUsedAsConfigurationReceiver(invocation, model, cancellationToken))
                        {
                            continue;
                        }

                        var argument = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                        if (argument is null)
                        {
                            continue;
                        }

                        var kind = methodName switch
                        {
                            "GetValue" => "get-value",
                            "GetSection" => "section",
                            _ => "required-section"
                        };
                        var resolutions = methodName == "GetValue"
                            ? ResolveConfigurationKey(receiver, argument, model, compilation, cancellationToken)
                            : ResolveConfigurationPath(invocation, model, compilation, cancellationToken);
                        foreach (var resolution in resolutions)
                        {
                            observations.Add(CreateObservation(
                                repositoryRoot,
                                document.FilePath,
                                GetAccessLocation(invocation),
                                kind,
                                resolution));
                        }
                        continue;
                    }

                    if (methodName == "GetChildren" && receiver is not null && IsConfigurationType(model.GetTypeInfo(receiver, cancellationToken).Type))
                    {
                        foreach (var section in ResolveConfigurationPath(receiver, model, compilation, cancellationToken))
                        {
                            if (section.Value is not null)
                            {
                                observations.Add(CreateObservation(
                                    repositoryRoot,
                                    document.FilePath,
                                    GetAccessLocation(invocation),
                                    "prefix",
                                    new StringResolution(section.Value, "static-section-prefix")));
                            }
                        }

                        continue;
                    }

                    if (methodName == "Bind" && receiver is not null &&
                        symbol is not null &&
                        IsConfigurationType(model.GetTypeInfo(receiver, cancellationToken).Type) &&
                        symbol.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.Configuration")
                    {
                        foreach (var section in ResolveConfigurationPath(receiver, model, compilation, cancellationToken))
                        {
                            if (section.Value is not null)
                            {
                                observations.Add(CreateObservation(
                                    repositoryRoot,
                                    document.FilePath,
                                    invocation,
                                    "options-bind",
                                    new StringResolution(section.Value, "static-options-section")));
                            }
                        }

                        continue;
                    }

                    if (methodName == "Bind" && receiver is not null &&
                        symbol is not null &&
                        IsOptionsBuilderType(model.GetTypeInfo(receiver, cancellationToken).Type))
                    {
                        foreach (var section in invocation.ArgumentList.Arguments
                            .Select(argument => argument.Expression)
                            .OfType<InvocationExpressionSyntax>()
                            .Where(argument => argument.Expression is MemberAccessExpressionSyntax memberAccess &&
                                memberAccess.Name.Identifier.Text is "GetSection" or "GetRequiredSection"))
                        {
                            foreach (var resolution in ResolveConfigurationPath(section, model, compilation, cancellationToken))
                            {
                                observations.Add(CreateObservation(
                                    repositoryRoot,
                                    document.FilePath,
                                    GetAccessLocation(invocation),
                                    "options-bind",
                                    new StringResolution(resolution.Value, resolution.Kind)));
                            }
                        }

                        continue;
                    }

                    if (methodName == "BindConfiguration" && receiver is not null)
                    {
                        var argument = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                        if (symbol is not null && IsSupportedBindConfiguration(symbol, model.GetTypeInfo(receiver, cancellationToken).Type) &&
                            argument is not null)
                        {
                            foreach (var resolution in ResolveKeys(argument, model, compilation, cancellationToken))
                            {
                                observations.Add(CreateObservation(
                                    repositoryRoot,
                                    document.FilePath,
                                    GetAccessLocation(invocation),
                                    "options-bind-configuration",
                                    resolution));
                            }
                        }
                        else
                        {
                            observations.Add(CreateObservation(
                                repositoryRoot,
                                document.FilePath,
                                invocation,
                                "options-bind-configuration",
                                new StringResolution(null, "unsupported-options-bind-configuration")));
                        }
                    }
                }
            }
        }

        if (selectedProjectPath is not null && !selectedProjectFound)
        {
            throw new InvalidOperationException(
                $"CONFIGGAP_WORKSPACE_LOAD_FAILURE: selected project '{Path.GetFileName(selectedProjectPath)}' was not loaded. " +
                "Verify the project path and restore its project assets.");
        }

        ThrowIfWorkspaceFailed(workspaceDiagnostics);

        return observations;
    }

    private static void ThrowIfWorkspaceFailed(IReadOnlyList<string> workspaceDiagnostics, string? projectName = null)
    {
        if (workspaceDiagnostics.Count == 0)
        {
            return;
        }

        var scope = projectName is null ? "solution" : $"project '{projectName}'";
        throw new InvalidOperationException(
            $"CONFIGGAP_WORKSPACE_LOAD_FAILURE: MSBuildWorkspace reported errors while loading {scope}. " +
            "Restore the project and verify its SDK, project references, and assets. " +
            string.Join(" | ", workspaceDiagnostics.Take(5)));
    }

    private static bool IsKnownBenignWorkspaceDiagnostic(string message)
    {
        if ((message.Contains("A FrameworkReference for 'Microsoft.AspNetCore.App' was included in the project", StringComparison.Ordinal) &&
                message.Contains("implicitly referenced by the .NET SDK", StringComparison.Ordinal)) ||
            (message.Contains("The IncludeOpenAPIAnalyzers property and its associated MVC API analyzers are deprecated", StringComparison.Ordinal) &&
                message.Contains("will be removed in a future release", StringComparison.Ordinal)))
        {
            return true;
        }

        // MSBuildWorkspace can surface advisory SDK/NuGet diagnostics as
        // failures even though it has produced a usable project compilation.
        // Ignore only messages whose wording identifies an advisory package or
        // support-lifecycle notice; unresolved references and load errors still
        // fail closed below.
        var normalized = message.ToLowerInvariant();
        var packageVulnerabilityNotice = normalized.Contains("vulnerab", StringComparison.Ordinal) &&
            (normalized.Contains("package", StringComparison.Ordinal) ||
             normalized.Contains("nu190", StringComparison.Ordinal) ||
             normalized.Contains("nuget", StringComparison.Ordinal));
        var frameworkLifecycleNotice =
            (normalized.Contains("out of support", StringComparison.Ordinal) ||
             normalized.Contains("end of life", StringComparison.Ordinal) ||
             normalized.Contains("end-of-life", StringComparison.Ordinal) ||
             normalized.Contains("eol", StringComparison.Ordinal) ||
             normalized.Contains("netsdk1138", StringComparison.Ordinal)) &&
            (normalized.Contains("framework", StringComparison.Ordinal) ||
             normalized.Contains("target framework", StringComparison.Ordinal) ||
             normalized.Contains("targeting", StringComparison.Ordinal));
        var toolingTargetFrameworkNotice =
            normalized.Contains("doesn't support net", StringComparison.Ordinal) &&
            normalized.Contains("has not been tested with it", StringComparison.Ordinal) &&
            normalized.Contains("suppresstfmsupportbuildwarnings", StringComparison.Ordinal);

        return packageVulnerabilityNotice || frameworkLifecycleNotice || toolingTargetFrameworkNotice;
    }

    private static string CreateSingleProjectSolution(string projectFileName)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
        builder.AppendLine("# Visual Studio Version 17");
        builder.AppendLine("VisualStudioVersion = 17.0.31903.59");
        builder.AppendLine("MinimumVisualStudioVersion = 10.0.40219.1");
        builder.AppendLine(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{0}\", \"{1}\", \"{{10000000-0000-0000-0000-000000000001}}\"",
            Path.GetFileNameWithoutExtension(projectFileName),
            projectFileName));
        builder.AppendLine("EndProject");
        builder.AppendLine("Global");
        builder.AppendLine("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution");
        builder.AppendLine("\t\tDebug|Any CPU = Debug|Any CPU");
        builder.AppendLine("\t\tRelease|Any CPU = Release|Any CPU");
        builder.AppendLine("\tEndGlobalSection");
        builder.AppendLine("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution");
        builder.AppendLine("\t\t{10000000-0000-0000-0000-000000000001}.Debug|Any CPU.ActiveCfg = Debug|Any CPU");
        builder.AppendLine("\t\t{10000000-0000-0000-0000-000000000001}.Debug|Any CPU.Build.0 = Debug|Any CPU");
        builder.AppendLine("\t\t{10000000-0000-0000-0000-000000000001}.Release|Any CPU.ActiveCfg = Release|Any CPU");
        builder.AppendLine("\t\t{10000000-0000-0000-0000-000000000001}.Release|Any CPU.Build.0 = Release|Any CPU");
        builder.AppendLine("\tEndGlobalSection");
        builder.AppendLine("EndGlobal");
        return builder.ToString();
    }

    private static void RegisterMsBuild(string repositoryRoot)
    {
        if (MSBuildLocator.IsRegistered)
        {
            return;
        }

        var globalJsonPath = Path.Combine(repositoryRoot, "global.json");
        if (File.Exists(globalJsonPath))
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(globalJsonPath));
            if (document.RootElement.TryGetProperty("sdk", out var sdk) &&
                sdk.TryGetProperty("version", out var versionElement))
            {
                var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT") ??
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
                var sdkPath = Path.Combine(dotnetRoot, "sdk", versionElement.GetString() ?? string.Empty);
                if (Directory.Exists(sdkPath))
                {
                    MSBuildLocator.RegisterMSBuildPath(sdkPath);
                    return;
                }
            }
        }

        MSBuildLocator.RegisterDefaults();
    }

    private static ObservedAccess CreateObservation(
        string repositoryRoot,
        string documentPath,
        SyntaxNode node,
        string kind,
        StringResolution resolution)
    {
        var location = resolution.PrimaryLocation ?? node.GetLocation();
        var sourcePath = location.SourceTree?.FilePath ?? documentPath;
        var lineSpan = location.GetLineSpan();
        return new ObservedAccess(
            Path.GetRelativePath(repositoryRoot, sourcePath).Replace(Path.DirectorySeparatorChar, '/'),
            kind,
            resolution.Kind,
            resolution.Value is null ? null : KeyNormalizer.Normalize(resolution.Value),
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character + 1);
    }

    private static IReadOnlyList<StringResolution> ResolveKeys(
        ExpressionSyntax expression,
        SemanticModel model,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var resolution = KeyResolution.Resolve(expression, model);
        if (resolution.IsStatic)
        {
            return [resolution];
        }

        var propagated = BoundedKeyPropagation.Resolve(expression, model, compilation, cancellationToken);
        return propagated.Count == 0 ? [resolution] : propagated;
    }

    private static IReadOnlyList<StringResolution> ResolveConfigurationKey(
        ExpressionSyntax receiver,
        ExpressionSyntax key,
        SemanticModel model,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var prefixes = ResolveSectionPrefix(receiver, model, compilation, cancellationToken);
        var keys = ResolveKeys(key, model, compilation, cancellationToken);
        return CombineConfigurationPaths(prefixes, keys);
    }

    private static SyntaxNode GetAccessLocation(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax memberAccess ? memberAccess.Name : invocation;

    private static bool IsConfigurationType(ITypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        return type.ToDisplayString() == "Microsoft.Extensions.Configuration.IConfiguration" ||
            type.ToDisplayString() == "Microsoft.Extensions.Configuration.IConfigurationSection" ||
            type.AllInterfaces.Any(interfaceType => interfaceType.ToDisplayString() == "Microsoft.Extensions.Configuration.IConfiguration");
    }

    private static bool IsSupportedBindConfiguration(IMethodSymbol symbol, ITypeSymbol? receiverType)
    {
        if (symbol.Name != "BindConfiguration" ||
            symbol.ContainingNamespace?.ToDisplayString() != "Microsoft.Extensions.DependencyInjection" ||
            symbol.ContainingType?.Name != "OptionsBuilderConfigurationExtensions")
        {
            return false;
        }

        var optionsBuilder = receiverType as INamedTypeSymbol;
        var originalDefinition = optionsBuilder?.OriginalDefinition;
        return originalDefinition is not null &&
            originalDefinition.Name == "OptionsBuilder" &&
            originalDefinition.Arity == 1 &&
            originalDefinition.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.Options";
    }

    private static bool IsOptionsBuilderType(ITypeSymbol? receiverType)
    {
        var optionsBuilder = receiverType as INamedTypeSymbol;
        var originalDefinition = optionsBuilder?.OriginalDefinition;
        return originalDefinition is not null &&
            originalDefinition.Name == "OptionsBuilder" &&
            originalDefinition.Arity == 1 &&
            originalDefinition.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.Options";
    }

    private static ExpressionSyntax? GetReceiver(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax memberAccess ? memberAccess.Expression : null;

    private static StringResolution[] ResolveConfigurationPath(
        ExpressionSyntax expression,
        SemanticModel model,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        if (expression is not InvocationExpressionSyntax invocation ||
            invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Name.Identifier.Text is not ("GetSection" or "GetRequiredSection"))
        {
            return [];
        }

        var receiver = memberAccess.Expression;
        if (!IsConfigurationType(model.GetTypeInfo(receiver, cancellationToken).Type))
        {
            return [];
        }

        var argument = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        if (argument is null)
        {
            return [new StringResolution(null, "dynamic-unresolvable")];
        }

        var prefixes = ResolveSectionPrefix(receiver, model, compilation, cancellationToken);
        var keys = ResolveKeys(argument, model, compilation, cancellationToken);
        return CombineConfigurationPaths(prefixes, keys).ToArray();
    }

    private static StringResolution[] ResolveSectionPrefix(
        ExpressionSyntax receiver,
        SemanticModel model,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var type = model.GetTypeInfo(receiver, cancellationToken).Type;
        if (type is null || !IsConfigurationType(type))
        {
            return [];
        }

        if (type.ToDisplayString() == "Microsoft.Extensions.Configuration.IConfiguration")
        {
            return [];
        }

        if (type.ToDisplayString() == "Microsoft.Extensions.Configuration.IConfigurationSection" &&
            receiver is not InvocationExpressionSyntax)
        {
            return [new StringResolution(null, "dynamic-section-prefix")];
        }

        if (receiver is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.Text is "GetSection" or "GetRequiredSection")
        {
            return ResolveConfigurationPath(invocation, model, compilation, cancellationToken);
        }

        return [new StringResolution(null, "dynamic-section-prefix")];
    }

    private static IReadOnlyList<StringResolution> CombineConfigurationPaths(
        IReadOnlyList<StringResolution> prefixes,
        IReadOnlyList<StringResolution> keys)
    {
        if (prefixes.Count == 0)
        {
            return keys;
        }

        if (keys.Count == 0)
        {
            return [new StringResolution(null, "dynamic-relative-key")];
        }

        var combined = new List<StringResolution>();
        foreach (var prefix in prefixes)
        {
            foreach (var key in keys)
            {
                if (prefix.Value is null || key.Value is null)
                {
                    combined.Add(new StringResolution(null, "dynamic-relative-key"));
                    continue;
                }

                var value = prefix.Value.Length == 0
                    ? key.Value
                    : $"{KeyNormalizer.Normalize(prefix.Value)}:{KeyNormalizer.Normalize(key.Value)}";
                combined.Add(key with { Value = value });
            }
        }

        return combined
            .GroupBy(resolution => resolution.Value ?? "<unknown>", StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    private static bool IsUsedAsConfigurationReceiver(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (invocation.Parent is ElementAccessExpressionSyntax elementAccess &&
            elementAccess.Expression == invocation)
        {
            return true;
        }

        if (invocation.Parent is not MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Expression != invocation ||
            memberAccess.Parent is not InvocationExpressionSyntax outer)
        {
            return false;
        }

        var symbol = model.GetSymbolInfo(outer, cancellationToken).Symbol as IMethodSymbol;
        var methodName = symbol?.Name ?? memberAccess.Name.Identifier.Text;
        return methodName is "GetValue" or "GetSection" or "GetRequiredSection" or "GetChildren" or "Bind";
    }

    private static string? GetKnownConsumerName(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var outer = GetKnownConsumerInvocation(invocation, model, cancellationToken);
        if (outer is null)
        {
            return null;
        }

        var symbol = model.GetSymbolInfo(outer, cancellationToken).Symbol as IMethodSymbol;
        var methodName = symbol?.Name ??
            (outer.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.Text;
        return methodName is "Bind" or "Configure" or "GetChildren" ? methodName : null;
    }

    private static InvocationExpressionSyntax? GetKnownConsumerInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var outer = invocation.Parent switch
        {
            MemberAccessExpressionSyntax memberAccess when memberAccess.Expression == invocation =>
                memberAccess.Parent as InvocationExpressionSyntax,
            ArgumentSyntax argument when argument.Parent?.Parent is InvocationExpressionSyntax outerInvocation =>
                outerInvocation,
            _ => null
        };
        return outer;
    }

    private static bool IsNestedKeyExpression(
        ElementAccessExpressionSyntax elementAccess,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        SyntaxNode current = elementAccess;
        while (current.Parent is not null && current.Parent is not ArgumentSyntax)
        {
            current = current.Parent;
        }

        if (current.Parent is not ArgumentSyntax argument || argument.Parent?.Parent is not InvocationExpressionSyntax invocation)
        {
            return false;
        }

        var symbol = model.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
        var methodName = symbol?.Name ??
            (invocation.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.Text;
        return methodName is "GetValue" or "GetSection" or "GetRequiredSection" or "BindConfiguration";
    }
}
