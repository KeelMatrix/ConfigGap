using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class AliasedRootIndexer
{
    public static string? Read(IConfiguration configuration)
    {
        IConfiguration alias = configuration;
        return alias["Section:Key"];
    }
}
