using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class FrameworkOwnedMissing
{
    public static string? Read(IConfiguration configuration)
    {
        return configuration["Kestrel:Endpoints:Https:Url"];
    }
}
