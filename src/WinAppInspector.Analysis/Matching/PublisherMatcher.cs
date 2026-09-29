namespace WinAppInspector.Analysis.Matching;

/// <summary>Compares publisher / company names across registry, version resources and certificates (§10.1).</summary>
public sealed class PublisherMatcher
{
    private static readonly HashSet<string> LegalSuffixes = new(StringComparer.Ordinal)
    {
        "inc", "incorporated", "llc", "ltd", "limited", "co", "corp", "corporation", "company", "gmbh", "ag", "sa", "srl", "sarl",
        "bv", "nv", "ab", "oy", "as", "kk", "plc", "pty", "llp", "lp", "spa", "sl", "sro", "oyj", "aps", "kft", "doo",
        "有限公司", "股份有限公司", "有限责任公司", "公司", "株式会社", "科技", "技术", "软件", "网络", "信息",
    };

    private static readonly HashSet<string> HardwareVendorTokens = new(StringComparer.Ordinal)
    {
        // §9.7 explicitly names NVIDIA, AMD, Intel, Realtek, HP, Lenovo and Dell; the rest are common OEM / component vendors.
        "nvidia", "amd", "intel", "realtek", "hp", "hewlett", "lenovo", "dell", "asus", "asustek", "acer", "synaptics",
        "qualcomm", "broadcom", "mediatek", "elan", "elantech", "conexant", "alps", "ricoh", "logitech", "logi", "razer",
        "corsair", "steelseries", "focusrite", "creative", "waves", "dts", "dolby", "cirrus", "fresco", "asmedia", "jmicron",
        "marvell", "killer", "rivet", "thunderbolt", "genesys", "goodix", "fingerprint", "wacom", "huion", "xp",
    };

    private static readonly string[] DriverKeywords =
    [
        "driver", "chipset", "firmware", "graphics", "audio", "bluetooth", "wireless", "wlan", "ethernet", "touchpad",
        "hotkey", "thermal", "management engine", "trusted execution", "serial io", "rapid storage", "wi-fi",
        "驱动", "芯片组", "固件",
    ];

    /// <summary>Lower-case core of a publisher name without legal suffixes or punctuation ("google llc" → "google").</summary>
    public string Normalize(string? publisher)
    {
        var tokens = NameNormalizer.Tokens(publisher).Where(t => !LegalSuffixes.Contains(t)).ToList();
        if (tokens.Count == 0)
        {
            // Everything was a suffix (e.g. "Company"); fall back to the raw tokens.
            tokens = NameNormalizer.Tokens(publisher).ToList();
        }

        return string.Join(' ', tokens);
    }

    /// <summary>True when two publisher strings plausibly name the same organisation.</summary>
    public bool Matches(string? a, string? b)
    {
        var na = Normalize(a);
        var nb = Normalize(b);
        if (na.Length == 0 || nb.Length == 0)
        {
            return false;
        }

        if (string.Equals(na, nb, StringComparison.Ordinal))
        {
            return true;
        }

        var ca = na.Replace(" ", string.Empty, StringComparison.Ordinal);
        var cb = nb.Replace(" ", string.Empty, StringComparison.Ordinal);
        var shorter = ca.Length <= cb.Length ? ca : cb;
        var longer = ca.Length <= cb.Length ? cb : ca;
        if (shorter.Length >= 3 && longer.Contains(shorter, StringComparison.Ordinal))
        {
            return true;
        }

        // "Google LLC" vs "Google Inc." reduce to the same first token.
        var fa = na.Split(' ')[0];
        var fb = nb.Split(' ')[0];
        return fa.Length >= 4 && string.Equals(fa, fb, StringComparison.Ordinal);
    }

    /// <summary>True for Microsoft Corporation and its variants (certificate CN, "Microsoft Windows", ...).</summary>
    public bool IsMicrosoft(string? publisher) =>
        NameNormalizer.Tokens(publisher).Contains("microsoft", StringComparer.Ordinal);

    /// <summary>§9.7: hardware and driver vendors whose components are kept by default.</summary>
    public bool IsHardwareVendor(string? publisher)
    {
        foreach (var token in NameNormalizer.Tokens(publisher))
        {
            if (HardwareVendorTokens.Contains(token))
            {
                return true;
            }
        }

        var normalized = Normalize(publisher);
        return normalized.Contains("advanced micro devices", StringComparison.Ordinal)
               || normalized.Contains("hewlett packard", StringComparison.Ordinal);
    }

    /// <summary>True when a product name reads like a driver / firmware / hardware utility.</summary>
    public bool LooksLikeDriver(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return false;
        }

        foreach (var keyword in DriverKeywords)
        {
            if (displayName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
