namespace WinAppInspector.Core.Evidence;

/// <summary>Kinds of attribution evidence from the §10.2 table. Weights live in <see cref="EvidenceWeights"/>.</summary>
public enum EvidenceKind
{
    /// <summary>Registry InstallLocation matches the directory exactly.</summary>
    RegistryInstallLocationMatch = 0,
    /// <summary>ProductName of the main executable matches the application name.</summary>
    MainExecutableProductNameMatch = 1,
    /// <summary>Authenticode publisher matches the registry / package publisher.</summary>
    SignaturePublisherMatch = 2,
    /// <summary>UninstallString (or QuietUninstallString) points into the directory.</summary>
    UninstallStringPointsToDirectory = 3,
    /// <summary>A service image path is inside the directory.</summary>
    ServiceExecutableInDirectory = 4,
    /// <summary>A running process image is inside the directory.</summary>
    RunningProcessInDirectory = 5,
    /// <summary>A startup item command points into the directory.</summary>
    StartupItemPointsToDirectory = 6,
    /// <summary>A scheduled task action points into the directory.</summary>
    ScheduledTaskPointsToDirectory = 7,
    /// <summary>Folder name is similar to the application or publisher name.</summary>
    FolderNameSimilar = 8,
    /// <summary>Directory was modified recently (auxiliary only).</summary>
    RecentlyModified = 9,
    /// <summary>A shortcut (.lnk) target points into the directory (§7.9).</summary>
    ShortcutPointsToDirectory = 10,
    /// <summary>CompanyName of an executable in the directory matches the publisher.</summary>
    ExecutableCompanyNameMatch = 11,
    /// <summary>AppX package InstallLocation matches the directory exactly.</summary>
    PackageInstallLocationMatch = 12,
    /// <summary>The directory is an ancestor of the registered InstallLocation (vendor folder).</summary>
    ParentOfInstallLocation = 13,
    /// <summary>The DisplayIcon path points into the directory.</summary>
    DisplayIconPointsToDirectory = 14,
    /// <summary>Folder name equals the application, install-folder or publisher name after normalisation (stronger than "similar", still name-only).</summary>
    FolderNameExactMatch = 15,
}

/// <summary>Weight buckets from §10.2. The integer value is the score contribution.</summary>
public enum EvidenceWeight
{
    Auxiliary = 1,
    Low = 2,
    Medium = 4,
    MediumHigh = 6,
    High = 8,
}
