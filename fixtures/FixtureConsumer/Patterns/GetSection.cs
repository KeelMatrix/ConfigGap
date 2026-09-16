using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class GetSection
{
    public static IConfigurationSection Read(IConfiguration configuration)
    {
        return configuration.GetSection("Section");
    }
}
