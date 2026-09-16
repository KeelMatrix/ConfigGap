using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureSupport;

public static class CustomConfigurationExtensions
{
    public static string? GetPaymentsApiKey(this IConfiguration configuration)
    {
        return configuration["Custom:WrappedKey"];
    }
}
