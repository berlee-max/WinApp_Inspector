namespace WinAppInspector.Core.Parsing;

/// <summary>
/// Minimal X.500 distinguished-name reader for certificate subjects and AppX publisher strings such as
/// <c>CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US</c>.
/// </summary>
public static class DistinguishedName
{
    /// <summary>The CN attribute, or the O attribute when there is no CN, or <c>null</c>.</summary>
    public static string? GetCommonName(string? distinguishedName)
    {
        var parts = Parse(distinguishedName);
        if (parts.TryGetValue("CN", out var cn) && !string.IsNullOrWhiteSpace(cn))
        {
            return cn;
        }

        return parts.TryGetValue("O", out var o) && !string.IsNullOrWhiteSpace(o) ? o : null;
    }

    /// <summary>The O attribute, when present.</summary>
    public static string? GetOrganization(string? distinguishedName) =>
        Parse(distinguishedName).TryGetValue("O", out var o) && !string.IsNullOrWhiteSpace(o) ? o : null;

    /// <summary>
    /// Splits a DN into attribute → value, honouring quoted values and backslash escapes.
    /// When the same attribute appears twice, the first occurrence wins (the most specific one comes first in RFC 4514 order).
    /// </summary>
    public static IReadOnlyDictionary<string, string> Parse(string? distinguishedName)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(distinguishedName))
        {
            return result;
        }

        foreach (var component in SplitComponents(distinguishedName))
        {
            var eq = component.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }

            var key = component[..eq].Trim();
            var value = Unquote(component[(eq + 1)..].Trim());
            if (key.Length > 0 && !result.ContainsKey(key))
            {
                result[key] = value;
            }
        }

        return result;
    }

    private static IEnumerable<string> SplitComponents(string dn)
    {
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < dn.Length; i++)
        {
            var c = dn[i];
            if (c == '\\' && i + 1 < dn.Length)
            {
                current.Append(c).Append(dn[++i]);
                continue;
            }

            if (c == '"')
            {
                inQuotes = !inQuotes;
                current.Append(c);
                continue;
            }

            if ((c == ',' || c == ';') && !inQuotes)
            {
                yield return current.ToString();
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1];
        }

        return value.Replace("\\,", ",", StringComparison.Ordinal)
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal)
            .Trim();
    }
}
