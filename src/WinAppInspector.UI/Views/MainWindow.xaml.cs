using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WinAppInspector.UI.ViewModels;

namespace WinAppInspector.UI.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IServiceProvider _services;

    public MainWindow(MainViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _services = services;
        DataContext = viewModel;

        viewModel.SettingsRequested += (_, _) => ShowSettings();
        viewModel.AboutRequested += (_, _) => new AboutWindow { Owner = this }.ShowDialog();

        // §33: show the previous results right away; scanning starts when the user presses the button.
        Loaded += async (_, _) => await viewModel.LoadCacheAsync();
    }

    private void ShowSettings()
    {
        var settingsViewModel = _services.GetRequiredService<SettingsViewModel>();
        var dialog = new SettingsWindow(settingsViewModel) { Owner = this };
        dialog.ShowDialog();
        if (settingsViewModel.Saved)
        {
            _viewModel.SettingsSaved();
        }
    }
}
