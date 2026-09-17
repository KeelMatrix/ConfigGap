using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureSupport;

public static class CrossProjectConfigurationExtensions
{
    public static string? GetCrossProjectValue(this IConfiguration configuration, string key)
    {
        return configuration[key];
    }
}
