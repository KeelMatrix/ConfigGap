using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class NonGenericGetValue
{
    public static string? Read(IConfiguration configuration)
    {
        return configuration.GetValue(typeof(string), "NotStaticallySupported")?.ToString();
    }
}
