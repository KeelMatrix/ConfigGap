using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class PinnedConfigurationShape
{
    public static string? Read(PinnedBuilder builder)
    {
        var config = builder.Configuration;
        return config.GetValue<string>("Regression:MissingConfigurationManagerProperty");
    }
}

public sealed class PinnedBuilder
{
    public IConfigurationManager Configuration { get; } = null!;
}
