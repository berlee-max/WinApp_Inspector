using System.Windows;
using WinAppInspector.UI.ViewModels;

namespace WinAppInspector.UI.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) =>
        {
            if (viewModel.ShellError is { } error)
            {
                MessageBox.Show(this, Localization.Localize.Get("Shell.RegisterFailed") + Environment.NewLine + error, Localization.Localize.Get("Settings.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            Close();
        };
    }
}
