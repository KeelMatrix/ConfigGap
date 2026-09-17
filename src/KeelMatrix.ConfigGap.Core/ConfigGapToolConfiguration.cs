using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeelMatrix.ConfigGap.Core;

public sealed class ConfigGapToolConfiguration
{
    public const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [JsonPropertyName("version")]
    public int Version { get; init; } = CurrentVersion;

    [JsonPropertyName("declarationSurfaces")]
    public List<ConfigGapDeclarationSurface> DeclarationSurfaces { get; init; } = [];

    [JsonPropertyName("frameworkOwnedPolicy")]
    public string FrameworkOwnedPolicy { get; init; } = "exclude";

    public static ConfigGapToolConfiguration Load(string path)
    {
        try
        {
            var configuration = JsonSerializer.Deserialize<ConfigGapToolConfiguration>(
                File.ReadAllText(path),
                JsonOptions);
            if (configuration is null || configuration.Version != CurrentVersion)
            {
                throw new InvalidOperationException("CONFIGGAP_CONFIG_VERSION: .configgap.json must declare version 1.");
            }

            if (!string.Equals(configuration.FrameworkOwnedPolicy, "exclude", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("CONFIGGAP_CONFIG_POLICY: frameworkOwnedPolicy must be 'exclude'.");
            }

            foreach (var surface in configuration.DeclarationSurfaces)
            {
                ValidateSurface(surface);
            }

            return configuration;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("CONFIGGAP_CONFIG_PARSE_FAILURE: .configgap.json is not valid JSON.", exception);
        }
    }

    private static void ValidateSurface(ConfigGapDeclarationSurface surface)
    {
        if (string.IsNullOrWhiteSpace(surface.Path) ||
            Path.IsPathRooted(surface.Path) ||
            surface.Path.Split('/', '\\').Contains("..", StringComparer.Ordinal))
        {
            throw new InvalidOperationException("CONFIGGAP_CONFIG_SURFACE: declaration surface paths must be non-empty relative paths.");
        }

        if (surface.Kind is not ("json" or "template"))
        {
            throw new InvalidOperationException("CONFIGGAP_CONFIG_SURFACE: declaration surface kind must be 'json' or 'template'.");
        }

        if (surface.Kind == "template" && Path.GetFileName(surface.Path).Equals(".env", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("CONFIGGAP_CONFIG_SURFACE: actual .env files are not declaration templates.");
        }
    }
}

public sealed class ConfigGapDeclarationSurface
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "json";

    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;
}
