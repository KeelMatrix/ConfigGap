using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class DynamicComputedKey
{
    public static string? Read(IConfiguration configuration)
    {
        var key = string.Join(":", "Section", "Computed");
        return configuration[key];
    }
}
