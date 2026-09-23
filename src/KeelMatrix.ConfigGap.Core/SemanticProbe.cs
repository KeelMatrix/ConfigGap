using KeelMatrix.ConfigGap.Core;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Operations;

namespace KeelMatrix.ConfigGap.Probe;

public sealed class SemanticProbe
{
    private static readonly object MsBuildRegistrationGate = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Compilation, ParameterCallSiteIndex> ParameterCallSiteIndexes = new();

    public static async Task<IReadOnlyList<ObservedAccess>> AnalyzeAsync(
        string solutionPath,
        string repositoryRoot,
        CancellationToken cancellationToken = default) =>
        (await AnalyzeDetailedAsync(solutionPath, repositoryRoot, selectedProjectPath: null, cancellationToken)).Observations;

    public static Task<SemanticAnalysisResult> AnalyzeDetailedAsync(
        string solutionPath,
        string repositoryRoot,
        string? selectedProjectPath = null,
        CancellationToken cancellationToken = default) =>
        AnalyzeSolutionAsync(solutionPath, repositoryRoot, repositoryRoot, selectedProjectPath, cancellationToken);

    public static async Task<IReadOnlyList<ObservedAccess>> AnalyzeProjectAsync(
        string projectPath,
        string repositoryRoot,
        CancellationToken cancellationToken = default)
        => (await AnalyzeProjectDetailedAsync(projectPath, repositoryRoot, cancellationToken)).Observations;

    public static async Task<SemanticAnalysisResult> AnalyzeProjectDetailedAsync(
        string projectPath,
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        var fullProjectPath = Path.GetFullPath(projectPath);
        var temporarySolution = Path.Combine(Path.GetTempPath(), $"configgap-{Guid.NewGuid():N}.sln");
        File.WriteAllText(temporarySolution, CreateSingleProjectSolution(fullProjectPath));
        try
        {
            return await AnalyzeSolutionAsync(
                temporarySolution,
                repositoryRoot,
                repositoryRoot,
                fullProjectPath,
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

    private static async Task<SemanticAnalysisResult> AnalyzeSolutionAsync(
        string solutionPath,
        string repositoryRoot,
        string msbuildRoot,
        string? selectedProjectPath,
        CancellationToken cancellationToken)
    {
        using var workspaceGate = await WorkspaceProcessGate.AcquireAsync(repositoryRoot, cancellationToken);
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
        var projectCount = 0;
        var analyzedFileCount = 0;
        foreach (var project in solution.Projects
            .Where(project => selectedProjectPath is null ||
                RepositoryPathPolicy.PathComparer.Equals(Path.GetFullPath(project.FilePath ?? string.Empty), selectedProjectPath))
            .OrderBy(project => project.FilePath, RepositoryPathPolicy.PathComparer))
        {
            projectCount++;
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

            var localHelperPropagation = new LocalHelperPropagation(
                compilation,
                (expression, semanticModel) => ResolveSectionPrefix(expression, semanticModel, compilation, cancellationToken));

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

            foreach (var document in project.Documents.OrderBy(document => document.FilePath, RepositoryPathPolicy.PathComparer))
            {
                if (document.FilePath is null)
                {
                    continue;
                }

                analyzedFileCount++;

                var tree = await document.GetSyntaxTreeAsync(cancellationToken);
                if (tree is null)
                {
                    continue;
                }

                var model = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync(cancellationToken);
                foreach (var elementAccess in root.DescendantNodes().OfType<ElementAccessExpressionSyntax>())
                {
                    if (localHelperPropagation.ShouldSkip(elementAccess))
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
                    if (localHelperPropagation.ShouldSkip(invocation))
                    {
                        continue;
                    }

                    if (localHelperPropagation.TryResolveInvocation(invocation, model, out var propagated))
                    {
                        observations.Add(CreateObservation(
                            repositoryRoot,
                            document.FilePath,
                            invocation,
                            propagated.Kind,
                            propagated.Resolution));
                        continue;
                    }

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
                                    new StringResolution(section.Value, section.Kind),
                                    isRequiredBinding: methodName == "GetRequiredSection"));
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

                        var kind = methodName switch
                        {
                            "GetValue" => "get-value",
                            "GetSection" => "section",
                            _ => "required-section"
                        };
                        var resolutions = methodName == "GetValue"
                            ? ResolveGetValueKey(receiver, invocation, symbol!, model, compilation, cancellationToken)
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
                        var prefixes = ResolveSectionPrefix(receiver, model, compilation, cancellationToken);
                        if (prefixes.Length == 0)
                        {
                            observations.Add(CreateObservation(
                                repositoryRoot,
                                document.FilePath,
                                GetAccessLocation(invocation),
                                "prefix",
                                new StringResolution(null, "dynamic-prefix")));
                        }

                        foreach (var section in prefixes)
                        {
                            observations.Add(CreateObservation(
                                repositoryRoot,
                                document.FilePath,
                                GetAccessLocation(invocation),
                                "prefix",
                                section.Value is null
                                    ? new StringResolution(null, "dynamic-prefix")
                                    : new StringResolution(section.Value, "static-section-prefix")));
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
                            observations.Add(CreateObservation(
                                repositoryRoot,
                                document.FilePath,
                                invocation,
                                "options-bind",
                                new StringResolution(section.Value, section.Kind),
                                isRequiredBinding: IsRequiredSectionInvocation(receiver)));
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
                                    invocation.Expression is MemberAccessExpressionSyntax memberAccess
                                        ? memberAccess.Name
                                        : invocation,
                                    "options-bind",
                                    new StringResolution(resolution.Value, resolution.Kind),
                                    isRequiredBinding: IsRequiredSectionInvocation(section)));
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

        return new SemanticAnalysisResult(observations, projectCount, analyzedFileCount);
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
        lock (MsBuildRegistrationGate)
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
    }

    private static ObservedAccess CreateObservation(
        string repositoryRoot,
        string documentPath,
        SyntaxNode node,
        string kind,
        StringResolution resolution,
        bool isRequiredBinding = false)
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
            lineSpan.StartLinePosition.Character + 1)
        {
            IsRequiredBinding = isRequiredBinding
        };
    }

    private static IReadOnlyList<StringResolution> ResolveGetValueKey(
        ExpressionSyntax receiver,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SemanticModel model,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        if (!method.IsGenericMethod)
        {
            return [new StringResolution(null, "unsupported-get-value")];
        }

        var key = GetInvocationArgument(invocation, "key", model, cancellationToken);
        return key is null
            ? [new StringResolution(null, "dynamic-unresolvable")]
            : ResolveConfigurationKey(receiver, key, model, compilation, cancellationToken);
    }

    private static ExpressionSyntax? GetInvocationArgument(
        InvocationExpressionSyntax invocation,
        string parameterName,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (model.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation)
        {
            return null;
        }

        foreach (var argument in operation.Arguments)
        {
            if (argument.Parameter?.Name.Equals(parameterName, StringComparison.Ordinal) == true)
            {
                return argument.Value.Syntax as ExpressionSyntax;
            }
        }

        return null;
    }

    private static bool IsRequiredSectionInvocation(ExpressionSyntax expression) =>
        expression is InvocationExpressionSyntax invocation &&
        invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
        memberAccess.Name.Identifier.Text == "GetRequiredSection";

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

    private static InvocationExpressionSyntax GetAccessLocation(InvocationExpressionSyntax invocation) => invocation;

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
        CancellationToken cancellationToken,
        HashSet<ISymbol>? visitedLocals = null)
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

        var prefixes = ResolveSectionPrefix(receiver, model, compilation, cancellationToken, visitedLocals);
        var keys = ResolveKeys(argument, model, compilation, cancellationToken);
        return CombineConfigurationPaths(prefixes, keys).ToArray();
    }

    private static StringResolution[] ResolveSectionPrefix(
        ExpressionSyntax receiver,
        SemanticModel model,
        Compilation compilation,
        CancellationToken cancellationToken,
        HashSet<ISymbol>? visitedLocals = null)
    {
        var type = model.GetTypeInfo(receiver, cancellationToken).Type;
        if (type is null || !IsConfigurationType(type))
        {
            return [];
        }

        var activeSymbols = visitedLocals ?? new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        if (receiver is IdentifierNameSyntax identifier)
        {
            var symbol = model.GetSymbolInfo(identifier, cancellationToken).Symbol;
            if (symbol is ILocalSymbol local)
            {
                return ResolveLocalSectionPrefix(local, compilation, activeSymbols, cancellationToken);
            }

            if (symbol is IParameterSymbol parameter)
            {
                return ResolveParameterSectionPrefix(parameter, compilation, activeSymbols, cancellationToken);
            }

            if (symbol is IFieldSymbol or IPropertySymbol)
            {
                return ResolveConfigurationMemberPrefix(symbol, compilation, activeSymbols, cancellationToken);
            }

            return [new StringResolution(null, "dynamic-section-prefix")];
        }

        if (receiver is MemberAccessExpressionSyntax memberAccess &&
            model.GetSymbolInfo(memberAccess, cancellationToken).Symbol is ISymbol member &&
            (member is IFieldSymbol || member is IPropertySymbol))
        {
            return ResolveConfigurationMemberPrefix(member, compilation, activeSymbols, cancellationToken);
        }

        if (IsProvenRootConfigurationExpression(receiver, model, cancellationToken))
        {
            return [];
        }

        if (!IsConfigurationSectionType(type))
        {
            if (receiver is InvocationExpressionSyntax)
            {
                return [new StringResolution(null, "dynamic-section-prefix")];
            }

            return [];
        }

        if (receiver is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax sectionMemberAccess &&
            sectionMemberAccess.Name.Identifier.Text is "GetSection" or "GetRequiredSection")
        {
            return ResolveConfigurationPath(invocation, model, compilation, cancellationToken, activeSymbols);
        }

        return [new StringResolution(null, "dynamic-section-prefix")];
    }

    private static StringResolution[] ResolveLocalSectionPrefix(
        ILocalSymbol local,
        Compilation compilation,
        HashSet<ISymbol> visitedSymbols,
        CancellationToken cancellationToken)
    {
        if (!visitedSymbols.Add(local))
        {
            return [new StringResolution(null, "dynamic-section-prefix-cycle")];
        }

        var declaration = local.DeclaringSyntaxReferences.SingleOrDefault()?.GetSyntax(cancellationToken);
        if (declaration is not VariableDeclaratorSyntax variable || variable.Initializer is null)
        {
            return [new StringResolution(null, "dynamic-section-local")];
        }

        var declarationModel = compilation.GetSemanticModel(variable.SyntaxTree);
        var root = variable.SyntaxTree.GetRoot(cancellationToken);
        if (root.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(identifier => SymbolEqualityComparer.Default.Equals(
                declarationModel.GetSymbolInfo(identifier, cancellationToken).Symbol,
                local))
            .Any(IsLocalWrite))
        {
            return [new StringResolution(null, "dynamic-section-reassigned")];
        }

        var resolutions = ResolveSectionPrefix(
            variable.Initializer.Value,
            declarationModel,
            compilation,
            cancellationToken,
            visitedSymbols);
        return resolutions;
    }

    private static StringResolution[] ResolveParameterSectionPrefix(
        IParameterSymbol parameter,
        Compilation compilation,
        HashSet<ISymbol> visitedSymbols,
        CancellationToken cancellationToken)
    {
        if (!visitedSymbols.Add(parameter))
        {
            return [new StringResolution(null, "dynamic-parameter-provenance-cycle")];
        }

        try
        {
            if (parameter.ContainingSymbol is not IMethodSymbol method ||
                method.DeclaringSyntaxReferences.Length != 1)
            {
                return [new StringResolution(null, "dynamic-parameter-provenance")];
            }

            var callSiteArguments = FindParameterCallSiteArguments(method, parameter, compilation, cancellationToken);
            if (callSiteArguments.Count == 0)
            {
                // A direct parameter receiver remains a supported v1 shape when
                // no same-compilation call site provides contradictory section
                // provenance. A visible section/unknown argument below makes
                // the receiver informational instead of inventing a root key.
                return [];
            }

            foreach (var (argument, callSiteModel) in callSiteArguments)
            {
                if (!IsProvenRootConfigurationExpression(
                        argument,
                        callSiteModel,
                        cancellationToken,
                        compilation,
                        visitedSymbols))
                {
                    return [new StringResolution(null, "dynamic-parameter-provenance")];
                }
            }

            return [];
        }
        finally
        {
            visitedSymbols.Remove(parameter);
        }
    }

    private static List<(ExpressionSyntax Argument, SemanticModel Model)> FindParameterCallSiteArguments(
        IMethodSymbol method,
        IParameterSymbol parameter,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var index = ParameterCallSiteIndexes.GetValue(
            compilation,
            currentCompilation => BuildParameterCallSiteIndex(currentCompilation, cancellationToken));

        var callSiteArguments = new List<(ExpressionSyntax Argument, SemanticModel Model)>();
        if (method.MethodKind == MethodKind.Constructor)
        {
            foreach (var objectCreation in index.ObjectCreations)
            {
                var model = compilation.GetSemanticModel(objectCreation.SyntaxTree);
                if (model.GetSymbolInfo(objectCreation, cancellationToken).Symbol is not IMethodSymbol invokedConstructor ||
                    !SymbolEqualityComparer.Default.Equals(invokedConstructor.OriginalDefinition, method.OriginalDefinition) ||
                    !TryGetArgument(objectCreation.ArgumentList, parameter, out var argument))
                {
                    continue;
                }

                callSiteArguments.Add((argument, model));
            }

            return callSiteArguments;
        }

        if (!index.CallsByName.TryGetValue(method.Name, out var calls))
        {
            return [];
        }

        foreach (var invocation in calls)
        {
            var model = compilation.GetSemanticModel(invocation.SyntaxTree);
            if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol invokedMethod ||
                !SymbolEqualityComparer.Default.Equals(invokedMethod.OriginalDefinition, method.OriginalDefinition) ||
                !TryGetArgument(invocation, parameter, out var argument))
            {
                continue;
            }

            callSiteArguments.Add((argument, model));
        }

        return callSiteArguments;
    }

    private static ParameterCallSiteIndex BuildParameterCallSiteIndex(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var callsByName = new Dictionary<string, List<InvocationExpressionSyntax>>(StringComparer.Ordinal);
        var objectCreations = new List<ObjectCreationExpressionSyntax>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = tree.GetRoot(cancellationToken);
            objectCreations.AddRange(root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>());
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var name = invocation.Expression switch
                {
                    IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                    MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
                    _ => null
                };
                if (name is null)
                {
                    continue;
                }

                if (!callsByName.TryGetValue(name, out var calls))
                {
                    calls = [];
                    callsByName.Add(name, calls);
                }

                calls.Add(invocation);
            }
        }

        return new ParameterCallSiteIndex(callsByName, objectCreations);
    }

    private static StringResolution[] ResolveConfigurationMemberPrefix(
        ISymbol member,
        Compilation compilation,
        HashSet<ISymbol> visitedSymbols,
        CancellationToken cancellationToken)
    {
        if (!visitedSymbols.Add(member))
        {
            return [new StringResolution(null, "dynamic-member-provenance-cycle")];
        }

        try
        {
            var memberType = member switch
            {
                IFieldSymbol field => field.Type,
                IPropertySymbol property => property.Type,
                _ => null
            };
            if (memberType is null || !IsConfigurationType(memberType))
            {
                return [new StringResolution(null, "dynamic-member-provenance")];
            }

            if (IsRootConfigurationType(memberType))
            {
                return [];
            }

            if (IsKnownRootConfigurationMember(member))
            {
                return [];
            }

            var expressions = GetConfigurationMemberValueExpressions(member, compilation, cancellationToken);
            if (expressions.Count != 1)
            {
                return [new StringResolution(null, "dynamic-member-provenance")];
            }

            var declaration = expressions[0];
            var declarationModel = compilation.GetSemanticModel(declaration.SyntaxTree);
            var type = declarationModel.GetTypeInfo(declaration, cancellationToken).Type;
            if (type is null || !IsConfigurationType(type))
            {
                return [new StringResolution(null, "dynamic-member-provenance")];
            }

            if (!IsConfigurationSectionType(memberType))
            {
                return IsProvenRootConfigurationExpression(
                        declaration,
                        declarationModel,
                        cancellationToken,
                        compilation,
                        visitedSymbols)
                    ? []
                    : [new StringResolution(null, "dynamic-member-provenance")];
            }

            return ResolveSectionPrefix(
                declaration,
                declarationModel,
                compilation,
                cancellationToken,
                visitedSymbols);
        }
        finally
        {
            visitedSymbols.Remove(member);
        }
    }

    private static List<ExpressionSyntax> GetConfigurationMemberValueExpressions(
        ISymbol member,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var expressions = new List<ExpressionSyntax>();
        foreach (var reference in member.DeclaringSyntaxReferences)
        {
            var declaration = reference.GetSyntax(cancellationToken);
            switch (declaration)
            {
                case VariableDeclaratorSyntax variable when variable.Initializer?.Value is { } initializer:
                    expressions.Add(initializer);
                    break;
                case PropertyDeclarationSyntax property:
                    if (property.ExpressionBody?.Expression is { } expressionBody)
                    {
                        expressions.Add(expressionBody);
                    }

                    if (property.Initializer?.Value is { } propertyInitializer)
                    {
                        expressions.Add(propertyInitializer);
                    }

                    foreach (var getter in property.AccessorList?.Accessors.Where(accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration)) ?? [])
                    {
                        if (getter.ExpressionBody?.Expression is { } getterExpression)
                        {
                            expressions.Add(getterExpression);
                        }

                        if (getter.Body is not null)
                        {
                            expressions.AddRange(getter.Body.Statements
                                .OfType<ReturnStatementSyntax>()
                                .Where(statement => statement.Expression is not null)
                                .Select(statement => statement.Expression!));
                        }
                    }

                    break;
            }
        }

        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = tree.GetRoot(cancellationToken);
            var model = compilation.GetSemanticModel(tree);
            foreach (var assignment in root.DescendantNodes()
                .OfType<AssignmentExpressionSyntax>()
                .Where(candidate => candidate.IsKind(SyntaxKind.SimpleAssignmentExpression)))
            {
                if (SymbolEqualityComparer.Default.Equals(
                    model.GetSymbolInfo(assignment.Left, cancellationToken).Symbol,
                    member))
                {
                    expressions.Add(assignment.Right);
                }
            }
        }

        return expressions;
    }

    private static bool IsProvenRootConfigurationExpression(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken,
        Compilation? compilation = null,
        HashSet<ISymbol>? visitedSymbols = null)
    {
        var type = model.GetTypeInfo(expression, cancellationToken).Type;
        if (type is null || !IsConfigurationType(type))
        {
            return false;
        }

        if (IsRootConfigurationType(type))
        {
            return true;
        }

        if (expression is MemberAccessExpressionSyntax memberAccess &&
            model.GetSymbolInfo(memberAccess, cancellationToken).Symbol is ISymbol member)
        {
            if (IsKnownRootConfigurationMember(member))
            {
                return true;
            }

            if (compilation is not null && (member is IFieldSymbol || member is IPropertySymbol))
            {
                var resolutions = ResolveConfigurationMemberPrefix(
                    member,
                    compilation,
                    visitedSymbols ?? new HashSet<ISymbol>(SymbolEqualityComparer.Default),
                    cancellationToken);
                return resolutions.Length == 0;
            }
        }

        if (expression is InvocationExpressionSyntax invocation &&
            model.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol method)
        {
            return IsRootConfigurationType(method.ReturnType) ||
                (method.Name == "Build" &&
                 method.ContainingType?.ToDisplayString() == "Microsoft.Extensions.Configuration.ConfigurationBuilder");
        }

        if (expression is IdentifierNameSyntax identifier &&
            model.GetSymbolInfo(identifier, cancellationToken).Symbol is IParameterSymbol parameter &&
            compilation is not null)
        {
            var resolutions = ResolveParameterSectionPrefix(
                parameter,
                compilation,
                visitedSymbols ?? new HashSet<ISymbol>(SymbolEqualityComparer.Default),
                cancellationToken);
            return resolutions.Length == 0;
        }

        if (expression is IdentifierNameSyntax localIdentifier &&
            model.GetSymbolInfo(localIdentifier, cancellationToken).Symbol is ILocalSymbol local &&
            compilation is not null)
        {
            var resolutions = ResolveLocalSectionPrefix(
                local,
                compilation,
                visitedSymbols ?? new HashSet<ISymbol>(SymbolEqualityComparer.Default),
                cancellationToken);
            return resolutions.Length == 0;
        }

        return false;
    }

    private static bool IsRootConfigurationType(ITypeSymbol type) =>
        type.ToDisplayString() is
            "Microsoft.Extensions.Configuration.IConfigurationRoot" or
            "Microsoft.Extensions.Configuration.IConfigurationManager" or
            "Microsoft.Extensions.Configuration.ConfigurationRoot" or
            "Microsoft.Extensions.Configuration.ConfigurationManager" ||
        type.AllInterfaces.Any(interfaceType => interfaceType.ToDisplayString() is
            "Microsoft.Extensions.Configuration.IConfigurationRoot" or
            "Microsoft.Extensions.Configuration.IConfigurationManager");

    private static bool IsKnownRootConfigurationMember(ISymbol member) =>
        member is IPropertySymbol property &&
        property.Name == "Configuration" &&
        property.ContainingType?.ToDisplayString() is
            "Microsoft.AspNetCore.Builder.WebApplication" or
            "Microsoft.AspNetCore.Builder.WebApplicationBuilder" or
            "Microsoft.Extensions.Hosting.HostApplicationBuilder" or
            "Microsoft.Extensions.Hosting.IHostApplicationBuilder";

    private sealed record ParameterCallSiteIndex(
        Dictionary<string, List<InvocationExpressionSyntax>> CallsByName,
        List<ObjectCreationExpressionSyntax> ObjectCreations);

    private static bool TryGetArgument(
        InvocationExpressionSyntax invocation,
        IParameterSymbol parameter,
        out ExpressionSyntax argument) =>
        TryGetArgument(invocation.ArgumentList, parameter, out argument);

    private static bool TryGetArgument(
        ArgumentListSyntax? argumentList,
        IParameterSymbol parameter,
        out ExpressionSyntax argument)
    {
        if (argumentList is null)
        {
            argument = null!;
            return false;
        }

        var named = argumentList.Arguments.FirstOrDefault(item =>
            item.NameColon?.Name.Identifier.ValueText == parameter.Name);
        if (named is not null)
        {
            argument = named.Expression;
            return true;
        }

        if (parameter.Ordinal < argumentList.Arguments.Count)
        {
            argument = argumentList.Arguments[parameter.Ordinal].Expression;
            return true;
        }

        argument = null!;
        return false;
    }

    private static bool IsLocalWrite(IdentifierNameSyntax identifier)
    {
        if (identifier.Parent is AssignmentExpressionSyntax assignment && assignment.Left == identifier)
        {
            return true;
        }

        if (identifier.Parent is PrefixUnaryExpressionSyntax prefix &&
            (prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression)) ||
            identifier.Parent is PostfixUnaryExpressionSyntax postfix &&
            (postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression)))
        {
            return true;
        }

        return identifier.Parent is ArgumentSyntax argument &&
            argument.Expression == identifier &&
            argument.RefKindKeyword.RawKind != 0;
    }

    private static bool IsConfigurationSectionType(ITypeSymbol type) =>
        type.ToDisplayString() == "Microsoft.Extensions.Configuration.IConfigurationSection" ||
        type.AllInterfaces.Any(interfaceType =>
            interfaceType.ToDisplayString() == "Microsoft.Extensions.Configuration.IConfigurationSection");

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
        return methodName is "GetValue" or "GetSection" or "GetRequiredSection" or "GetChildren" or "Bind" ||
            symbol is not null &&
            symbol.DeclaringSyntaxReferences.Length == 1 &&
            SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, model.Compilation.Assembly);
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

}
