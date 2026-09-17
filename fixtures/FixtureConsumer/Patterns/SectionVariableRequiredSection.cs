using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class SectionVariableRequiredSection
{
    public static IConfigurationSection Read(IConfiguration configuration)
    {
        var section = configuration.GetSection("Section");
        return section.GetRequiredSection("AliasKey");
    }
}
