using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class LiteralIndexer
{
    public static string? Read(IConfiguration configuration)
    {
        return configuration["Section:Key"];
    }
}
