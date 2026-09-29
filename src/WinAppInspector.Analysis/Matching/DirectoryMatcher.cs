using WinAppInspector.Analysis.Resolution;
using WinAppInspector.Core.Evidence;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Parsing;
using WinAppInspector.Core.Scanning;

namespace WinAppInspector.Analysis.Matching;

/// <summary>Outcome of matching one directory against one candidate owner.</summary>
public sealed record DirectoryMatch(EntityDraft Candidate, IReadOnlyList<EvidenceItem> Evidence, bool IsParentOfInstallLocation, bool IsPublisherNameOnly)
{
    public int Score => ConfidenceCalculator.Score(Evidence);

    /// <summary>A vendor folder: an ancestor of the registered install location, or a data folder matched only through the publisher name.</summary>
    public bool IsSharedVendorFolder => IsParentOfInstallLocation || IsPublisherNameOnly;
}

/// <summary>
/// Produces §10.2 evidence that a discovered directory belongs to a candidate application. Combines registry,
/// package, executable metadata and signature facts; folder-name similarity alone is deliberately weak.
/// </summary>
public sealed class DirectoryMatcher
{
    private readonly PublisherMatcher _publishers;

    public DirectoryMatcher(PublisherMatcher publishers)
    {
        _publishers = publishers;
    }

    public DirectoryMatch Match(AppDirectory directory, EntityDraft candidate, ScanSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(snapshot);

        var evidence = new List<EvidenceItem>();
        var isParent = false;
        var dirPath = directory.Path;

        // Registered install locations that sit below this directory: paths inside them are explained by the child, not by this folder.
        var childLocations = candidate.RegistryEntries.Select(e => e.InstallLocation)
            .Concat(candidate.Packages.Select(p => p.InstallLocation))
            .Where(l => l is { Length: > 0 } && WindowsPath.IsUnder(l, dirPath))
            .ToList();
        bool ExplainedByChild(string path) => childLocations.Any(l => WindowsPath.IsSameOrUnder(path, l));

        foreach (var entry in candidate.RegistryEntries)
        {
            if (entry.InstallLocation is { Length: > 0 } location)
            {
                if (WindowsPath.AreEqual(location, dirPath))
                {
                    Add(evidence, EvidenceKind.RegistryInstallLocationMatch, location);
                }
                else if (WindowsPath.IsUnder(location, dirPath))
                {
                    isParent = true;
                    Add(evidence, EvidenceKind.ParentOfInstallLocation, location);
                }
                else if (WindowsPath.IsUnder(dirPath, location))
                {
                    // A sub-folder of the install location (e.g. Application\128.0) belongs to the app as well.
                    Add(evidence, EvidenceKind.RegistryInstallLocationMatch, location);
                }
            }

            foreach (var command in new[] { entry.UninstallString, entry.QuietUninstallString })
            {
                var exe = CommandLine.ExtractExecutable(command);
                if (exe is not null && WindowsPath.IsUnder(exe, dirPath) && !ExplainedByChild(exe) && !exe.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
                {
                    Add(evidence, EvidenceKind.UninstallStringPointsToDirectory, exe);
                }
            }

            var icon = CommandLine.ExtractExecutable(entry.DisplayIcon?.Split(',')[0]);
            if (icon is not null && WindowsPath.IsUnder(icon, dirPath) && !ExplainedByChild(icon))
            {
                Add(evidence, EvidenceKind.DisplayIconPointsToDirectory, icon);
            }
        }

        foreach (var package in candidate.Packages)
        {
            if (package.InstallLocation is { Length: > 0 } location)
            {
                if (WindowsPath.AreEqual(location, dirPath) || WindowsPath.IsUnder(dirPath, location))
                {
                    Add(evidence, EvidenceKind.PackageInstallLocationMatch, location);
                }
                else if (WindowsPath.IsUnder(location, dirPath))
                {
                    isParent = true;
                    Add(evidence, EvidenceKind.ParentOfInstallLocation, location);
                }
            }
        }

        if (candidate.Seed == SeedKind.Directory && candidate.InstallLocation is { } ownLocation && WindowsPath.IsUnder(dirPath, ownLocation))
        {
            Add(evidence, EvidenceKind.RegistryInstallLocationMatch, ownLocation);
        }

        // Executable metadata and signatures inside the directory.
        foreach (var exePath in directory.ExecutablePaths)
        {
            var metadata = snapshot.MetadataOf(exePath);
            if (metadata is not null)
            {
                if (NameNormalizer.IsSimilar(metadata.ProductName, candidate.Name))
                {
                    Add(evidence, EvidenceKind.MainExecutableProductNameMatch, $"{WindowsPath.GetFileName(exePath)}: {metadata.ProductName}");
                }

                if (candidate.Publisher is not null && _publishers.Matches(metadata.CompanyName, candidate.Publisher))
                {
                    Add(evidence, EvidenceKind.ExecutableCompanyNameMatch, $"{WindowsPath.GetFileName(exePath)}: {metadata.CompanyName}");
                }
            }

            var signature = snapshot.SignatureOf(exePath);
            if (signature is { IsValid: true, Publisher: { } signer } && candidate.Publisher is not null && _publishers.Matches(signer, candidate.Publisher))
            {
                Add(evidence, EvidenceKind.SignaturePublisherMatch, $"{WindowsPath.GetFileName(exePath)}: {signer}");
            }
        }

        foreach (var shortcut in candidate.Shortcuts)
        {
            if (WindowsPath.IsUnder(shortcut.TargetPath, dirPath))
            {
                Add(evidence, EvidenceKind.ShortcutPointsToDirectory, shortcut.TargetPath);
            }
        }

        // Folder name: exact (normalised) equality with the app name or its install folder is medium evidence; similarity is low.
        var folderName = WindowsPath.GetFileName(dirPath);
        var compactFolder = NameNormalizer.Compact(folderName);
        var publisherOnly = false;
        if (compactFolder.Length >= 3 && !directory.ContainsExecutables)
        {
            var installFolder = WindowsPath.GetFileName(candidate.InstallLocation);
            if (compactFolder == NameNormalizer.Compact(candidate.Name) ||
                (installFolder.Length > 0 && compactFolder == NameNormalizer.Compact(installFolder)))
            {
                Add(evidence, EvidenceKind.FolderNameExactMatch, $"{folderName} = {candidate.Name}");
            }
            else if (candidate.Seed != SeedKind.Directory && candidate.Publisher is not null &&
                     compactFolder == NameNormalizer.Compact(_publishers.Normalize(candidate.Publisher)))
            {
                Add(evidence, EvidenceKind.FolderNameExactMatch, $"{folderName} = {candidate.Publisher}");
                publisherOnly = evidence.All(e => e.Kind is EvidenceKind.FolderNameExactMatch or EvidenceKind.FolderNameSimilar);
            }
        }

        if (NameNormalizer.IsSimilar(folderName, candidate.Name) ||
            (candidate.Publisher is not null && NameNormalizer.IsSimilar(folderName, candidate.Publisher) && candidate.Seed != SeedKind.Directory))
        {
            Add(evidence, EvidenceKind.FolderNameSimilar, folderName);
        }

        return new DirectoryMatch(candidate, evidence, isParent, publisherOnly);
    }

    private static void Add(List<EvidenceItem> evidence, EvidenceKind kind, string detail)
    {
        if (!evidence.Any(e => e.Kind == kind))
        {
            evidence.Add(new EvidenceItem(kind, detail));
        }
    }
}
