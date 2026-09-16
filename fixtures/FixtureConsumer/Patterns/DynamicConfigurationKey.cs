using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class DynamicConfigurationKey
{
    public static string? Read(IConfiguration configuration)
    {
        return configuration.GetValue<string>(configuration["KeyNameSource"]!);
    }
}
