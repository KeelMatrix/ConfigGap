using System.Text.Json;

namespace KeelMatrix.ConfigGap.Probe;

internal sealed class CorpusIndex
{
    public int Version { get; set; }
    public List<CorpusRepository> Repositories { get; set; } = [];
}

internal sealed class CorpusRepository
{
    public string Id { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string CommitSha { get; set; } = string.Empty;
    public string Qualification { get; set; } = string.Empty;
}

internal sealed class CorpusLabel
{
    public int Version { get; set; }
    public string RepositoryId { get; set; } = string.Empty;
    public string Solution { get; set; } = string.Empty;
    public List<CorpusFileLabel> ReviewedFiles { get; set; } = [];
    public List<CorpusDeclaredKey> DeclaredKeys { get; set; } = [];
    public List<CorpusAccessLabel> Accesses { get; set; } = [];
    public List<CorpusUncertainty> Uncertainties { get; set; } = [];
}

internal sealed class CorpusFileLabel
{
    public string Path { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

internal sealed class CorpusDeclaredKey
{
    public string Key { get; set; } = string.Empty;
    public string Surface { get; set; } = string.Empty;
}

internal sealed class CorpusAccessLabel
{
    public string Source { get; set; } = string.Empty;
    public int Line { get; set; }
    public int? Column { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Key { get; set; }
    public string Owner { get; set; } = "application";
    public bool SupportedStatic { get; set; }
    public string Certainty { get; set; } = string.Empty;
    public string? Note { get; set; }
}

internal sealed class CorpusUncertainty
{
    public string Source { get; set; } = string.Empty;
    public int Line { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Disposition { get; set; } = string.Empty;
}

internal sealed class CorpusEvaluationReport
{
    public int Version { get; set; } = 1;
    public string Analyzer { get; set; } = "MSBuildWorkspace/Roslyn semantic probe";
    public string Protocol { get; set; } = "Distinct normalized configuration keys per repository; labels are independent manual truth data.";
    public int RepositoryCount { get; set; }
    public int RepositoryTimeoutSeconds { get; set; }
    public int StaticKeyTruePositives { get; set; }
    public int StaticKeyPredictions { get; set; }
    public int StaticKeyRecallNumerator { get; set; }
    public int StaticKeyRecallDenominator { get; set; }
    public int AllLabeledStaticRecallNumerator { get; set; }
    public int AllLabeledStaticRecallDenominator { get; set; }
    public int DynamicBlockingFindings { get; set; }
    public int LoadFailureCount { get; set; }
    public bool DynamicAccessesNeverBlock { get; set; }
    public decimal BlockingPrecision { get; set; }
    public decimal StaticKeyRecall { get; set; }
    public decimal AllLabeledStaticRecall { get; set; }
    public string Verdict { get; set; } = "FAIL";
    public string RecallDomain { get; set; } =
        "Supported static application accesses are hand-labeled supportedStatic=true, owner=application, with a non-null key and kind indexer, get-value, section, required-section, options-bind, or options-bind-configuration. Exclude dynamic/unresolvable, unsupported APIs, framework-owned keys, and provider-specific forms under section 8; membership is determined from labels, never from analyzer output.";
    public List<CorpusRepositoryResult> Repositories { get; set; } = [];
}

internal sealed class CorpusRepositoryResult
{
    public string Id { get; set; } = string.Empty;
    public string CommitSha { get; set; } = string.Empty;
    public string Status { get; set; } = "analyzed";
    public string? LoadFailureCode { get; set; }
    public string? LoadFailure { get; set; }
    public int AnalyzerStaticKeys { get; set; }
    public int LabeledStaticKeys { get; set; }
    public int AllLabeledStaticKeys { get; set; }
    public int BlockingTruePositives { get; set; }
    public int BlockingPredictions { get; set; }
    public int RecallNumerator { get; set; }
    public int RecallDenominator { get; set; }
    public int AllRecallNumerator { get; set; }
    public int AllRecallDenominator { get; set; }
    public int LabeledDynamicAccesses { get; set; }
    public int DynamicBlockingFindings { get; set; }
    public List<string> AnalyzerMissingKeys { get; set; } = [];
    public List<string> LabeledMissingKeys { get; set; } = [];
    public List<string> FalsePositiveKeys { get; set; } = [];
}

internal static class CorpusJson
{
    public static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    public static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}
