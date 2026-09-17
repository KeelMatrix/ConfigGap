using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class SameNameAnalyzedHelper
{
    public static string? Read(IConfiguration configuration, string key) =>
        Require(configuration, key);

    private static string? Require(IConfiguration configuration, string key) =>
        configuration[key];
}

public static class SameNameUnrelatedHelper
{
    public static string? Read(IConfiguration configuration) =>
        Require(configuration, "Helpers:Unrelated");

    private static string? Require(IConfiguration configuration, string key) =>
        null;
}
