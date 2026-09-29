using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;
using WinAppInspector.UI.Localization;
using WinAppInspector.UI.Services;

namespace WinAppInspector.UI.ViewModels;

/// <summary>Row model for an <see cref="ApplicationEntity"/> (§13.2). The entity is replaced when sizes or scans update it.</summary>
public sealed partial class ApplicationItemViewModel : ObservableObject
{
    private readonly IconService _icons;
    private ImageSource? _icon;
    private bool _iconLoaded;

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
    public string InstallLocation => Entity.InstallLocation ?? (Entity.Directories.Count > 0 ? Entity.Directories[0].Path : string.Empty);
    public string FolderName => WindowsPath.GetFileName(InstallLocation);
    public long? DiskUsageBytes => Entity.DiskUsageBytes ?? Entity.EstimatedSizeBytes;
    public string DiskUsageText => Entity.DiskUsageBytes is not null
        ? Localize.Bytes(Entity.DiskUsageBytes)
        : Entity.EstimatedSizeBytes is not null ? Localize.Bytes(Entity.EstimatedSizeBytes) + " *" : Localize.Get("Size.Pending");
    public bool IsRunning => Entity.IsRunning;
    public bool IsMicrosoft => Entity.IsMicrosoft;
    public bool IsSystemLike => Entity.AppType.IsSystemLike();
    public bool HasOfficialUninstaller => Entity.HasOfficialUninstaller;

    /// <summary>§13.2 状态: 运行中 / 残留 / 待判断 / 正常.</summary>
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

    public void Update(ApplicationEntity entity)
    {
        Entity = entity;
        OnPropertyChanged(string.Empty);
    }
}
