using WinAppInspector.Core.Models;

namespace WinAppInspector.Core.Scanning;

/// <summary>User-configurable scan scope and behaviour (§26).</summary>
public sealed record ScanOptions
{
    public static ScanOptions Default { get; } = new();

    /// <summary>§26.1: which fixed roots the directory scanner visits.</summary>
    public IReadOnlySet<ScanRoot> DirectoryRoots { get; init; } = new HashSet<ScanRoot>
    {
        ScanRoot.ProgramFiles,
        ScanRoot.ProgramFilesX86,
        ScanRoot.ProgramData,
        ScanRoot.LocalAppData,
        ScanRoot.RoamingAppData,
        ScanRoot.LocalLowAppData,
    };

    /// <summary>§26.2 扫描数字签名.</summary>
    public bool ReadSignatures { get; init; } = true;

    /// <summary>§26.2 扫描服务.</summary>
    public bool ScanServices { get; init; } = true;

    /// <summary>§26.2 扫描计划任务.</summary>
    public bool ScanScheduledTasks { get; init; } = true;

    public bool ScanStartupItems { get; init; } = true;

    public bool ScanProcesses { get; init; } = true;

    /// <summary>§26.2 隐藏系统组件 (a display filter, not a scan filter).</summary>
    public bool HideSystemComponents { get; init; } = true;

    /// <summary>
    /// How deep the directory scanner looks for executables below a discovered application directory:
    /// 1 = only the directory itself, 2 = also its immediate sub-directories (Squirrel <c>app-x.y.z</c>, <c>bin</c>).
    /// Discovery never recurses further; sizes are computed in a later background pass (§32).
    /// </summary>
    public int ExecutableSearchDepth { get; init; } = 2;

    /// <summary>Upper bound on executables recorded per directory to keep the entity small.</summary>
    public int MaxExecutablesPerDirectory { get; init; } = 40;
}

/// <summary>Second-stage size computation (§32). Runs in the background after discovery.</summary>
public interface IDirectorySizeCalculator
{
    Task<DirectorySize> ComputeAsync(string path, CancellationToken cancellationToken);
}

public sealed record DirectorySize(long Bytes, int FileCount, bool Complete, string? Error = null);

/// <summary>Resolves a Windows shortcut (.lnk) to its target (§7.9).</summary>
public interface IShortcutResolver
{
    /// <summary>Returns the target path and arguments, or <c>null</c> when the file cannot be resolved.</summary>
    ShortcutTarget? Resolve(string shortcutPath);
}

public sealed record ShortcutTarget(string TargetPath, string? Arguments, string? WorkingDirectory);
