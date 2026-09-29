using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Parsing;

namespace WinAppInspector.UI.Services;

/// <summary>
/// Application icons (§13.2): the Start-menu logo for packaged apps, the registry <c>DisplayIcon</c> (with its index) for
/// installed programs, and the executable's own icon otherwise. Results are frozen and cached, so they can be produced
/// on a pool thread and shown from the UI thread.
/// </summary>
public sealed class IconService
{
    private const int IconSize = 64;

    private readonly ConcurrentDictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<IconService> _logger;

    public IconService(ILogger<IconService> logger)
    {
        _logger = logger;
    }

    /// <summary>Resolves the icon for an application; null when none of its sources yields one.</summary>
    public ImageSource? GetIcon(ApplicationEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var appUserModelId = entity.Packages.Select(p => p.AppUserModelId).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
        if (appUserModelId is not null)
        {
            var logo = _cache.GetOrAdd("aumid:" + appUserModelId, _ => Try(() => ShellImage.Load(@"shell:AppsFolder\" + appUserModelId, IconSize, plated: true), appUserModelId));
            if (logo is not null)
            {
                return logo;
            }
        }

        if (!string.IsNullOrWhiteSpace(entity.IconPath))
        {
            var icon = _cache.GetOrAdd("icon:" + entity.IconPath, _ => LoadIconPath(entity.IconPath));
            if (icon is not null)
            {
                return icon;
            }
        }

        return string.IsNullOrWhiteSpace(entity.MainExecutable) ? null : _cache.GetOrAdd("file:" + entity.MainExecutable, _ => LoadFile(entity.MainExecutable));
    }

    /// <summary>A registry DisplayIcon: <c>"C:\App\app.exe",0</c>, <c>C:\App\app.ico</c> or <c>C:\App\res.dll,-101</c>.</summary>
    private ImageSource? LoadIconPath(string value)
    {
        var (file, index) = SplitIconIndex(value);
        if (file is null || !File.Exists(file))
        {
            return null;
        }

        var isExecutableResource = file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        if (isExecutableResource && index != 0)
        {
            return Try(() => ShellImage.LoadIndexedIcon(file, index, IconSize), value) ?? LoadFile(file);
        }

        return LoadFile(file);
    }

    private ImageSource? LoadFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return Try(() => ShellImage.Load(path, IconSize, plated: false), path)
            ?? Try(() => ShellImage.LoadIndexedIcon(path, 0, IconSize), path);
    }

    private ImageSource? Try(Func<ImageSource?> load, string subject)
    {
        try
        {
            return load();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception
                                       or System.Runtime.InteropServices.COMException or FileNotFoundException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogDebug(ex, "Icon of {Subject} could not be loaded", subject);
            return null;
        }
    }

    /// <summary>Splits <c>path,index</c>; quotes are removed and a trailing index is parsed when present.</summary>
    internal static (string? File, int Index) SplitIconIndex(string value)
    {
        var trimmed = value.Trim();
        var index = 0;
        var comma = trimmed.LastIndexOf(',');
        if (comma > 0 && int.TryParse(trimmed[(comma + 1)..].Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            index = parsed;
            trimmed = trimmed[..comma];
        }

        var file = CommandLine.ExtractExecutable(trimmed) ?? trimmed.Trim('"');
        return (string.IsNullOrWhiteSpace(file) ? null : file, index);
    }
}
