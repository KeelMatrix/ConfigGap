using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class SectionVariableIndexer
{
    public static string? Read(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection("Section");
        return section["AliasMissing"];
    }
}
