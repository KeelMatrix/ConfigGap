namespace KeelMatrix.ConfigGap.Probe;

internal sealed class PatternManifest
{
    public int Version { get; set; }
    public List<ExpectedPattern> Patterns { get; set; } = [];
}

internal sealed class ExpectedPattern
{
    public string Id { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Owner { get; set; } = "application";
    public string ExpectedClassification { get; set; } = string.Empty;
    public List<string> ExpectedKeys { get; set; } = [];
    public int? ExpectedLine { get; set; }
}

internal sealed record ObservedAccess(
    string Source,
    string Kind,
    string Resolution,
    string? Key,
    int Line,
    int Column);

internal sealed class PatternResult
{
    public string Id { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Owner { get; set; } = "application";
    public string ExpectedClassification { get; set; } = string.Empty;
    public string ObservedClassification { get; set; } = string.Empty;
    public List<string> ExpectedKeys { get; set; } = [];
    public List<string> ObservedKeys { get; set; } = [];
    public List<string> Resolutions { get; set; } = [];
    public int? ExpectedLine { get; set; }
    public int? ObservedLine { get; set; }
    public int ObservationCount { get; set; }
    public bool Pass { get; set; }
    public string? Failure { get; set; }
}

internal sealed class ProbeReport
{
    public int Version { get; set; } = 1;
    public string Analyzer { get; set; } = "MSBuildWorkspace/Roslyn semantic probe";
    public string KeyNormalization { get; set; } = ": and __ are equivalent; matching is case-insensitive";
    public string SupportedStaticResolution { get; set; } = "literals, const values, static concatenations, static interpolations, and bounded same-compilation literal/const propagation through direct string-parameter helper forwarding (at most two hops)";
    public string DynamicResolution { get; set; } = "variables, parameters, method calls, computed values, and configuration-supplied key expressions are unknown";
    public List<string> DeclarationSurfaces { get; set; } = [];
    public int PatternCount { get; set; }
    public int PassedPatternCount { get; set; }
    public int NormalizationFixtureCount { get; set; }
    public int NormalizationPassCount { get; set; }
    public int OptionsFixtureCount { get; set; }
    public int OptionsPassCount { get; set; }
    public int UnknownAccessCount { get; set; }
    public bool UnknownAccessesNeverClassifiedAsMissing { get; set; }
    public bool AllPatternsPass { get; set; }
    public List<PatternResult> Results { get; set; } = [];
}
