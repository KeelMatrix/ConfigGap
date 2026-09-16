using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

namespace KeelMatrix.ConfigGap.Probe;

internal sealed class SemanticProbe
{
    public static Task<IReadOnlyList<ObservedAccess>> AnalyzeAsync(string solutionPath, string repositoryRoot) =>
        AnalyzeSolutionAsync(solutionPath, repositoryRoot, repositoryRoot, selectedProjectPath: null);

    public static async Task<IReadOnlyList<ObservedAccess>> AnalyzeProjectAsync(string projectPath, string repositoryRoot)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath) ?? throw new InvalidOperationException("The project path has no directory.");
        var temporarySolution = Path.Combine(projectDirectory, ".configgap-evaluation.sln");
        File.WriteAllText(temporarySolution, CreateSingleProjectSolution(Path.GetFileName(projectPath)));
        try
        {
            return await AnalyzeSolutionAsync(temporarySolution, repositoryRoot, repositoryRoot, Path.GetFullPath(projectPath));
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
        string? selectedProjectPath)
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

        var solution = await workspace.OpenSolutionAsync(solutionPath);
        var observations = new List<ObservedAccess>();
        foreach (var project in solution.Projects
            .Where(project => selectedProjectPath is null ||
                string.Equals(Path.GetFullPath(project.FilePath ?? string.Empty), selectedProjectPath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(project => project.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation is null)
            {
                throw new InvalidOperationException($"Roslyn did not produce a compilation for {project.Name}.");
            }

            foreach (var document in project.Documents.OrderBy(document => document.FilePath, StringComparer.OrdinalIgnoreCase))
            {
                if (document.FilePath is null)
                {
                    continue;
                }

                var tree = await document.GetSyntaxTreeAsync();
                if (tree is null)
                {
                    continue;
                }

                var model = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync();
                foreach (var elementAccess in root.DescendantNodes().OfType<ElementAccessExpressionSyntax>())
                {
                    if (IsNestedKeyExpression(elementAccess, model))
                    {
                        continue;
                    }

                    if (!IsConfigurationType(model.GetTypeInfo(elementAccess.Expression).Type))
                    {
                        continue;
                    }

                    var argument = elementAccess.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                    if (argument is null)
                    {
                        continue;
                    }

                    observations.Add(CreateObservation(
                        repositoryRoot,
                        document.FilePath,
                        elementAccess,
                        "indexer",
                        KeyResolution.Resolve(argument, model)));
                }

                foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var symbol = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                    if (symbol is null)
                    {
                        continue;
                    }

                    var receiver = GetReceiver(invocation);
                    var methodName = symbol.Name;
                    if (methodName is "GetValue" or "GetSection" or "GetRequiredSection")
                    {
                        if (receiver is null || !IsConfigurationType(model.GetTypeInfo(receiver).Type))
                        {
                            continue;
                        }

                        if (IsReceiverOfKnownConsumer(invocation, model))
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
                        observations.Add(CreateObservation(
                            repositoryRoot,
                            document.FilePath,
                            invocation,
                            kind,
                            KeyResolution.Resolve(argument, model)));
                        continue;
                    }

                    if (methodName == "GetChildren" && receiver is not null && IsConfigurationType(model.GetTypeInfo(receiver).Type))
                    {
                        var section = ResolveConfigurationPath(receiver, model);
                        if (section is not null)
                        {
                            observations.Add(CreateObservation(
                                repositoryRoot,
                                document.FilePath,
                                invocation,
                                "prefix",
                                new StringResolution(section, "static-section-prefix")));
                        }

                        continue;
                    }

                    if (methodName == "Bind" && receiver is not null &&
                        IsConfigurationType(model.GetTypeInfo(receiver).Type) &&
                        symbol.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.Configuration")
                    {
                        var section = ResolveConfigurationPath(receiver, model);
                        if (section is not null)
                        {
                            observations.Add(CreateObservation(
                                repositoryRoot,
                                document.FilePath,
                                invocation,
                                "options-bind",
                                new StringResolution(section, "static-options-section")));
                        }

                        continue;
                    }

                    if (methodName == "BindConfiguration" && receiver is not null &&
                        model.GetTypeInfo(receiver).Type?.Name.Contains("OptionsBuilder", StringComparison.Ordinal) == true)
                    {
                        var argument = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                        if (argument is not null)
                        {
                            observations.Add(CreateObservation(
                                repositoryRoot,
                                document.FilePath,
                                invocation,
                                "options-bind-configuration",
                                KeyResolution.Resolve(argument, model)));
                        }
                    }
                }
            }
        }

        if (workspaceDiagnostics.Count > 0)
        {
            throw new InvalidOperationException("MSBuildWorkspace failed: " + string.Join(" | ", workspaceDiagnostics));
        }

        return observations;
    }

    private static bool IsKnownBenignWorkspaceDiagnostic(string message) =>
        (message.Contains("A FrameworkReference for 'Microsoft.AspNetCore.App' was included in the project", StringComparison.Ordinal) &&
            message.Contains("implicitly referenced by the .NET SDK", StringComparison.Ordinal)) ||
        (message.Contains("The IncludeOpenAPIAnalyzers property and its associated MVC API analyzers are deprecated", StringComparison.Ordinal) &&
            message.Contains("will be removed in a future release", StringComparison.Ordinal));

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
        var lineSpan = node.GetLocation().GetLineSpan();
        return new ObservedAccess(
            Path.GetRelativePath(repositoryRoot, documentPath).Replace(Path.DirectorySeparatorChar, '/'),
            kind,
            resolution.Kind,
            resolution.Value is null ? null : KeyNormalizer.Normalize(resolution.Value),
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character + 1);
    }

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

    private static ExpressionSyntax? GetReceiver(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax memberAccess ? memberAccess.Expression : null;

    private static string? ResolveConfigurationPath(ExpressionSyntax expression, SemanticModel model)
    {
        if (expression is not InvocationExpressionSyntax invocation ||
            invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Name.Identifier.Text is not ("GetSection" or "GetRequiredSection"))
        {
            return null;
        }

        var receiver = memberAccess.Expression;
        if (!IsConfigurationType(model.GetTypeInfo(receiver).Type))
        {
            return null;
        }

        var argument = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        var resolution = argument is null ? new StringResolution(null, "dynamic-unresolvable") : KeyResolution.Resolve(argument, model);
        return resolution.Value is null ? null : KeyNormalizer.Normalize(resolution.Value);
    }

    private static bool IsReceiverOfKnownConsumer(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        if (invocation.Parent is not MemberAccessExpressionSyntax memberAccess || memberAccess.Expression != invocation ||
            memberAccess.Parent is not InvocationExpressionSyntax outer)
        {
            return false;
        }

        var symbol = model.GetSymbolInfo(outer).Symbol as IMethodSymbol;
        return symbol?.Name is "Bind" or "GetChildren";
    }

    private static bool IsNestedKeyExpression(ElementAccessExpressionSyntax elementAccess, SemanticModel model)
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

        var symbol = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        var methodName = symbol?.Name ??
            (invocation.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.Text;
        return methodName is "GetValue" or "GetSection" or "GetRequiredSection" or "BindConfiguration";
    }
}
