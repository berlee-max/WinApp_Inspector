namespace WinAppInspector.Core.Models;

/// <summary>
/// Risk of removing an application (§38 <c>RiskLevel</c>, §23 deletion protection).
/// This is an internal classification for gating actions; the UI does not render it as a red / yellow / green score (§29–30).
/// </summary>
public enum RiskLevel
{
    /// <summary>Not evaluated yet.</summary>
    Unknown = 0,

    /// <summary>Clear attribution, official uninstaller or clear residue, nothing running.</summary>
    Low = 1,

    /// <summary>Some uncertainty: no official uninstaller, medium confidence, or running processes that must be closed first.</summary>
    Medium = 2,

    /// <summary>Weak attribution or shared / referenced resources; one-click removal is refused (§23).</summary>
    High = 3,

    /// <summary>System component, driver or shared runtime: never offered for deletion (§9.7–9.9, §11).</summary>
    Protected = 4,
}

/// <summary>Confidence that a set of evidence attributes a directory or entity to an application (§10.2, §39).</summary>
public enum ConfidenceLevel
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}
