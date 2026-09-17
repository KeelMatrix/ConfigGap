using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class RelativeRequiredSection
{
    public static IConfigurationSection Read(IConfiguration configuration)
    {
        return configuration.GetSection("Section").GetRequiredSection("Key");
    }
}
