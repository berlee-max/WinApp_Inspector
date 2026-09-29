using System.Windows;
using WinAppInspector.UI.ViewModels;

namespace WinAppInspector.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
