using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class InterfaceTypedRootGetValue
{
    public static string? Read(IConfiguration configuration) =>
        configuration.GetValue<string>("Section:Key");
}
