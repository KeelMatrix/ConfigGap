using System.Globalization;
using System.Reflection;
using System.Text.Json;
using KeelMatrix.Telemetry;
using Xunit;

namespace KeelMatrix.ConfigGap.Tests;

[CollectionDefinition("Telemetry wire", DisableParallelization = true)]
public sealed class TelemetryWireGroup
{
}

[Collection("Telemetry wire")]
public sealed class TelemetryWireContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string CleanProject = Path.Combine("tests", "FixtureClean", "FixtureClean.csproj");
    private static readonly string CleanConfig = Path.Combine("tests", "FixtureClean", "configgap.json");
    private static readonly string[] ActivationFields =
    [
        "ci", "event", "installation_hash", "os", "project_hash", "runtime",
        "schema_version", "telemetry_version", "timestamp", "tool", "tool_version"
    ];

    [Fact]
    public void SerializedWirePayloadContainsOnlyDocumentedFields()
    {
        var payload = SerializeActivationPayload();
        using var json = JsonDocument.Parse(payload);
        var names = json.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToArray();

        Assert.Equal(ActivationFields, names);
        Assert.Equal("activation", json.RootElement.GetProperty("event").GetString());
        Assert.DoesNotContain("Payments:Provider:ApiKey", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("FixtureClean.csproj", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("appsettings.json", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("report contents", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SharedClientHonorsProcessOptOut()
    {
        var previousOptOut = Environment.GetEnvironmentVariable("KEELMATRIX_NO_TELEMETRY");
        try
        {
            Environment.SetEnvironmentVariable("KEELMATRIX_NO_TELEMETRY", "1");
            var client = new Client("configgap-optout-" + Guid.NewGuid().ToString("N"), typeof(Program));
            var field = typeof(Client).GetField("client", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(field);
            Assert.Contains("NullTelemetryClient", field!.GetValue(client)!.GetType().Name, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("KEELMATRIX_NO_TELEMETRY", previousOptOut);
        }
    }

    [Fact]
    public async Task TelemetryFailureDoesNotChangeAnalysisResult()
    {
        var baselineOutput = new StringWriter(CultureInfo.InvariantCulture);
        var baselineError = new StringWriter(CultureInfo.InvariantCulture);
        var baselineExit = await ConfigGapApplication.RunAsync(
            ["check", "--project", CleanProject, "--config", CleanConfig, "--format", "json"],
            RepositoryRoot,
            new RecordingTelemetry(),
            baselineOutput,
            baselineError);

        var failingOutput = new StringWriter(CultureInfo.InvariantCulture);
        var failingError = new StringWriter(CultureInfo.InvariantCulture);
        var failingExit = await ConfigGapApplication.RunAsync(
            ["check", "--project", CleanProject, "--config", CleanConfig, "--format", "json"],
            RepositoryRoot,
            new ThrowingTelemetry(),
            failingOutput,
            failingError);

        Assert.Equal(baselineExit, failingExit);
        Assert.Equal(baselineOutput.ToString(), failingOutput.ToString());
        Assert.Equal(baselineError.ToString(), failingError.ToString());
    }

    private static string SerializeActivationPayload()
    {
        var assembly = typeof(Client).Assembly;
        var eventType = assembly.GetType("KeelMatrix.Telemetry.Events.ActivationEvent", throwOnError: true)!;
        var constructor = eventType.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [
                typeof(string), typeof(string), typeof(string), typeof(int), typeof(string), typeof(string),
                typeof(string), typeof(string), typeof(bool), typeof(string)
            ],
            modifiers: null);
        Assert.NotNull(constructor);
        var activation = constructor!.Invoke(
        [
            "configgap",
            "0.1.0",
            "0.1.0",
            1,
            new string('a', 64),
            new string('b', 64),
            "net8.0",
            "windows",
            false,
            "2026-09-19T00:00:00Z"
        ]);

        var serializerType = assembly.GetType("KeelMatrix.Telemetry.Serialization.TelemetrySerializer", throwOnError: true)!;
        var serialize = serializerType.GetMethod("Serialize", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(serialize);
        return (string)serialize!.Invoke(null, [activation, "configgap"])!;
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

    private sealed class RecordingTelemetry : IUsageTelemetry
    {
        public void RecordSuccessfulAnalysis()
        {
        }
    }

    private sealed class ThrowingTelemetry : IUsageTelemetry
    {
        public void RecordSuccessfulAnalysis() => throw new InvalidOperationException("synthetic telemetry failure");
    }
}
