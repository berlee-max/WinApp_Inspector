using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;

namespace WinAppInspector.UI.Services;

/// <summary>Extracts application icons from executables (§13.2). Results are frozen so they can be created off the UI thread.</summary>
public sealed class IconService
{
    private readonly ConcurrentDictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<IconService> _logger;

    public IconService(ILogger<IconService> logger)
    {
        _logger = logger;
    }

    public ImageSource? GetIcon(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        return _cache.GetOrAdd(executablePath, Extract);
    }

    private ImageSource? Extract(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
            source.Freeze();
            return source;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
        {
            _logger.LogDebug(ex, "Icon of {Path} could not be extracted", path);
            return null;
        }
    }
}
