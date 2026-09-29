using System.Globalization;
using System.Windows;
using System.Windows.Data;
using WinAppInspector.Core.Evidence;
using WinAppInspector.Core.Models;
using WinAppInspector.UI.Localization;

namespace WinAppInspector.UI.Converters;

/// <summary>Enum → localized text. ConverterParameter is ignored; the enum type selects the resource prefix.</summary>
[ValueConversion(typeof(Enum), typeof(string))]
public sealed class EnumTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        AppType t => Localize.AppType(t),
        RiskLevel r => Localize.RiskLevel(r),
        ConfidenceLevel c => Localize.Confidence(c),
        SignatureStatus s => Localize.Signature(s),
        UninstallMethod u => Localize.UninstallMethod(u),
        null => string.Empty,
        _ => value.ToString(),
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

[ValueConversion(typeof(long?), typeof(string))]
public sealed class BytesConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Localize.Bytes(value as long?);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>null / empty string / empty collection / false → Collapsed.</summary>
public sealed class EmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value switch
        {
            null => false,
            string s => s.Length > 0,
            bool b => b,
            int i => i > 0,
            System.Collections.ICollection c => c.Count > 0,
            _ => true,
        };
        if (parameter is string p && p.Equals("invert", StringComparison.OrdinalIgnoreCase))
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

[ValueConversion(typeof(Reason), typeof(string))]
public sealed class ReasonTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is Reason r ? Localize.Reason(r) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

[ValueConversion(typeof(Reason), typeof(string))]
public sealed class ReasonMarkConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is Reason { IsPositive: false } ? "✗" : "✓";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

[ValueConversion(typeof(EvidenceItem), typeof(string))]
public sealed class EvidenceTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is EvidenceItem e ? Localize.Evidence(e) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

[ValueConversion(typeof(DirectoryRole), typeof(string))]
public sealed class DirectoryRoleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is DirectoryRole r ? Localize.Get("DirectoryRole." + r) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Visible only when every bound value is true (used to show the detail panel in the manager only).</summary>
public sealed class AllTrueToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.All(v => v is true) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
