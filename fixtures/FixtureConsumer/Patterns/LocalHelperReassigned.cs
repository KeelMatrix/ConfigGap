using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class LocalHelperReassigned
{
    public static string? Read(IConfiguration configuration)
    {
        Require(configuration, "Helpers:Reassigned");
        return null;
    }

    private static void Require(IConfiguration configuration, string key)
    {
        key = "Helpers:Other";
        _ = configuration[key];
    }
}
