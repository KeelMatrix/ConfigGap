using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class FrameworkOwnedKey
{
    public static string? Read(IConfiguration configuration)
    {
        return configuration["Kestrel:Endpoints:Http:Url"];
    }
}
