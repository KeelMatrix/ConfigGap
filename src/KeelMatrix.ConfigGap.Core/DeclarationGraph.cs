using System.Text.Json;
using KeelMatrix.ConfigGap.Core;

namespace KeelMatrix.ConfigGap.Probe;

public sealed class DeclarationGraph
{
    private readonly Dictionary<string, string> displayKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> leafKeys = new(StringComparer.OrdinalIgnoreCase);

    private DeclarationGraph(IReadOnlyList<string> surfaces, IEnumerable<string>? keys = null)
    {
        Surfaces = surfaces;
        if (keys is not null)
        {
            foreach (var key in keys)
            {
                Add(key, leaf: true);
            }
        }
    }

    public IReadOnlyList<string> Surfaces { get; }

    public IReadOnlyList<string> DeclaredKeys => displayKeys.Values.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToArray();

    public IReadOnlyList<string> LeafKeys => leafKeys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToArray();

    public bool Contains(string key) => displayKeys.ContainsKey(KeyNormalizer.Normalize(key));

    public bool IsLeaf(string key) => leafKeys.Contains(KeyNormalizer.Normalize(key));

    public static DeclarationGraph CreateSynchronized(IEnumerable<string> keys) =>
        new(["<precision-protocol-synchronized-surface>"], keys);

    public DeclarationGraph Without(string key)
    {
        var normalizedKey = KeyNormalizer.Normalize(key);
        return new DeclarationGraph(
            Surfaces,
            displayKeys.Values.Where(existing => !existing.Equals(normalizedKey, StringComparison.OrdinalIgnoreCase)));
    }

    public static DeclarationGraph Load(string repositoryRoot, string? configurationPath = null)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var configurationFiles = configurationPath is null
            ? Directory.EnumerateFiles(root, ".configgap.json", SearchOption.AllDirectories)
                .Where(path => !IsIgnoredPath(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [Path.GetFullPath(configurationPath)];

        var surfaces = configurationFiles.Length == 0
            ? Directory.EnumerateFiles(root, "appsettings.json", SearchOption.AllDirectories)
                .Where(path => !IsIgnoredPath(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => new SurfaceFile(path, "json"))
                .ToArray()
            : configurationFiles
                .SelectMany(LoadConfiguredSurfaces)
                .OrderBy(surface => surface.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var graph = new DeclarationGraph(
            surfaces.Select(surface => ToRepositoryRelative(root, surface.Path)).ToArray());
        foreach (var surface in surfaces)
        {
            if (surface.Kind.Equals("template", StringComparison.OrdinalIgnoreCase))
            {
                graph.AddEnvironmentNames(surface.Path);
            }
            else
            {
                graph.AddJson(surface.Path);
            }
        }

        return graph;
    }

    private static IReadOnlyList<SurfaceFile> LoadConfiguredSurfaces(string configurationPath)
    {
        var configuration = ConfigGapToolConfiguration.Load(configurationPath);
        var configurationDirectory = Path.GetDirectoryName(configurationPath)!;
        var surfaces = new List<SurfaceFile>();
        foreach (var surface in configuration.DeclarationSurfaces.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.GetFullPath(Path.Combine(configurationDirectory, surface.Path));
            if (!File.Exists(path))
            {
                throw new InvalidOperationException($"CONFIGGAP_DECLARATION_LOAD_FAILURE: configured declaration surface '{surface.Path}' does not exist.");
            }

            surfaces.Add(new SurfaceFile(path, surface.Kind));
        }

        return surfaces;
    }

    private void AddJson(string path)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(path),
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        AddJsonElement(document.RootElement, string.Empty);
    }

    private void AddJsonElement(JsonElement element, string prefix)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (prefix.Length > 0)
                {
                    Add(prefix, leaf: false);
                }

                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    var key = prefix.Length == 0 ? property.Name : $"{prefix}:{property.Name}";
                    AddJsonElement(property.Value, key);
                }

                break;

            case JsonValueKind.Array:
                if (prefix.Length > 0)
                {
                    Add(prefix, leaf: false);
                }

                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    AddJsonElement(item, $"{prefix}:{index}");
                    index++;
                }

                break;

            default:
                if (prefix.Length > 0)
                {
                    Add(prefix, leaf: true);
                }

                break;
        }
    }

    private void AddEnvironmentNames(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (trimmed.StartsWith("export ", StringComparison.Ordinal))
            {
                trimmed = trimmed[7..].TrimStart();
            }

            var equals = trimmed.IndexOf('=');
            var name = equals < 0 ? trimmed : trimmed[..equals].Trim();
            if (name.Length > 0)
            {
                Add(name, leaf: true);
            }
        }
    }

    private void Add(string key, bool leaf)
    {
        var normalized = KeyNormalizer.Normalize(key);
        if (normalized.Length == 0)
        {
            return;
        }

        displayKeys.TryAdd(normalized, normalized);

        if (leaf)
        {
            leafKeys.Add(normalized);
        }
    }

    private static bool IsIgnoredPath(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("artifacts", StringComparison.OrdinalIgnoreCase));
    }

    private static string ToRepositoryRelative(string repositoryRoot, string path) =>
        Path.GetRelativePath(repositoryRoot, path).Replace(Path.DirectorySeparatorChar, '/');

    private readonly record struct SurfaceFile(string Path, string Kind);
}
