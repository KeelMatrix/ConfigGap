using Microsoft.Extensions.Configuration;

public static class HelperLiteralArgument
{
    public static void Read(IConfiguration configuration)
    {
        static void Require(IConfiguration config, string key)
        {
            if (string.IsNullOrWhiteSpace(config[key]))
            {
                throw new InvalidOperationException($"Missing required configuration '{key}'.");
            }
        }

        Require(configuration, "Helpers:Literal");
    }
}
