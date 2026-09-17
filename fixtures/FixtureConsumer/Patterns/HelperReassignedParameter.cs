using Microsoft.Extensions.Configuration;

public static class HelperReassignedParameter
{
    public static string? Read(IConfiguration configuration) =>
        Require(configuration, "Helpers:Reassigned");

    private static string? Require(IConfiguration configuration, string key)
    {
        key = "Helpers:ReassignedElsewhere";
        return configuration[key];
    }
}
