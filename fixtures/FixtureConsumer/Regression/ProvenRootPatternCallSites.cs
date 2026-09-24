using ConfigGap.FixtureConsumer.Patterns;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ConfigGap.FixtureConsumer.Regression;

public static class ProvenRootPatternCallSites
{
    private static IConfigurationRoot Root { get; } = null!;

    public static void Exercise(IServiceCollection services, string dynamicKey, string dynamicSection)
    {
        ArrayOptions.Read(Root);
        _ = ConcatenatedKey.Read(Root);
        ConfigureOptions.Read(services, Root);
        _ = ConstKey.Read(Root);
        _ = CustomExtensionCall.Read(Root);
        DynamicBind.Read(Root, dynamicSection);
        _ = DynamicComputedKey.Read(Root);
        _ = DynamicConfigurationKey.Read(Root);
        _ = DynamicVariableKey.Read(Root);
        _ = EnvironmentDoubleUnderscore.Read(Root);
        _ = EnvironmentSpecificSettings.Read(Root);
        _ = FrameworkOwnedKey.Read(Root);
        _ = FrameworkOwnedMissing.Read(Root);
        _ = GetRequiredSection.Read(Root);
        _ = GetSection.Read(Root);
        _ = GetValue.Read(Root);
        _ = GetValueFallback.Read(Root);
        _ = HelperBeyondDepth.Read(Root);
        _ = HelperBranchCondition.Read(Root);
        _ = HelperConstArgument.Read(Root);
        HelperLiteralArgument.Read(Root);
        _ = HelperNonConstantArgument.Read(Root, dynamicKey);
        _ = HelperReassignedParameter.Read(Root);
        _ = InterpolatedKey.Read(Root);
        _ = JsonNesting.Read(Root);
        _ = LiteralIndexer.Read(Root);
        _ = LiteralUndeclared.Read(Root);
        _ = LocalHelperConst.Read(Root);
        _ = LocalHelperDepthBound.Read(Root);
        _ = LocalHelperLiteral.Read(Root);
        _ = LocalHelperNonConstant.Read(Root, dynamicKey);
        _ = LocalHelperReassigned.Read(Root);
        _ = MixedNamedGetValueArguments.Read(Root);
        _ = MultiProjectReference.Read(Root);
        _ = NamedGetValueArguments.Read(Root);
        NestedOptions.Read(Root);
        _ = NonGenericGetValue.Read(Root);
        NullableOptions.Read(Root);
        OptionsBind.Read(Root);
        _ = PrefixAccess.Read(Root);
        _ = RelativeGetValue.Read(Root);
        _ = RelativeIndexer.Read(Root);
        _ = RelativeRequiredSection.Read(Root);
        RequiredOptionsBind.Read(Root);
        _ = RootGetChildren.Read(Root);
        _ = SameNameAnalyzedHelper.Read(Root, dynamicKey);
        _ = SameNameUnrelatedHelper.Read(Root);
        _ = SectionHelperIndexer.Read(Root);
        _ = SectionInterfaceAliasIndexer.Read(Root);
        _ = SectionInterfaceAliasMissing.Read(Root);
        _ = SectionVariableGetValue.Read(Root);
        _ = SectionVariableIndexer.Read(Root);
        _ = SectionVariableReassigned.Read(Root);
        _ = SectionVariableRequiredSection.Read(Root);
        TemplateOnlyOptions.Read(Root);

        _ = RootConfigurationAliases.InterfaceTypedDeclared(Root);
        _ = RootConfigurationAliases.InterfaceTypedMissing(Root);
        _ = RootConfigurationAliases.AliasedGetValueDeclared(Root);
        _ = RootConfigurationAliases.AliasedGetValueMissing(Root);
        _ = RootConfigurationAliases.AliasedIndexerDeclared(Root);
        _ = RootConfigurationAliases.AliasedIndexerMissing(Root);
    }

}
