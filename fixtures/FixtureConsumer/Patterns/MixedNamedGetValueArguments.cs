using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class MixedNamedGetValueArguments
{
    public static string? Read(IConfiguration configuration)
    {
        return configuration.GetValue<string>("Named:Key", defaultValue: "fallback");
    }
}
