using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinAppInspector.Actions.Platform;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Rules;
using WinAppInspector.UI.Localization;
using WinAppInspector.UI.Services;

namespace WinAppInspector.UI.ViewModels;

/// <summary>§26 settings dialog. Edits a copy; Save writes it back and persists.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly IShellIntegration _shell;
    private readonly ProtectedPathRule _protectedPaths;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCustomDirectoryCommand))]
    private string? _selectedCustomDirectory;

    [ObservableProperty]
    private string? _customDirectoryError;

    public SettingsViewModel(SettingsService settings, IShellIntegration shell, ProtectedPathRule protectedPaths)
    {
        _settings = settings;
        _shell = shell;
        _protectedPaths = protectedPaths;
        Draft = settings.Current.Clone();
        Draft.ExplorerContextMenu = shell.IsRegistered;
        CustomDirectories = new ObservableCollection<string>(Draft.CustomScanDirectories);
    }

    /// <summary>Set when the context-menu registration failed; shown by the window.</summary>
    public string? ShellError { get; private set; }

    public AppSettings Draft { get; }

    /// <summary>§26.1 user-added scan folders, edited here and written back to <see cref="Draft"/> on Save.</summary>
    public ObservableCollection<string> CustomDirectories { get; }

    /// <summary>True after Save; the caller decides whether a rescan is needed.</summary>
    public bool Saved { get; private set; }

    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void AddCustomDirectory()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Localize.Get("Settings.AddDirectory") };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        TryAddCustomDirectory(dialog.FolderName);
    }

    /// <summary>Validates and adds a folder; returns false (with <see cref="CustomDirectoryError"/> set) when it is refused.</summary>
    public bool TryAddCustomDirectory(string? path)
    {
        CustomDirectoryError = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = WindowsPath.Normalize(path);
        var protection = _protectedPaths.Check(normalized);
        if (protection.Kind is PathProtectionKind.DriveRoot or PathProtectionKind.WindowsDirectory or PathProtectionKind.WindowsApps or PathProtectionKind.UserProfileRoot or PathProtectionKind.NotAbsolute)
        {
            // §11: system locations, whole drives and user profiles (Desktop, Downloads, other accounts) are never scanned as a portable-software folder.
            CustomDirectoryError = Localize.Format("Settings.DirectoryRefusedFormat", normalized, Localize.Get("Protection." + protection.Kind));
            return false;
        }

        if (protection.Kind == PathProtectionKind.ScanRoot)
        {
            CustomDirectoryError = Localize.Format("Settings.DirectoryAlreadyScannedFormat", normalized);
            return false;
        }

        if (CustomDirectories.Any(d => WindowsPath.AreEqual(d, normalized)))
        {
            return true;
        }

        CustomDirectories.Add(normalized);
        SelectedCustomDirectory = normalized;
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanRemoveCustomDirectory))]
    private void RemoveCustomDirectory()
    {
        if (SelectedCustomDirectory is { } selected)
        {
            CustomDirectories.Remove(selected);
            SelectedCustomDirectory = null;
        }
    }

    private bool CanRemoveCustomDirectory() => SelectedCustomDirectory is not null;

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

        Draft.CustomScanDirectories = [.. CustomDirectories];
        _settings.Current.CopyFrom(Draft);
        _settings.Save();
        Saved = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
