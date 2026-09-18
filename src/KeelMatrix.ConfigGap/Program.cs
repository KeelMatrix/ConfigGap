namespace KeelMatrix.ConfigGap;

internal static class Program
{
    internal static Task<int> Main(string[] args) =>
        ConfigGapApplication.RunAsync(args, Directory.GetCurrentDirectory(), new SharedTelemetryReporter(), Console.Out, Console.Error);
}
