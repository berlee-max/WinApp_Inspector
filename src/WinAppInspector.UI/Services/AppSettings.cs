using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Scanning;

namespace WinAppInspector.UI.Services;

/// <summary>User settings (§26). Persisted as JSON next to the logs; every field has a safe default.</summary>
public sealed class AppSettings
{
    // §26.1 scan scope
    public bool ScanProgramFiles { get; set; } = true;
    public bool ScanProgramFilesX86 { get; set; } = true;
    public bool ScanProgramData { get; set; } = true;
    public bool ScanLocalAppData { get; set; } = true;
    public bool ScanRoamingAppData { get; set; } = true;
    public bool ScanLocalLowAppData { get; set; } = true;

    // §26.2 defaults
    public bool RecycleBinFirst { get; set; } = true;
    public bool CreateRestorePointBeforeUninstall { get; set; }
    public bool HideSystemComponents { get; set; } = true;
    public bool ReadSignatures { get; set; } = true;
    public bool ScanScheduledTasks { get; set; } = true;
    public bool ScanServices { get; set; } = true;

    /// <summary>§19: Explorer context menu registered for the current user.</summary>
    public bool ExplorerContextMenu { get; set; }

    public ScanOptions ToScanOptions()
    {
        var roots = new HashSet<ScanRoot>();
        if (ScanProgramFiles) roots.Add(ScanRoot.ProgramFiles);
        if (ScanProgramFilesX86) roots.Add(ScanRoot.ProgramFilesX86);
        if (ScanProgramData) roots.Add(ScanRoot.ProgramData);
        if (ScanLocalAppData) roots.Add(ScanRoot.LocalAppData);
        if (ScanRoamingAppData) roots.Add(ScanRoot.RoamingAppData);
        if (ScanLocalLowAppData) roots.Add(ScanRoot.LocalLowAppData);

        return ScanOptions.Default with
        {
            DirectoryRoots = roots,
            ReadSignatures = ReadSignatures,
            ScanServices = ScanServices,
            ScanScheduledTasks = ScanScheduledTasks,
            HideSystemComponents = HideSystemComponents,
        };
    }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    public void CopyFrom(AppSettings other)
    {
        ArgumentNullException.ThrowIfNull(other);
        foreach (var property in typeof(AppSettings).GetProperties())
        {
            if (property.CanWrite)
            {
                property.SetValue(this, property.GetValue(other));
            }
        }
    }
}

/// <summary>Loads and saves <see cref="AppSettings"/>. Failures are logged with their cause and never crash the app.</summary>
public sealed class SettingsService : IScanOptionsProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly string _path;
    private readonly ILogger<SettingsService> _logger;

    public SettingsService(string dataDirectory, ILogger<SettingsService> logger)
    {
        _path = Path.Combine(dataDirectory, "settings.json");
        _logger = logger;
        Current = Load();
    }

    public AppSettings Current { get; }

    ScanOptions IScanOptionsProvider.Current => Current.ToScanOptions();

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Settings could not be saved to {Path}", _path);
        }
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Settings at {Path} could not be read; defaults are used", _path);
        }

        return new AppSettings();
    }
}
