using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class InterfaceTypedRootGetValueMissing
{
    public static string? Read(IConfiguration configuration) =>
        configuration.GetValue<string>("Regression:MissingGetValue");
}
