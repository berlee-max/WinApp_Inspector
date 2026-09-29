using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using WinAppInspector.Actions.Residue;
using WinAppInspector.Actions.Uninstall;
using WinAppInspector.Analysis.Cleanup;
using WinAppInspector.Core.Actions;
using WinAppInspector.Core.Models;
using WinAppInspector.UI.Localization;
using WinAppInspector.UI.Services;

namespace WinAppInspector.UI.ViewModels;

/// <summary>
/// Drives the uninstall / removal of one or several applications (§20–§25): one confirmation listing exactly what will run,
/// an optional restore point, then per application the running-process check, the official uninstaller, the residue rescan
/// and the per-item cleanup checklist. Applications without an uninstaller go straight to the §22 manual-removal checklist.
/// Nothing is deleted without the user ticking it in that checklist.
/// </summary>
public sealed partial class UninstallFlowViewModel : ObservableObject
{
    private readonly IUninstallManager _uninstaller;
    private readonly IResidueScanner _residue;
    private readonly CleanupPlanner _planner;
    private readonly ICleanupManager _cleanup;
    private readonly IProcessController _processes;
    private readonly IRestorePointManager _restorePoints;
    private readonly IDialogService _dialogs;
    private readonly SettingsService _settings;
    private readonly ILogger<UninstallFlowViewModel> _logger;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyText = string.Empty;

    public UninstallFlowViewModel(
        IUninstallManager uninstaller,
        IResidueScanner residue,
        CleanupPlanner planner,
        ICleanupManager cleanup,
        IProcessController processes,
        IRestorePointManager restorePoints,
        IDialogService dialogs,
        SettingsService settings,
        ILogger<UninstallFlowViewModel> logger)
    {
        _uninstaller = uninstaller;
        _residue = residue;
        _planner = planner;
        _cleanup = cleanup;
        _processes = processes;
        _restorePoints = restorePoints;
        _dialogs = dialogs;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Raised after the flow so the shell can rescan.</summary>
    public event EventHandler? Completed;

    /// <summary>Runs the flow for every actionable item; protected types are skipped with a note (§9.7–9.9).</summary>
    public async Task RunAsync(IReadOnlyList<ApplicationItemViewModel> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var work = items.Where(i => i.IsActionable).ToList();
        if (work.Count == 0)
        {
            _dialogs.Info(Localize.Get("Uninstall.Action"), Localize.Get("Uninstall.NothingActionable"));
            return;
        }

        var lines = work.Select(i =>
        {
            var app = i.Entity;
            if (app.HasOfficialUninstaller)
            {
                var (method, command) = UninstallManager.ChooseRoute(app, preferQuiet: false);
                return $"{app.Name}  —  {Localize.UninstallMethod(method)}\n    {command}";
            }

            return $"{app.Name}  —  {Localize.Get("Uninstall.ManualAction")}";
        });
        var confirmed = _dialogs.Confirm(
            Localize.Get("Uninstall.Action"),
            Localize.Format("Uninstall.ConfirmManyFormat", work.Count),
            string.Join(Environment.NewLine, lines) + Environment.NewLine + Environment.NewLine + Localize.Get("Uninstall.SaveDataHint"));
        if (!confirmed)
        {
            return;
        }

        IsBusy = true;
        var succeeded = 0;
        var failed = new List<string>();
        try
        {
            // §27: one restore point for the whole batch; a failure is reported and the user decides whether to go on.
            if (_settings.Current.CreateRestorePointBeforeUninstall)
            {
                BusyText = Localize.Get("Uninstall.CreatingRestorePoint");
                var (created, error) = await _restorePoints.CreateAsync("WinApp Inspector: uninstall", CancellationToken.None);
                if (!created && !_dialogs.Confirm(Localize.Get("Uninstall.Action"), Localize.Get("Uninstall.RestorePointFailed"), error))
                {
                    return;
                }
            }

            foreach (var item in work)
            {
                var app = item.Entity;
                try
                {
                    var ok = app.HasOfficialUninstaller
                        ? await UninstallOneAsync(app)
                        : await CleanupAfterAsync(app, afterUninstall: false);
                    if (ok)
                    {
                        succeeded++;
                    }
                    else
                    {
                        failed.Add(app.Name);
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _logger.LogError(ex, "Uninstall flow failed for {App}", app.Name);
                    failed.Add($"{app.Name}: {ex.Message}");
                }
            }

            if (work.Count > 1)
            {
                var summary = Localize.Format("Uninstall.BatchSummaryFormat", succeeded, work.Count);
                if (failed.Count == 0)
                {
                    _dialogs.Info(Localize.Get("Uninstall.Action"), summary);
                }
                else
                {
                    _dialogs.Warn(Localize.Get("Uninstall.Action"), summary, string.Join(Environment.NewLine, failed));
                }
            }
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
            Completed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>§20.1 for one application. Returns false when the user cancelled or the uninstaller failed.</summary>
    private async Task<bool> UninstallOneAsync(ApplicationEntity app)
    {
        // Step 1: running processes.
        if (app.IsRunning)
        {
            switch (_dialogs.AskAboutRunningProcesses(app))
            {
                case RunningProcessDecision.Cancel:
                    return false;
                case RunningProcessDecision.Terminate:
                    foreach (var process in app.Processes)
                    {
                        BusyText = Localize.Format("Uninstall.TerminatingFormat", process.Name);
                        var (ok, error) = await _processes.TerminateAsync(process.ProcessId, process.Name, userConfirmed: true, CancellationToken.None);
                        if (!ok)
                        {
                            _dialogs.Warn(Localize.Get("Uninstall.Action"), Localize.Format("Uninstall.TerminateFailedFormat", process.Name), error);
                        }
                    }

                    break;
                default:
                    break;
            }
        }

        // Steps 3–4: run the official uninstaller and wait.
        BusyText = Localize.Format("Uninstall.RunningUninstallerFormat", app.Name);
        var progress = new Progress<string>(text => BusyText = text);
        var result = await _uninstaller.UninstallAsync(new UninstallRequest { Application = app, UserConfirmed = true }, progress, CancellationToken.None);

        if (result.Outcome == UninstallOutcome.CancelledByUser)
        {
            _dialogs.Info(Localize.Get("Uninstall.Action"), Localize.Format("Uninstall.CancelledByUserFormat", app.Name));
            return false;
        }

        if (!result.Succeeded)
        {
            // §34: the concrete cause, then still offer the residue check because partial uninstalls are common.
            var proceed = _dialogs.Confirm(Localize.Get("Uninstall.Action"), Localize.Format("Uninstall.FailedFormat", app.Name), result.Error + Environment.NewLine + Environment.NewLine + Localize.Get("Uninstall.CheckResidueAnyway"));
            if (!proceed)
            {
                return false;
            }
        }
        else if (result.Outcome == UninstallOutcome.SucceededRestartRequired)
        {
            _dialogs.Info(Localize.Get("Uninstall.Action"), Localize.Get("Uninstall.RestartRequired"));
        }

        // Steps 5–6 / §21: rescan for residue and let the user choose.
        await CleanupAfterAsync(app, afterUninstall: true);
        return result.Succeeded;
    }

    /// <summary>§21–§22: residue rescan, checklist, execution. Returns false only when the cleanup itself was refused or failed.</summary>
    private async Task<bool> CleanupAfterAsync(ApplicationEntity app, bool afterUninstall)
    {
        BusyText = Localize.Format("Uninstall.ScanningResidueFormat", app.Name);
        var report = await _residue.ScanAsync(app, CancellationToken.None);
        if (report.IsEmpty)
        {
            _dialogs.Info(Localize.Get(afterUninstall ? "Uninstall.Action" : "Uninstall.ManualAction"), Localize.Format(afterUninstall ? "Uninstall.NoResidueFormat" : "Cleanup.NothingFormat", app.Name));
            return true;
        }

        var candidates = _planner.Build(app, report, afterUninstall);
        var plan = _dialogs.ShowCleanupChecklist(app, candidates, afterUninstall, _settings.Current.RecycleBinFirst);
        if (plan is null)
        {
            return afterUninstall;
        }

        var problems = _planner.Validate(plan);
        if (problems.Count > 0)
        {
            var lines = problems.Select(p =>
            {
                var colon = p.LastIndexOf(':');
                var target = colon < 0 ? null : p[..colon].Trim();
                var codes = (colon < 0 ? p : p[(colon + 1)..]).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var text = string.Join(", ", codes.Select(c => Localize.Get("Blocker." + c)));
                return target is null ? text : $"{target}: {text}";
            });
            _dialogs.Warn(Localize.Get("Cleanup.Execute"), Localize.Get("Cleanup.Refused"), string.Join(Environment.NewLine, lines));
            return false;
        }

        BusyText = Localize.Get("Cleanup.Executing");
        var progress = new Progress<string>(target => BusyText = Localize.Format("Cleanup.RemovingFormat", target));
        var result = await _cleanup.ExecuteAsync(plan, progress, CancellationToken.None);

        var failed = result.Items.Where(i => !i.Succeeded).ToList();
        var summary = Localize.Format("Cleanup.SummaryFormat", result.SucceededCount, plan.Items.Count);
        if (failed.Count == 0)
        {
            _dialogs.Info(Localize.Get("Cleanup.Execute"), summary);
            return true;
        }

        _dialogs.Warn(Localize.Get("Cleanup.Execute"), summary, string.Join(Environment.NewLine, failed.Select(f => $"{f.Item.Target}: {f.Error}")));
        return false;
    }
}
