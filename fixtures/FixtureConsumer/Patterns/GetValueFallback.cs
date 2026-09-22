using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class GetValueFallback
{
    public static string? Read(IConfiguration configuration)
    {
        return configuration.GetValue<string>("Primary", configuration["Fallback"]!);
    }
}
