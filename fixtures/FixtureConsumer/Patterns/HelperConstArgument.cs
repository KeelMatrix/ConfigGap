using Microsoft.Extensions.Configuration;

public static class HelperConstArgument
{
    private const string Key = "Helpers:Const";

    public static string? Read(IConfiguration configuration) =>
        Require(configuration, Key);

    private static string? Require(IConfiguration configuration, string key) =>
        configuration[key];
}
