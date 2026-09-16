using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class InterpolatedKey
{
    private const string Section = "Section";
    private const string Key = "InterpolatedKey";

    public static string? Read(IConfiguration configuration)
    {
        return configuration[$"{Section}:{Key}"];
    }
}
