using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;

namespace WinAppInspector.Core.Rules;

/// <summary>What the user asked to remove. Every target path is checked individually.</summary>
public sealed record DeletionRequest
{
    public required ApplicationEntity Application { get; init; }
    public required IReadOnlyList<string> TargetPaths { get; init; }
    /// <summary>The user has seen the evidence and confirmed (§5.1). Never defaults to true.</summary>
    public bool UserConfirmed { get; init; }
    /// <summary>Whether services referencing the target have been stopped / removed already.</summary>
    public bool ServicesHandled { get; init; }
    /// <summary>
    /// The user has read that attribution could not be established and takes responsibility for the removal (the §22 checklist
    /// of a 待判断 entry). Lifts <see cref="DeletionBlockerKind.UndeterminedAttribution"/> and <see cref="DeletionBlockerKind.LowConfidence"/>
    /// only; the "safe to delete" label stays forbidden (§9.10) and every other rule still applies.
    /// </summary>
    public bool AttributionAcknowledged { get; init; }
}

public enum DeletionBlockerKind
{
    /// <summary>§5.1: scan → auto-delete is forbidden; the user must confirm.</summary>
    UserConfirmationRequired = 0,
    /// <summary>§11: the target is a protected Windows / WindowsApps / root path.</summary>
    ProtectedPath = 1,
    /// <summary>§9.7–9.9: system components, drivers and shared runtimes are kept.</summary>
    ProtectedApplicationType = 2,
    /// <summary>§9.10: undetermined entities are not deleted unless the user explicitly acknowledges the missing attribution.</summary>
    UndeterminedAttribution = 3,
    /// <summary>§23: a process from the target is running.</summary>
    ApplicationRunning = 4,
    /// <summary>§23: a service still points into the target.</summary>
    ReferencedByService = 5,
    /// <summary>§23: attribution confidence is too low for removal.</summary>
    LowConfidence = 6,
    /// <summary>§5.2: an official uninstaller exists and must be used instead of manual deletion.</summary>
    OfficialUninstallerAvailable = 7,
    /// <summary>The target is not one of the application's known directories.</summary>
    PathNotOwnedByApplication = 8,
    /// <summary>§5.4: the target is a vendor / parent folder shared with other applications.</summary>
    SharedDirectory = 9,
}

/// <summary>One reason a deletion is refused, with the concrete target it applies to.</summary>
public sealed record DeletionBlocker(DeletionBlockerKind Kind, string? Target = null);

/// <summary>Outcome of <see cref="DeletionGuard.Evaluate"/>.</summary>
public sealed record DeletionVerdict(IReadOnlyList<DeletionBlocker> Blockers, bool CanShowSafeToDeleteLabel)
{
    public bool IsAllowed => Blockers.Count == 0;

    /// <summary>Blockers that do not depend on the user (i.e. everything except the missing confirmation).</summary>
    public IReadOnlyList<DeletionBlocker> HardBlockers =>
        Blockers.Where(b => b.Kind != DeletionBlockerKind.UserConfirmationRequired).ToArray();
}

/// <summary>
/// Enforces the deletion safety rules from §5.1, §5.4, §9.10, §11, §22 and §23 before any file, directory or
/// registry removal is executed. The <c>CleanupManager</c> must refuse to act unless <see cref="DeletionVerdict.IsAllowed"/> is true.
/// </summary>
public sealed class DeletionGuard
{
    private readonly ProtectedPathRule _protectedPaths;

    public DeletionGuard(ProtectedPathRule protectedPaths)
    {
        _protectedPaths = protectedPaths ?? throw new ArgumentNullException(nameof(protectedPaths));
    }

    public DeletionVerdict Evaluate(DeletionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var app = request.Application;
        var blockers = new List<DeletionBlocker>();

        if (!request.UserConfirmed)
        {
            blockers.Add(new DeletionBlocker(DeletionBlockerKind.UserConfirmationRequired));
        }

        if (app.AppType == AppType.Undetermined && !request.AttributionAcknowledged)
        {
            blockers.Add(new DeletionBlocker(DeletionBlockerKind.UndeterminedAttribution, app.Name));
        }
        else if (app.AppType.IsProtectedByDefault())
        {
            blockers.Add(new DeletionBlocker(DeletionBlockerKind.ProtectedApplicationType, app.AppType.ToString()));
        }
        else if (app.HasOfficialUninstaller && app.AppType != AppType.SuspectedResidue && app.AppType != AppType.AppCache)
        {
            // Manual deletion is only for portable apps, residue and user-level apps without an uninstaller (§22).
            blockers.Add(new DeletionBlocker(DeletionBlockerKind.OfficialUninstallerAvailable, app.PreferredUninstallMethod.ToString()));
        }

        if (app.DetectionConfidence is ConfidenceLevel.Unknown or ConfidenceLevel.Low && !request.AttributionAcknowledged)
        {
            blockers.Add(new DeletionBlocker(DeletionBlockerKind.LowConfidence, app.DetectionConfidence.ToString()));
        }

        foreach (var target in request.TargetPaths)
        {
            var protection = _protectedPaths.Check(target);
            if (protection.IsProtected)
            {
                blockers.Add(new DeletionBlocker(DeletionBlockerKind.ProtectedPath, target));
                continue;
            }

            var owners = app.Directories.Where(d => WindowsPath.IsSameOrUnder(target, d.Path)).ToArray();
            if (owners.Length == 0)
            {
                blockers.Add(new DeletionBlocker(DeletionBlockerKind.PathNotOwnedByApplication, target));
            }
            else if (owners.All(d => d.Role == DirectoryRole.SharedParent))
            {
                // Program Files\Vendor is owned "through" its children; deleting it would take other products with it.
                blockers.Add(new DeletionBlocker(DeletionBlockerKind.SharedDirectory, target));
            }
            else if (owners.Any(d => d.AttributionEvidence.Count > 0 && Evidence.ConfidenceCalculator.Score(d.AttributionEvidence) < Evidence.ConfidenceCalculator.MediumThreshold))
            {
                // The directory itself was linked to the application on weak evidence (§23 低可信度关联).
                blockers.Add(new DeletionBlocker(DeletionBlockerKind.LowConfidence, target));
            }

            foreach (var process in app.Processes)
            {
                if (WindowsPath.IsSameOrUnder(process.ExecutablePath, target))
                {
                    blockers.Add(new DeletionBlocker(DeletionBlockerKind.ApplicationRunning, $"{process.Name} (PID {process.ProcessId})"));
                }
            }

            if (!request.ServicesHandled)
            {
                foreach (var service in app.Services)
                {
                    if (WindowsPath.IsSameOrUnder(service.ExecutablePath, target))
                    {
                        blockers.Add(new DeletionBlocker(DeletionBlockerKind.ReferencedByService, service.Name));
                    }
                }
            }
        }

        var canLabelSafe = app.AppType.AllowsSafeToDeleteLabel()
                           && !blockers.Any(b => b.Kind != DeletionBlockerKind.UserConfirmationRequired);

        return new DeletionVerdict(blockers, canLabelSafe);
    }
}
