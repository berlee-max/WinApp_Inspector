using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;
using WinAppInspector.UI.Localization;
using WinAppInspector.UI.Services;

namespace WinAppInspector.UI.ViewModels;

/// <summary>How the application was installed, as the user thinks of it: the Store, an MSI package, an EXE installer, or none of these.</summary>
public enum SourceKind
{
    Store = 0,
    Msi = 1,
    Exe = 2,
    Other = 3,
}

/// <summary>Row model for an <see cref="ApplicationEntity"/>. The entity is replaced when sizes or scans update it.</summary>
public sealed partial class ApplicationItemViewModel : ObservableObject
{
    private readonly IconService _icons;
    private ImageSource? _icon;
    private bool _iconLoaded;

    /// <summary>Ticked in the app manager for a bulk uninstall / removal.</summary>
    [ObservableProperty]
    private bool _isChecked;

    public ApplicationItemViewModel(ApplicationEntity entity, IconService icons)
    {
        Entity = entity;
        _icons = icons;
    }

    public ApplicationEntity Entity { get; private set; }

    public string Id => Entity.Id;
    public string Name => Entity.Name;
    public string Publisher => Entity.Publisher ?? Localize.Get("Value.Unknown");
    public string Version => Entity.Version ?? string.Empty;
    public AppType AppType => Entity.AppType;
    public string TypeText => Localize.AppType(Entity.AppType);

    /// <summary>The install folder, or the folder the program runs from when it was found only as a process.</summary>
    public string InstallLocation => Entity.InstallLocation
        ?? (Entity.Directories.Count > 0 ? Entity.Directories[0].Path : WindowsPath.GetDirectoryName(Entity.MainExecutable) ?? string.Empty);

    public string FolderName => WindowsPath.GetFileName(InstallLocation);
    public long? DiskUsageBytes => Entity.DiskUsageBytes ?? Entity.EstimatedSizeBytes;

    /// <summary>Sort key: unknown sizes sort last.</summary>
    public long SizeSortKey => DiskUsageBytes ?? -1;

    public string DiskUsageText => Entity.DiskUsageBytes is not null
        ? Localize.Bytes(Entity.DiskUsageBytes)
        : Entity.EstimatedSizeBytes is not null ? Localize.Bytes(Entity.EstimatedSizeBytes) + " *" : Localize.Get("Size.Pending");

    /// <summary>Install date from the registry, otherwise the newest directory change; empty when neither is known.</summary>
    public DateTime? InstallDate => Entity.InstallDate?.ToDateTime(TimeOnly.MinValue) ?? Entity.LastModified?.ToLocalTime().DateTime;

    public string InstallDateText => InstallDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    public bool IsRunning => Entity.IsRunning;
    public bool IsMicrosoft => Entity.IsMicrosoft;
    public bool IsSystemLike => Entity.AppType.IsSystemLike();
    public bool HasOfficialUninstaller => Entity.HasOfficialUninstaller;

    /// <summary>§9.7–9.9: system components, drivers and shared runtimes are kept; no removal entry point at all.</summary>
    public bool IsProtected => Entity.AppType.IsProtectedByDefault();

    /// <summary>
    /// A folder analysed ad hoc (§18 / Explorer context menu) rather than found by the scan. It is shown for information;
    /// the folder the user pointed at (Desktop, Downloads, a USB stick) is never offered for removal from here.
    /// </summary>
    public bool IsAdHoc { get; init; }

    /// <summary>
    /// §22: no official uninstaller, but a type the user may remove by hand, and something of its own to remove. A program
    /// known only as a running process (no directory, no install location, no registry entry) has nothing to offer.
    /// </summary>
    public bool CanRemoveManually => !Entity.HasOfficialUninstaller && Entity.AppType.IsEligibleForManualRemoval()
        && (Entity.Directories.Count > 0 || Entity.InstallLocation is not null || Entity.RegistryEntries.Count > 0);

    /// <summary>True when the uninstall / remove action may run for this item.</summary>
    public bool IsActionable => !IsProtected && !IsAdHoc && (HasOfficialUninstaller || CanRemoveManually);

    public SourceKind Source => Entity.Packages.Count > 0 ? SourceKind.Store
        : Entity.RegistryEntries.Count == 0 ? SourceKind.Other
        : Entity.MsiProductCode is not null || Entity.PreferredUninstallMethod == UninstallMethod.Msi ? SourceKind.Msi
        : SourceKind.Exe;

    public string SourceText => Localize.Get("Source." + Source);

    /// <summary>
    /// A portable program that was discovered only because its folder is under a default scan root, is not running and is
    /// referenced by nothing. Such folders are noise in the main list (the user asked to see running tools, not every folder).
    /// </summary>
    public bool IsIdlePortable => Entity.AppType == AppType.Portable
        && !Entity.IsRunning
        && !Entity.Sources.HasFlag(DiscoverySource.Process)
        && Entity.StartupItems.Count == 0 && Entity.Services.Count == 0 && Entity.ScheduledTasks.Count == 0
        && Entity.Directories.All(d => d.Root != ScanRoot.Custom);

    /// <summary>状态: 运行中 / 残留 / 待判断 / 正常.</summary>
    public string StatusText => Entity.IsRunning
        ? Localize.Get("Status.Running")
        : Entity.AppType switch
        {
            AppType.SuspectedResidue => Localize.Get("Status.Residue"),
            AppType.Undetermined => Localize.Get("Status.Undetermined"),
            _ => Localize.Get("Status.Normal"),
        };

    /// <summary>§30: which subdued status colour applies.</summary>
    public string StatusTone => Entity.IsRunning ? "Running" : Entity.AppType switch
    {
        AppType.SuspectedResidue => "Reminder",
        AppType.Undetermined => "Attention",
        AppType.SystemComponent or AppType.SharedRuntime or AppType.HardwareOrDriver => "Muted",
        _ => "Normal",
    };

    /// <summary>One-line secondary text under the name: publisher · type, plus the status when it matters.</summary>
    public string SubtitleText => Entity.IsRunning || Entity.AppType is AppType.SuspectedResidue or AppType.Undetermined
        ? $"{Publisher} · {TypeText} · {StatusText}"
        : $"{Publisher} · {TypeText}";

    public ImageSource? Icon
    {
        get
        {
            if (!_iconLoaded)
            {
                _iconLoaded = true;
                _icon = _icons.GetIcon(Entity.MainExecutable ?? Core.Parsing.CommandLine.ExtractExecutable(Entity.IconPath));
            }

            return _icon;
        }
    }

    /// <summary>First letter shown when no icon can be extracted.</summary>
    public string Initial => Entity.Name.Length > 0 ? Entity.Name[..1].ToUpperInvariant() : "?";

    private static readonly (Color From, Color To)[] TilePalette =
    [
        (Color.FromRgb(0x13, 0xBC, 0xD9), Color.FromRgb(0x0B, 0x68, 0xD1)),
        (Color.FromRgb(0x33, 0xA9, 0xEF), Color.FromRgb(0x04, 0x7D, 0xCC)),
        (Color.FromRgb(0x16, 0xBF, 0x62), Color.FromRgb(0x0B, 0x9B, 0x4B)),
        (Color.FromRgb(0xF0, 0x50, 0x33), Color.FromRgb(0xC9, 0x35, 0x20)),
        (Color.FromRgb(0x7C, 0x5C, 0xE8), Color.FromRgb(0x4C, 0x2F, 0xB5)),
        (Color.FromRgb(0xF2, 0x9A, 0x1F), Color.FromRgb(0xD1, 0x6B, 0x0B)),
        (Color.FromRgb(0x44, 0x4C, 0x5A), Color.FromRgb(0x25, 0x2A, 0x33)),
        (Color.FromRgb(0xE0, 0x4E, 0x8C), Color.FromRgb(0xAF, 0x2A, 0x6A)),
    ];

    /// <summary>A stable gradient for the fallback tile, chosen from the name so the same app always gets the same colour.</summary>
    public Brush IconBrush
    {
        get
        {
            var hash = 0;
            foreach (var c in Entity.Name)
            {
                hash = unchecked(hash * 31 + c);
            }

            var (from, to) = TilePalette[Math.Abs(hash) % TilePalette.Length];
            var brush = new LinearGradientBrush(from, to, 45);
            brush.Freeze();
            return brush;
        }
    }

    public bool HasIcon => Icon is not null;

    public void Update(ApplicationEntity entity)
    {
        Entity = entity;
        OnPropertyChanged(string.Empty);
    }
}
