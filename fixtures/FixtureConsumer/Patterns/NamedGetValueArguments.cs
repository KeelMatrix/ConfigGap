using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class NamedGetValueArguments
{
    public static string? Read(IConfiguration configuration)
    {
        return configuration.GetValue<string>(defaultValue: "fallback", key: "Named:Key");
    }
}
