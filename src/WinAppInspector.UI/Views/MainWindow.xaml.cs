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

        // Populate the overview on first launch; the scan runs off the UI thread and reports progress (§31).
        Loaded += (_, _) =>
        {
            if (viewModel.RescanCommand.CanExecute(null))
            {
                viewModel.RescanCommand.Execute(null);
            }
        };
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
