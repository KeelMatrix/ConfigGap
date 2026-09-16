using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace ConfigGap.FixtureSupport;

public static class CustomConfigurationExtensions
{
    public static string? GetPaymentsApiKey(this IConfiguration configuration)
    {
        return configuration["Custom:WrappedKey"];
    }

    // This fixture-shaped extension keeps the probe independent from the full
    // Options configuration package while preserving the OptionsBuilder call shape.
    public static OptionsBuilder<TOptions> BindConfiguration<TOptions>(
        this OptionsBuilder<TOptions> builder,
        string sectionName)
        where TOptions : class
    {
        return builder;
    }
}
