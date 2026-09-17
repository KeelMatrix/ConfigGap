using System.Text.Json;
using System.Text.Json.Serialization;
using KeelMatrix.ConfigGap.Probe;

namespace KeelMatrix.ConfigGap.Core;

public enum ConfigGapExitCode
{
    Clean = 0,
    BlockingFindings = 1,
    AnalysisFailure = 2
}

public sealed record ConfigGapAnalysisOptions
{
    public required string RepositoryRoot { get; init; }

    public required string SolutionPath { get; init; }

    public string? ConfigurationPath { get; init; }
}

public sealed record ConfigGapFinding(
    string Code,
    string Severity,
    string Message,
    string? Key,
    string? Source,
    int? Line,
    int? Column);

public sealed class ConfigGapReport
{
    public const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public int ToolConfigurationSchemaVersion { get; init; } = ConfigGapToolConfiguration.CurrentVersion;

    public bool Trustworthy { get; init; }

    public int ExitCode { get; init; }

    public int BlockingFindingCount { get; init; }

    public int WarningFindingCount { get; init; }

    public int InformationalFindingCount { get; init; }

    public int UnknownAccessCount { get; init; }

    public IReadOnlyList<string> KnownKeys { get; init; } = [];

    public IReadOnlyList<string> BindableKeys { get; init; } = [];

    public IReadOnlyList<string> RequiredKeys { get; init; } = [];

    public IReadOnlyList<string> ActuallyReadKeys { get; init; } = [];

    public string? FailureCode { get; init; }

    public IReadOnlyList<ConfigGapFinding> Findings { get; init; } = [];

    public string ToDeterministicJson()
    {
        return JsonSerializer.Serialize(
            this,
            JsonOptions) + Environment.NewLine;
    }
}

public sealed class ConfigGapAnalysisResult
{
    public required ConfigGapReport Report { get; init; }

    public ConfigGapExitCode ExitCode => (ConfigGapExitCode)Report.ExitCode;
}

public static class ConfigurationAnalysisEngine
{
    public static async Task<ConfigGapAnalysisResult> AnalyzeAsync(
        ConfigGapAnalysisOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var repositoryRoot = Path.GetFullPath(options.RepositoryRoot);
            var solutionPath = Path.GetFullPath(options.SolutionPath);
            var declarations = DeclarationGraph.Load(repositoryRoot, options.ConfigurationPath);
            var observations = solutionPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                ? await SemanticProbe.AnalyzeProjectAsync(solutionPath, repositoryRoot, cancellationToken)
                : await SemanticProbe.AnalyzeAsync(solutionPath, repositoryRoot, cancellationToken);

            var findings = BuildFindings(declarations, observations);
            var ordered = findings
                .OrderBy(finding => finding.Source ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(finding => finding.Line ?? int.MaxValue)
                .ThenBy(finding => finding.Column ?? int.MaxValue)
                .ThenBy(finding => finding.Code, StringComparer.Ordinal)
                .ThenBy(finding => finding.Key ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var blockingCount = ordered.Count(finding => finding.Severity.Equals("ERROR", StringComparison.Ordinal));
            var warningCount = ordered.Count(finding => finding.Severity.Equals("WARNING", StringComparison.Ordinal));
            var infoCount = ordered.Count(finding => finding.Severity.Equals("INFO", StringComparison.Ordinal));
            var exitCode = blockingCount == 0 ? ConfigGapExitCode.Clean : ConfigGapExitCode.BlockingFindings;
            return new ConfigGapAnalysisResult
            {
                Report = new ConfigGapReport
                {
                    Trustworthy = true,
                    ExitCode = (int)exitCode,
                    BlockingFindingCount = blockingCount,
                    WarningFindingCount = warningCount,
                    InformationalFindingCount = infoCount,
                    UnknownAccessCount = observations.Count(observation => observation.Key is null),
                    KnownKeys = GetKeys(observations),
                    BindableKeys = GetKeys(observations.Where(observation => observation.Evidence == "bindable")),
                    RequiredKeys = GetKeys(observations.Where(observation => observation.Evidence == "required")),
                    ActuallyReadKeys = GetKeys(observations.Where(observation => observation.Evidence == "actually-read")),
                    Findings = ordered
                }
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new ConfigGapAnalysisResult
            {
                Report = new ConfigGapReport
                {
                    Trustworthy = false,
                    ExitCode = (int)ConfigGapExitCode.AnalysisFailure,
                    FailureCode = GetFailureCode(exception.Message),
                    Findings = []
                }
            };
        }
    }

    private static List<ConfigGapFinding> BuildFindings(
        DeclarationGraph declarations,
        IReadOnlyList<ObservedAccess> observations)
    {
        var findings = new List<ConfigGapFinding>();
        var known = observations
            .Where(observation => observation.Key is not null)
            .GroupBy(observation => KeyNormalizer.Normalize(observation.Key!), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(observation => observation.Source, StringComparer.OrdinalIgnoreCase)
                .ThenBy(observation => observation.Line)
                .ThenBy(observation => observation.Column)
                .First())
            .ToArray();

        foreach (var observation in known)
        {
            var key = KeyNormalizer.Normalize(observation.Key!);
            if (FrameworkOwnedKeys.IsOwned(key) || declarations.Contains(key))
            {
                continue;
            }

            findings.Add(new ConfigGapFinding(
                "CG001",
                "ERROR",
                "The statically used configuration key is absent from the configured declaration surfaces.",
                key,
                observation.Source,
                observation.Line,
                observation.Column));
        }

        foreach (var observation in observations.Where(observation => observation.Key is null)
            .OrderBy(observation => observation.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(observation => observation.Line)
            .ThenBy(observation => observation.Column))
        {
            findings.Add(new ConfigGapFinding(
                "CG900",
                "INFO",
                "The configuration access could not be resolved statically. No blocking drift finding was produced.",
                null,
                observation.Source,
                observation.Line,
                observation.Column));
        }

        foreach (var key in declarations.LeafKeys.Where(key => !FrameworkOwnedKeys.IsOwned(key)))
        {
            if (observations.Any(observation => observation.Key is not null && Covers(key, observation.Key)))
            {
                continue;
            }

            findings.Add(new ConfigGapFinding(
                "CG002",
                "WARNING",
                "The declared example key was not observed in analyzed code.",
                key,
                null,
                null,
                null));
        }

        return findings;
    }

    private static bool Covers(string declaredKey, string usedKey)
    {
        var declared = KeyNormalizer.Normalize(declaredKey);
        var used = KeyNormalizer.Normalize(usedKey);
        return declared.Equals(used, StringComparison.OrdinalIgnoreCase) ||
            declared.StartsWith(used + ":", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] GetKeys(IEnumerable<ObservedAccess> observations) =>
        observations
            .Where(observation => observation.Key is not null)
            .Select(observation => KeyNormalizer.Normalize(observation.Key!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string GetFailureCode(string message)
    {
        var separator = message.IndexOf(':');
        return separator > 0 ? message[..separator] : "CONFIGGAP_ANALYSIS_FAILURE";
    }
}
