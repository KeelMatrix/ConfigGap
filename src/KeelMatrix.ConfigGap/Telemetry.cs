using KeelMatrix.Telemetry;

namespace KeelMatrix.ConfigGap;

internal interface IUsageTelemetry
{
    void RecordSuccessfulAnalysis(TelemetrySummary summary);
}

internal sealed class SharedTelemetryReporter : IUsageTelemetry
{
    private Client? client;

    public void RecordSuccessfulAnalysis(TelemetrySummary summary)
    {
        _ = summary;
        try
        {
            client ??= new Client("configgap", typeof(Program));
            client.TrackActivation();
            client.TrackHeartbeat();
        }
        catch
        {
            // Telemetry is best-effort and must never affect analysis or its exit code.
        }
    }
}

internal static class TelemetryBuckets
{
    public static string Count(int value) => value switch
    {
        <= 0 => "0",
        <= 5 => "1-5",
        <= 20 => "6-20",
        <= 100 => "21-100",
        _ => "101+"
    };

    public static string Duration(TimeSpan duration) => duration.TotalSeconds switch
    {
        < 1 => "under-1s",
        < 5 => "1-5s",
        < 30 => "5-30s",
        _ => "30s-plus"
    };

    public static string ExecutionClass()
    {
        foreach (var name in new[] { "CI", "GITHUB_ACTIONS", "TF_BUILD", "GITLAB_CI", "JENKINS_URL", "BUILDKITE" })
        {
            if (string.Equals(Environment.GetEnvironmentVariable(name), "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Environment.GetEnvironmentVariable(name), "1", StringComparison.OrdinalIgnoreCase))
            {
                return "ci";
            }
        }

        return "local";
    }
}
