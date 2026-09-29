using System.Globalization;
using System.Text.RegularExpressions;
using WinAppInspector.Core.Models;

namespace WinAppInspector.Core.Parsing;

/// <summary>
/// Turns the raw values of one Uninstall sub-key (§7.1) into a <see cref="RegistryUninstallEntry"/>.
/// Pure logic so it can be unit-tested with fake value dictionaries on any OS.
/// </summary>
public static partial class RegistryUninstallEntryParser
{
    [GeneratedRegex(@"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}")]
    private static partial Regex GuidPattern();

    /// <param name="scope">Which of the three Uninstall keys the entry came from.</param>
    /// <param name="keyName">The sub-key name.</param>
    /// <param name="values">Value name → raw registry value (string, int, long, byte[], string[] or null).</param>
    public static RegistryUninstallEntry Parse(RegistryScope scope, string keyName, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(keyName);
        ArgumentNullException.ThrowIfNull(values);

        var lookup = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);
        var keyPath = KeyPathOf(scope, keyName);

        return new RegistryUninstallEntry
        {
            Scope = scope,
            KeyName = keyName,
            KeyPath = keyPath,
            DisplayName = GetString(lookup, "DisplayName"),
            DisplayVersion = GetString(lookup, "DisplayVersion"),
            Publisher = GetString(lookup, "Publisher"),
            InstallLocation = GetString(lookup, "InstallLocation"),
            InstallDate = GetString(lookup, "InstallDate"),
            EstimatedSizeKb = GetLong(lookup, "EstimatedSize"),
            UninstallString = GetString(lookup, "UninstallString"),
            QuietUninstallString = GetString(lookup, "QuietUninstallString"),
            DisplayIcon = GetString(lookup, "DisplayIcon"),
            WindowsInstaller = GetBool(lookup, "WindowsInstaller"),
            SystemComponent = GetBool(lookup, "SystemComponent"),
            ReleaseType = GetString(lookup, "ReleaseType"),
            ParentKeyName = GetString(lookup, "ParentKeyName"),
        };
    }

    public static string KeyPathOf(RegistryScope scope, string keyName)
    {
        var root = scope switch
        {
            RegistryScope.MachineNative => @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall",
            RegistryScope.MachineWow6432 => @"HKEY_LOCAL_MACHINE\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            RegistryScope.CurrentUser => @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall",
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
        };
        return root + "\\" + keyName;
    }

    /// <summary>
    /// Parses the InstallDate value. Windows Installer writes <c>yyyyMMdd</c>; other installers use a handful of other shapes.
    /// </summary>
    public static DateOnly? ParseInstallDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var s = raw.Trim();
        string[] formats = ["yyyyMMdd", "yyyy-MM-dd", "yyyy/MM/dd", "yyyy.MM.dd", "yyyyMMddHHmmss", "M/d/yyyy", "d.M.yyyy", "dd/MM/yyyy"];
        foreach (var format in formats)
        {
            if (DateOnly.TryParseExact(s, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return date;
            }
        }

        if (DateTime.TryParseExact(s, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return DateOnly.FromDateTime(dt);
        }

        return null;
    }

    /// <summary>True when the sub-key name is a GUID in braces, as Windows Installer products are.</summary>
    public static bool IsGuidKeyName(string keyName) =>
        keyName.Length == 38 && GuidPattern().IsMatch(keyName) && keyName[0] == '{';

    /// <summary>
    /// The MSI product code, when the entry is a Windows Installer product: taken from an <c>msiexec</c> uninstall string
    /// or from the GUID key name when the WindowsInstaller flag is set.
    /// </summary>
    public static string? GetMsiProductCode(RegistryUninstallEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        foreach (var command in new[] { entry.UninstallString, entry.QuietUninstallString })
        {
            if (command is not null && command.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
            {
                var match = GuidPattern().Match(command);
                if (match.Success)
                {
                    return match.Value.ToUpperInvariant();
                }
            }
        }

        if (entry.WindowsInstaller && IsGuidKeyName(entry.KeyName))
        {
            return entry.KeyName.ToUpperInvariant();
        }

        return null;
    }

    /// <summary>
    /// Mirrors the filter "Settings → Apps" applies: entries without a DisplayName, SystemComponent entries,
    /// child entries (ParentKeyName) and update / hotfix release types are not listed as applications.
    /// They are still kept for attribution and the "show system components" filter (§9.9).
    /// </summary>
    public static bool IsVisibleInAppsAndFeatures(RegistryUninstallEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (string.IsNullOrWhiteSpace(entry.DisplayName) || entry.SystemComponent || entry.ParentKeyName is not null)
        {
            return false;
        }

        if (entry.ReleaseType is not null &&
            (entry.ReleaseType.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
             entry.ReleaseType.Contains("Hotfix", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // Some updates omit ReleaseType but keep a KB name.
        return !(entry.DisplayName.StartsWith("Security Update for", StringComparison.OrdinalIgnoreCase) ||
                 entry.DisplayName.StartsWith("Update for", StringComparison.OrdinalIgnoreCase) ||
                 entry.DisplayName.StartsWith("Hotfix for", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The uninstall route to prefer for this entry (§5.2 order), or <see cref="UninstallMethod.None"/>.</summary>
    public static UninstallMethod PreferredUninstallMethod(RegistryUninstallEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!string.IsNullOrWhiteSpace(entry.QuietUninstallString))
        {
            return UninstallMethod.QuietUninstallString;
        }

        if (GetMsiProductCode(entry) is not null)
        {
            return UninstallMethod.Msi;
        }

        return string.IsNullOrWhiteSpace(entry.UninstallString) ? UninstallMethod.None : UninstallMethod.UninstallString;
    }

    private static string? GetString(Dictionary<string, object?> values, string name)
    {
        if (!values.TryGetValue(name, out var raw) || raw is null)
        {
            return null;
        }

        var text = raw switch
        {
            string s => s,
            string[] parts => string.Join("; ", parts.Where(p => !string.IsNullOrWhiteSpace(p))),
            byte[] bytes => bytes.Length == 0 ? null : Convert.ToHexString(bytes),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => raw.ToString(),
        };

        text = text?.Trim().TrimEnd('\0');
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static long? GetLong(Dictionary<string, object?> values, string name)
    {
        if (!values.TryGetValue(name, out var raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            int i => i < 0 ? (long)(uint)i : i, // REG_DWORD is unsigned; large sizes come back negative through int
            long l => l,
            uint u => u,
            string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    private static bool GetBool(Dictionary<string, object?> values, string name)
    {
        if (!values.TryGetValue(name, out var raw) || raw is null)
        {
            return false;
        }

        return raw switch
        {
            int i => i != 0,
            long l => l != 0,
            uint u => u != 0,
            bool b => b,
            string s => s.Trim() is "1" || s.Trim().Equals("true", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }
}
