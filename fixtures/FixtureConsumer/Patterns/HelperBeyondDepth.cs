using Microsoft.Extensions.Configuration;

public static class HelperBeyondDepth
{
    public static string? Read(IConfiguration configuration) =>
        HopThree(configuration, "Helpers:BeyondDepth");

    private static string? HopThree(IConfiguration configuration, string key) =>
        HopTwo(configuration, key);

    private static string? HopTwo(IConfiguration configuration, string key) =>
        HopOne(configuration, key);

    private static string? HopOne(IConfiguration configuration, string key) =>
        configuration[key];
}
