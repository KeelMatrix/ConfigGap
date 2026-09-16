using ConfigGap.FixtureSupport;
using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class CustomExtensionCall
{
    public static string? Read(IConfiguration configuration)
    {
        return configuration.GetPaymentsApiKey();
    }
}
