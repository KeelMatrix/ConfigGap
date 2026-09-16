using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class LiteralUndeclared
{
    public static string? Read(IConfiguration configuration)
    {
        return configuration["Unlisted:Key"];
    }
}
