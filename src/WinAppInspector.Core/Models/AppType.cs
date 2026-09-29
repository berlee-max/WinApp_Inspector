namespace WinAppInspector.Core.Models;

/// <summary>
/// The ten application categories from requirements §9.
/// The numeric values are stable and may be persisted (§33 cache).
/// </summary>
public enum AppType
{
    /// <summary>§9.10 待判断 — attribution could not be established. Must never be shown as "safe to delete".</summary>
    Undetermined = 0,

    /// <summary>§9.1 第三方应用 — a normally installed third-party program with an uninstall registry entry.</summary>
    ThirdPartyInstalled = 1,

    /// <summary>§9.2 商店应用 — Microsoft Store / UWP / MSIX package.</summary>
    StoreApp = 2,

    /// <summary>§9.3 便携应用 — portable / "green" program with executables but no uninstall entry.</summary>
    Portable = 3,

    /// <summary>§9.4 用户级应用 — installed under the user profile (AppData\Local\Programs, AppData\Roaming, ...).</summary>
    UserLevel = 4,

    /// <summary>§9.5 疑似残留 — leftovers of a program that appears to have been uninstalled.</summary>
    SuspectedResidue = 5,

    /// <summary>§9.6 应用缓存 — cache / logs / temp directory whose owning application has been identified.</summary>
    AppCache = 6,

    /// <summary>§9.7 硬件或驱动组件 — driver or hardware vendor component. Removal is not recommended.</summary>
    HardwareOrDriver = 7,

    /// <summary>§9.8 共享运行组件 — shared runtime such as VC++ Redistributable, .NET, WebView2. Removal is not recommended.</summary>
    SharedRuntime = 8,

    /// <summary>§9.9 系统组件 — Windows / Microsoft system component. Hidden by default.</summary>
    SystemComponent = 9,
}

/// <summary>Helpers describing how each <see cref="AppType"/> must be treated by the safety rules (§5.4, §9, §11).</summary>
public static class AppTypeExtensions
{
    /// <summary>
    /// Types that are kept by default and never get a direct delete entry point
    /// (system components, drivers / hardware components, shared runtimes).
    /// </summary>
    public static bool IsProtectedByDefault(this AppType type) => type switch
    {
        AppType.SystemComponent => true,
        AppType.HardwareOrDriver => true,
        AppType.SharedRuntime => true,
        _ => false,
    };

    /// <summary>§9.10: the "safe to delete" label is forbidden for undetermined entities.</summary>
    public static bool AllowsSafeToDeleteLabel(this AppType type) =>
        type != AppType.Undetermined && !type.IsProtectedByDefault();

    /// <summary>
    /// §22: manual removal applies to portable apps, clear residue and user-level apps without an uninstaller. Undetermined
    /// folders are included as well, at the owner's request: they go through the same per-item checklist, but only after the
    /// user acknowledges that attribution is unknown (<c>DeletionRequest.AttributionAcknowledged</c>), and they are never
    /// labelled "safe to delete" (§9.10).
    /// </summary>
    public static bool IsEligibleForManualRemoval(this AppType type) => type switch
    {
        AppType.Portable => true,
        AppType.SuspectedResidue => true,
        AppType.UserLevel => true,
        AppType.AppCache => true,
        AppType.Undetermined => true,
        _ => false,
    };

    /// <summary>Whether the entity counts as Microsoft / Windows for the overview filters (§13.3).</summary>
    public static bool IsSystemLike(this AppType type) =>
        type is AppType.SystemComponent or AppType.SharedRuntime;
}
