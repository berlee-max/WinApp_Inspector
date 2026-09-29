using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace WinAppInspector.UI.ViewModels;

/// <summary>The three top-level pages (§12).</summary>
public enum MainPage
{
    Overview = 0,
    Scan = 1,
    Uninstall = 2,
}

/// <summary>Shell view model: navigation and the top-right actions. Page content arrives in later phases.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly ILogger<MainViewModel> _logger;

    [ObservableProperty]
    private MainPage _currentPage = MainPage.Overview;

    [ObservableProperty]
    private bool _isScanning;

    public MainViewModel(ILogger<MainViewModel> logger)
    {
        _logger = logger;
    }

    public bool IsOverviewSelected
    {
        get => CurrentPage == MainPage.Overview;
        set => SelectPage(MainPage.Overview, value);
    }

    public bool IsScanSelected
    {
        get => CurrentPage == MainPage.Scan;
        set => SelectPage(MainPage.Scan, value);
    }

    public bool IsUninstallSelected
    {
        get => CurrentPage == MainPage.Uninstall;
        set => SelectPage(MainPage.Uninstall, value);
    }

    [RelayCommand(CanExecute = nameof(CanRescan))]
    private Task RescanAsync()
    {
        // The scan pipeline is wired in phase 2 / 4.
        _logger.LogInformation("Rescan requested");
        return Task.CompletedTask;
    }

    private bool CanRescan() => !IsScanning;

    partial void OnIsScanningChanged(bool value) => RescanCommand.NotifyCanExecuteChanged();

    partial void OnCurrentPageChanged(MainPage value)
    {
        OnPropertyChanged(nameof(IsOverviewSelected));
        OnPropertyChanged(nameof(IsScanSelected));
        OnPropertyChanged(nameof(IsUninstallSelected));
    }

    private void SelectPage(MainPage page, bool selected)
    {
        if (selected)
        {
            CurrentPage = page;
        }
    }
}
