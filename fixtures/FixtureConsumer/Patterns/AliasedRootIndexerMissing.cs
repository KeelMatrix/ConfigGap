using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class AliasedRootIndexerMissing
{
    public static string? Read(IConfiguration configuration)
    {
        IConfiguration alias = configuration;
        return alias["Regression:MissingAliasedIndexer"];
    }
}
