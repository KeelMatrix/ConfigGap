using KeelMatrix.Telemetry;

namespace KeelMatrix.ConfigGap;

internal interface IUsageTelemetry
{
    void RecordSuccessfulAnalysis();
}

internal sealed class SharedTelemetryReporter : IUsageTelemetry
{
    public void RecordSuccessfulAnalysis()
    {
        var client = new Client("configgap", typeof(Program));
        client.TrackActivation();
        client.TrackHeartbeat();
    }
}
