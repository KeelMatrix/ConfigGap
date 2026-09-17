using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class HelperBranchCondition
{
    public static string? Read(IConfiguration configuration) =>
        Require(configuration, "Helpers:Branch");

    private static string? Require(IConfiguration configuration, string key)
    {
        if (key.Length == 0)
        {
            return null;
        }

        return configuration[key];
    }
}
