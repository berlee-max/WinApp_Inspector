using WinAppInspector.Analysis.Resolution;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Parsing;
using WinAppInspector.Core.Scanning;

namespace WinAppInspector.Analysis.Matching;

/// <summary>Chooses the main executable of an entity and derives name / publisher / version for unregistered directories (§10.1, §39).</summary>
public sealed class ExecutableMatcher
{
    private static readonly string[] HelperKeywords =
    [
        "unins", "uninstall", "setup", "install", "update", "updater", "crash", "report", "helper", "handler", "service", "launcher",
        "elevate", "notif", "bug", "repair", "cef", "subprocess", "broker", "tool", "cli", "daemon", "host", "watchdog",
    ];

    /// <summary>Picks the executable most likely to be the application itself.</summary>
    public string? SelectMainExecutable(EntityDraft draft, ScanSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(snapshot);

        var iconExe = draft.RegistryEntries
            .Select(e => CommandLine.ExtractExecutable(e.DisplayIcon?.Split(',')[0]))
            .FirstOrDefault(p => p is not null && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

        string? best = null;
        var bestScore = int.MinValue;
        foreach (var directory in draft.ProgramDirectories)
        {
            var folderName = WindowsPath.GetFileName(directory.Path);
            foreach (var exe in directory.ExecutablePaths)
            {
                var score = Score(exe, draft, folderName, directory.Path, iconExe, snapshot);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = exe;
                }
            }
        }

        // A registered app whose directory was not discovered still has its icon / uninstaller path.
        if (best is null && iconExe is not null)
        {
            return iconExe;
        }

        return best;
    }

    /// <summary>Name, publisher and version for a directory without a registry or package record (§39).</summary>
    public (string Name, string? Publisher, string? Version) Describe(AppDirectory directory, ScanSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(snapshot);

        var folderName = WindowsPath.GetFileName(directory.Path);
        var scored = directory.ExecutablePaths
            .Select(exe => (Path: exe, Metadata: snapshot.MetadataOf(exe), Signature: snapshot.SignatureOf(exe)))
            .OrderByDescending(x => ScoreForDescription(x.Path, x.Metadata, folderName, directory.Path))
            .ToList();

        var primary = scored.FirstOrDefault();
        var metadata = primary.Metadata;

        var name = metadata?.ProductName ?? metadata?.FileDescription ?? folderName;
        var publisher = metadata?.CompanyName
                        ?? scored.Select(x => x.Metadata?.CompanyName).FirstOrDefault(c => c is not null)
                        ?? scored.Select(x => x.Signature?.IsValid == true ? x.Signature.Publisher : null).FirstOrDefault(p => p is not null);
        var version = metadata?.ProductVersion ?? metadata?.FileVersion;

        return (name.Trim(), publisher, version);
    }

    private static int Score(string exe, EntityDraft draft, string folderName, string directoryPath, string? iconExe, ScanSnapshot snapshot)
    {
        var fileName = WindowsPath.GetFileName(exe);
        var stem = fileName[..^4];
        var metadata = snapshot.MetadataOf(exe);
        var score = 0;

        if (iconExe is not null && WindowsPath.AreEqual(iconExe, exe))
        {
            score += 6;
        }

        if (metadata?.ProductName is not null && NameNormalizer.IsSimilar(metadata.ProductName, draft.Name))
        {
            score += 3;
        }

        if (NameNormalizer.IsSimilar(stem, draft.Name) || NameNormalizer.IsSimilar(stem, folderName))
        {
            score += 2;
        }

        if (WindowsPath.AreEqual(WindowsPath.GetDirectoryName(exe), directoryPath))
        {
            score += 1;
        }

        if (draft.Processes.Any(p => WindowsPath.AreEqual(p.ExecutablePath, exe)))
        {
            score += 1;
        }

        if (draft.Shortcuts.Any(s => WindowsPath.AreEqual(s.TargetPath, exe)))
        {
            score += 3;
        }

        if (IsHelper(stem))
        {
            score -= 3;
        }

        return score;
    }

    private static int ScoreForDescription(string exe, ExecutableMetadata? metadata, string folderName, string directoryPath)
    {
        var stem = WindowsPath.GetFileName(exe)[..^4];
        var score = 0;
        if (metadata?.ProductName is not null)
        {
            score += 2;
        }

        if (metadata?.CompanyName is not null)
        {
            score += 1;
        }

        if (NameNormalizer.IsSimilar(stem, folderName) || (metadata?.ProductName is not null && NameNormalizer.IsSimilar(metadata.ProductName, folderName)))
        {
            score += 3;
        }

        if (WindowsPath.AreEqual(WindowsPath.GetDirectoryName(exe), directoryPath))
        {
            score += 1;
        }

        if (IsHelper(stem))
        {
            score -= 3;
        }

        return score;
    }

    private static bool IsHelper(string stem)
    {
        foreach (var keyword in HelperKeywords)
        {
            if (stem.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
