using Microsoft.Extensions.Logging;
using WinAppInspector.Analysis.Classification;
using WinAppInspector.Analysis.Matching;
using WinAppInspector.Core.Evidence;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Parsing;
using WinAppInspector.Core.Scanning;

namespace WinAppInspector.Analysis.Resolution;

/// <summary>Entities plus the runtime items that could not be attributed to any application.</summary>
public sealed record ResolutionResult(
    IReadOnlyList<ApplicationEntity> Applications,
    IReadOnlyList<StartupItemRecord> OrphanStartupItems,
    IReadOnlyList<ServiceRecord> OrphanServices,
    IReadOnlyList<ScheduledTaskRecord> OrphanScheduledTasks);

public interface IApplicationResolver
{
    ResolutionResult Resolve(ScanSnapshot snapshot);
}

/// <summary>
/// The attribution engine (§10, §36 stage two): merges registry entries, packages, directories, executables,
/// signatures, processes, services, startup items, scheduled tasks and shortcuts into <see cref="ApplicationEntity"/>s,
/// scoring every link with §10.2 evidence and explaining every classification with §42 reasons.
/// </summary>
public sealed class ApplicationResolver : IApplicationResolver
{
    private readonly PublisherMatcher _publishers;
    private readonly DirectoryMatcher _directories;
    private readonly ExecutableMatcher _executables;
    private readonly AppClassifier _classifier;
    private readonly RiskAssessor _risk;
    private readonly KnownComponentCatalog _catalog;
    private readonly ILogger<ApplicationResolver> _logger;

    public ApplicationResolver(
        PublisherMatcher publishers,
        DirectoryMatcher directories,
        ExecutableMatcher executables,
        AppClassifier classifier,
        RiskAssessor risk,
        KnownComponentCatalog catalog,
        ILogger<ApplicationResolver> logger)
    {
        _publishers = publishers;
        _directories = directories;
        _executables = executables;
        _classifier = classifier;
        _risk = risk;
        _catalog = catalog;
        _logger = logger;
    }

    public ResolutionResult Resolve(ScanSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var drafts = new List<EntityDraft>();
        SeedFromRegistry(snapshot, drafts);
        SeedFromPackages(snapshot, drafts);
        AttachShortcuts(snapshot, drafts);
        AttachDirectories(snapshot, drafts);
        var (orphanStartup, orphanServices, orphanTasks) = LinkRuntime(snapshot, drafts);

        var entities = drafts.Select(d => Finalize(d, snapshot)).OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        _logger.LogInformation("Resolved {Count} applications from {Registry} registry entries, {Packages} packages and {Directories} directories",
            entities.Count, snapshot.RegistryEntries.Count, snapshot.Packages.Count, snapshot.Directories.Count);

        return new ResolutionResult(entities, orphanStartup, orphanServices, orphanTasks);
    }

    // ---- Seeding -------------------------------------------------------------------------------------------------

    private void SeedFromRegistry(ScanSnapshot snapshot, List<EntityDraft> drafts)
    {
        var visible = snapshot.RegistryEntries.Where(RegistryUninstallEntryParser.IsVisibleInAppsAndFeatures).ToList();
        var hidden = snapshot.RegistryEntries.Except(visible).ToList();
        var byKeyName = new Dictionary<string, EntityDraft>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in visible)
        {
            // The same product registered in two hives (native + WOW6432, or HKLM + HKCU) is one application.
            var existing = drafts.FirstOrDefault(d =>
                d.Seed == SeedKind.Registry &&
                string.Equals(NameNormalizer.Normalize(d.Name), NameNormalizer.Normalize(entry.DisplayName), StringComparison.Ordinal) &&
                _publishers.Matches(d.Publisher, entry.Publisher) &&
                string.Equals(d.Version, entry.DisplayVersion, StringComparison.OrdinalIgnoreCase));

            var draft = existing ?? CreateRegistryDraft(entry);
            if (existing is null)
            {
                drafts.Add(draft);
            }
            else
            {
                draft.RegistryEntries.Add(entry);
            }

            byKeyName.TryAdd(entry.KeyName, draft);
        }

        foreach (var entry in hidden)
        {
            if (entry.ParentKeyName is not null && byKeyName.TryGetValue(entry.ParentKeyName, out var parent))
            {
                parent.RegistryEntries.Add(entry);
                continue;
            }

            var draft = CreateRegistryDraft(entry);
            draft.IsHiddenRegistryEntry = true;
            drafts.Add(draft);
        }
    }

    private static EntityDraft CreateRegistryDraft(RegistryUninstallEntry entry)
    {
        var draft = new EntityDraft
        {
            Id = entry.KeyPath,
            Seed = SeedKind.Registry,
            Name = entry.DisplayName ?? entry.KeyName,
            Version = entry.DisplayVersion,
            Publisher = entry.Publisher,
            InstallLocation = entry.InstallLocation is null ? null : WindowsPath.Normalize(entry.InstallLocation),
            IconPath = entry.DisplayIcon,
            UninstallCommand = entry.UninstallString,
            QuietUninstallCommand = entry.QuietUninstallString,
            MsiProductCode = RegistryUninstallEntryParser.GetMsiProductCode(entry),
            PreferredUninstallMethod = RegistryUninstallEntryParser.PreferredUninstallMethod(entry),
            InstallDate = RegistryUninstallEntryParser.ParseInstallDate(entry.InstallDate),
            EstimatedSizeBytes = entry.EstimatedSizeKb is { } kb ? kb * 1024 : null,
            Sources = DiscoverySource.Registry,
        };
        draft.RegistryEntries.Add(entry);
        return draft;
    }

    private static void SeedFromPackages(ScanSnapshot snapshot, List<EntityDraft> drafts)
    {
        foreach (var family in snapshot.Packages.GroupBy(p => p.PackageFamilyName, StringComparer.OrdinalIgnoreCase))
        {
            var packages = family.OrderByDescending(p => p.Version, StringComparer.OrdinalIgnoreCase).ToList();
            var primary = packages[0];
            var draft = new EntityDraft
            {
                Id = "appx:" + family.Key,
                Seed = SeedKind.Package,
                Name = primary.DisplayName ?? primary.Name,
                Version = primary.Version,
                Publisher = primary.PublisherDisplayName ?? DistinguishedName.GetCommonName(primary.Publisher) ?? primary.Publisher,
                InstallLocation = packages.Select(p => p.InstallLocation).FirstOrDefault(l => l is not null),
                AppxPackageFullName = primary.PackageFullName,
                PreferredUninstallMethod = UninstallMethod.Appx,
                Sources = DiscoverySource.Appx,
            };
            draft.Packages.AddRange(packages);
            drafts.Add(draft);
        }
    }

    private static void AttachShortcuts(ScanSnapshot snapshot, List<EntityDraft> drafts)
    {
        foreach (var shortcut in snapshot.Shortcuts)
        {
            var owner = drafts.FirstOrDefault(d =>
                d.InstallLocation is not null && WindowsPath.IsSameOrUnder(shortcut.TargetPath, d.InstallLocation))
                ?? drafts.FirstOrDefault(d => NameNormalizer.IsSimilar(shortcut.Name, d.Name) && d.Seed != SeedKind.Package);
            if (owner is not null)
            {
                owner.Shortcuts.Add(shortcut);
                owner.Sources |= DiscoverySource.Shortcut;
            }
        }
    }

    // ---- Directories ---------------------------------------------------------------------------------------------

    private void AttachDirectories(ScanSnapshot snapshot, List<EntityDraft> drafts)
    {
        var ordered = snapshot.Directories
            .OrderByDescending(d => d.ContainsExecutables)
            .ThenBy(d => RootPriority(d.Root))
            .ThenBy(d => d.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var directory in ordered)
        {
            var matches = drafts
                .Select(candidate => _directories.Match(directory, candidate, snapshot))
                .Where(m => m.Evidence.Count > 0)
                .OrderByDescending(m => m.Score)
                .ThenBy(m => m.Candidate.Seed) // registry and packages before directory-seeded drafts on ties
                .ToList();

            var best = matches.FirstOrDefault();
            if (best is not null && best.Score >= ConfidenceCalculator.MediumThreshold)
            {
                if (best.IsSharedVendorFolder)
                {
                    // Vendor folder (Program Files\Google, AppData\Local\Google): shared by every product registered beneath it
                    // or published by that vendor. It is attached to each of them and never deleted as a whole (§5.4).
                    foreach (var vendorMatch in matches.Where(m => m.IsSharedVendorFolder && m.Score >= ConfidenceCalculator.MediumThreshold))
                    {
                        Attach(vendorMatch.Candidate, directory with { Role = DirectoryRole.SharedParent }, vendorMatch.Evidence);
                    }
                }
                else
                {
                    Attach(best.Candidate, directory with { Role = RoleOf(directory) }, best.Evidence);
                }

                continue;
            }

            drafts.Add(CreateDirectoryDraft(directory, best, snapshot));
        }
    }

    private static void Attach(EntityDraft owner, AppDirectory directory, IReadOnlyList<EvidenceItem> evidence)
    {
        owner.AddDirectory(directory with { AttributionEvidence = evidence });
        owner.Sources |= DiscoverySource.Directory;
        foreach (var item in evidence)
        {
            owner.AddEvidence(item.Kind, item.Detail);
        }
    }

    private EntityDraft CreateDirectoryDraft(AppDirectory directory, DirectoryMatch? weakMatch, ScanSnapshot snapshot)
    {
        var folderName = WindowsPath.GetFileName(directory.Path);
        var (name, publisher, version) = directory.ContainsExecutables
            ? _executables.Describe(directory, snapshot)
            : (folderName, null, null);

        var draft = new EntityDraft
        {
            Id = "dir:" + directory.Path,
            Seed = SeedKind.Directory,
            Name = name,
            Publisher = publisher,
            Version = version,
            InstallLocation = directory.Path,
            Sources = DiscoverySource.Directory,
        };
        draft.AddDirectory(directory with { Role = RoleOf(directory) });

        if (directory.ContainsExecutables)
        {
            draft.Sources |= DiscoverySource.Executable;
            foreach (var exe in directory.ExecutablePaths)
            {
                var metadata = snapshot.MetadataOf(exe);
                if (metadata is not null)
                {
                    draft.Executables.Add(metadata);
                    if (NameNormalizer.IsSimilar(metadata.ProductName, folderName) || NameNormalizer.IsSimilar(metadata.ProductName, name))
                    {
                        draft.AddEvidence(EvidenceKind.MainExecutableProductNameMatch, $"{WindowsPath.GetFileName(exe)}: {metadata.ProductName}");
                    }

                    if (publisher is not null && _publishers.Matches(metadata.CompanyName, publisher))
                    {
                        draft.AddEvidence(EvidenceKind.ExecutableCompanyNameMatch, $"{WindowsPath.GetFileName(exe)}: {metadata.CompanyName}");
                    }
                }

                var signature = snapshot.SignatureOf(exe);
                if (signature is { IsValid: true, Publisher: { } signer } && publisher is not null && _publishers.Matches(signer, publisher))
                {
                    draft.AddEvidence(EvidenceKind.SignaturePublisherMatch, $"{WindowsPath.GetFileName(exe)}: {signer}");
                    draft.Sources |= DiscoverySource.Signature;
                }
            }

            if (NameNormalizer.IsSimilar(folderName, name))
            {
                draft.AddEvidence(EvidenceKind.FolderNameSimilar, folderName);
            }
        }

        // Keep the weak hint visible in "why" even though it was not enough to attach.
        if (weakMatch is not null)
        {
            foreach (var item in weakMatch.Evidence.Where(e => e.Kind == EvidenceKind.FolderNameSimilar))
            {
                draft.AddEvidence(item.Kind, $"{item.Detail} ~ {weakMatch.Candidate.Name}");
            }
        }

        return draft;
    }

    private static DirectoryRole RoleOf(AppDirectory directory)
    {
        if (directory.ContainsExecutables)
        {
            return DirectoryRole.Program;
        }

        var name = WindowsPath.GetFileName(directory.Path);
        if (CacheDirectoryNames.IsLogName(name))
        {
            return DirectoryRole.Logs;
        }

        if (CacheDirectoryNames.IsCacheName(name))
        {
            return DirectoryRole.Cache;
        }

        return DirectoryRole.Data;
    }

    private static int RootPriority(ScanRoot root) => root switch
    {
        ScanRoot.ProgramFiles => 0,
        ScanRoot.ProgramFilesX86 => 1,
        ScanRoot.LocalAppData => 2,
        ScanRoot.RoamingAppData => 3,
        ScanRoot.ProgramData => 4,
        ScanRoot.LocalLowAppData => 5,
        _ => 6,
    };

    // ---- Runtime links -------------------------------------------------------------------------------------------

    private static (List<StartupItemRecord>, List<ServiceRecord>, List<ScheduledTaskRecord>) LinkRuntime(ScanSnapshot snapshot, List<EntityDraft> drafts)
    {
        var orphanStartup = new List<StartupItemRecord>();
        var orphanServices = new List<ServiceRecord>();
        var orphanTasks = new List<ScheduledTaskRecord>();

        foreach (var process in snapshot.Processes)
        {
            var (owner, directory) = FindOwner(drafts, process.ExecutablePath);
            if (owner is not null)
            {
                owner.Processes.Add(process);
                owner.Sources |= DiscoverySource.Process;
                owner.AddEvidence(EvidenceKind.RunningProcessInDirectory, $"{process.Name} (PID {process.ProcessId}) in {directory}");
            }
        }

        foreach (var service in snapshot.Services)
        {
            var (owner, directory) = FindOwner(drafts, service.ExecutablePath);
            if (owner is not null)
            {
                owner.Services.Add(service);
                owner.Sources |= DiscoverySource.Service;
                owner.AddEvidence(EvidenceKind.ServiceExecutableInDirectory, $"{service.Name} -> {directory}");
            }
            else if (service.ExecutablePath is not null && !IsSystemPath(snapshot.Folders, service.ExecutablePath))
            {
                orphanServices.Add(service);
            }
        }

        foreach (var item in snapshot.StartupItems)
        {
            var (owner, directory) = FindOwner(drafts, item.ExecutablePath);
            if (owner is not null)
            {
                owner.StartupItems.Add(item);
                owner.Sources |= DiscoverySource.Startup;
                owner.AddEvidence(EvidenceKind.StartupItemPointsToDirectory, $"{item.Name} -> {directory}");
            }
            else if (item.ExecutablePath is not null && !IsSystemPath(snapshot.Folders, item.ExecutablePath))
            {
                orphanStartup.Add(item);
            }
        }

        foreach (var task in snapshot.ScheduledTasks)
        {
            var (owner, directory) = FindOwner(drafts, task.Execute);
            if (owner is not null)
            {
                owner.ScheduledTasks.Add(task);
                owner.Sources |= DiscoverySource.ScheduledTask;
                owner.AddEvidence(EvidenceKind.ScheduledTaskPointsToDirectory, $"{task.TaskPath.TrimEnd('\\')}\\{task.TaskName} -> {directory}");
            }
            else if (task.Execute is not null && !IsSystemPath(snapshot.Folders, task.Execute))
            {
                orphanTasks.Add(task);
            }
        }

        return (orphanStartup, orphanServices, orphanTasks);
    }

    /// <summary>The draft owning the most specific (longest) non-shared directory containing <paramref name="path"/>.</summary>
    private static (EntityDraft? Owner, string? Directory) FindOwner(List<EntityDraft> drafts, string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return (null, null);
        }

        EntityDraft? bestOwner = null;
        string? bestDirectory = null;
        EntityDraft? sharedOwner = null;
        string? sharedDirectory = null;
        foreach (var draft in drafts)
        {
            foreach (var directory in draft.Directories)
            {
                if (!WindowsPath.IsSameOrUnder(path, directory.Path))
                {
                    continue;
                }

                if (directory.Role == DirectoryRole.SharedParent)
                {
                    // Only a fallback: a vendor folder (Program Files\Google) hosts helpers shared by several products.
                    sharedOwner ??= draft;
                    sharedDirectory ??= directory.Path;
                    continue;
                }

                if (bestDirectory is null || directory.Path.Length > bestDirectory.Length)
                {
                    bestOwner = draft;
                    bestDirectory = directory.Path;
                }
            }

            if (draft.InstallLocation is not null && draft.Seed != SeedKind.Directory && WindowsPath.IsSameOrUnder(path, draft.InstallLocation)
                && (bestDirectory is null || draft.InstallLocation.Length > bestDirectory.Length))
            {
                bestOwner = draft;
                bestDirectory = draft.InstallLocation;
            }
        }

        return bestOwner is not null ? (bestOwner, bestDirectory) : (sharedOwner, sharedDirectory);
    }

    private static bool IsSystemPath(WindowsKnownFolders folders, string path) => WindowsPath.IsSameOrUnder(path, folders.SystemRoot);

    /// <summary>
    /// Registry and package records are authoritative for themselves. Directory-seeded entities are as sure as their evidence;
    /// a residue verdict rests on the §9.5 facts (stale, cache-only) rather than on attribution evidence.
    /// </summary>
    private static ConfidenceLevel ComputeConfidence(EntityDraft draft, ClassificationResult classification, List<Reason> reasons)
    {
        if (draft.Seed != SeedKind.Directory)
        {
            return ConfidenceLevel.High;
        }

        if (draft.Directories.Any(d => d.ContainsExecutables))
        {
            return ConfidenceCalculator.Level(draft.Evidence);
        }

        if (classification.Type == AppType.SuspectedResidue)
        {
            var stale = reasons.Any(r => r.Kind == ReasonKind.LongUnmodified);
            var cacheOnly = reasons.Any(r => r.Kind == ReasonKind.OnlyCacheOrLogContent);
            return stale && cacheOnly ? ConfidenceLevel.High : ConfidenceLevel.Medium;
        }

        if (classification.Type == AppType.SystemComponent)
        {
            return ConfidenceLevel.High;
        }

        return draft.Evidence.Count > 0 ? ConfidenceCalculator.Level(draft.Evidence) : ConfidenceLevel.Unknown;
    }

    // ---- Finalisation --------------------------------------------------------------------------------------------

    private ApplicationEntity Finalize(EntityDraft draft, ScanSnapshot snapshot)
    {
        // Executable metadata for registered apps whose directories were attached.
        foreach (var exe in draft.ProgramDirectories.SelectMany(d => d.ExecutablePaths))
        {
            var metadata = snapshot.MetadataOf(exe);
            if (metadata is not null && !draft.Executables.Any(m => WindowsPath.AreEqual(m.Path, exe)))
            {
                draft.Executables.Add(metadata);
                draft.Sources |= DiscoverySource.Executable;
            }
        }

        draft.MainExecutable = _executables.SelectMainExecutable(draft, snapshot);
        var signatureSource = draft.MainExecutable ?? CommandLine.ExtractExecutable(draft.UninstallCommand);
        if (snapshot.SignatureOf(signatureSource) is { } signature)
        {
            draft.Signature = signature;
            draft.Sources |= DiscoverySource.Signature;
        }

        var mainMetadata = snapshot.MetadataOf(draft.MainExecutable);
        draft.Version ??= mainMetadata?.ProductVersion ?? mainMetadata?.FileVersion;
        draft.Publisher ??= mainMetadata?.CompanyName ?? (draft.Signature.IsValid ? draft.Signature.Publisher : null);
        draft.IconPath ??= draft.MainExecutable;

        var classification = _classifier.Classify(draft, snapshot.Folders, snapshot.ScanTime);
        var reasons = new List<Reason>(classification.Reasons);
        var confidence = ComputeConfidence(draft, classification, reasons);
        var risk = _risk.Assess(draft, classification.Type, confidence, reasons);

        if (draft.Seed == SeedKind.Directory && draft.Directories.Count > 0 && draft.Directories[0].AttributionEvidence.Count == 0)
        {
            // The seed directory is the entity itself; its attribution evidence is the entity's.
            draft.Directories[0] = draft.Directories[0] with { AttributionEvidence = draft.Evidence.ToArray() };
        }

        return new ApplicationEntity
        {
            Id = draft.Id,
            Name = draft.Name,
            Version = draft.Version,
            Publisher = draft.Publisher,
            AppType = classification.Type,
            InstallLocation = draft.InstallLocation ?? draft.ProgramDirectories.FirstOrDefault()?.Path,
            MainExecutable = draft.MainExecutable,
            IconPath = draft.IconPath,
            UninstallCommand = draft.UninstallCommand,
            QuietUninstallCommand = draft.QuietUninstallCommand,
            MsiProductCode = draft.MsiProductCode,
            AppxPackageFullName = draft.AppxPackageFullName,
            PreferredUninstallMethod = draft.PreferredUninstallMethod,
            Signature = draft.Signature,
            IsMicrosoft = classification.IsMicrosoft,
            RiskLevel = risk,
            DetectionConfidence = confidence,
            Sources = draft.Sources,
            InstallDate = draft.InstallDate,
            LastModified = draft.Directories.Select(d => d.LastWriteTime).Where(t => t is not null).DefaultIfEmpty(null).Max(),
            EstimatedSizeBytes = draft.EstimatedSizeBytes,
            Directories = draft.Directories.ToArray(),
            Processes = draft.Processes.ToArray(),
            Services = draft.Services.ToArray(),
            StartupItems = draft.StartupItems.ToArray(),
            ScheduledTasks = draft.ScheduledTasks.ToArray(),
            RegistryEntries = draft.RegistryEntries.ToArray(),
            Packages = draft.Packages.ToArray(),
            Executables = draft.Executables.ToArray(),
            Evidence = draft.Evidence.ToArray(),
            Reasons = reasons,
        };
    }
}
