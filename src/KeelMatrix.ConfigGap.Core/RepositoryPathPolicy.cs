namespace KeelMatrix.ConfigGap.Core;

/// <summary>
/// Applies the repository boundary using both the host filesystem's casing
/// rules and resolved filesystem links. Metadata is inspected, but file
/// contents are not read while validating the boundary.
/// </summary>
public static class RepositoryPathPolicy
{
    public static StringComparer PathComparer { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static StringComparison PathComparison { get; } =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static void EnsureInsideRepository(
        string repositoryRoot,
        string path,
        string failureMessage)
    {
        var fullRoot = Path.GetFullPath(repositoryRoot);
        var fullPath = Path.GetFullPath(path);
        if (!IsWithin(fullRoot, fullPath))
        {
            throw new InvalidOperationException(failureMessage);
        }

        try
        {
            var resolvedRoot = ResolveFileSystemPath(fullRoot);
            var resolvedPath = ResolveFileSystemPath(fullPath);
            if (!IsWithin(resolvedRoot, resolvedPath))
            {
                throw new InvalidOperationException(failureMessage);
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(failureMessage, exception);
        }
    }

    private static bool IsWithin(string root, string path)
    {
        var normalizedRoot = TrimTrailingSeparators(Path.GetFullPath(root));
        var normalizedPath = TrimTrailingSeparators(Path.GetFullPath(path));
        if (PathComparer.Equals(normalizedRoot, normalizedPath))
        {
            return true;
        }

        var separator = normalizedRoot.EndsWith(Path.DirectorySeparatorChar) ||
            normalizedRoot.EndsWith(Path.AltDirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(separator, PathComparison);
    }

    private static string ResolveFileSystemPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var volumeRoot = Path.GetPathRoot(fullPath)
            ?? throw new IOException("The configured path has no filesystem root.");
        var current = volumeRoot;
        var segments = fullPath[volumeRoot.Length..]
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            current = ResolveLink(current);
        }

        return TrimTrailingSeparators(Path.GetFullPath(current));
    }

    private static string ResolveLink(string path)
    {
        var directory = new DirectoryInfo(path);
        if (directory.LinkTarget is not null)
        {
            return directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                ?? throw new IOException("The configured directory link target could not be resolved.");
        }

        var file = new FileInfo(path);
        if (file.LinkTarget is not null)
        {
            return file.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                ?? throw new IOException("The configured file link target could not be resolved.");
        }

        return path;
    }

    private static string TrimTrailingSeparators(string path)
    {
        var root = Path.GetPathRoot(path);
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.Length == 0 && root is not null ? root : trimmed;
    }
}
