using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class RelativeGetValue
{
    public static string? Read(IConfiguration configuration)
    {
        return configuration.GetSection("Section").GetValue<string>("Key");
    }
}
