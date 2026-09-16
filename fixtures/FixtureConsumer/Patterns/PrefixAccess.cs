using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class PrefixAccess
{
    public static IEnumerable<IConfigurationSection> Read(IConfiguration configuration)
    {
        return configuration.GetSection("Features").GetChildren();
    }
}
