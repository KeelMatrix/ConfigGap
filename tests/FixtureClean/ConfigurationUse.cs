using Microsoft.Extensions.Configuration;

namespace FixtureClean;

public static class ConfigurationUse
{
    public static string? Read(IConfiguration configuration, string suffix) =>
        configuration["Clean:Key"] ?? configuration["Dynamic:" + suffix];
}
