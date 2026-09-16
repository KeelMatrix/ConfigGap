using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class ConcatenatedKey
{
    private const string Section = "Section";
    private const string Key = "ConcatKey";

    public static string? Read(IConfiguration configuration)
    {
        return configuration[Section + ":" + Key];
    }
}
