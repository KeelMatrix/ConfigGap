using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class RelativeIndexer
{
    public static string? Read(IConfiguration configuration)
    {
        return configuration.GetSection("Section")["Key"];
    }
}
