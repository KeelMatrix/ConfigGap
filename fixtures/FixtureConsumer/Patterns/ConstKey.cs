using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class ConstKey
{
    private const string Key = "Section:ConstKey";

    public static string? Read(IConfiguration configuration)
    {
        return configuration[Key];
    }
}
