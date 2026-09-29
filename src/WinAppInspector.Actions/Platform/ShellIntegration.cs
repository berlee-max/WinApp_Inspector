using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace WinAppInspector.Actions.Platform;

/// <summary>Explorer context-menu entry "使用 WinApp Inspector 分析" for folders, .exe and .dll files (§19). Registered per user under HKCU, no elevation needed.</summary>
public interface IShellIntegration
{
    bool IsRegistered { get; }

    /// <summary>Registers the verb pointing at <paramref name="executablePath"/> with <paramref name="menuText"/>.</summary>
    (bool Succeeded, string? Error) Register(string executablePath, string menuText);

    (bool Succeeded, string? Error) Unregister();
}

public sealed class ShellIntegration : IShellIntegration
{
    /// <summary>Command-line switch the app handles on startup: <c>WinAppInspector.exe --analyze "&lt;path&gt;"</c>.</summary>
    public const string AnalyzeSwitch = "--analyze";

    private const string VerbName = "WinAppInspector.Analyze";

    private static readonly string[] ShellRoots =
    [
        @"Software\Classes\Directory\shell\" + VerbName,
        @"Software\Classes\SystemFileAssociations\.exe\shell\" + VerbName,
        @"Software\Classes\SystemFileAssociations\.dll\shell\" + VerbName,
    ];

    private readonly ILogger<ShellIntegration> _logger;

    public ShellIntegration(ILogger<ShellIntegration> logger)
    {
        _logger = logger;
    }

    public bool IsRegistered
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(ShellRoots[0]);
                return key is not null;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return false;
            }
        }
    }

    public (bool Succeeded, string? Error) Register(string executablePath, string menuText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(menuText);

        try
        {
            foreach (var root in ShellRoots)
            {
                using var verb = Registry.CurrentUser.CreateSubKey(root, writable: true);
                verb.SetValue(string.Empty, menuText);
                verb.SetValue("Icon", $"\"{executablePath}\",0");
                using var command = verb.CreateSubKey("command", writable: true);
                command.SetValue(string.Empty, $"\"{executablePath}\" {AnalyzeSwitch} \"%1\"");
            }

            _logger.LogInformation("Explorer context menu registered for {Exe}", executablePath);
            return (true, null);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning(ex, "Explorer context menu could not be registered");
            return (false, ex.Message);
        }
    }

    public (bool Succeeded, string? Error) Unregister()
    {
        try
        {
            foreach (var root in ShellRoots)
            {
                Registry.CurrentUser.DeleteSubKeyTree(root, throwOnMissingSubKey: false);
            }

            return (true, null);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning(ex, "Explorer context menu could not be removed");
            return (false, ex.Message);
        }
    }
}
