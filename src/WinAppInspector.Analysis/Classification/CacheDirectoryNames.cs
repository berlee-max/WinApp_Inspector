using WinAppInspector.Core.IO;

namespace WinAppInspector.Analysis.Classification;

/// <summary>
/// Folder names that typically hold cache / log content (§9.6). A cache-like name is only a hint:
/// the owning application must still be identified before the directory is classified as <c>AppCache</c>.
/// </summary>
public static class CacheDirectoryNames
{
    private static readonly HashSet<string> CacheNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cache", "Caches", "Code Cache", "GPUCache", "DawnCache", "ShaderCache", "Temp", "tmp", "CachedData",
        "Service Worker", "blob_storage", "IndexedDB",
    };

    private static readonly HashSet<string> LogNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Logs", "Log", "Crashpad", "CrashReports", "CrashDumps", "Diagnostics", "Reports",
    };

    public static bool IsCacheName(string? name) => name is not null && CacheNames.Contains(name.Trim());

    public static bool IsLogName(string? name) => name is not null && LogNames.Contains(name.Trim());

    public static bool IsCacheOrLogName(string? name) => IsCacheName(name) || IsLogName(name);

    /// <summary>True when the last segment of <paramref name="path"/> is a cache or log folder name.</summary>
    public static bool IsCacheOrLogPath(string? path) => IsCacheOrLogName(WindowsPath.GetFileName(path));
}
