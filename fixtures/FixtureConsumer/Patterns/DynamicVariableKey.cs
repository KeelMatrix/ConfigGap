using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class DynamicVariableKey
{
    public static string? Read(IConfiguration configuration)
    {
        var key = GetKey();
        return configuration[key];
    }

    private static string GetKey() => "Section:DynamicVariable";
}
