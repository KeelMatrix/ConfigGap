using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class EnvironmentSpecificSettings
{
    public static string? Read(IConfiguration configuration)
    {
        return configuration["ProductionOnly:Endpoint"];
    }
}
