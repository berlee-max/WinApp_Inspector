using System.Windows;
using WinAppInspector.UI.ViewModels;

namespace WinAppInspector.UI.Views;

public partial class CleanupWindow : Window
{
    public CleanupWindow(CleanupViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ExecuteClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
