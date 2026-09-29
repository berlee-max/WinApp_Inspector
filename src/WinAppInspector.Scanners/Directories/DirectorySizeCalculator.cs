using WinAppInspector.Core.Scanning;

namespace WinAppInspector.Scanners.Directories;

/// <summary>Recursive size pass for the second stage (§32). Skips reparse points so junctions are not double counted.</summary>
public sealed class DirectorySizeCalculator : IDirectorySizeCalculator
{
    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false,
    };

    public Task<DirectorySize> ComputeAsync(string path, CancellationToken cancellationToken) =>
        Task.Run(() => Compute(path, cancellationToken), cancellationToken);

    private static DirectorySize Compute(string path, CancellationToken cancellationToken)
    {
        long bytes = 0;
        var files = 0;
        try
        {
            var dir = new DirectoryInfo(path);
            if (!dir.Exists)
            {
                return new DirectorySize(0, 0, false, "Directory does not exist.");
            }

            foreach (var file in dir.EnumerateFiles("*", Options))
            {
                if ((files & 0xFF) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                files++;
                try
                {
                    bytes += file.Length;
                }
                catch (IOException)
                {
                    // Length can fail for files being written; the total stays an estimate.
                }
            }

            return new DirectorySize(bytes, files, true);
        }
        catch (OperationCanceledException)
        {
            return new DirectorySize(bytes, files, false, "Cancelled.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return new DirectorySize(bytes, files, false, ex.Message);
        }
    }
}
