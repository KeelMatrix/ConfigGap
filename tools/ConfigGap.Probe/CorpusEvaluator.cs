using System.Diagnostics;
using System.Text.Json;

namespace KeelMatrix.ConfigGap.Probe;

internal static class CorpusEvaluator
{
    public static async Task<int> RunAsync(
        string indexPath,
        string labelsRoot,
        string clonesRoot,
        string outputPath)
    {
        var stopwatch = Stopwatch.StartNew();
        var index = JsonSerializer.Deserialize<CorpusIndex>(await File.ReadAllTextAsync(indexPath), CorpusJson.ReadOptions)
            ?? throw new InvalidOperationException("The corpus index is missing or invalid.");

        if (index.Version != 1 || index.Repositories.Count is < 10 or > 20)
        {
            throw new InvalidOperationException("The corpus index must be version 1 and contain 10 to 20 repositories.");
        }

        var report = new CorpusEvaluationReport();
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
            var observations = solutionPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                ? await SemanticProbe.AnalyzeProjectAsync(solutionPath, clonePath)
                : await SemanticProbe.AnalyzeAsync(solutionPath, clonePath);
            var labeledStatic = label.Accesses
                .Where(access => access.SupportedStatic && access.Key is not null)
                .Where(access => !string.Equals(access.Owner, "framework", StringComparison.OrdinalIgnoreCase))
                .Select(access => KeyNormalizer.Normalize(access.Key!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var analyzerStatic = observations
                .Where(observation => observation.Key is not null)
                .Select(observation => KeyNormalizer.Normalize(observation.Key!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var analyzerMissing = analyzerStatic
                .Where(key => !declarations.Contains(key) && !FrameworkOwnedKeys.IsOwned(key))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var labeledMissing = labeledStatic
                .Where(key => !declarations.Contains(key) && !FrameworkOwnedKeys.IsOwned(key))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var falsePositiveKeys = analyzerMissing.Except(labeledMissing, StringComparer.OrdinalIgnoreCase).OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList();
            var truePositiveCount = analyzerMissing.Intersect(labeledMissing, StringComparer.OrdinalIgnoreCase).Count();
            var recallCount = analyzerStatic.Intersect(labeledStatic, StringComparer.OrdinalIgnoreCase).Count();
            var dynamicAccesses = label.Accesses.Where(access => !access.SupportedStatic && access.Key is null).ToArray();
            var dynamicBlocking = dynamicAccesses
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

            var result = new CorpusRepositoryResult
            {
                Id = repository.Id,
                CommitSha = repository.CommitSha,
                AnalyzerStaticKeys = analyzerStatic.Count,
                LabeledStaticKeys = labeledStatic.Count,
                BlockingTruePositives = truePositiveCount,
                BlockingPredictions = analyzerMissing.Count,
                RecallNumerator = recallCount,
                RecallDenominator = labeledStatic.Count,
                LabeledDynamicAccesses = dynamicAccesses.Length,
                DynamicBlockingFindings = dynamicBlocking,
                AnalyzerMissingKeys = analyzerMissing.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList(),
                LabeledMissingKeys = labeledMissing.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList(),
                FalsePositiveKeys = falsePositiveKeys
            };
            report.Repositories.Add(result);
            report.StaticKeyTruePositives += truePositiveCount;
            report.StaticKeyPredictions += analyzerMissing.Count;
            report.StaticKeyRecallNumerator += recallCount;
            report.StaticKeyRecallDenominator += labeledStatic.Count;
            report.DynamicBlockingFindings += dynamicBlocking;
        }

        report.RepositoryCount = report.Repositories.Count;
        report.BlockingPrecision = Percentage(report.StaticKeyTruePositives, report.StaticKeyPredictions);
        report.StaticKeyRecall = Percentage(report.StaticKeyRecallNumerator, report.StaticKeyRecallDenominator);
        report.DynamicAccessesNeverBlock = report.DynamicBlockingFindings == 0;

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(report, CorpusJson.WriteOptions) + Environment.NewLine);
        PrintReport(report, outputPath, stopwatch.Elapsed);
        return report.BlockingPrecision >= 95m && report.StaticKeyRecall >= 90m && report.DynamicAccessesNeverBlock ? 0 : 1;
    }

    private static decimal Percentage(int numerator, int denominator) =>
        denominator == 0 ? 100m : Math.Round(numerator * 100m / denominator, 2, MidpointRounding.AwayFromZero);

    private static void PrintReport(CorpusEvaluationReport report, string outputPath, TimeSpan duration)
    {
        Console.WriteLine("ConfigGap Phase 0B corpus evaluation");
        Console.WriteLine("Repository                       TP/blocking  Predicted  Recall       Dynamic blocking");
        Console.WriteLine("------------------------------  -----------  ---------  -----------  ----------------");
        foreach (var item in report.Repositories)
        {
            Console.WriteLine($"{item.Id,-30}  {item.BlockingTruePositives,5}/{item.BlockingPredictions,-5}  {item.BlockingPredictions,9}  {item.RecallNumerator,5}/{item.RecallDenominator,-5}  {item.DynamicBlockingFindings,16}");
        }

        Console.WriteLine();
        Console.WriteLine($"Blocking precision: {report.StaticKeyTruePositives}/{report.StaticKeyPredictions} = {report.BlockingPrecision:F2}%");
        Console.WriteLine($"Static-key recall: {report.StaticKeyRecallNumerator}/{report.StaticKeyRecallDenominator} = {report.StaticKeyRecall:F2}%");
        Console.WriteLine($"Dynamic blocking findings: {report.DynamicBlockingFindings}");
        Console.WriteLine($"Machine-readable report: {Path.GetFullPath(outputPath)}");
        Console.WriteLine($"Duration: {duration.TotalMilliseconds:F0} ms");
    }
}
