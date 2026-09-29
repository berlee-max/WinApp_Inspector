using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinAppInspector.UI.Services;

namespace WinAppInspector.UI.ViewModels;

/// <summary>§26 settings dialog. Edits a copy; Save writes it back and persists.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;

    public SettingsViewModel(SettingsService settings)
    {
        _settings = settings;
        Draft = settings.Current.Clone();
    }

    public AppSettings Draft { get; }

    /// <summary>True after Save; the caller decides whether a rescan is needed.</summary>
    public bool Saved { get; private set; }

    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void Save()
    {
        _settings.Current.CopyFrom(Draft);
        _settings.Save();
        Saved = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
