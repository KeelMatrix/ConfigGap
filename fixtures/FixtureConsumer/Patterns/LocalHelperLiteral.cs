using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class LocalHelperLiteral
{
    public static string? Read(IConfiguration configuration)
    {
        static void Require(IConfiguration config, string key)
        {
            if (string.IsNullOrWhiteSpace(config[key]))
            {
                throw new InvalidOperationException($"Missing required configuration '{key}'.");
            }
        }

        Require(configuration, "Helpers:Literal");
        return null;
    }
}
