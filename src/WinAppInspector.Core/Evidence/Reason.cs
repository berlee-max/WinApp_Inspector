namespace WinAppInspector.Core.Evidence;

/// <summary>
/// A fact that explains a classification or risk decision (§14.6, §42 "why").
/// The kind is machine-readable so the UI can localize it; <see cref="Detail"/> carries the concrete value (a path, a date, a name).
/// </summary>
public sealed record Reason(ReasonKind Kind, string? Detail = null)
{
    /// <summary>Whether this fact supports treating the entity as present and healthy (true) or as absent / residue (false).</summary>
    public bool IsPositive => Kind.IsPositive();
}

/// <summary>Facts used to justify an <see cref="Models.AppType"/> (§9) and a <see cref="Models.RiskLevel"/> (§23).</summary>
public enum ReasonKind
{
    // Registration
    UninstallEntryFound = 0,
    UninstallEntryMissing = 1,
    PublisherKnown = 2,
    InstallLocationKnown = 3,
    OfficialUninstallerFound = 4,
    OfficialUninstallerMissing = 5,
    InstalledViaMsi = 6,
    RegistrySystemComponentFlag = 7,

    // Files
    MainExecutableFound = 10,
    MainExecutableMissing = 11,
    OnlyCacheOrLogContent = 12,
    CacheLikeFolderName = 13,
    LongUnmodified = 14,
    LocatedInUserProfile = 15,
    LocatedInProgramFiles = 16,
    FilesInSingleDirectory = 17,

    // Runtime state
    RunningProcessFound = 20,
    NoRunningProcess = 21,
    ServiceFound = 22,
    NoService = 23,
    StartupItemFound = 24,
    NoStartupItem = 25,
    ScheduledTaskFound = 26,
    NoScheduledTask = 27,

    // Signature and publisher
    SignatureValid = 30,
    SignatureInvalid = 31,
    SignatureMissing = 32,
    PublisherIsMicrosoft = 33,
    PublisherIsHardwareVendor = 34,
    KnownSharedRuntime = 35,

    // Packages
    AppxPackageFound = 40,
    AppxFrameworkPackage = 41,
    AppxNonRemovable = 42,

    // Safety
    PathIsProtected = 50,
    ReferencedByOtherApplication = 51,
    LowAttributionConfidence = 52,
}

public static class ReasonKindExtensions
{
    /// <summary>Negative facts are the ones that argue for residue / absence; everything else is positive or neutral.</summary>
    public static bool IsPositive(this ReasonKind kind) => kind switch
    {
        ReasonKind.UninstallEntryMissing => false,
        ReasonKind.OfficialUninstallerMissing => false,
        ReasonKind.MainExecutableMissing => false,
        ReasonKind.OnlyCacheOrLogContent => false,
        ReasonKind.LongUnmodified => false,
        ReasonKind.NoRunningProcess => false,
        ReasonKind.NoService => false,
        ReasonKind.NoStartupItem => false,
        ReasonKind.NoScheduledTask => false,
        ReasonKind.SignatureInvalid => false,
        ReasonKind.SignatureMissing => false,
        ReasonKind.LowAttributionConfidence => false,
        _ => true,
    };
}
