using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class GetValue
{
    public static string? Read(IConfiguration configuration)
    {
        return configuration.GetValue<string>("Section:Key");
    }
}
