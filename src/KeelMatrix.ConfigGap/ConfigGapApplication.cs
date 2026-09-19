using KeelMatrix.ConfigGap.Core;

namespace KeelMatrix.ConfigGap;

internal static class ConfigGapApplication
{
    internal static async Task<int> RunAsync(
        string[] args,
        string currentDirectory,
        IUsageTelemetry telemetry,
        TextWriter output,
        TextWriter errorOutput,
        CancellationToken cancellationToken = default)
    {
        CliParseResult parsed;
        try
        {
            parsed = CommandLineParser.Parse(args);
        }
        catch (InvalidOperationException exception)
        {
            errorOutput.WriteLine($"ConfigGap: {exception.Message}");
            errorOutput.WriteLine(CommandLineParser.Usage());
            return 2;
        }

        if (parsed.Error is not null)
        {
            errorOutput.WriteLine($"ConfigGap: {parsed.Error}");
            errorOutput.WriteLine(CommandLineParser.Usage());
            return 2;
        }

        if (parsed.Options!.ShowHelp)
        {
            output.WriteLine(CommandLineParser.Usage());
            return 0;
        }

        ConfigGapReport report;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            var selection = WorkspaceSelector.Resolve(parsed.Options, currentDirectory);
            var analysis = await ConfigurationAnalysisEngine.AnalyzeAsync(new ConfigGapAnalysisOptions
            {
                RepositoryRoot = selection.RepositoryRoot,
                SolutionPath = selection.SolutionPath ?? selection.SelectedProjectPath!,
                SelectedProjectPath = selection.SolutionPath is null ? null : selection.SelectedProjectPath,
                ConfigurationPath = selection.ConfigPath
            }, timeout.Token);
            report = BuildReport(analysis.Report);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            report = FailureReport(new InvalidOperationException(
                "CONFIGGAP_ANALYSIS_TIMEOUT: analysis exceeded the 120-second limit. " +
                "Use --project to narrow the workspace or reduce the selected declaration surfaces."));
        }
        catch (Exception exception)
        {
            report = FailureReport(exception);
        }

        Render(report, parsed.Options.Format, output, errorOutput);

        if (report.TrustworthyAnalysis)
        {
            try
            {
                telemetry.RecordSuccessfulAnalysis();
            }
            catch
            {
                // A failing telemetry adapter is never an analysis failure.
            }
        }

        return report.ExitCode;
    }

    private static ConfigGapReport BuildReport(KeelMatrix.ConfigGap.Core.ConfigGapReport analysis)
    {
        var findings = analysis.Findings.Select(finding => new ConfigGapFinding
        {
            Code = finding.Code,
            Severity = finding.Severity.ToLowerInvariant(),
            Key = finding.Key,
            Source = finding.Source,
            Line = finding.Line,
            Column = finding.Column,
            Message = finding.Message
        }).ToArray();

        return new ConfigGapReport
        {
            TrustworthyAnalysis = analysis.Trustworthy,
            ExitCode = analysis.ExitCode,
            ProjectCount = analysis.ProjectCount,
            AnalyzedFileCount = analysis.AnalyzedFileCount,
            DeclarationSurfaces = analysis.DeclarationSurfaces.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
            KnownKeys = analysis.KnownKeys,
            BindableKeys = analysis.BindableKeys,
            RequiredKeys = analysis.RequiredKeys,
            ActuallyReadKeys = analysis.ActuallyReadKeys,
            Findings = findings,
            Diagnostics = analysis.Trustworthy
                ? []
                : [new ConfigGapDiagnostic
                {
                    Code = analysis.FailureCode ?? "CONFIGGAP_ANALYSIS_FAILURE",
                    Message = analysis.FailureMessage ?? "Analysis failed before a trustworthy report was produced."
                }]
        };
    }

    private static ConfigGapReport FailureReport(Exception exception)
    {
        var message = exception.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        var separator = message.IndexOf(':');
        var code = message.StartsWith("CONFIGGAP_", StringComparison.Ordinal) && separator > 0
            ? message[..separator]
            : "CONFIGGAP_EXECUTION_FAILURE";
        if (code != "CONFIGGAP_EXECUTION_FAILURE")
        {
            message = message[(separator + 1)..].Trim();
        }
        if (message.Length > 800)
        {
            message = message[..800];
        }

        return new ConfigGapReport
        {
            TrustworthyAnalysis = false,
            ExitCode = 2,
            Diagnostics = [new ConfigGapDiagnostic { Code = code, Message = message }]
        };
    }

    private static void Render(ConfigGapReport report, OutputFormat format, TextWriter output, TextWriter errorOutput)
    {
        if (format == OutputFormat.Json)
        {
            output.Write(report.ToJson());
            return;
        }

        if (!report.TrustworthyAnalysis)
        {
            errorOutput.WriteLine("ConfigGap check failed; no trustworthy analysis was completed.");
            foreach (var diagnostic in report.Diagnostics)
            {
                errorOutput.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
            }

            return;
        }

        output.WriteLine("ConfigGap check complete.");
        output.WriteLine($"{report.ProjectCount} project(s) and {report.AnalyzedFileCount} source file(s) analyzed.");
        foreach (var finding in report.Findings)
        {
            output.WriteLine();
            output.WriteLine($"{finding.Code} {finding.Severity}");
            if (finding.Key is not null)
            {
                output.WriteLine($"Configuration key: {finding.Key}");
            }

            output.WriteLine(finding.Source is null
                ? "Location: declaration surface"
                : $"Location: {finding.Source}:{finding.Line}:{finding.Column}");
            output.WriteLine(finding.Message);
        }

        var blockingCount = report.Findings.Count(finding => finding.Code == "CG001");
        output.WriteLine();
        output.WriteLine(blockingCount == 0
            ? "No blocking configuration gaps."
            : $"{blockingCount} blocking configuration gap(s).");
    }

}

internal sealed record WorkspaceSelection(
    string RepositoryRoot,
    string? SolutionPath,
    string? SelectedProjectPath,
    string? ConfigPath);

internal static class WorkspaceSelector
{
    internal static WorkspaceSelection Resolve(CliOptions options, string currentDirectory)
    {
        var current = Path.GetFullPath(currentDirectory);
        var explicitWorkspacePath = options.SolutionPath ?? options.ProjectPath;
        var anchor = explicitWorkspacePath is null ? current : Path.GetFullPath(Path.Combine(current, explicitWorkspacePath));
        var anchorDirectory = File.Exists(anchor) ? Path.GetDirectoryName(anchor)! : anchor;
        var root = FindRepositoryRoot(current) ?? FindRepositoryRoot(anchorDirectory) ?? anchorDirectory;

        var solutionPath = options.SolutionPath is null
            ? FindDefaultSolution(root, current)
            : ResolveExistingPath(root, current, options.SolutionPath, "solution");
        var projectPath = options.ProjectPath is null
            ? null
            : ResolveExistingPath(root, current, options.ProjectPath, "project");

        if (solutionPath is null && projectPath is null)
        {
            projectPath = Directory.EnumerateFiles(current, "*.csproj", SearchOption.TopDirectoryOnly)
                .SingleOrDefault();
        }

        if (solutionPath is null && projectPath is null)
        {
            throw new InvalidOperationException(
                "CONFIGGAP_WORKSPACE_LOAD_FAILURE: no solution or project was found. Pass --solution <path> or --project <path> from the repository.");
        }

        var configPath = options.ConfigPath is null
            ? null
            : ResolveExistingPath(root, current, options.ConfigPath, "config");
        return new WorkspaceSelection(root, solutionPath, projectPath, configPath);
    }

    private static string? FindDefaultSolution(string root, string current)
    {
        var candidates = Directory.EnumerateFiles(current, "*.sln", SearchOption.TopDirectoryOnly)
            .Concat(!string.Equals(current, root, StringComparison.OrdinalIgnoreCase)
                ? Directory.EnumerateFiles(root, "*.sln", SearchOption.TopDirectoryOnly)
                : [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (candidates.Length == 1)
        {
            return candidates[0];
        }

        if (candidates.Length > 1)
        {
            throw new InvalidOperationException(
                "CONFIGGAP_WORKSPACE_LOAD_FAILURE: multiple solutions were found. Pass --solution <path> to select one.");
        }

        return null;
    }

    private static string ResolveExistingPath(string root, string current, string path, string kind)
    {
        var full = Path.GetFullPath(Path.Combine(current, path));
        if (!File.Exists(full))
        {
            throw new InvalidOperationException($"CONFIGGAP_WORKSPACE_LOAD_FAILURE: the {kind} '{path}' does not exist.");
        }

        var rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(full, root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"CONFIGGAP_WORKSPACE_LOAD_FAILURE: the {kind} must stay inside the repository.");
        }

        return full;
    }

    private static string? FindRepositoryRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
