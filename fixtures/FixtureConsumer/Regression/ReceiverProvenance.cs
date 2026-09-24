using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Regression;

public static class ReceiverProvenance
{
    private static IConfigurationRoot Root { get; } = null!;

    private static IConfiguration SectionProperty => Root.GetSection("Payments");

    private static IConfiguration SectionField = Root.GetSection("Payments");

    public static string? ExternalEntryPoint(IConfiguration configuration) =>
        configuration["UnseenCallerLeaf"];

    public static string? ExternalEntryPointParenthesized(IConfiguration configuration) =>
        (configuration)["UnseenWrappedLeaf"];

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

    public static string? ConstructorAssignedRoot()
    {
        var receiver = new ConstructorAssignedReceiver(Root);
        return receiver.ReadField() ?? receiver.ReadProperty();
    }

    public static string? ConstructorAssignedSectionOnly() =>
        new ConstructorAssignedSectionReceiver(Root.GetSection("Payments")).Read();

    private static string? ReadParameterRootOnly(IConfiguration configuration) =>
        configuration["ParameterRootOnly"];

    private static string? ReadParameterSectionOnly(IConfiguration configuration) =>
        configuration["ParameterSectionOnly"];

    private static string? ReadProvenRoot(IConfiguration configuration) =>
        configuration["ProvenRootRead"];
}

public sealed class ConstructorAssignedReceiver
{
    private readonly IConfiguration _field;

    private IConfiguration Property { get; }

    public ConstructorAssignedReceiver(IConfiguration configuration)
    {
        _field = configuration;
        Property = configuration;
    }

    public string? ReadField() => _field["ConstructorFieldRoot"];

    public string? ReadProperty() => Property["ConstructorPropertyRoot"];
}

public sealed class ConstructorAssignedSectionReceiver
{
    private readonly IConfiguration _field;

    public ConstructorAssignedSectionReceiver(IConfiguration configuration)
    {
        _field = configuration;
    }

    public string? Read() => _field["ConstructorAssignedSectionOnly"];
}
