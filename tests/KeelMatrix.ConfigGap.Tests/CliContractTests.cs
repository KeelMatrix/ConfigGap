using System.Diagnostics;
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
    public async Task CliPreservesSectionScopeAndIndependentFallbackReads()
    {
        var result = await RunAsync("check", "--project", ConsumerProject, "--config", Path.Combine("fixtures", "FixtureConsumer", ".configgap.json"), "--format", "json");

        using var report = JsonDocument.Parse(result.Output);
        var findings = report.RootElement.GetProperty("findings").EnumerateArray().ToArray();
        var readKeys = report.RootElement.GetProperty("actuallyReadKeys").EnumerateArray().Select(key => key.GetString()).ToArray();
        Assert.Contains(readKeys, key => key?.Equals("Payments:Provider:ApiKey", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains("Payments:HelperApiKey", readKeys);
        Assert.Contains("Fallback", readKeys);
        Assert.DoesNotContain("Provider:ApiKey", readKeys);
        Assert.Contains(findings, finding => finding.GetProperty("code").GetString() == "CG001" && finding.GetProperty("key").GetString() == "Fallback");
        Assert.Contains(findings, finding => finding.GetProperty("code").GetString() == "CG001" && finding.GetProperty("key").GetString() == "Payments:AliasRootOnly");
        Assert.DoesNotContain(findings, finding => finding.GetProperty("code").GetString() == "CG001" && finding.GetProperty("key").GetString() is "Provider:ApiKey" or "HelperApiKey");
        Assert.Contains(findings, finding => finding.GetProperty("code").GetString() == "CG900" && finding.GetProperty("source").GetString() == "fixtures/FixtureConsumer/Patterns/DynamicBind.cs");
    }

    [Fact]
    public async Task CliReportsUnprovenConfigurationReceiverProvenanceAsInformational()
    {
        var result = await RunAsync("check", "--project", ConsumerProject, "--config", Path.Combine("fixtures", "FixtureConsumer", ".configgap.json"), "--format", "json");

        using var report = JsonDocument.Parse(result.Output);
        var readKeys = report.RootElement.GetProperty("actuallyReadKeys")
            .EnumerateArray()
            .Select(key => key.GetString())
            .ToArray();
        var findings = report.RootElement.GetProperty("findings")
            .EnumerateArray()
            .Where(finding => finding.GetProperty("source").GetString()?.EndsWith("ReceiverProvenance.cs", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Equal(7, findings.Count(finding => finding.GetProperty("code").GetString() == "CG900"));
        Assert.DoesNotContain(findings, finding => finding.GetProperty("code").GetString() == "CG001");
        Assert.Contains("ConstructorFieldRoot", readKeys);
        Assert.Contains("ConstructorPropertyRoot", readKeys);
        Assert.DoesNotContain("ConstructorAssignedSectionOnly", readKeys);
    }

    [Theory]
    [InlineData("{\"version\":1,\"frameworkOwnedPolicy\":\"include\",\"declarationSurfaces\":[]}")]
    [InlineData("{\"version\":1,\"declarationSurfaces\":[\"appsettings.json\"]}")]
    public async Task InvalidConfigurationVariantsReturnExitTwo(string json)
    {
        var directory = Path.Combine(RepositoryRoot, "artifacts", $"config-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var configPath = Path.Combine(directory, ".configgap.json");
        try
        {
            await File.WriteAllTextAsync(configPath, json);
            var result = await RunAsync(
                "check",
                "--project", CleanProject,
                "--config", Path.GetRelativePath(RepositoryRoot, configPath),
                "--format", "json");

            Assert.Equal(2, result.ExitCode);
            using var report = JsonDocument.Parse(result.Output);
            Assert.False(report.RootElement.GetProperty("trustworthyAnalysis").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExplicitProjectSelectionDoesNotInferAnAmbiguousSolution()
    {
        var root = Path.Combine(Path.GetTempPath(), "configgap-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            File.WriteAllText(Path.Combine(root, "first.sln"), string.Empty);
            File.WriteAllText(Path.Combine(root, "second.sln"), string.Empty);
            File.WriteAllText(Path.Combine(root, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(root, "Program.cs"), "public static class Program { public static void Main() { } }");
            File.WriteAllText(Path.Combine(root, "configgap.json"), "{\"version\":1,\"declarationSurfaces\":[]}");

            var result = await RunAsync(root, ["check", "--project", "App.csproj", "--config", "configgap.json", "--format", "json"], new RecordingTelemetry());

            Assert.Equal(0, result.ExitCode);
            using var report = JsonDocument.Parse(result.Output);
            Assert.True(report.RootElement.GetProperty("trustworthyAnalysis").GetBoolean(), result.Error);
            Assert.Equal(1, report.RootElement.GetProperty("projectCount").GetInt32());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void WorkspaceSelectionUsesFilesystemCaseRulesForExplicitProjects()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var parent = Path.Combine(Path.GetTempPath(), "configgap-cli-tests", Guid.NewGuid().ToString("N"));
        var root = Path.Combine(parent, "repo");
        var sibling = Path.Combine(parent, "REPO");
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        Directory.CreateDirectory(sibling);
        try
        {
            File.WriteAllText(Path.Combine(sibling, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

            var error = Assert.Throws<InvalidOperationException>(() => WorkspaceSelector.Resolve(
                new CliOptions { ProjectPath = "../REPO/App.csproj" },
                root));
            Assert.Contains("must stay inside the repository", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(parent))
            {
                Directory.Delete(parent, recursive: true);
            }
        }
    }

    [Fact]
    public void WorkspaceSelectionRejectsAProjectLinkThatEscapesTheRepository()
    {
        var root = Path.Combine(Path.GetTempPath(), "configgap-cli-tests", Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "configgap-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        Directory.CreateDirectory(outside);
        var link = Path.Combine(root, "linked.csproj");
        try
        {
            var outsideProject = Path.Combine(outside, "external.csproj");
            File.WriteAllText(outsideProject, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            try
            {
                File.CreateSymbolicLink(link, outsideProject);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw Xunit.Sdk.SkipException.ForSkip($"The test environment does not allow symbolic links: {exception.Message}");
            }
            catch (PlatformNotSupportedException exception)
            {
                throw Xunit.Sdk.SkipException.ForSkip($"The test platform does not support symbolic links: {exception.Message}");
            }

            var error = Assert.Throws<InvalidOperationException>(() => WorkspaceSelector.Resolve(
                new CliOptions { ProjectPath = "linked.csproj" },
                root));
            Assert.Contains("must stay inside the repository", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            if (Directory.Exists(outside))
            {
                Directory.Delete(outside, recursive: true);
            }
        }
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
    public async Task TelemetryIsRequestedOnlyAfterTrustworthyAnalysis()
    {
        var telemetry = new RecordingTelemetry();
        var result = await RunAsync(
            ["check", "--project", CleanProject, "--config", CleanConfig, "--format", "json"],
            telemetry);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        Assert.Equal(1, telemetry.Calls);
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

    [Fact]
    public async Task ConcurrentCliAnalysesRemainTrustworthy()
    {
        var results = await Task.WhenAll(
            RunChildAnalysisAsync(useWholeSolution: true),
            RunChildAnalysisAsync(useWholeSolution: false));

        Assert.All(results, result =>
        {
            Assert.Equal(1, result.ExitCode);
            using var report = JsonDocument.Parse(result.Output);
            Assert.True(report.RootElement.GetProperty("trustworthyAnalysis").GetBoolean(), result.Error);
        });
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

    private static async Task<ChildRunResult> RunChildAnalysisAsync(bool useWholeSolution)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = RepositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(typeof(ConfigGapApplication).Assembly.Location);
        startInfo.ArgumentList.Add("check");
        startInfo.ArgumentList.Add(useWholeSolution ? "--solution" : "--project");
        startInfo.ArgumentList.Add(useWholeSolution ? "KeelMatrix.ConfigGap.sln" : ConsumerProject);
        if (!useWholeSolution)
        {
            startInfo.ArgumentList.Add("--config");
            startInfo.ArgumentList.Add(DefaultConfig);
        }
        startInfo.ArgumentList.Add("--format");
        startInfo.ArgumentList.Add("json");
        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Could not start the child ConfigGap process.");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The concurrent ConfigGap child analysis exceeded 60 seconds.");
        }

        return new ChildRunResult(process.ExitCode, await outputTask, await errorTask);
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

    private sealed record ChildRunResult(int ExitCode, string Output, string Error);

    private sealed class RecordingTelemetry : IUsageTelemetry
    {
        public int Calls { get; private set; }

        public void RecordSuccessfulAnalysis() => Calls++;
    }

    private sealed class ThrowingTelemetry : IUsageTelemetry
    {
        public int Calls { get; private set; }

        public void RecordSuccessfulAnalysis()
        {
            Calls++;
            throw new InvalidOperationException("synthetic telemetry failure");
        }
    }
}
