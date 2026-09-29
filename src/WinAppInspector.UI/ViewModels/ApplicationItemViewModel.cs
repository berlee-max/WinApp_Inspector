using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;
using WinAppInspector.UI.Localization;
using WinAppInspector.UI.Services;

namespace WinAppInspector.UI.ViewModels;

/// <summary>Where the application came from, as the user thinks of it: the Store, an installer, or a folder.</summary>
public enum SourceKind
{
    Store = 0,
    Installed = 1,
    Portable = 2,
    Residue = 3,
    Other = 4,
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

    /// <summary>§22: no official uninstaller, but a type the user may remove by hand.</summary>
    public bool CanRemoveManually => !Entity.HasOfficialUninstaller && Entity.AppType.IsEligibleForManualRemoval();

    /// <summary>True when the bulk action button can do anything with this item.</summary>
    public bool IsActionable => HasOfficialUninstaller || CanRemoveManually;

    public SourceKind Source => Entity.Packages.Count > 0 ? SourceKind.Store
        : Entity.RegistryEntries.Count > 0 ? SourceKind.Installed
        : Entity.AppType == AppType.SuspectedResidue ? SourceKind.Residue
        : Entity.AppType is AppType.Portable or AppType.UserLevel ? SourceKind.Portable
        : SourceKind.Other;

    public string SourceText => Localize.Get("Source." + Source);

    /// <summary>Publisher group for the sidebar: Microsoft is one group, every other publisher its own.</summary>
    public string VendorKey => Entity.IsMicrosoft ? "Microsoft" : Entity.Publisher is { Length: > 0 } p ? p : Localize.Get("Value.Unknown");

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

    /// <summary>One-line secondary text under the name: publisher, and the status when it is not simply "normal".</summary>
    public string SubtitleText => Entity.IsRunning || Entity.AppType is AppType.SuspectedResidue or AppType.Undetermined
        ? $"{Publisher} · {StatusText}"
        : Publisher;

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

    public bool HasIcon => Icon is not null;

    public void Update(ApplicationEntity entity)
    {
        Entity = entity;
        OnPropertyChanged(string.Empty);
    }
}
