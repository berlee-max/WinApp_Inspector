using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinAppInspector.Actions.Platform;
using WinAppInspector.UI.Localization;
using WinAppInspector.UI.Services;

namespace WinAppInspector.UI.ViewModels;

/// <summary>§26 settings dialog. Edits a copy; Save writes it back and persists.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly IShellIntegration _shell;

    public SettingsViewModel(SettingsService settings, IShellIntegration shell)
    {
        _settings = settings;
        _shell = shell;
        Draft = settings.Current.Clone();
        Draft.ExplorerContextMenu = shell.IsRegistered;
    }

    /// <summary>Set when the context-menu registration failed; shown by the window.</summary>
    public string? ShellError { get; private set; }

    public AppSettings Draft { get; }

    /// <summary>True after Save; the caller decides whether a rescan is needed.</summary>
    public bool Saved { get; private set; }

    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void Save()
    {
        if (Draft.ExplorerContextMenu != _shell.IsRegistered)
        {
            var (ok, error) = Draft.ExplorerContextMenu
                ? _shell.Register(Environment.ProcessPath ?? System.IO.Path.Combine(AppContext.BaseDirectory, "WinAppInspector.exe"), Localize.Get("Shell.MenuText"))
                : _shell.Unregister();
            if (!ok)
            {
                ShellError = error;
                Draft.ExplorerContextMenu = _shell.IsRegistered;
            }
        }

        _settings.Current.CopyFrom(Draft);
        _settings.Save();
        Saved = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
