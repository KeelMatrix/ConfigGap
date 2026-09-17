using Microsoft.Extensions.Configuration;

public static class HelperNonConstantArgument
{
    public static string? Read(IConfiguration configuration, string key) =>
        Require(configuration, key);

    private static string? Require(IConfiguration configuration, string key) =>
        configuration[key];
}
