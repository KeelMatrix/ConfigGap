using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class GetRequiredSection
{
    public static IConfigurationSection Read(IConfiguration configuration)
    {
        return configuration.GetRequiredSection("Section");
    }
}
