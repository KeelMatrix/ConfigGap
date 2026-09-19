using KeelMatrix.Telemetry;

namespace KeelMatrix.ConfigGap;

internal interface IUsageTelemetry
{
    void RecordSuccessfulAnalysis();
}

internal sealed class SharedTelemetryReporter : IUsageTelemetry
{
    private readonly Func<Client> clientFactory;
    private Client? client;

    public SharedTelemetryReporter()
        : this(() => new Client("configgap", typeof(Program)))
    {
    }

    internal SharedTelemetryReporter(Func<Client> clientFactory)
    {
        this.clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    public void RecordSuccessfulAnalysis()
    {
        try
        {
            client ??= clientFactory();
            client.TrackActivation();
            client.TrackHeartbeat();
        }
        catch
        {
            // Telemetry is best-effort and must never affect analysis or its exit code.
        }
    }
}
