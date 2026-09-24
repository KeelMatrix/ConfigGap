using System.Text.Json;
using KeelMatrix.ConfigGap.Core;
using KeelMatrix.ConfigGap.Probe;
using Xunit;

namespace KeelMatrix.ConfigGap.Core.Tests;

public sealed class ConfigurationAnalysisTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void NormalizationIsDeterministicAndPortable()
    {
        Assert.Equal("Payments:Provider:ApiKey", KeyNormalizer.Normalize("Payments__Provider__ApiKey"));
        Assert.Equal("Payments:Provider:ApiKey", KeyNormalizer.Normalize("Payments:Provider:ApiKey"));
        Assert.Equal("a::b:c", KeyNormalizer.Normalize("a____b__c"));
    }

    [Fact]
    public async Task FixtureAnalysisPreservesMissingUnknownAndOptionsDistinctions()
    {
        var result = await AnalyzeFixtureAsync();

        Assert.Equal(ConfigGapExitCode.BlockingFindings, result.ExitCode);
        Assert.True(result.Report.Trustworthy);
        Assert.Contains("Payments", result.Report.BindableKeys);
        Assert.Contains("Section", result.Report.RequiredKeys);
        Assert.Contains("Section:Key", result.Report.ActuallyReadKeys);
        Assert.Contains("Section:AliasKey", result.Report.ActuallyReadKeys);
        Assert.Contains("RequiredOnly", result.Report.RequiredKeys);
        Assert.Contains("RequiredOnly", result.Report.BindableKeys);
        Assert.True(result.Report.UnknownAccessCount > 0);
        Assert.Contains(result.Report.Findings, finding =>
            finding.Code == "CG900" &&
            finding.Source == "fixtures/FixtureConsumer/Patterns/RootGetChildren.cs");
        Assert.Contains(result.Report.Findings, finding => finding.Code == "CG001" && finding.Key == "Unlisted:Key");
        Assert.Contains(result.Report.Findings, finding => finding.Code == "CG001" && finding.Key == "Section:AliasMissing");
        Assert.DoesNotContain(result.Report.Findings, finding => finding.Code == "CG900" && finding.Severity == "ERROR");
        Assert.True(result.Report.Findings.Where(finding => finding.Code == "CG900").All(finding => finding.Severity == "INFO"));
    }

    [Fact]
    public async Task ReportIsDeterministicAndContainsNoValuesOrAbsolutePaths()
    {
        var first = await AnalyzeFixtureAsync();
        var second = await AnalyzeFixtureAsync();
        var json = first.Report.ToDeterministicJson();

        Assert.Equal(json, second.Report.ToDeterministicJson());
        Assert.DoesNotContain(RepositoryRoot, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-value", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("=", json, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("toolConfigurationSchemaVersion").GetInt32());
    }

    [Fact]
    public void ActualEnvIsNotReadWithoutExplicitTemplateConfiguration()
    {
        var root = Path.Combine(Path.GetTempPath(), "configgap-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "appsettings.json"), "{\"Declared\":\"secret-value\"}");
            File.WriteAllText(Path.Combine(root, ".env"), "Undeclared=secret-value");
            var graph = DeclarationGraph.Load(root);

            Assert.True(graph.Contains("Declared"));
            Assert.False(graph.Contains("Undeclared"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ExplicitTemplateConfigurationReadsNamesOnly()
    {
        var fixtureRoot = Path.Combine(RepositoryRoot, "fixtures", "FixtureConsumer");
        var graph = DeclarationGraph.Load(fixtureRoot);

        Assert.True(graph.Contains("PAYMENTS:PROVIDER:APIKEY"));
        Assert.DoesNotContain("secret-value", graph.DeclaredKeys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RelativeSectionAccessesComposeTheirSectionPrefix()
    {
        var result = await AnalyzeFixtureAsync();

        Assert.Contains("Section:Key", result.Report.ActuallyReadKeys);
        Assert.DoesNotContain("Key", result.Report.ActuallyReadKeys);
        Assert.Contains("Section:Key", result.Report.RequiredKeys);
        Assert.Contains("Section:AliasKey", result.Report.RequiredKeys);
        Assert.DoesNotContain("Key", result.Report.RequiredKeys);
        Assert.DoesNotContain("AliasKey", result.Report.RequiredKeys);
        Assert.DoesNotContain(result.Report.Findings, finding => finding.Code == "CG001" && finding.Key == "Key");
        Assert.DoesNotContain(result.Report.Findings, finding => finding.Code == "CG001" && finding.Key == "AliasKey");
    }

    [Fact]
    public async Task InterfaceTypedSectionAliasesPreserveScopeForDirectAndHelperReads()
    {
        var result = await AnalyzeFixtureAsync();

        Assert.Contains(result.Report.ActuallyReadKeys, key => key.Equals("Payments:Provider:ApiKey", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Payments:HelperApiKey", result.Report.ActuallyReadKeys);
        Assert.DoesNotContain("Provider:ApiKey", result.Report.ActuallyReadKeys);
        Assert.DoesNotContain("HelperApiKey", result.Report.ActuallyReadKeys);
        Assert.DoesNotContain(result.Report.Findings, finding => finding.Code == "CG001" && finding.Key is "Provider:ApiKey" or "HelperApiKey");
        Assert.Contains(result.Report.Findings, finding => finding.Code == "CG001" && finding.Key == "Payments:AliasRootOnly");
    }

    [Fact]
    public async Task RootInterfaceAndAliasesPreserveLiteralGetValueAndIndexerReads()
    {
        var result = await AnalyzeFixtureAsync();

        Assert.Contains("Section:Key", result.Report.ActuallyReadKeys);
        Assert.Contains("Regression:MissingGetValue", result.Report.ActuallyReadKeys);
        Assert.Contains("Regression:MissingAliasedGetValue", result.Report.ActuallyReadKeys);
        Assert.Contains("Regression:MissingAliasedIndexer", result.Report.ActuallyReadKeys);
        Assert.Contains("Regression:MissingConfigurationManagerProperty", result.Report.ActuallyReadKeys);
        Assert.DoesNotContain(result.Report.Findings, finding =>
            finding.Code == "CG900" &&
            finding.Source?.EndsWith("RootConfigurationAliases.cs", StringComparison.Ordinal) == true);
        Assert.Contains(result.Report.Findings, finding => finding.Code == "CG001" && finding.Key == "Regression:MissingGetValue");
        Assert.Contains(result.Report.Findings, finding => finding.Code == "CG001" && finding.Key == "Regression:MissingAliasedGetValue");
        Assert.Contains(result.Report.Findings, finding => finding.Code == "CG001" && finding.Key == "Regression:MissingAliasedIndexer");
        Assert.Contains(result.Report.Findings, finding => finding.Code == "CG001" && finding.Key == "Regression:MissingConfigurationManagerProperty");
    }

    [Fact]
    public async Task UnsupportedConfigurationAliasesRemainUnknownInsteadOfBecomingRootReads()
    {
        var result = await AnalyzeFixtureAsync();

        Assert.DoesNotContain(result.Report.Findings, finding => finding.Code == "CG001" && finding.Key == "Untrusted:Key");
        Assert.Contains(result.Report.Findings, finding => finding.Code == "CG900" && finding.Source == "fixtures/FixtureConsumer/Patterns/UnsupportedConfigurationAlias.cs");
    }

    [Fact]
    public async Task IndependentlyEvaluatedFallbackReadsRemainVisible()
    {
        var result = await AnalyzeFixtureAsync();

        Assert.Contains("Primary", result.Report.ActuallyReadKeys);
        Assert.Contains("Fallback", result.Report.ActuallyReadKeys);
        Assert.Contains(result.Report.Findings, finding => finding.Code == "CG001" && finding.Key == "Fallback");
    }

    [Fact]
    public async Task UnknownBindSectionProducesAnInformationalObservation()
    {
        var result = await AnalyzeFixtureAsync();

        Assert.Contains(result.Report.Findings, finding => finding.Code == "CG900" && finding.Source == "fixtures/FixtureConsumer/Patterns/DynamicBind.cs");
        Assert.DoesNotContain(result.Report.Findings, finding => finding.Code == "CG001" && finding.Source == "fixtures/FixtureConsumer/Patterns/DynamicBind.cs");
    }

    [Fact]
    public async Task ConfigurationReceiverProvenanceDoesNotInventRootReads()
    {
        var result = await AnalyzeFixtureAsync();
        var findings = result.Report.Findings
            .Where(finding => finding.Source?.EndsWith("ReceiverProvenance.cs", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Equal(9, findings.Count(finding => finding.Code == "CG900"));
        Assert.DoesNotContain(findings, finding => finding.Code == "CG001");
        Assert.Contains("ProvenRootRead", result.Report.ActuallyReadKeys);
        Assert.Contains("ConstructorFieldRoot", result.Report.ActuallyReadKeys);
        Assert.Contains("ConstructorPropertyRoot", result.Report.ActuallyReadKeys);
        Assert.DoesNotContain("UnseenCallerLeaf", result.Report.ActuallyReadKeys);
        Assert.DoesNotContain("UnseenWrappedLeaf", result.Report.ActuallyReadKeys);
        Assert.DoesNotContain(result.Report.ActuallyReadKeys, key => key is
            "ParameterRootOnly" or "ParameterSectionOnly" or
            "PropertyRootOnly" or "PropertySectionOnly" or
            "FieldRootOnly" or "FieldSectionOnly" or
            "ConstructorAssignedSectionOnly");
    }

    [Fact]
    public async Task BoundedFrameworkAndReducedExtensionProvenancePreserveRootReads()
    {
        var result = await AnalyzeFixtureAsync();
        var sourceFindings = result.Report.Findings
            .Where(finding => finding.Source?.EndsWith("FrameworkRootProvenance.cs", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.DoesNotContain(sourceFindings, finding => finding.Code == "CG900");
        Assert.Contains("StartupRoot", result.Report.ActuallyReadKeys);
        Assert.Contains("ControllerRoot", result.Report.ActuallyReadKeys);
        Assert.Contains("RegisteredRoot", result.Report.ActuallyReadKeys);
        Assert.Contains("ReducedExtensionRoot", result.Report.ActuallyReadKeys);
    }

    [Theory]
    [InlineData("fixtures/FixtureConsumer/appsettings.json", "UnseenCallerLeaf", true)]
    [InlineData("fixtures/FixtureConsumer/appsettings.receiver-provenance-section.json", "Payments:UnseenCallerLeaf", false)]
    public async Task ParameterWithoutVisibleCallSiteRemainsUnknownForEveryDeclarationShape(
        string configurationPath,
        string declaredKey,
        bool expectUnusedDeclaration)
    {
        var result = await AnalyzeFixtureAsync(configurationPath);
        var findings = result.Report.Findings
            .Where(finding => finding.Source?.EndsWith("ReceiverProvenance.cs", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Equal(9, findings.Count(finding => finding.Code == "CG900"));
        Assert.DoesNotContain(findings, finding => finding.Code == "CG001" && finding.Key is "UnseenCallerLeaf" or "UnseenWrappedLeaf");
        Assert.DoesNotContain(result.Report.ActuallyReadKeys, key => key is "UnseenCallerLeaf" or "UnseenWrappedLeaf");
        if (expectUnusedDeclaration)
        {
            Assert.Contains(result.Report.Findings, finding => finding.Code == "CG002" && finding.Key == declaredKey);
            Assert.Contains(result.Report.Findings, finding => finding.Code == "CG002" && finding.Key == "UnseenWrappedLeaf");
        }
    }

    [Theory]
    [InlineData("{\"declarationSurfaces\":[]}")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":1,\"declarationSurfaces\":[],\"unexpected\":true}")]
    public void ToolConfigurationRejectsSchemaInvalidDocuments(string json)
    {
        var root = Path.Combine(Path.GetTempPath(), "configgap-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, ".configgap.json");
        try
        {
            File.WriteAllText(path, json);
            Assert.Throws<InvalidOperationException>(() => ConfigGapToolConfiguration.Load(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("{\"version\":1,\"frameworkOwnedPolicy\":\"include\",\"declarationSurfaces\":[]}")]
    [InlineData("{\"version\":1,\"declarationSurfaces\":[\"appsettings.json\"]}")]
    public void DeclarationGraphRejectsInvalidConfiguredSurfaceVariants(string json)
    {
        var root = Path.Combine(Path.GetTempPath(), "configgap-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, ".configgap.json");
        try
        {
            File.WriteAllText(Path.Combine(root, "appsettings.json"), "{\"Declared\":null}");
            File.WriteAllText(path, json);
            Assert.Throws<InvalidOperationException>(() => DeclarationGraph.Load(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DeclarationGraphRejectsAConfiguredFileLinkThatEscapesTheRepository()
    {
        var root = Path.Combine(Path.GetTempPath(), "configgap-tests", Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "configgap-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        var link = Path.Combine(root, "linked.json");
        try
        {
            File.WriteAllText(Path.Combine(root, ".configgap.json"), "{\"version\":1,\"declarationSurfaces\":[{\"kind\":\"json\",\"path\":\"linked.json\"}]}");
            var outsideFile = Path.Combine(outside, "external.json");
            File.WriteAllText(outsideFile, "not-json");
            try
            {
                File.CreateSymbolicLink(link, outsideFile);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw Xunit.Sdk.SkipException.ForSkip($"The test environment does not allow symbolic links: {exception.Message}");
            }
            catch (PlatformNotSupportedException exception)
            {
                throw Xunit.Sdk.SkipException.ForSkip($"The test platform does not support symbolic links: {exception.Message}");
            }

            var error = Assert.Throws<InvalidOperationException>(() => DeclarationGraph.Load(root));
            Assert.Contains("must stay inside the repository", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            if (Directory.Exists(outside))
            {
                Directory.Delete(outside, recursive: true);
            }
        }
    }

    [Fact]
    public void DeclarationGraphUsesFilesystemCaseRulesForConfiguredPaths()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var parent = Path.Combine(Path.GetTempPath(), "configgap-tests", Guid.NewGuid().ToString("N"));
        var root = Path.Combine(parent, "repo");
        var sibling = Path.Combine(parent, "REPO");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(sibling);
        try
        {
            var configPath = Path.Combine(sibling, ".configgap.json");
            File.WriteAllText(configPath, "{\"version\":1,\"declarationSurfaces\":[]}");

            var error = Assert.Throws<InvalidOperationException>(() => DeclarationGraph.Load(root, configPath));
            Assert.Contains("must stay inside the repository", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(parent))
            {
                Directory.Delete(parent, recursive: true);
            }
        }
    }

    [Fact]
    public async Task WorkspaceFailureReturnsExitCodeTwo()
    {
        var result = await ConfigurationAnalysisEngine.AnalyzeAsync(new ConfigGapAnalysisOptions
        {
            RepositoryRoot = RepositoryRoot,
            SolutionPath = Path.Combine(RepositoryRoot, "missing.sln")
        });

        Assert.Equal(ConfigGapExitCode.AnalysisFailure, result.ExitCode);
        Assert.False(result.Report.Trustworthy);
        Assert.NotNull(result.Report.FailureCode);
        Assert.NotNull(result.Report.FailureMessage);
        var failureMessage = result.Report.FailureMessage;
        Assert.Contains("Restore", failureMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(RepositoryRoot, failureMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("missing.sln", failureMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", failureMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ConfigGapAnalysisResult> AnalyzeFixtureAsync(string? configurationPath = null)
    {
        return await ConfigurationAnalysisEngine.AnalyzeAsync(new ConfigGapAnalysisOptions
        {
            RepositoryRoot = RepositoryRoot,
            SolutionPath = Path.Combine(RepositoryRoot, "KeelMatrix.ConfigGap.sln"),
            ConfigurationPath = configurationPath is null
                ? null
                : Path.Combine(RepositoryRoot, configurationPath)
        });
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KeelMatrix.ConfigGap.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The ConfigGap repository root was not found.");
    }
}
