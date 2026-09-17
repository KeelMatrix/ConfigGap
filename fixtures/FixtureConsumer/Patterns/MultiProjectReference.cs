using ConfigGap.FixtureSupport;
using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class MultiProjectReference
{
    public static string? Read(IConfiguration configuration)
    {
        _ = configuration.GetPaymentsApiKey();
        _ = configuration.GetCrossProjectValue("CrossProject:Key");
        return configuration["MultiProject:Key"];
    }
}
