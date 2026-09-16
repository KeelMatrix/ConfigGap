using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class JsonNesting
{
    public static string? Read(IConfiguration configuration)
    {
        return configuration["Payments:Provider:ApiKey"];
    }
}
