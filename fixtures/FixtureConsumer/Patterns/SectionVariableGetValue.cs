using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class SectionVariableGetValue
{
    public static string? Read(IConfiguration configuration)
    {
        var section = configuration.GetSection("Section");
        return section.GetValue<string>("AliasKey");
    }
}
