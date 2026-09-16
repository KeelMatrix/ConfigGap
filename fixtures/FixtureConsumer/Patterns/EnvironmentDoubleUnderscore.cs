using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class EnvironmentDoubleUnderscore
{
    public static string? Read(IConfiguration configuration)
    {
        return configuration["PAYMENTS__PROVIDER__APIKEY"];
    }
}
