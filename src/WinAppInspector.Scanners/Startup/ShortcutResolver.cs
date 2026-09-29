using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using WinAppInspector.Core.Scanning;

namespace WinAppInspector.Scanners.Startup;

/// <summary>Resolves .lnk files through the Windows Script Host shell object (§7.9). No extra interop assemblies needed.</summary>
public sealed class ShortcutResolver : IShortcutResolver
{
    private readonly ILogger<ShortcutResolver> _logger;

    public ShortcutResolver(ILogger<ShortcutResolver> logger)
    {
        _logger = logger;
    }

    public ShortcutTarget? Resolve(string shortcutPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutPath);
        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell", throwOnError: false);
            if (shellType is null)
            {
                return null;
            }

            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return null;
            }

            shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, [shortcutPath], System.Globalization.CultureInfo.InvariantCulture);
            if (shortcut is null)
            {
                return null;
            }

            var target = GetProperty(shortcut, "TargetPath");
            if (string.IsNullOrWhiteSpace(target))
            {
                return null;
            }

            return new ShortcutTarget(target, NullIfEmpty(GetProperty(shortcut, "Arguments")), NullIfEmpty(GetProperty(shortcut, "WorkingDirectory")));
        }
        catch (Exception ex) when (ex is COMException or System.Reflection.TargetInvocationException or MissingMethodException or InvalidCastException)
        {
            _logger.LogDebug(ex, "Shortcut {Path} could not be resolved", shortcutPath);
            return null;
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                Marshal.FinalReleaseComObject(shortcut);
            }

            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static string? GetProperty(object comObject, string name) =>
        comObject.GetType().InvokeMember(name, System.Reflection.BindingFlags.GetProperty, null, comObject, null, System.Globalization.CultureInfo.InvariantCulture) as string;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
