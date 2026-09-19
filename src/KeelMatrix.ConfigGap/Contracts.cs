using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeelMatrix.ConfigGap;

internal enum OutputFormat
{
    Text,
    Json
}

internal sealed class CliOptions
{
    public bool ShowHelp { get; init; }
    public string? SolutionPath { get; init; }
    public string? ProjectPath { get; init; }
    public string? ConfigPath { get; init; }
    public OutputFormat Format { get; init; } = OutputFormat.Text;
}

internal sealed class CliParseResult
{
    public CliOptions? Options { get; init; }
    public string? Error { get; init; }
}

internal sealed class ConfigGapReport
{
    public int Version { get; init; } = 1;
    public string Tool { get; init; } = "KeelMatrix.ConfigGap";
    public bool TrustworthyAnalysis { get; init; }
    public int ExitCode { get; init; }
    public int ProjectCount { get; init; }
    public int AnalyzedFileCount { get; init; }
    public IReadOnlyList<string> DeclarationSurfaces { get; init; } = [];
    public IReadOnlyList<string> KnownKeys { get; init; } = [];
    public IReadOnlyList<string> BindableKeys { get; init; } = [];
    public IReadOnlyList<string> RequiredKeys { get; init; } = [];
    public IReadOnlyList<string> ActuallyReadKeys { get; init; } = [];
    public IReadOnlyList<ConfigGapFinding> Findings { get; init; } = [];
    public IReadOnlyList<ConfigGapDiagnostic> Diagnostics { get; init; } = [];

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions.Default) + Environment.NewLine;
}

internal sealed class ConfigGapFinding
{
    public string Code { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string? Key { get; init; }
    public string? Source { get; init; }
    public int? Line { get; init; }
    public int? Column { get; init; }
    public string Message { get; init; } = string.Empty;
}

internal sealed class ConfigGapDiagnostic
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
}
