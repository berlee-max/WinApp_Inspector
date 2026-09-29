using System.Reflection;
using System.Windows;
using WinAppInspector.UI.Localization;

namespace WinAppInspector.UI.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = Localize.Format("App.VersionFormat", ProductVersion);
    }

    /// <summary>The version the build was published with (release.yml passes -p:Version), without the source-revision suffix.</summary>
    public static string ProductVersion
    {
        get
        {
            var assembly = typeof(AboutWindow).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
            {
                var plus = informational.IndexOf('+', StringComparison.Ordinal);
                return plus > 0 ? informational[..plus] : informational;
            }

            return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
