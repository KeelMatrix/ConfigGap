using System.Text.Json;

namespace KeelMatrix.ConfigGap.Probe;

internal sealed class DeclarationGraph
{
    private readonly Dictionary<string, string> displayKeys = new(StringComparer.OrdinalIgnoreCase);

    private DeclarationGraph(IReadOnlyList<string> surfaces)
    {
        Surfaces = surfaces;
    }

    public IReadOnlyList<string> Surfaces { get; }

    public bool Contains(string key) => displayKeys.ContainsKey(KeyNormalizer.Normalize(key));

    public static DeclarationGraph Load(string repositoryRoot)
    {
        var files = Directory.EnumerateFiles(repositoryRoot, "*", SearchOption.AllDirectories)
            .Where(path => !IsIgnoredPath(path))
            .Where(path => IsDeclarationSurface(Path.GetFileName(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var graph = new DeclarationGraph(files.Select(path => ToRepositoryRelative(repositoryRoot, path)).ToArray());
        foreach (var file in files)
        {
            if (string.Equals(Path.GetFileName(file), ".env.example", StringComparison.OrdinalIgnoreCase))
            {
                graph.AddEnvironmentNames(file);
            }
            else
            {
                graph.AddJson(file);
            }
        }

        return graph;
    }

    private static bool IsDeclarationSurface(string fileName) =>
        fileName.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) &&
        fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(fileName, ".env.example", StringComparison.OrdinalIgnoreCase);

    private static bool IsIgnoredPath(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("artifacts", StringComparison.OrdinalIgnoreCase));
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
                    Add(prefix);
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
                    Add(prefix);
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
                    Add(prefix);
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

            var equals = trimmed.IndexOf('=');
            var name = equals < 0 ? trimmed : trimmed[..equals].Trim();
            if (name.Length > 0)
            {
                Add(name);
            }
        }
    }

    private void Add(string key)
    {
        var normalized = KeyNormalizer.Normalize(key);
        if (normalized.Length > 0 && !displayKeys.ContainsKey(normalized))
        {
            displayKeys.Add(normalized, normalized);
        }
    }

    private static string ToRepositoryRelative(string repositoryRoot, string path) =>
        Path.GetRelativePath(repositoryRoot, path).Replace(Path.DirectorySeparatorChar, '/');
}
