using WinAppInspector.Core.Models;

namespace WinAppInspector.Core.Scanning;

/// <summary>Progress report for the scan page (§16). <see cref="Stage"/> is a stable key the UI localizes.</summary>
public sealed record ScanProgress(string Stage, string? CurrentItem = null, int? Completed = null, int? Total = null);

/// <summary>Well-known stage keys (§16). Kept as constants so the UI resource file can map them to Chinese text.</summary>
public static class ScanStages
{
    public const string Registry = "registry";
    public const string Appx = "appx";
    public const string Directories = "directories";
    public const string Executables = "executables";
    public const string Signatures = "signatures";
    public const string Processes = "processes";
    public const string Services = "services";
    public const string Startup = "startup";
    public const string ScheduledTasks = "tasks";
    public const string Resolving = "resolving";
    public const string Sizes = "sizes";
}

/// <summary>
/// One scanner per data source (§7). Scanners are asynchronous, cancellable and report progress;
/// they must never block the UI thread and must not swallow exceptions (§34) — a failure to read one item
/// is reported through <see cref="ScanResult{T}.Errors"/> while the rest of the scan continues.
/// </summary>
public interface IScanner<T>
{
    /// <summary>Stable name for logs and progress, e.g. "RegistryScanner".</summary>
    string Name { get; }

    Task<ScanResult<T>> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>An error encountered while scanning one item; the scan itself continues.</summary>
public sealed record ScanError(string Source, string Target, string Message, Exception? Exception = null);

/// <summary>Items found plus the errors met on the way.</summary>
public sealed record ScanResult<T>(IReadOnlyList<T> Items, IReadOnlyList<ScanError> Errors)
{
    public bool HasErrors => Errors.Count > 0;
}

public static class ScanResult
{
    public static ScanResult<T> Empty<T>() => new([], []);

    public static ScanResult<T> Of<T>(IReadOnlyList<T> items) => new(items, []);
}

/// <summary>Reads the version resource of an executable (§7.3). Implemented in Scanners with FileVersionInfo.</summary>
public interface IExecutableMetadataReader
{
    ExecutableMetadata Read(string path);
}

/// <summary>Reads the Authenticode signature of a file (§7.4). Implemented in Scanners.</summary>
public interface ISignatureReader
{
    SignatureInfo Read(string path);
}
