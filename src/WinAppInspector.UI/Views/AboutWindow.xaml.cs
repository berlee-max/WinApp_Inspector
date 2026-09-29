using System.Windows;

namespace WinAppInspector.UI.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
