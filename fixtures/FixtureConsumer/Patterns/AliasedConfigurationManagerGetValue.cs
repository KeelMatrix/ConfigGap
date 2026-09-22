using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class AliasedConfigurationManagerGetValue
{
    public static string? Read(IConfigurationManager configurationManager)
    {
        var config = configurationManager;
        return config.GetValue<string>("Regression:MissingConfigurationManager");
    }
}
