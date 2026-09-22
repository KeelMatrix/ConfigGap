using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class SectionInterfaceAliasIndexer
{
    public static string? Read(IConfiguration configuration)
    {
        IConfiguration alias = configuration.GetSection("Payments");
        return alias["Provider:ApiKey"];
    }
}
