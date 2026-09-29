using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace WinAppInspector.UI.Converters;

/// <summary>§30: maps a status tone key to one of the subdued theme brushes. Colour is auxiliary, the text carries the meaning.</summary>
public sealed class StatusToneBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = (value as string) switch
        {
            "Running" => "Brush.StatusRunning",
            "Reminder" => "Brush.StatusReminder",
            "Attention" => "Brush.StatusAttention",
            "Muted" => "Brush.TextDisabled",
            _ => "Brush.TextPrimary",
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Black;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
