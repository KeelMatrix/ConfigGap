using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Regression;

public static class RootConfigurationAliases
{
    public static string? InterfaceTypedDeclared(IConfiguration configuration) =>
        configuration.GetValue<string>("Section:Key");

    public static string? InterfaceTypedMissing(IConfiguration configuration) =>
        configuration.GetValue<string>("Regression:MissingGetValue");

    public static string? AliasedGetValueDeclared(IConfiguration configuration)
    {
        IConfiguration alias = configuration;
        return alias.GetValue<string>("Section:Key");
    }

    public static string? AliasedGetValueMissing(IConfiguration configuration)
    {
        IConfiguration alias = configuration;
        return alias.GetValue<string>("Regression:MissingAliasedGetValue");
    }

    public static string? AliasedIndexerDeclared(IConfiguration configuration)
    {
        IConfiguration alias = configuration;
        return alias["Section:Key"];
    }

    public static string? AliasedIndexerMissing(IConfiguration configuration)
    {
        IConfiguration alias = configuration;
        return alias["Regression:MissingAliasedIndexer"];
    }

    public static string? ConfigurationManagerPropertyAlias(PinnedBuilder builder)
    {
        var config = builder.Configuration;
        return config.GetValue<string>("Regression:MissingConfigurationManagerProperty");
    }
}

public sealed class PinnedBuilder
{
    public IConfigurationManager Configuration { get; } = null!;
}
