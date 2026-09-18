using System.Text.Json;
using KeelMatrix.ConfigGap.Core;

namespace KeelMatrix.ConfigGap.Probe;

public sealed class DeclarationGraph
{
    private const int MaximumDeclarationSurfaces = 128;
    private const long MaximumDeclarationFileBytes = 1024 * 1024;
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
        IReadOnlyList<SurfaceFile> surfaces;
        if (configurationPath is null)
        {
            var configurationFiles = EnumerateFiles(root, ".configgap.json")
                .Where(path => !IsIgnoredPath(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            surfaces = configurationFiles.Length == 0
                ? EnumerateFiles(root, "appsettings.json")
                    .Where(path => !IsIgnoredPath(path))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .Select(path => new SurfaceFile(path, "json"))
                    .ToArray()
                : configurationFiles.SelectMany(path => LoadConfiguredSurfaces(root, path)).ToArray();
        }
        else
        {
            var configuredPath = Path.GetFullPath(configurationPath);
            EnsureInsideRepository(root, configuredPath);
            if (!File.Exists(configuredPath))
            {
                throw new InvalidOperationException(
                    $"CONFIGGAP_DECLARATION_LOAD_FAILURE: configured file '{ToRepositoryRelative(root, configuredPath)}' does not exist.");
            }

            EnsureDeclarationFileBounded(configuredPath);
            surfaces = IsDeclarationSurface(Path.GetFileName(configuredPath))
                ? [new SurfaceFile(configuredPath, KindForSurface(configuredPath))]
                : LoadConfiguredSurfaces(root, configuredPath);
        }

        if (surfaces.Count > MaximumDeclarationSurfaces)
        {
            throw new InvalidOperationException(
                $"CONFIGGAP_DECLARATION_LOAD_FAILURE: more than {MaximumDeclarationSurfaces} declaration surfaces were configured. " +
                "Use --config to select a bounded set of surfaces.");
        }

        var orderedSurfaces = surfaces
            .OrderBy(surface => surface.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var graph = new DeclarationGraph(
            orderedSurfaces.Select(surface => ToRepositoryRelative(root, surface.Path)).ToArray());
        foreach (var surface in orderedSurfaces)
        {
            EnsureDeclarationFileBounded(surface.Path);
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

    private static IReadOnlyList<SurfaceFile> LoadConfiguredSurfaces(string repositoryRoot, string configurationPath)
    {
        try
        {
            var configuration = ConfigGapToolConfiguration.Load(configurationPath);
            var configurationDirectory = Path.GetDirectoryName(configurationPath)!;
            var surfaces = new List<SurfaceFile>();
            foreach (var surface in configuration.DeclarationSurfaces.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
            {
                var path = Path.GetFullPath(Path.Combine(configurationDirectory, surface.Path));
                EnsureInsideRepository(repositoryRoot, path);
                if (!File.Exists(path))
                {
                    throw new InvalidOperationException(
                        $"CONFIGGAP_DECLARATION_LOAD_FAILURE: configured declaration surface '{surface.Path}' does not exist.");
                }

                EnsureDeclarationFileBounded(path);
                surfaces.Add(new SurfaceFile(path, surface.Kind));
            }

            return surfaces;
        }
        catch (InvalidOperationException) when (HasSimpleSurfaceArray(configurationPath))
        {
            return LoadSimpleSurfaceConfiguration(repositoryRoot, configurationPath);
        }
    }

    private static bool HasSimpleSurfaceArray(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("declarationSurfaces", out var surfaces) &&
                surfaces.ValueKind == JsonValueKind.Array &&
                surfaces.EnumerateArray().All(surface => surface.ValueKind == JsonValueKind.String);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static SurfaceFile[] LoadSimpleSurfaceConfiguration(string repositoryRoot, string configurationPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(configurationPath), new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });
        if (!document.RootElement.TryGetProperty("version", out var version) ||
            version.ValueKind != JsonValueKind.Number || version.GetInt32() != ConfigGapToolConfiguration.CurrentVersion ||
            !document.RootElement.TryGetProperty("declarationSurfaces", out var surfaces) ||
            surfaces.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "CONFIGGAP_DECLARATION_LOAD_FAILURE: --config must be a version 1 JSON file with a declarationSurfaces array.");
        }

        var result = new List<SurfaceFile>();
        foreach (var surface in surfaces.EnumerateArray())
        {
            if (surface.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(surface.GetString()))
            {
                throw new InvalidOperationException(
                    "CONFIGGAP_DECLARATION_LOAD_FAILURE: every declaration surface must be a non-empty relative path.");
            }

            var path = Path.GetFullPath(Path.Combine(repositoryRoot, surface.GetString()!));
            EnsureInsideRepository(repositoryRoot, path);
            if (!File.Exists(path) || !IsDeclarationSurface(Path.GetFileName(path)))
            {
                throw new InvalidOperationException(
                    $"CONFIGGAP_DECLARATION_LOAD_FAILURE: '{surface.GetString()}' is not an existing supported declaration surface.");
            }

            EnsureDeclarationFileBounded(path);
            result.Add(new SurfaceFile(path, KindForSurface(path)));
        }

        return result.DistinctBy(surface => surface.Path, StringComparer.OrdinalIgnoreCase).ToArray();
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
                AddEnvironmentHierarchy(name);
            }
        }
    }

    private void AddEnvironmentHierarchy(string key)
    {
        var normalized = KeyNormalizer.Normalize(key);
        var segmentEnd = normalized.IndexOf(':');
        while (segmentEnd >= 0)
        {
            Add(normalized[..segmentEnd], leaf: false);
            segmentEnd = normalized.IndexOf(':', segmentEnd + 1);
        }

        Add(normalized, leaf: true);
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

    private static IEnumerable<string> EnumerateFiles(string root, string pattern) =>
        Directory.EnumerateFiles(root, pattern, new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        });

    private static bool IsDeclarationSurface(string fileName) =>
        (fileName.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) &&
            fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) ||
        string.Equals(fileName, ".env.example", StringComparison.OrdinalIgnoreCase);

    private static string KindForSurface(string path) =>
        Path.GetFileName(path).Equals(".env.example", StringComparison.OrdinalIgnoreCase) ? "template" : "json";

    private static bool IsIgnoredPath(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("artifacts", StringComparison.OrdinalIgnoreCase));
    }

    private static void EnsureInsideRepository(string repositoryRoot, string path)
    {
        var root = Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path);
        var rootWithSeparator = root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "CONFIGGAP_DECLARATION_LOAD_FAILURE: configured declaration paths must stay inside the repository.");
        }
    }

    private static void EnsureDeclarationFileBounded(string path)
    {
        if (new FileInfo(path).Length > MaximumDeclarationFileBytes)
        {
            throw new InvalidOperationException(
                $"CONFIGGAP_DECLARATION_LOAD_FAILURE: declaration surface '{Path.GetFileName(path)}' exceeds the 1 MiB analysis limit.");
        }
    }

    private static string ToRepositoryRelative(string repositoryRoot, string path) =>
        Path.GetRelativePath(repositoryRoot, path).Replace(Path.DirectorySeparatorChar, '/');

    private readonly record struct SurfaceFile(string Path, string Kind);
}
