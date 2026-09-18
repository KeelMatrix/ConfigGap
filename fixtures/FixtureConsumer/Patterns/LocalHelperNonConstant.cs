using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class LocalHelperNonConstant
{
    public static string? Read(IConfiguration configuration, string key)
    {
        Require(configuration, key);
        return null;
    }

    private static void Require(IConfiguration configuration, string key)
    {
        _ = configuration[key];
    }
}
