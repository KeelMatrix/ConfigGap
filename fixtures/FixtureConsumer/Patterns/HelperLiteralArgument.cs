using Microsoft.Extensions.Configuration;

public static class HelperLiteralArgument
{
    public static string? Read(IConfiguration configuration) =>
        Require(configuration, "Helpers:Literal");

    private static string? Require(IConfiguration configuration, string key) =>
        configuration[key];
}
