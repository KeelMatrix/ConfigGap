using System.Security.Cryptography;
using System.Text;

namespace KeelMatrix.ConfigGap.Probe;

internal sealed class WorkspaceProcessGate : IDisposable
{
    private readonly FileStream lockFile;

    private WorkspaceProcessGate(FileStream lockFile)
    {
        this.lockFile = lockFile;
    }

    internal static async Task<WorkspaceProcessGate> AcquireAsync(
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        var canonicalRoot = Path.GetFullPath(repositoryRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (OperatingSystem.IsWindows())
        {
            canonicalRoot = canonicalRoot.ToUpperInvariant();
        }

        var repositoryKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRoot)));
        var lockPath = Path.Combine(Path.GetTempPath(), $"configgap-msbuild-{repositoryKey}.lock");

        while (true)
        {
            try
            {
                return new WorkspaceProcessGate(new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    options: FileOptions.Asynchronous));
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }
        }
    }

    public void Dispose() => lockFile.Dispose();
}
