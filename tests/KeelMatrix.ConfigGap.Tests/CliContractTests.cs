using System.Text.Json;
using KeelMatrix.ConfigGap;
using Xunit;

namespace KeelMatrix.ConfigGap.Tests;

public sealed class CliContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string ConsumerProject = Path.Combine("fixtures", "FixtureConsumer", "FixtureConsumer.csproj");
    private static readonly string DefaultConfig = Path.Combine("fixtures", "FixtureConsumer", "appsettings.json");
    private static readonly string CleanProject = Path.Combine("tests", "FixtureClean", "FixtureClean.csproj");
    private static readonly string CleanConfig = Path.Combine("tests", "FixtureClean", "configgap.json");

    [Fact]
    public async Task CleanProjectProducesDeterministicJsonAndExitZero()
    {
        var first = await RunAsync("check", "--project", CleanProject, "--config", CleanConfig, "--format", "json");
        var second = await RunAsync("check", "--project", CleanProject, "--config", CleanConfig, "--format", "json");

        Assert.True(first.ExitCode == 0, first.Error + first.Output);
        Assert.Equal(first.Output, second.Output);
        using var report = JsonDocument.Parse(first.Output);
        Assert.True(report.RootElement.GetProperty("trustworthyAnalysis").GetBoolean());
        Assert.Equal(0, report.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Contains(report.RootElement.GetProperty("findings").EnumerateArray(), finding => finding.GetProperty("code").GetString() == "CG900");
    }

    [Fact]
    public async Task CoreFindingClassesAndKeyStateDistinctionsReachTheCliReport()
    {
        var result = await RunAsync("check", "--project", ConsumerProject, "--config", DefaultConfig, "--format", "json");

        Assert.Equal(1, result.ExitCode);
        using var report = JsonDocument.Parse(result.Output);
        var findings = report.RootElement.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Contains(findings, finding =>
            finding.GetProperty("code").GetString() == "CG002" &&
            finding.GetProperty("severity").GetString() == "warning" &&
            finding.GetProperty("key").GetString() == "Helpers:DepthBound");
        Assert.Contains(findings, finding =>
            finding.GetProperty("code").GetString() == "CG002" &&
            finding.GetProperty("key").GetString() == "Helpers:Reassigned");
        Assert.Contains("Payments", report.RootElement.GetProperty("bindableKeys").EnumerateArray().Select(key => key.GetString()));
        Assert.Contains("Section", report.RootElement.GetProperty("requiredKeys").EnumerateArray().Select(key => key.GetString()));
        Assert.Contains("Section:Key", report.RootElement.GetProperty("actuallyReadKeys").EnumerateArray().Select(key => key.GetString()));
        Assert.Contains("Section:Key", report.RootElement.GetProperty("knownKeys").EnumerateArray().Select(key => key.GetString()));
    }

    [Fact]
    public async Task MissingDeclarationProducesBlockingTextDiagnosticAndExitOne()
    {
        var result = await RunAsync("check", "--project", ConsumerProject, "--config", DefaultConfig, "--format", "text");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("CG001 error", result.Output, StringComparison.Ordinal);
        Assert.Contains("Configuration key: Unlisted:Key", result.Output, StringComparison.Ordinal);
        Assert.Contains("LiteralUndeclared.cs:9", result.Output, StringComparison.Ordinal);
        Assert.Contains("CG900 info", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DynamicAccessIsInformationalAndDoesNotBlockCleanAnalysis()
    {
        var result = await RunAsync("check", "--project", CleanProject, "--config", CleanConfig, "--format", "json");

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using var report = JsonDocument.Parse(result.Output);
        var findings = report.RootElement.GetProperty("findings").EnumerateArray().ToArray();
        Assert.NotEmpty(findings);
        Assert.All(findings.Where(finding => finding.GetProperty("code").GetString() == "CG900"), finding =>
        {
            Assert.Equal("info", finding.GetProperty("severity").GetString());
            Assert.Null(finding.GetProperty("key").GetString());
        });
        Assert.DoesNotContain(findings, finding => finding.GetProperty("code").GetString() == "CG001");
    }

    [Fact]
    public async Task InvalidOptionReturnsExitTwo()
    {
        var result = await RunAsync("check", "--format", "yaml");

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("--format must be 'text' or 'json'", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData()]
    [InlineData("--help")]
    [InlineData("check", "--help")]
    public async Task EmptyInvocationAndHelpUseStdoutAndExitZero(params string[] args)
    {
        var result = await RunAsync(args);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage:", result.Output, StringComparison.Ordinal);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task MissingOptionValueUsesStderrAndExitTwo()
    {
        var result = await RunAsync("check", "--solution");

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("option '--solution' requires a value", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DuplicateOptionUsesStderrAndExitTwo()
    {
        var result = await RunAsync("check", "--format", "text", "--format", "json");

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("option '--format' may only be specified once", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuccessfulTextAnalysisUsesStdoutOnly()
    {
        var result = await RunAsync("check", "--project", CleanProject, "--config", CleanConfig);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("ConfigGap check complete.", result.Output, StringComparison.Ordinal);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task SolutionAndProjectOptionsSelectOneProject()
    {
        var result = await RunAsync(
            "check",
            "--solution",
            "KeelMatrix.ConfigGap.sln",
            "--project",
            CleanProject,
            "--config",
            CleanConfig,
            "--format",
            "json");

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using var report = JsonDocument.Parse(result.Output);
        Assert.Equal(1, report.RootElement.GetProperty("projectCount").GetInt32());
    }

    [Fact]
    public async Task WorkspaceFailureCannotLookClean()
    {
        var result = await RunAsync("check", "--project", Path.Combine("fixtures", "FixtureBroken", "FixtureBroken.csproj"), "--format", "json");

        Assert.Equal(2, result.ExitCode);
        using var report = JsonDocument.Parse(result.Output);
        Assert.False(report.RootElement.GetProperty("trustworthyAnalysis").GetBoolean());
        Assert.Equal(2, report.RootElement.GetProperty("exitCode").GetInt32());
        Assert.NotEmpty(report.RootElement.GetProperty("diagnostics").EnumerateArray());
    }

    [Fact]
    public async Task TelemetryReceivesOnlyCoarseSummaryAfterTrustworthyAnalysis()
    {
        var telemetry = new RecordingTelemetry();
        var result = await RunAsync(
            ["check", "--project", CleanProject, "--config", CleanConfig, "--format", "json"],
            telemetry);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        var summary = Assert.Single(telemetry.Summaries);
        var serialized = JsonSerializer.Serialize(summary);
        Assert.DoesNotContain("Unlisted:Key", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("FixtureConsumer", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("appsettings", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TelemetryFailureIsNonFatalAndFailedWorkspaceDoesNotActivate()
    {
        var throwingTelemetry = new ThrowingTelemetry();
        var clean = await RunAsync(["check", "--project", CleanProject, "--config", CleanConfig], throwingTelemetry);
        var broken = await RunAsync(["check", "--project", Path.Combine("fixtures", "FixtureBroken", "FixtureBroken.csproj")], throwingTelemetry);

        Assert.True(clean.ExitCode == 0, clean.Error + clean.Output);
        Assert.Equal(2, broken.ExitCode);
        Assert.Equal(1, throwingTelemetry.Calls);
    }

    private static async Task<RunResult> RunAsync(params string[] args) => await RunAsync(RepositoryRoot, args, new RecordingTelemetry());

    private static async Task<RunResult> RunAsync(string[] args, IUsageTelemetry telemetry) => await RunAsync(RepositoryRoot, args, telemetry);

    private static async Task<RunResult> RunAsync(string currentDirectory, string[] args, IUsageTelemetry telemetry)
    {
        var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        var error = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        var exitCode = await ConfigGapApplication.RunAsync(args, currentDirectory, telemetry, output, error);
        return new RunResult(exitCode, output.ToString(), error.ToString());
    }


    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KeelMatrix.ConfigGap.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The ConfigGap repository root was not found.");
    }

    private sealed record RunResult(int ExitCode, string Output, string Error);

    private sealed class RecordingTelemetry : IUsageTelemetry
    {
        public List<TelemetrySummary> Summaries { get; } = [];

        public void RecordSuccessfulAnalysis(TelemetrySummary summary) => Summaries.Add(summary);
    }

    private sealed class ThrowingTelemetry : IUsageTelemetry
    {
        public int Calls { get; private set; }

        public void RecordSuccessfulAnalysis(TelemetrySummary summary)
        {
            _ = summary;
            Calls++;
            throw new InvalidOperationException("synthetic telemetry failure");
        }
    }
}
