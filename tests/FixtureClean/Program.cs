using Microsoft.Extensions.Configuration;

namespace FixtureClean;

public static class Program
{
    private static IConfigurationRoot Root { get; } = null!;

    public static void Main()
    {
        _ = ConfigurationUse.Read(Root, GetDynamicSuffix());
    }

    private static string GetDynamicSuffix() => string.Empty;
}
