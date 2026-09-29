using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using WinAppInspector.UI.ViewModels;

namespace WinAppInspector.UI.Views;

public partial class OverviewView : UserControl
{
    public OverviewView()
    {
        InitializeComponent();
    }

    private void HeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is GridViewColumnHeader { Tag: string property } && DataContext is OverviewViewModel vm)
        {
            vm.SortBy(property);
        }
    }

    private void FilterChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: AppFilter filter } && DataContext is OverviewViewModel vm)
        {
            vm.Filter = filter;
        }
    }
}
