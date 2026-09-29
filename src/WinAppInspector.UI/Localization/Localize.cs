using System.Globalization;
using System.Windows;
using WinAppInspector.Core.Evidence;
using WinAppInspector.Core.Models;

namespace WinAppInspector.UI.Localization;

/// <summary>Looks up user-facing text in the merged resource dictionaries (Resources/Strings.zh-CN.xaml). Falls back to the key.</summary>
public static class Localize
{
    public static string Get(string key)
    {
        if (Application.Current?.TryFindResource(key) is string text)
        {
            return text;
        }

        return key;
    }

    public static string Format(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, Get(key), args);

    public static string AppType(AppType type) => Get("AppType." + type);

    public static string RiskLevel(RiskLevel level) => Get("RiskLevel." + level);

    public static string Confidence(ConfidenceLevel level) => Get("Confidence." + level);

    public static string Signature(SignatureStatus status) => Get("Signature." + status);

    public static string UninstallMethod(UninstallMethod method) => Get("UninstallMethod." + method);

    public static string Stage(string stage) => Get("Stage." + stage);

    public static string Reason(Reason reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        var text = Get("Reason." + reason.Kind);
        return string.IsNullOrEmpty(reason.Detail) ? text : $"{text}：{reason.Detail}";
    }

    public static string Evidence(EvidenceItem evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var weight = Get("Weight." + evidence.Weight);
        return $"{Get("Evidence." + evidence.Kind)}（{weight}）：{evidence.Detail}";
    }

    public static string Bytes(long? bytes)
    {
        if (bytes is null)
        {
            return Get("Size.Unknown");
        }

        double value = bytes.Value;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.#} {units[unit]}";
    }
}
