using System.Windows;
using WinAppInspector.Core.Logging;

namespace WinAppInspector.UI.Views;

public partial class OperationLogWindow : Window
{
    public OperationLogWindow(IOperationLog log)
    {
        InitializeComponent();
        Loaded += async (_, _) => Entries.ItemsSource = await log.ReadAsync(500);
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
