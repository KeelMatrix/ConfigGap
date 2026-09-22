using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class UnsupportedConfigurationAlias
{
    public static string? Read()
    {
        IConfiguration alias = GetUnknownConfiguration();
        return alias["Untrusted:Key"];
    }

    private static IConfiguration GetUnknownConfiguration() => throw new NotSupportedException();
}
