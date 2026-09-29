using System.Windows;
using System.Windows.Controls;

namespace WinAppInspector.UI.Views;

public partial class HeroEmblem : UserControl
{
    public static readonly DependencyProperty IsScanningProperty =
        DependencyProperty.Register(nameof(IsScanning), typeof(bool), typeof(HeroEmblem), new PropertyMetadata(false));

    public HeroEmblem()
    {
        InitializeComponent();
    }

    /// <summary>Shows the scan ring over the particles.</summary>
    public bool IsScanning
    {
        get => (bool)GetValue(IsScanningProperty);
        set => SetValue(IsScanningProperty, value);
    }
}
