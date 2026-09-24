using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureSupport;

public static class CustomConfigurationExtensions
{
    private static IConfigurationRoot Root { get; } = null!;

    public static string? GetPaymentsApiKey(this IConfiguration configuration)
    {
        return configuration["Custom:WrappedKey"];
    }

    public static string? ExerciseProvenRootCallSite() => Root.GetPaymentsApiKey();
}
