using System.Diagnostics;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Scanning;

namespace WinAppInspector.Scanners.Executables;

/// <summary>Reads the version resource (§7.3) with <see cref="FileVersionInfo"/>. Never throws for a readable path; missing fields stay null.</summary>
public sealed class ExeMetadataReader : IExecutableMetadataReader
{
    public ExecutableMetadata Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = WindowsPath.Normalize(path);
        var info = new FileInfo(normalized);
        if (!info.Exists)
        {
            throw new FileNotFoundException("Executable not found.", normalized);
        }

        var version = FileVersionInfo.GetVersionInfo(normalized);
        return new ExecutableMetadata
        {
            Path = normalized,
            FileDescription = Clean(version.FileDescription),
            ProductName = Clean(version.ProductName),
            ProductVersion = Clean(version.ProductVersion),
            FileVersion = Clean(version.FileVersion),
            CompanyName = Clean(version.CompanyName),
            OriginalFilename = Clean(version.OriginalFilename),
            InternalName = Clean(version.InternalName),
            Copyright = Clean(version.LegalCopyright),
            FileSizeBytes = info.Length,
            LastWriteTime = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
        };
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim().TrimEnd('\0').Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
