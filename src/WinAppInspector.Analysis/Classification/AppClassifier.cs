using WinAppInspector.Analysis.Matching;
using WinAppInspector.Analysis.Resolution;
using WinAppInspector.Core.Evidence;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Parsing;

namespace WinAppInspector.Analysis.Classification;

/// <summary>Result of classification: the §9 type, whether the publisher is Microsoft, and the §42 reasons.</summary>
public sealed record ClassificationResult(AppType Type, bool IsMicrosoft, IReadOnlyList<Reason> Reasons);

/// <summary>
/// Assigns one of the ten §9 categories to a resolved entity and records every fact behind the decision (§14.6, §42).
/// Pure logic over the draft; no I/O.
/// </summary>
public sealed class AppClassifier
{
    private readonly PublisherMatcher _publishers;
    private readonly KnownComponentCatalog _catalog;
    private readonly ResidueDetector _residue;

    public AppClassifier(PublisherMatcher publishers, KnownComponentCatalog catalog, ResidueDetector residue)
    {
        _publishers = publishers;
        _catalog = catalog;
        _residue = residue;
    }

    public ClassificationResult Classify(EntityDraft draft, WindowsKnownFolders folders, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(folders);

        var reasons = new List<Reason>();
        var signerIsMicrosoft = draft.Signature.IsValid && _publishers.IsMicrosoft(draft.Signature.Publisher);
        var isMicrosoft = _publishers.IsMicrosoft(draft.Publisher) || signerIsMicrosoft;
        if (isMicrosoft)
        {
            reasons.Add(new Reason(ReasonKind.PublisherIsMicrosoft, draft.Publisher ?? draft.Signature.Publisher));
        }

        AddSignatureReason(draft, reasons);
        AddRuntimeReasons(draft, reasons);

        var type = draft.Seed switch
        {
            SeedKind.Package => ClassifyPackage(draft, isMicrosoft, reasons),
            SeedKind.Registry => ClassifyRegistry(draft, folders, isMicrosoft, reasons),
            _ => ClassifyDirectory(draft, folders, isMicrosoft, reasons, now),
        };

        return new ClassificationResult(type, isMicrosoft, reasons);
    }

    private AppType ClassifyPackage(EntityDraft draft, bool isMicrosoft, List<Reason> reasons)
    {
        var package = draft.Packages[0];
        reasons.Add(new Reason(ReasonKind.AppxPackageFound, package.PackageFamilyName));
        if (package.InstallLocation is not null)
        {
            reasons.Add(new Reason(ReasonKind.InstallLocationKnown, package.InstallLocation));
        }

        if (draft.Packages.Any(p => p.IsFramework))
        {
            reasons.Add(new Reason(ReasonKind.AppxFrameworkPackage));
            return AppType.SharedRuntime;
        }

        if (draft.Packages.Any(p => p.NonRemovable == true))
        {
            reasons.Add(new Reason(ReasonKind.AppxNonRemovable));
            return AppType.SystemComponent;
        }

        if (isMicrosoft && draft.Packages.Any(p => string.Equals(p.SignatureKind, "System", StringComparison.OrdinalIgnoreCase)))
        {
            reasons.Add(new Reason(ReasonKind.RegistrySystemComponentFlag, "SignatureKind=System"));
            return AppType.SystemComponent;
        }

        if (_catalog.IsSharedRuntimeName(package.Name) || _catalog.IsSharedRuntimeName(draft.Name))
        {
            reasons.Add(new Reason(ReasonKind.KnownSharedRuntime, draft.Name));
            return AppType.SharedRuntime;
        }

        reasons.Add(new Reason(ReasonKind.OfficialUninstallerFound, "AppX"));
        return AppType.StoreApp;
    }

    private AppType ClassifyRegistry(EntityDraft draft, WindowsKnownFolders folders, bool isMicrosoft, List<Reason> reasons)
    {
        var entry = draft.RegistryEntries[0];
        reasons.Add(new Reason(ReasonKind.UninstallEntryFound, entry.KeyPath));
        if (draft.Publisher is not null)
        {
            reasons.Add(new Reason(ReasonKind.PublisherKnown, draft.Publisher));
        }

        if (draft.InstallLocation is not null)
        {
            reasons.Add(new Reason(ReasonKind.InstallLocationKnown, draft.InstallLocation));
        }

        if (draft.MsiProductCode is not null)
        {
            reasons.Add(new Reason(ReasonKind.InstalledViaMsi, draft.MsiProductCode));
        }

        reasons.Add(new Reason(draft.HasOfficialUninstaller ? ReasonKind.OfficialUninstallerFound : ReasonKind.OfficialUninstallerMissing,
            draft.HasOfficialUninstaller ? draft.PreferredUninstallMethod.ToString() : null));

        if (draft.IsHiddenRegistryEntry || draft.RegistryEntries.Any(e => e.SystemComponent))
        {
            reasons.Add(new Reason(ReasonKind.RegistrySystemComponentFlag, entry.SystemComponent ? "SystemComponent=1" : entry.ReleaseType ?? entry.ParentKeyName));
            return _catalog.IsSharedRuntimeName(draft.Name) ? AppType.SharedRuntime : AppType.SystemComponent;
        }

        if (_catalog.IsSharedRuntimeName(draft.Name))
        {
            reasons.Add(new Reason(ReasonKind.KnownSharedRuntime, draft.Name));
            return AppType.SharedRuntime;
        }

        if (_publishers.IsHardwareVendor(draft.Publisher) || _publishers.LooksLikeDriver(draft.Name))
        {
            reasons.Add(new Reason(ReasonKind.PublisherIsHardwareVendor, draft.Publisher ?? draft.Name));
            return AppType.HardwareOrDriver;
        }

        var location = draft.InstallLocation ?? draft.ProgramDirectories.FirstOrDefault()?.Path ?? CommandLine.ExtractExecutable(draft.UninstallCommand);
        if (location is not null && folders.IsInUserProfile(location))
        {
            reasons.Add(new Reason(ReasonKind.LocatedInUserProfile, location));
            return AppType.UserLevel;
        }

        if (location is not null && (folders.RootOf(location) is ScanRoot.ProgramFiles or ScanRoot.ProgramFilesX86))
        {
            reasons.Add(new Reason(ReasonKind.LocatedInProgramFiles, location));
        }

        return AppType.ThirdPartyInstalled;
    }

    private AppType ClassifyDirectory(EntityDraft draft, WindowsKnownFolders folders, bool isMicrosoft, List<Reason> reasons, DateTimeOffset now)
    {
        var primary = draft.Directories[0];
        reasons.Add(new Reason(ReasonKind.UninstallEntryMissing));

        if (_catalog.IsSystemFolder(primary.Root, primary.Path))
        {
            reasons.Add(new Reason(ReasonKind.RegistrySystemComponentFlag, "Known Windows / platform folder: " + WindowsPath.GetFileName(primary.Path)));
            return isMicrosoft || !_publishers.IsHardwareVendor(WindowsPath.GetFileName(primary.Path))
                ? AppType.SystemComponent
                : AppType.HardwareOrDriver;
        }

        if (!draft.Directories.Any(d => d.ContainsExecutables))
        {
            if (primary.Root == ScanRoot.Custom)
            {
                // The stale / cache-only residue heuristics (§9.5) assume an OS-defined application container. In a folder the
                // user added, an executable-free sub-folder is just as likely to be their data, so it stays 待判断 (§9.10).
                reasons.Add(new Reason(ReasonKind.MainExecutableMissing, primary.Path));
                return AppType.Undetermined;
            }

            var verdict = _residue.Evaluate(draft, now);
            reasons.AddRange(verdict.Reasons.Where(r => r.Kind != ReasonKind.UninstallEntryMissing));
            return verdict.Type;
        }

        reasons.Add(new Reason(ReasonKind.MainExecutableFound, draft.MainExecutable is null ? null : WindowsPath.GetFileName(draft.MainExecutable)));
        if (draft.Publisher is not null)
        {
            reasons.Add(new Reason(ReasonKind.PublisherKnown, draft.Publisher));
        }

        reasons.Add(new Reason(ReasonKind.OfficialUninstallerMissing));

        if (_catalog.IsSharedRuntimeName(draft.Name) || _catalog.IsSharedRuntimeName(WindowsPath.GetFileName(primary.Path)))
        {
            reasons.Add(new Reason(ReasonKind.KnownSharedRuntime, draft.Name));
            return AppType.SharedRuntime;
        }

        if (isMicrosoft && primary.Root is ScanRoot.ProgramFiles or ScanRoot.ProgramFilesX86 or ScanRoot.ProgramData)
        {
            // Unregistered Microsoft binaries under Program Files are platform pieces (Edge WebView, OneDrive setup, ...), not user apps.
            reasons.Add(new Reason(ReasonKind.RegistrySystemComponentFlag, "Microsoft-signed, unregistered"));
            return AppType.SystemComponent;
        }

        if (_publishers.IsHardwareVendor(draft.Publisher) || _publishers.LooksLikeDriver(draft.Name))
        {
            reasons.Add(new Reason(ReasonKind.PublisherIsHardwareVendor, draft.Publisher ?? draft.Name));
            return AppType.HardwareOrDriver;
        }

        if (draft.Directories.Count == 1)
        {
            reasons.Add(new Reason(ReasonKind.FilesInSingleDirectory, primary.Path));
        }

        if (folders.IsInUserProfile(primary.Path))
        {
            reasons.Add(new Reason(ReasonKind.LocatedInUserProfile, primary.Path));
            return AppType.UserLevel;
        }

        if (primary.Root is ScanRoot.ProgramFiles or ScanRoot.ProgramFilesX86)
        {
            reasons.Add(new Reason(ReasonKind.LocatedInProgramFiles, primary.Path));
        }

        return AppType.Portable;
    }

    private static void AddSignatureReason(EntityDraft draft, List<Reason> reasons)
    {
        switch (draft.Signature.Status)
        {
            case SignatureStatus.Valid:
                reasons.Add(new Reason(ReasonKind.SignatureValid, draft.Signature.Publisher));
                break;
            case SignatureStatus.Invalid:
                reasons.Add(new Reason(ReasonKind.SignatureInvalid, draft.Signature.Error));
                break;
            case SignatureStatus.NotSigned:
                reasons.Add(new Reason(ReasonKind.SignatureMissing));
                break;
            default:
                break;
        }
    }

    private static void AddRuntimeReasons(EntityDraft draft, List<Reason> reasons)
    {
        if (draft.Processes.Count > 0)
        {
            reasons.Add(new Reason(ReasonKind.RunningProcessFound, string.Join(", ", draft.Processes.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Take(3))));
        }

        if (draft.Services.Count > 0)
        {
            reasons.Add(new Reason(ReasonKind.ServiceFound, string.Join(", ", draft.Services.Select(s => s.Name).Take(3))));
        }

        if (draft.StartupItems.Count > 0)
        {
            reasons.Add(new Reason(ReasonKind.StartupItemFound, string.Join(", ", draft.StartupItems.Select(s => s.Name).Take(3))));
        }

        if (draft.ScheduledTasks.Count > 0)
        {
            reasons.Add(new Reason(ReasonKind.ScheduledTaskFound, string.Join(", ", draft.ScheduledTasks.Select(t => t.TaskName).Distinct(StringComparer.OrdinalIgnoreCase).Take(3))));
        }
    }
}
