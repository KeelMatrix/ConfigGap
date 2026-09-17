using System.Text.Json;
using KeelMatrix.ConfigGap.Core;
using KeelMatrix.ConfigGap.Probe;
using Xunit;

namespace KeelMatrix.ConfigGap.Core.Tests;

public sealed class ConfigurationAnalysisTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void NormalizationIsDeterministicAndPortable()
    {
        Assert.Equal("Payments:Provider:ApiKey", KeyNormalizer.Normalize("Payments__Provider__ApiKey"));
        Assert.Equal("Payments:Provider:ApiKey", KeyNormalizer.Normalize("Payments:Provider:ApiKey"));
        Assert.Equal("a::b:c", KeyNormalizer.Normalize("a____b__c"));
    }

    [Fact]
    public async Task FixtureAnalysisPreservesMissingUnknownAndOptionsDistinctions()
    {
        var result = await AnalyzeFixtureAsync();

        Assert.Equal(ConfigGapExitCode.BlockingFindings, result.ExitCode);
        Assert.True(result.Report.Trustworthy);
        Assert.Contains("Payments", result.Report.BindableKeys);
        Assert.Contains("Section", result.Report.RequiredKeys);
        Assert.Contains("Section:Key", result.Report.ActuallyReadKeys);
        Assert.Contains(result.Report.Findings, finding => finding.Code == "CG001" && finding.Key == "Unlisted:Key");
        Assert.DoesNotContain(result.Report.Findings, finding => finding.Code == "CG900" && finding.Severity == "ERROR");
        Assert.True(result.Report.Findings.Where(finding => finding.Code == "CG900").All(finding => finding.Severity == "INFO"));
    }

    [Fact]
    public async Task ReportIsDeterministicAndContainsNoValuesOrAbsolutePaths()
    {
        var first = await AnalyzeFixtureAsync();
        var second = await AnalyzeFixtureAsync();
        var json = first.Report.ToDeterministicJson();

        Assert.Equal(json, second.Report.ToDeterministicJson());
        Assert.DoesNotContain(RepositoryRoot, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-value", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("=", json, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("toolConfigurationSchemaVersion").GetInt32());
    }

    [Fact]
    public void ActualEnvIsNotReadWithoutExplicitTemplateConfiguration()
    {
        var root = Path.Combine(Path.GetTempPath(), "configgap-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "appsettings.json"), "{\"Declared\":\"secret-value\"}");
            File.WriteAllText(Path.Combine(root, ".env"), "Undeclared=secret-value");
            var graph = DeclarationGraph.Load(root);

            Assert.True(graph.Contains("Declared"));
            Assert.False(graph.Contains("Undeclared"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ExplicitTemplateConfigurationReadsNamesOnly()
    {
        var fixtureRoot = Path.Combine(RepositoryRoot, "fixtures", "FixtureConsumer");
        var graph = DeclarationGraph.Load(fixtureRoot);

        Assert.True(graph.Contains("PAYMENTS:PROVIDER:APIKEY"));
        Assert.DoesNotContain("secret-value", graph.DeclaredKeys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WorkspaceFailureReturnsExitCodeTwo()
    {
        var result = await ConfigurationAnalysisEngine.AnalyzeAsync(new ConfigGapAnalysisOptions
        {
            RepositoryRoot = RepositoryRoot,
            SolutionPath = Path.Combine(RepositoryRoot, "missing.sln")
        });

        Assert.Equal(ConfigGapExitCode.AnalysisFailure, result.ExitCode);
        Assert.False(result.Report.Trustworthy);
        Assert.NotNull(result.Report.FailureCode);
    }

    private static async Task<ConfigGapAnalysisResult> AnalyzeFixtureAsync()
    {
        return await ConfigurationAnalysisEngine.AnalyzeAsync(new ConfigGapAnalysisOptions
        {
            RepositoryRoot = RepositoryRoot,
            SolutionPath = Path.Combine(RepositoryRoot, "KeelMatrix.ConfigGap.sln")
        });
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KeelMatrix.ConfigGap.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The ConfigGap repository root was not found.");
    }
}
