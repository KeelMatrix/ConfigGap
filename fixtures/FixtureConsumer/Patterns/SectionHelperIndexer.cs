using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class SectionHelperIndexer
{
    public static string? Read(IConfiguration configuration) =>
        ReadKey(configuration, "HelperApiKey");

    private static string? ReadKey(IConfiguration configuration, string key) =>
        configuration.GetSection("Payments")[key];
}
