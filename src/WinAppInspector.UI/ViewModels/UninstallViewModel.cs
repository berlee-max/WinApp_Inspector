using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
/// Page three, 卸载与清理 (§20–§25). Drives the official uninstall flow
/// (running check → confirmation → optional restore point → uninstaller → residue rescan → per-item cleanup)
/// and the manual-removal flow for portable apps, residue and user-level apps without an uninstaller (§22).
/// Nothing is deleted without the user ticking the confirmation in the checklist.
/// </summary>
public sealed partial class UninstallViewModel : ObservableObject
{
    private readonly DetailViewModel _detail;
    private readonly IUninstallManager _uninstaller;
    private readonly IResidueScanner _residue;
    private readonly CleanupPlanner _planner;
    private readonly ICleanupManager _cleanup;
    private readonly IProcessController _processes;
    private readonly IRestorePointManager _restorePoints;
    private readonly IDialogService _dialogs;
    private readonly SettingsService _settings;
    private readonly ILogger<UninstallViewModel> _logger;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(ManualRemoveCommand))]
    private ApplicationItemViewModel? _selectedItem;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(ManualRemoveCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyText = string.Empty;

    public UninstallViewModel(
        DetailViewModel detail,
        IUninstallManager uninstaller,
        IResidueScanner residue,
        CleanupPlanner planner,
        ICleanupManager cleanup,
        IProcessController processes,
        IRestorePointManager restorePoints,
        IDialogService dialogs,
        SettingsService settings,
        ILogger<UninstallViewModel> logger)
    {
        _detail = detail;
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

    /// <summary>Applications with an official uninstaller (§20.1).</summary>
    public ObservableCollection<ApplicationItemViewModel> Uninstallable { get; } = [];

    /// <summary>§22: portable apps, clear residue and user-level apps without an uninstaller.</summary>
    public ObservableCollection<ApplicationItemViewModel> ManualCandidates { get; } = [];

    /// <summary>Raised after an uninstall or cleanup so the shell can rescan.</summary>
    public event EventHandler? RescanRequested;

    public void Load(IEnumerable<ApplicationItemViewModel> items)
    {
        Uninstallable.Clear();
        ManualCandidates.Clear();
        foreach (var item in items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var app = item.Entity;
            if (app.AppType.IsProtectedByDefault())
            {
                continue; // §9.7–9.9: no removal entry point at all
            }

            if (app.HasOfficialUninstaller)
            {
                Uninstallable.Add(item);
            }
            else if (app.AppType.IsEligibleForManualRemoval())
            {
                ManualCandidates.Add(item);
            }
        }
    }

    private bool CanUninstall() => !IsBusy && SelectedItem is { Entity.HasOfficialUninstaller: true };

    private bool CanManualRemove() => !IsBusy && SelectedItem is { } item && !item.Entity.HasOfficialUninstaller && item.Entity.AppType.IsEligibleForManualRemoval();

    [RelayCommand(CanExecute = nameof(CanUninstall))]
    private async Task UninstallAsync()
    {
        var item = SelectedItem;
        if (item is null)
        {
            return;
        }

        var app = item.Entity;
        IsBusy = true;
        try
        {
            // §20.1 step 1: running processes.
            if (app.IsRunning)
            {
                switch (_dialogs.AskAboutRunningProcesses(app))
                {
                    case RunningProcessDecision.Cancel:
                        return;
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

            // §20.1 step 2: confirmation with the exact command that will run (§5.1 evidence before action).
            var (method, command) = UninstallManager.ChooseRoute(app, preferQuiet: false);
            var confirmed = _dialogs.Confirm(
                Localize.Get("Uninstall.Action"),
                Localize.Format("Uninstall.ConfirmFormat", app.Name, Localize.UninstallMethod(method)),
                command + Environment.NewLine + Environment.NewLine + Localize.Get("Uninstall.SaveDataHint"));
            if (!confirmed)
            {
                return;
            }

            // §27: optional restore point; a failure is reported and the user decides whether to go on.
            if (_settings.Current.CreateRestorePointBeforeUninstall)
            {
                BusyText = Localize.Get("Uninstall.CreatingRestorePoint");
                var (created, error) = await _restorePoints.CreateAsync($"WinApp Inspector: uninstall {app.Name}", CancellationToken.None);
                if (!created && !_dialogs.Confirm(Localize.Get("Uninstall.Action"), Localize.Get("Uninstall.RestorePointFailed"), error))
                {
                    return;
                }
            }

            // §20.1 steps 3–4: run and wait.
            BusyText = Localize.Format("Uninstall.RunningUninstallerFormat", app.Name);
            var progress = new Progress<string>(text => BusyText = text);
            var result = await _uninstaller.UninstallAsync(new UninstallRequest { Application = app, UserConfirmed = true }, progress, CancellationToken.None);

            if (result.Outcome == UninstallOutcome.CancelledByUser)
            {
                _dialogs.Info(Localize.Get("Uninstall.Action"), Localize.Get("Uninstall.CancelledByUser"));
                return;
            }

            if (!result.Succeeded)
            {
                // §34: the concrete cause, then still offer the residue check because partial uninstalls are common.
                var proceed = _dialogs.Confirm(Localize.Get("Uninstall.Action"), Localize.Format("Uninstall.FailedFormat", app.Name), result.Error + Environment.NewLine + Environment.NewLine + Localize.Get("Uninstall.CheckResidueAnyway"));
                if (!proceed)
                {
                    return;
                }
            }
            else if (result.Outcome == UninstallOutcome.SucceededRestartRequired)
            {
                _dialogs.Info(Localize.Get("Uninstall.Action"), Localize.Get("Uninstall.RestartRequired"));
            }

            // §20.1 steps 5–6 / §21: rescan for residue and let the user choose.
            await CleanupAfterAsync(app, afterUninstall: true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(ex, "Uninstall flow failed for {App}", app.Name);
            _dialogs.Warn(Localize.Get("Uninstall.Action"), Localize.Format("Uninstall.FailedFormat", app.Name), ex.Message);
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
            RescanRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand(CanExecute = nameof(CanManualRemove))]
    private async Task ManualRemoveAsync()
    {
        var item = SelectedItem;
        if (item is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await CleanupAfterAsync(item.Entity, afterUninstall: false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(ex, "Manual removal failed for {App}", item.Entity.Name);
            _dialogs.Warn(Localize.Get("Uninstall.ManualAction"), Localize.Format("Cleanup.FailedFormat", item.Entity.Name), ex.Message);
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
            RescanRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void ShowOperationLog() => _dialogs.ShowOperationLog();

    private async Task CleanupAfterAsync(ApplicationEntity app, bool afterUninstall)
    {
        BusyText = Localize.Format("Uninstall.ScanningResidueFormat", app.Name);
        var report = await _residue.ScanAsync(app, CancellationToken.None);
        if (report.IsEmpty)
        {
            _dialogs.Info(Localize.Get(afterUninstall ? "Uninstall.Action" : "Uninstall.ManualAction"), Localize.Format(afterUninstall ? "Uninstall.NoResidueFormat" : "Cleanup.NothingFormat", app.Name));
            return;
        }

        var candidates = _planner.Build(app, report, afterUninstall);
        var plan = _dialogs.ShowCleanupChecklist(app, candidates, afterUninstall, _settings.Current.RecycleBinFirst);
        if (plan is null)
        {
            return;
        }

        var problems = _planner.Validate(plan);
        if (problems.Count > 0)
        {
            _dialogs.Warn(Localize.Get("Cleanup.Execute"), Localize.Get("Cleanup.Refused"), string.Join(Environment.NewLine, problems.Select(p => Localize.Get("Blocker." + p.Split(':')[^1].Trim()))));
            return;
        }

        BusyText = Localize.Get("Cleanup.Executing");
        var progress = new Progress<string>(target => BusyText = Localize.Format("Cleanup.RemovingFormat", target));
        var result = await _cleanup.ExecuteAsync(plan, progress, CancellationToken.None);

        var failed = result.Items.Where(i => !i.Succeeded).ToList();
        var summary = Localize.Format("Cleanup.SummaryFormat", result.SucceededCount, plan.Items.Count);
        if (failed.Count == 0)
        {
            _dialogs.Info(Localize.Get("Cleanup.Execute"), summary);
        }
        else
        {
            _dialogs.Warn(Localize.Get("Cleanup.Execute"), summary, string.Join(Environment.NewLine, failed.Select(f => $"{f.Item.Target}: {f.Error}")));
        }
    }

    partial void OnSelectedItemChanged(ApplicationItemViewModel? value)
    {
        if (value is not null)
        {
            _detail.Current = value;
        }
    }
}
