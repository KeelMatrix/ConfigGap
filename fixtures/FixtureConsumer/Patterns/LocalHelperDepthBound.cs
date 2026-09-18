using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class LocalHelperDepthBound
{
    public static string? Read(IConfiguration configuration)
    {
        RequireFirst(configuration, "Helpers:DepthBound");
        return null;
    }

    private static void RequireFirst(IConfiguration configuration, string key)
    {
        RequireSecond(configuration, key);
    }

    private static void RequireSecond(IConfiguration configuration, string key)
    {
        RequireThird(configuration, key);
    }

    private static void RequireThird(IConfiguration configuration, string key)
    {
        _ = configuration[key];
    }
}
