using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class AliasedRootGetValueMissing
{
    public static string? Read(IConfiguration configuration)
    {
        IConfiguration alias = configuration;
        return alias.GetValue<string>("Regression:MissingAliasedGetValue");
    }
}
