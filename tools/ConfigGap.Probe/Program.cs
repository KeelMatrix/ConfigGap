using System.Diagnostics;
using System.Text.Json;

namespace KeelMatrix.ConfigGap.Probe;

internal static class Program
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions ReportJsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static async Task<int> Main(string[] args)
    {
        try
        {
            var options = ProbeOptions.Parse(args);
            var stopwatch = Stopwatch.StartNew();
            var manifest = LoadManifest(options.ManifestPath);
            var declarations = DeclarationGraph.Load(options.RepositoryRoot);
            var observations = await SemanticProbe.AnalyzeAsync(options.SolutionPath, options.RepositoryRoot);
            var report = BuildReport(manifest, declarations, observations);

            Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath)!);
            var json = JsonSerializer.Serialize(report, ReportJsonOptions);
            await File.WriteAllTextAsync(options.OutputPath, json + Environment.NewLine);

            PrintTable(report, options.OutputPath, stopwatch.Elapsed);
            return report.AllPatternsPass ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Probe failed: {exception.Message}");
            return 2;
        }
    }

    private static PatternManifest LoadManifest(string path)
    {
        var manifest = JsonSerializer.Deserialize<PatternManifest>(File.ReadAllText(path), ManifestJsonOptions);
        return manifest is { Version: 1 } ? manifest : throw new InvalidOperationException("The fixture manifest is missing or has an unsupported version.");
    }

    private static ProbeReport BuildReport(
        PatternManifest manifest,
        DeclarationGraph declarations,
        IReadOnlyList<ObservedAccess> observations)
    {
        var results = new List<PatternResult>();
        foreach (var expected in manifest.Patterns.OrderBy(pattern => pattern.Id, StringComparer.Ordinal))
        {
            var matched = observations
                .Where(observation => observation.Source.Equals(expected.Source, StringComparison.OrdinalIgnoreCase))
                .OrderBy(observation => observation.Line)
                .ThenBy(observation => observation.Column)
                .ToArray();
            var observedKeys = matched.Where(observation => observation.Key is not null)
                .Select(observation => observation.Key!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var observedClassification = matched.Length == 0
                ? "not-observed"
                : matched.Any(observation => observation.Key is null)
                    ? "unknown"
                    : observedKeys.All(declarations.Contains) ? "declared" : "discovered";
            var expectedKeys = expected.ExpectedKeys
                .Select(KeyNormalizer.Normalize)
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var observedKinds = matched.Select(observation => observation.Kind).Distinct(StringComparer.Ordinal).ToArray();
            var resolutions = matched.Select(observation => observation.Resolution).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToList();
            var failure = GetFailure(expected, matched, observedClassification, expectedKeys, observedKeys, observedKinds);

            results.Add(new PatternResult
            {
                Id = expected.Id,
                Source = expected.Source,
                Kind = expected.Kind,
                Owner = expected.Owner,
                ExpectedClassification = expected.ExpectedClassification,
                ObservedClassification = observedClassification,
                ExpectedKeys = expectedKeys,
                ObservedKeys = observedKeys,
                Resolutions = resolutions,
                ObservationCount = matched.Length,
                Pass = failure is null,
                Failure = failure
            });
        }

        var normalizationResults = results.Where(result => result.Id is "environment-double-underscore" or "json-nesting").ToArray();
        var optionsResults = results.Where(result => result.Kind.StartsWith("options-", StringComparison.Ordinal)).ToArray();
        var unknownResults = results.Where(result => result.ExpectedClassification == "unknown").ToArray();
        return new ProbeReport
        {
            DeclarationSurfaces = declarations.Surfaces.ToList(),
            PatternCount = results.Count,
            PassedPatternCount = results.Count(result => result.Pass),
            NormalizationFixtureCount = normalizationResults.Length,
            NormalizationPassCount = normalizationResults.Count(result => result.Pass),
            OptionsFixtureCount = optionsResults.Length,
            OptionsPassCount = optionsResults.Count(result => result.Pass),
            UnknownAccessCount = unknownResults.Length,
            UnknownAccessesNeverClassifiedAsMissing = unknownResults.All(result => result.ObservedClassification == "unknown"),
            AllPatternsPass = results.All(result => result.Pass),
            Results = results
        };
    }

    private static string? GetFailure(
        ExpectedPattern expected,
        IReadOnlyList<ObservedAccess> matched,
        string observedClassification,
        IReadOnlyList<string> expectedKeys,
        IReadOnlyList<string> observedKeys,
        IReadOnlyList<string> observedKinds)
    {
        if (matched.Count != 1)
        {
            return $"expected exactly one semantic observation, found {matched.Count}";
        }

        if (!string.Equals(expected.Kind, observedKinds[0], StringComparison.Ordinal))
        {
            return $"expected kind '{expected.Kind}', observed '{observedKinds[0]}'";
        }

        if (!string.Equals(expected.ExpectedClassification, observedClassification, StringComparison.Ordinal))
        {
            return $"expected classification '{expected.ExpectedClassification}', observed '{observedClassification}'";
        }

        if (!expectedKeys.SequenceEqual(observedKeys, StringComparer.OrdinalIgnoreCase))
        {
            return "expected and observed normalized keys differ";
        }

        return null;
    }

    private static void PrintTable(ProbeReport report, string outputPath, TimeSpan duration)
    {
        Console.WriteLine("ConfigGap Roslyn feasibility probe");
        Console.WriteLine("Pattern                         Kind                       Expected    Observed    Keys                              Result");
        Console.WriteLine("------------------------------  -------------------------  ----------  ----------  --------------------------------  ------");
        foreach (var result in report.Results)
        {
            var keys = result.ObservedKeys.Count == 0 ? "-" : string.Join(',', result.ObservedKeys);
            Console.WriteLine($"{result.Id,-30}  {result.Kind,-25}  {result.ExpectedClassification,-10}  {result.ObservedClassification,-10}  {keys,-32}  {(result.Pass ? "PASS" : "FAIL")}");
            if (!result.Pass)
            {
                Console.WriteLine($"  failure: {result.Failure}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Patterns: {report.PassedPatternCount}/{report.PatternCount} passed");
        Console.WriteLine($"Normalization: {report.NormalizationPassCount}/{report.NormalizationFixtureCount} passed");
        Console.WriteLine($"Options sections: {report.OptionsPassCount}/{report.OptionsFixtureCount} passed");
        Console.WriteLine($"Dynamic/unresolvable: {report.UnknownAccessCount} unknown; never classified as missing: {report.UnknownAccessesNeverClassifiedAsMissing}");
        Console.WriteLine($"Machine-readable report: {Path.GetFullPath(outputPath)}");
        Console.WriteLine($"Duration: {duration.TotalMilliseconds:F0} ms");
    }

    private sealed class ProbeOptions
    {
        public required string RepositoryRoot { get; init; }
        public required string SolutionPath { get; init; }
        public required string ManifestPath { get; init; }
        public required string OutputPath { get; init; }

        public static ProbeOptions Parse(string[] args)
        {
            var repositoryRoot = Path.GetFullPath(Get(args, "--repository-root") ?? ".");
            var solutionPath = Path.GetFullPath(Get(args, "--solution") ?? "KeelMatrix.ConfigGap.sln");
            var manifestPath = Path.GetFullPath(Get(args, "--manifest") ?? "fixtures/expected.json");
            var outputPath = Path.GetFullPath(Get(args, "--output") ?? "artifacts/probe-results.json");
            return new ProbeOptions { RepositoryRoot = repositoryRoot, SolutionPath = solutionPath, ManifestPath = manifestPath, OutputPath = outputPath };
        }

        private static string? Get(string[] args, string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }
}
