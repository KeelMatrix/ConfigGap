using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class SectionVariableReassigned
{
    public static string? Read(IConfiguration configuration)
    {
        var section = configuration.GetSection("Section");
        section = configuration.GetSection("Other");
        return section["Key"];
    }
}
