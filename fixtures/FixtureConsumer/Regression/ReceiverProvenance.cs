using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Regression;

public static class ReceiverProvenance
{
    private static IConfigurationRoot Root { get; } = null!;

    private static IConfiguration SectionProperty => Root.GetSection("Payments");

    private static IConfiguration SectionField = Root.GetSection("Payments");

    public static string? ParameterRootOnly() => ReadParameterRootOnly(Root.GetSection("Payments"));

    public static string? ParameterSectionOnly() => ReadParameterSectionOnly(Root.GetSection("Payments"));

    public static string? PropertyRootOnly() => SectionProperty["PropertyRootOnly"];

    public static string? PropertySectionOnly() => SectionProperty["PropertySectionOnly"];

    public static string? FieldRootOnly() => SectionField["FieldRootOnly"];

    public static string? FieldSectionOnly() => SectionField["FieldSectionOnly"];

    public static string? ProvenRootAlias()
    {
        IConfiguration alias = Root;
        return ReadProvenRoot(alias);
    }

    private static string? ReadParameterRootOnly(IConfiguration configuration) =>
        configuration["ParameterRootOnly"];

    private static string? ReadParameterSectionOnly(IConfiguration configuration) =>
        configuration["ParameterSectionOnly"];

    private static string? ReadProvenRoot(IConfiguration configuration) =>
        configuration["ProvenRootRead"];
}
