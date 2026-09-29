using System.Text.Json;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;

namespace WinAppInspector.Core.Parsing;

/// <summary>
/// Parses the JSON produced by the PowerShell fallback
/// (<c>Get-AppxPackage | Select-Object ... | ConvertTo-Json</c>) into <see cref="AppxPackageRecord"/>s (§7.2).
/// Tolerates the single-object shape PowerShell emits for one result and both numeric and string enums.
/// </summary>
public static class AppxPackageJsonParser
{
    private static readonly string[] VersionParts = ["Major", "Minor", "Build", "Revision"];

    public static IReadOnlyList<AppxPackageRecord> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = document.RootElement;

        return root.ValueKind switch
        {
            JsonValueKind.Array => root.EnumerateArray().Select(ParseOne).OfType<AppxPackageRecord>().ToArray(),
            JsonValueKind.Object => ParseOne(root) is { } single ? [single] : [],
            _ => [],
        };
    }

    private static AppxPackageRecord? ParseOne(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var name = GetString(element, "Name");
        var fullName = GetString(element, "PackageFullName");
        var familyName = GetString(element, "PackageFamilyName");
        if (name is null || fullName is null || familyName is null)
        {
            return null;
        }

        var publisher = GetString(element, "Publisher");
        return new AppxPackageRecord
        {
            Name = name,
            PackageFullName = fullName,
            PackageFamilyName = familyName,
            DisplayName = GetString(element, "DisplayName"),
            Publisher = publisher,
            PublisherDisplayName = GetString(element, "PublisherDisplayName") ?? DistinguishedName.GetCommonName(publisher),
            Version = GetVersion(element),
            InstallLocation = GetString(element, "InstallLocation") is { } loc ? WindowsPath.Normalize(loc) : null,
            Architecture = GetArchitecture(element),
            IsFramework = GetBool(element, "IsFramework") ?? false,
            NonRemovable = GetBool(element, "NonRemovable"),
            IsBundle = GetBool(element, "IsBundle") ?? false,
            SignatureKind = GetSignatureKind(element),
        };
    }

    private static string? GetVersion(JsonElement element)
    {
        if (!element.TryGetProperty("Version", out var v))
        {
            return null;
        }

        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Object => string.Join('.', VersionParts
                .Select(p => v.TryGetProperty(p, out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture) : "0")),
            _ => null,
        };
    }

    private static string? GetArchitecture(JsonElement element)
    {
        if (!element.TryGetProperty("Architecture", out var a))
        {
            return null;
        }

        return a.ValueKind switch
        {
            JsonValueKind.String => a.GetString(),
            JsonValueKind.Number => a.GetInt32() switch
            {
                0 => "X86",
                5 => "Arm",
                9 => "X64",
                11 => "Neutral",
                12 => "Arm64",
                14 => "X86OnArm64",
                var other => other.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            _ => null,
        };
    }

    private static string? GetSignatureKind(JsonElement element)
    {
        if (!element.TryGetProperty("SignatureKind", out var k))
        {
            return null;
        }

        return k.ValueKind switch
        {
            JsonValueKind.String => k.GetString(),
            JsonValueKind.Number => k.GetInt32() switch
            {
                0 => "None",
                1 => "Developer",
                2 => "Enterprise",
                3 => "Store",
                4 => "System",
                var other => other.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            _ => null,
        };
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var p))
        {
            return null;
        }

        var s = p.ValueKind switch
        {
            JsonValueKind.String => p.GetString(),
            JsonValueKind.Number => p.GetRawText(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private static bool? GetBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var p))
        {
            return null;
        }

        return p.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(p.GetString(), out var b) ? b : null,
            JsonValueKind.Number => p.GetInt32() != 0,
            _ => null,
        };
    }
}
