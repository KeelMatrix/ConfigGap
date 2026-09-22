using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KeelMatrix.ConfigGap.Probe;

internal static class CorpusEvaluator
{
    private const int MinimumPrecisionPredictions = 10;
    private static readonly TimeSpan RepositoryTimeout = TimeSpan.FromSeconds(120);
    private static readonly HashSet<string> SupportedKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "indexer",
        "get-value",
        "section",
        "required-section",
        "options-bind",
        "options-bind-configuration"
    };

    public static async Task<int> RunAsync(
        string indexPath,
        string labelsRoot,
        string clonesRoot,
        string outputPath,
        string? preflightFailuresPath = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var index = JsonSerializer.Deserialize<CorpusIndex>(await File.ReadAllTextAsync(indexPath), CorpusJson.ReadOptions)
            ?? throw new InvalidOperationException("The corpus index is missing or invalid.");

        if (index.Version != 1 || index.Repositories.Count is < 10 or > 20)
        {
            throw new InvalidOperationException("The corpus index must be version 1 and contain 10 to 20 repositories.");
        }

        var preflightFailures = LoadPreflightFailures(preflightFailuresPath);
        var report = new CorpusEvaluationReport
        {
            RepositoryTimeoutSeconds = (int)RepositoryTimeout.TotalSeconds,
            PrecisionMinimumPredictions = MinimumPrecisionPredictions
        };

        foreach (var repository in index.Repositories.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var clonePath = Path.Combine(clonesRoot, repository.Id);
            var labelPath = Path.Combine(labelsRoot, repository.Id + ".json");
            if (!Directory.Exists(clonePath))
            {
                throw new InvalidOperationException($"The scratch clone for '{repository.Id}' is missing.");
            }

            var label = JsonSerializer.Deserialize<CorpusLabel>(await File.ReadAllTextAsync(labelPath), CorpusJson.ReadOptions)
                ?? throw new InvalidOperationException($"The label file for '{repository.Id}' is missing or invalid.");
            if (label.Version != 1 || !string.Equals(label.RepositoryId, repository.Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"The label file for '{repository.Id}' has the wrong identity or version.");
            }

            var solutionPath = Path.GetFullPath(Path.Combine(clonePath, label.Solution));
            if (!File.Exists(solutionPath))
            {
                throw new InvalidOperationException($"The labeled solution for '{repository.Id}' is missing: {label.Solution}");
            }

            Console.WriteLine($"Analyzing {repository.Id} ({label.Solution})");
            var declarations = DeclarationGraph.Load(clonePath);
            var labeledStatic = GetLabeledKeys(label, includeFrameworkOwned: false);
            var allLabeledStatic = GetLabeledKeys(label, includeFrameworkOwned: true);
            var labeledMissing = labeledStatic
                .Where(key => !declarations.Contains(key) && !FrameworkOwnedKeys.IsOwned(key))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var dynamicAccesses = label.Accesses.Where(access => !access.SupportedStatic && access.Key is null).ToArray();

            CorpusRepositoryResult result;
            if (preflightFailures.TryGetValue(repository.Id, out var preflightFailure))
            {
                result = CreateLoadFailureResult(
                    repository,
                    labeledStatic,
                    allLabeledStatic,
                    labeledMissing,
                    dynamicAccesses,
                    "CONFIGGAP_RESTORE_FAILURE",
                    preflightFailure);
            }
            else
            {
                result = await AnalyzeRepositoryAsync(
                    repository,
                    solutionPath,
                    clonePath,
                    declarations,
                    label,
                    labeledStatic,
                    allLabeledStatic,
                    labeledMissing,
                    dynamicAccesses);
            }

            report.Repositories.Add(result);
            AddToReport(report, result);
        }

        report.RepositoryCount = report.Repositories.Count;
        report.BlockingPrecision = PercentageOrNull(report.PrecisionProtocolTruePositives, report.PrecisionProtocolPredictions);
        report.CorpusBlockingPrecision = PercentageOrNull(report.StaticKeyTruePositives, report.StaticKeyPredictions);
        report.BlockingPrecisionStatus = report.PrecisionProtocolPredictions < MinimumPrecisionPredictions
            ? "UNVERIFIED"
            : report.PrecisionProtocolControlBlockingFindings > 0 || report.PrecisionProtocolFailedCases > 0
                ? "FAIL"
                : report.BlockingPrecision >= 95m ? "VERIFIED" : "FAIL";
        report.StaticKeyRecall = Percentage(report.StaticKeyRecallNumerator, report.StaticKeyRecallDenominator);
        report.AllLabeledStaticRecall = Percentage(report.AllLabeledStaticRecallNumerator, report.AllLabeledStaticRecallDenominator);
        report.DynamicAccessesNeverBlock = report.DynamicBlockingFindings == 0;
        report.Verdict = report.LoadFailureCount == 0 &&
            report.BlockingPrecisionStatus == "VERIFIED" &&
            report.StaticKeyRecall >= 90m &&
            report.DynamicAccessesNeverBlock
                ? "PASS"
                : "FAIL";

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(report, CorpusJson.WriteOptions) + Environment.NewLine);
        PrintReport(report, outputPath, stopwatch.Elapsed);
        return report.Verdict == "PASS" ? 0 : 1;
    }

    private static async Task<CorpusRepositoryResult> AnalyzeRepositoryAsync(
        CorpusRepository repository,
        string solutionPath,
        string clonePath,
        DeclarationGraph declarations,
        CorpusLabel label,
        HashSet<string> labeledStatic,
        HashSet<string> allLabeledStatic,
        HashSet<string> labeledMissing,
        IReadOnlyList<CorpusAccessLabel> dynamicAccesses)
    {
        using var timeout = new CancellationTokenSource();
        try
        {
            var observationsTask = solutionPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                ? SemanticProbe.AnalyzeProjectAsync(solutionPath, clonePath, timeout.Token)
                : SemanticProbe.AnalyzeAsync(solutionPath, clonePath, timeout.Token);
            var observations = await observationsTask.WaitAsync(RepositoryTimeout);
            if (observations.Count == 0 ||
                (labeledStatic.Count > 0 && !observations.Any(observation =>
                    observation.Key is not null && labeledStatic.Contains(KeyNormalizer.Normalize(observation.Key)))))
            {
                // A completion without a labeled static key is not an accepted analysis result.
                // Reopen the selected project once to avoid retaining a transient workspace load.
                observations = await (solutionPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                    ? SemanticProbe.AnalyzeProjectAsync(solutionPath, clonePath, timeout.Token)
                    : SemanticProbe.AnalyzeAsync(solutionPath, clonePath, timeout.Token)).WaitAsync(RepositoryTimeout);
            }
            var analyzerStatic = observations
                .Where(observation => observation.Key is not null)
                .Select(observation => KeyNormalizer.Normalize(observation.Key!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var analyzerMissing = analyzerStatic
                .Where(key => !declarations.Contains(key) && !FrameworkOwnedKeys.IsOwned(key))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var falsePositiveKeys = analyzerMissing
                .Except(labeledMissing, StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var dynamicBlocking = CountDynamicBlocking(dynamicAccesses, observations, declarations);
            var precision = EvaluatePrecisionProtocol(label, observations);

            return new CorpusRepositoryResult
            {
                Id = repository.Id,
                CommitSha = repository.CommitSha,
                Status = "analyzed",
                AnalyzerStaticKeys = analyzerStatic.Count,
                LabeledStaticKeys = labeledStatic.Count,
                AllLabeledStaticKeys = allLabeledStatic.Count,
                BlockingTruePositives = analyzerMissing.Intersect(labeledMissing, StringComparer.OrdinalIgnoreCase).Count(),
                BlockingPredictions = analyzerMissing.Count,
                RecallNumerator = analyzerStatic.Intersect(labeledStatic, StringComparer.OrdinalIgnoreCase).Count(),
                RecallDenominator = labeledStatic.Count,
                AllRecallNumerator = analyzerStatic.Intersect(allLabeledStatic, StringComparer.OrdinalIgnoreCase).Count(),
                AllRecallDenominator = allLabeledStatic.Count,
                LabeledDynamicAccesses = dynamicAccesses.Count,
                DynamicBlockingFindings = dynamicBlocking,
                PrecisionProtocolCases = precision.Cases,
                PrecisionProtocolTruePositives = precision.TruePositives,
                PrecisionProtocolPredictions = precision.Predictions,
                PrecisionProtocolControlBlockingFindings = precision.ControlBlockingFindings,
                PrecisionProtocolFailedCases = precision.FailedCases,
                AnalyzerMissingKeys = analyzerMissing.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList(),
                LabeledMissingKeys = labeledMissing.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList(),
                FalsePositiveKeys = falsePositiveKeys,
                MissedKeys = ClassifyMissedKeys(label, analyzerStatic, clonePath),
                PrecisionProtocolFailures = precision.Failures
            };
        }
        catch (TimeoutException)
        {
            timeout.Cancel();
            return CreateLoadFailureResult(
                repository,
                labeledStatic,
                allLabeledStatic,
                labeledMissing,
                dynamicAccesses,
                "CONFIGGAP_REPOSITORY_TIMEOUT",
                $"Analysis exceeded the {RepositoryTimeout.TotalSeconds:0}-second per-repository limit.");
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return CreateLoadFailureResult(
                repository,
                labeledStatic,
                allLabeledStatic,
                labeledMissing,
                dynamicAccesses,
                "CONFIGGAP_REPOSITORY_TIMEOUT",
                $"Analysis exceeded the {RepositoryTimeout.TotalSeconds:0}-second per-repository limit.");
        }
        catch (Exception exception)
        {
            var message = SanitizeFailure(exception.Message);
            return CreateLoadFailureResult(
                repository,
                labeledStatic,
                allLabeledStatic,
                labeledMissing,
                dynamicAccesses,
                FailureCode(message),
                message.Length > 600 ? message[..600] : message);
        }
    }

    private static CorpusRepositoryResult CreateLoadFailureResult(
        CorpusRepository repository,
        HashSet<string> labeledStatic,
        HashSet<string> allLabeledStatic,
        HashSet<string> labeledMissing,
        IReadOnlyList<CorpusAccessLabel> dynamicAccesses,
        string code,
        string message) => new()
        {
            Id = repository.Id,
            CommitSha = repository.CommitSha,
            Status = "load-failed",
            LoadFailureCode = code,
            LoadFailure = SanitizeFailure(message),
            LabeledStaticKeys = labeledStatic.Count,
            AllLabeledStaticKeys = allLabeledStatic.Count,
            RecallDenominator = labeledStatic.Count,
            AllRecallDenominator = allLabeledStatic.Count,
            LabeledDynamicAccesses = dynamicAccesses.Count,
            LabeledMissingKeys = labeledMissing.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList(),
            MissedKeys = ClassifyMissedKeys(label: null, labeledMissing, clonePath: null, loadFailureCode: code)
        };

    private static void AddToReport(CorpusEvaluationReport report, CorpusRepositoryResult result)
    {
        report.StaticKeyTruePositives += result.BlockingTruePositives;
        report.StaticKeyPredictions += result.BlockingPredictions;
        report.PrecisionProtocolCases += result.PrecisionProtocolCases;
        report.PrecisionProtocolTruePositives += result.PrecisionProtocolTruePositives;
        report.PrecisionProtocolPredictions += result.PrecisionProtocolPredictions;
        report.PrecisionProtocolControlBlockingFindings += result.PrecisionProtocolControlBlockingFindings;
        report.PrecisionProtocolFailedCases += result.PrecisionProtocolFailedCases;
        report.StaticKeyRecallNumerator += result.RecallNumerator;
        report.StaticKeyRecallDenominator += result.RecallDenominator;
        report.AllLabeledStaticRecallNumerator += result.AllRecallNumerator;
        report.AllLabeledStaticRecallDenominator += result.AllRecallDenominator;
        report.DynamicBlockingFindings += result.DynamicBlockingFindings;
        if (result.Status == "load-failed")
        {
            report.LoadFailureCount++;
        }
    }

    private static HashSet<string> GetLabeledKeys(CorpusLabel label, bool includeFrameworkOwned) =>
        label.Accesses
            .Where(access => access.SupportedStatic && access.Key is not null)
            .Where(access => includeFrameworkOwned || SupportedKinds.Contains(access.Kind))
            .Where(access => includeFrameworkOwned || !string.Equals(access.Owner, "framework", StringComparison.OrdinalIgnoreCase))
            .Select(access => KeyNormalizer.Normalize(access.Key!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static int CountDynamicBlocking(
        IReadOnlyList<CorpusAccessLabel> dynamicAccesses,
        IReadOnlyList<ObservedAccess> observations,
        DeclarationGraph declarations) =>
        dynamicAccesses
            .Where(access => observations.Any(observation =>
                observation.Source.Equals(access.Source, StringComparison.OrdinalIgnoreCase) &&
                observation.Line == access.Line &&
                (access.Column is null || observation.Column == access.Column.Value) &&
                observation.Key is not null &&
                !declarations.Contains(observation.Key) &&
                !FrameworkOwnedKeys.IsOwned(observation.Key)))
            .Select(access => $"{access.Source}:{access.Line}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    private static Dictionary<string, string> LoadPreflightFailures(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var failures = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), CorpusJson.ReadOptions);
        return failures is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(failures, StringComparer.OrdinalIgnoreCase);
    }

    private static string FailureCode(string message)
    {
        var separator = message.IndexOf(':');
        return separator > 0 && message[..separator].StartsWith("CONFIGGAP_", StringComparison.Ordinal)
            ? message[..separator]
            : "CONFIGGAP_CORPUS_ANALYSIS_FAILURE";
    }

    private static string SanitizeFailure(string message)
    {
        var compact = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        compact = Regex.Replace(compact, "(?i)[a-z]:\\\\[^\\s'\\\"]+", "<path>");
        compact = Regex.Replace(compact, @"(?i)(?<![a-z0-9])/(?:[^\s/]+/)+[^\s]+", "<path>");
        return compact.Length > 600 ? compact[..600] : compact;
    }

    private static decimal Percentage(int numerator, int denominator) =>
        denominator == 0 ? 100m : Math.Round(numerator * 100m / denominator, 2, MidpointRounding.AwayFromZero);

    private static decimal? PercentageOrNull(int numerator, int denominator) =>
        denominator == 0 ? null : Math.Round(numerator * 100m / denominator, 2, MidpointRounding.AwayFromZero);

    private static void PrintReport(CorpusEvaluationReport report, string outputPath, TimeSpan duration)
    {
        Console.WriteLine("ConfigGap labeled-corpus evaluation");
        Console.WriteLine("Repository                       Status       TP/blocking  Supported recall  All-key recall  Dynamic blocking");
        Console.WriteLine("------------------------------  -----------  -----------  -----------------  --------------  ----------------");
        foreach (var item in report.Repositories)
        {
            Console.WriteLine($"{item.Id,-30}  {item.Status,-11}  {item.BlockingTruePositives,5}/{item.BlockingPredictions,-5}  {item.RecallNumerator,8}/{item.RecallDenominator,-7}  {item.AllRecallNumerator,7}/{item.AllRecallDenominator,-6}  {item.DynamicBlockingFindings,16}");
            if (item.LoadFailureCode is not null)
            {
                Console.WriteLine($"  load failure: {item.LoadFailureCode}: {item.LoadFailure}");
            }
        }

        Console.WriteLine();
        if (report.BlockingPrecision is null)
        {
            Console.WriteLine($"Blocking precision: UNVERIFIED ({report.PrecisionProtocolTruePositives}/{report.PrecisionProtocolPredictions}; minimum {report.PrecisionMinimumPredictions} predictions)");
        }
        else
        {
            Console.WriteLine($"Blocking precision: {report.PrecisionProtocolTruePositives}/{report.PrecisionProtocolPredictions} = {report.BlockingPrecision:F2}% ({report.BlockingPrecisionStatus})");
        }
        Console.WriteLine($"Precision protocol: {report.PrecisionProtocolTruePositives}/{report.PrecisionProtocolCases} cases passed; control blocking findings: {report.PrecisionProtocolControlBlockingFindings}; failed cases: {report.PrecisionProtocolFailedCases}");
        var corpusPrecision = report.CorpusBlockingPrecision is null ? "UNVERIFIED" : $"{report.CorpusBlockingPrecision:F2}%";
        Console.WriteLine($"Observed corpus precision: {report.StaticKeyTruePositives}/{report.StaticKeyPredictions} = {corpusPrecision}");
        Console.WriteLine($"Supported-domain recall: {report.StaticKeyRecallNumerator}/{report.StaticKeyRecallDenominator} = {report.StaticKeyRecall:F2}%");
        Console.WriteLine($"All-labeled-static-key recall: {report.AllLabeledStaticRecallNumerator}/{report.AllLabeledStaticRecallDenominator} = {report.AllLabeledStaticRecall:F2}%");
        Console.WriteLine($"Dynamic blocking findings: {report.DynamicBlockingFindings}");
        Console.WriteLine($"Load failures: {report.LoadFailureCount}");
        Console.WriteLine($"Verdict: {report.Verdict}");
        Console.WriteLine($"Machine-readable report: {Path.GetFullPath(outputPath)}");
        Console.WriteLine($"Duration: {duration.TotalMilliseconds:F0} ms");
    }

    private static PrecisionProtocolResult EvaluatePrecisionProtocol(
        CorpusLabel label,
        IReadOnlyList<ObservedAccess> observations)
    {
        var labeledAccesses = label.Accesses
            .Where(access => access.SupportedStatic &&
                access.Key is not null &&
                string.Equals(access.Owner, "application", StringComparison.OrdinalIgnoreCase) &&
                SupportedKinds.Contains(access.Kind))
            .ToArray();
        var keys = labeledAccesses
            .Select(access => KeyNormalizer.Normalize(access.Key!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var synchronizedDeclarations = DeclarationGraph.CreateSynchronized(keys);
        var controlFindings = GetBlockingFindings(observations, synchronizedDeclarations);
        var failures = new List<string>();
        var predictions = 0;
        var truePositives = 0;

        foreach (var key in keys)
        {
            var expectedAccess = labeledAccesses
                .Where(access => KeyNormalizer.Normalize(access.Key!).Equals(key, StringComparison.OrdinalIgnoreCase))
                .OrderBy(access => access.Source, StringComparer.OrdinalIgnoreCase)
                .ThenBy(access => access.Line)
                .ThenBy(access => access.Column ?? 0)
                .First();
            var findings = GetBlockingFindings(observations, synchronizedDeclarations.Without(key));
            predictions += findings.Length;
            var passed = findings.Length == 1 &&
                findings[0].Key.Equals(key, StringComparison.OrdinalIgnoreCase) &&
                findings[0].Source.Equals(expectedAccess.Source, StringComparison.OrdinalIgnoreCase) &&
                findings[0].Line == expectedAccess.Line &&
                (expectedAccess.Column is null || findings[0].Column == expectedAccess.Column.Value);
            if (passed)
            {
                truePositives++;
            }
            else
            {
                var observed = findings.Length == 0
                    ? "-"
                    : string.Join(", ", findings.Select(finding => $"{finding.Key}@{finding.Source}:{finding.Line}"));
                failures.Add($"{key} expected one finding at {expectedAccess.Source}:{expectedAccess.Line}, observed {findings.Length} ({observed})");
            }
        }

        if (controlFindings.Length > 0)
        {
            failures.Add($"control variant observed {controlFindings.Length} blocking finding(s): {string.Join(", ", controlFindings.Select(finding => finding.Key).OrderBy(key => key, StringComparer.OrdinalIgnoreCase))}");
        }

        return new PrecisionProtocolResult(
            keys.Length,
            predictions,
            truePositives,
            controlFindings.Length,
            keys.Length - truePositives,
            failures);
    }

    private static ProtocolFinding[] GetBlockingFindings(
        IReadOnlyList<ObservedAccess> observations,
        DeclarationGraph declarations) =>
        observations
            .Where(observation => observation.Key is not null &&
                SupportedKinds.Contains(observation.Kind) &&
                !declarations.Contains(observation.Key) &&
                !FrameworkOwnedKeys.IsOwned(observation.Key))
            .GroupBy(observation => KeyNormalizer.Normalize(observation.Key!), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(observation => observation.Source, StringComparer.OrdinalIgnoreCase)
                .ThenBy(observation => observation.Line)
                .ThenBy(observation => observation.Column)
                .Select(observation => new ProtocolFinding(
                    group.Key,
                    observation.Source,
                    observation.Line,
                    observation.Column))
                .First())
            .OrderBy(finding => finding.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static List<CorpusMissedKey> ClassifyMissedKeys(
        CorpusLabel? label,
        HashSet<string> analyzerStaticKeys,
        string? clonePath,
        string? loadFailureCode = null)
    {
        if (label is null)
        {
            return analyzerStaticKeys
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .Select(key => new CorpusMissedKey
                {
                    Key = key,
                    Cause = "workspace/load failure",
                    Detail = loadFailureCode ?? "The selected project was not analyzed."
                })
                .ToList();
        }

        var misses = new List<CorpusMissedKey>();
        foreach (var access in label.Accesses.Where(access => access.Key is not null))
        {
            var key = KeyNormalizer.Normalize(access.Key!);
            if (analyzerStaticKeys.Contains(key))
            {
                continue;
            }

            var cause = !access.SupportedStatic || !SupportedKinds.Contains(access.Kind)
                ? "genuinely unsupported pattern"
                : clonePath is not null && !File.Exists(Path.Combine(clonePath, access.Source.Replace('/', Path.DirectorySeparatorChar)))
                    ? "label error"
                    : "semantic analyzer miss";
            misses.Add(new CorpusMissedKey
            {
                Key = key,
                Source = access.Source,
                Line = access.Line,
                Cause = cause,
                Detail = access.Note ?? $"Labeled {access.Kind} access was not observed by the analyzer."
            });
        }

        foreach (var access in label.Accesses.Where(access => !access.SupportedStatic && access.Key is null))
        {
            misses.Add(new CorpusMissedKey
            {
                Source = access.Source,
                Line = access.Line,
                Cause = "dynamic/unknown",
                Detail = access.Note ?? "The access is intentionally outside static resolution."
            });
        }

        return misses;
    }

    private sealed record PrecisionProtocolResult(
        int Cases,
        int Predictions,
        int TruePositives,
        int ControlBlockingFindings,
        int FailedCases,
        List<string> Failures);

    private sealed record ProtocolFinding(string Key, string Source, int Line, int Column);
}
