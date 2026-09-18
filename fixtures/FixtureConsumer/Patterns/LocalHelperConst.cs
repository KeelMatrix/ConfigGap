using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class LocalHelperConst
{
    private const string Key = "Helpers:Const";

    public static string? Read(IConfiguration configuration)
    {
        Require(configuration, Key);
        return null;
    }

    private static void Require(IConfiguration configuration, string key)
    {
        _ = configuration[key];
    }
}
