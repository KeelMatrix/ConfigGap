using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class DynamicParameterKey
{
    public static string? Read(IConfiguration configuration, string key)
    {
        return configuration.GetValue<string>(key);
    }
}
