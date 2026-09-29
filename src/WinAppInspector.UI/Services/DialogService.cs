using System.Windows;
using WinAppInspector.Core.Actions;
using WinAppInspector.Core.Models;
using WinAppInspector.UI.Localization;
using WinAppInspector.UI.ViewModels;
using WinAppInspector.UI.Views;

namespace WinAppInspector.UI.Services;

public enum RunningProcessDecision
{
    Cancel = 0,
    Terminate = 1,
    Continue = 2,
}

/// <summary>Modal interactions the view models need. Kept behind an interface so the flow can be driven without a window.</summary>
public interface IDialogService
{
    bool Confirm(string title, string message, string? details = null);

    void Info(string title, string message, string? details = null);

    void Warn(string title, string message, string? details = null);

    /// <summary>§20.1 step 1: the application is running; ask whether to terminate, continue anyway, or cancel.</summary>
    RunningProcessDecision AskAboutRunningProcesses(ApplicationEntity application);

    /// <summary>§21–§22: the per-item checklist. Returns <c>null</c> when the user cancels.</summary>
    CleanupPlan? ShowCleanupChecklist(ApplicationEntity application, IReadOnlyList<CleanupCandidate> candidates, bool afterUninstall, bool recycleBinDefault);

    void ShowOperationLog();
}

/// <summary>WPF implementation using system message boxes for simple prompts and dedicated windows for the checklists.</summary>
public sealed class DialogService : IDialogService
{
    private readonly IServiceProvider _services;

    public DialogService(IServiceProvider services)
    {
        _services = services;
    }

    private static Window? Owner => Application.Current?.MainWindow;

    public bool Confirm(string title, string message, string? details = null)
    {
        var text = details is null ? message : message + Environment.NewLine + Environment.NewLine + details;
        var result = Owner is null
            ? MessageBox.Show(text, title, MessageBoxButton.OKCancel, MessageBoxImage.Question)
            : MessageBox.Show(Owner, text, title, MessageBoxButton.OKCancel, MessageBoxImage.Question);
        return result == MessageBoxResult.OK;
    }

    public void Info(string title, string message, string? details = null)
    {
        var text = details is null ? message : message + Environment.NewLine + Environment.NewLine + details;
        if (Owner is null)
        {
            MessageBox.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(Owner, text, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public void Warn(string title, string message, string? details = null)
    {
        var text = details is null ? message : message + Environment.NewLine + Environment.NewLine + details;
        if (Owner is null)
        {
            MessageBox.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            MessageBox.Show(Owner, text, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public RunningProcessDecision AskAboutRunningProcesses(ApplicationEntity application)
    {
        var processes = string.Join(Environment.NewLine, application.Processes.Take(8).Select(p => $"  {p.Name} (PID {p.ProcessId})"));
        var text = Localize.Format("Uninstall.RunningFormat", application.Name, application.Processes.Count) + Environment.NewLine + processes
                   + Environment.NewLine + Environment.NewLine + Localize.Get("Uninstall.RunningChoices");
        var result = Owner is null
            ? MessageBox.Show(text, Localize.Get("Uninstall.Action"), MessageBoxButton.YesNoCancel, MessageBoxImage.Warning)
            : MessageBox.Show(Owner, text, Localize.Get("Uninstall.Action"), MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        return result switch
        {
            MessageBoxResult.Yes => RunningProcessDecision.Terminate,
            MessageBoxResult.No => RunningProcessDecision.Continue,
            _ => RunningProcessDecision.Cancel,
        };
    }

    public CleanupPlan? ShowCleanupChecklist(ApplicationEntity application, IReadOnlyList<CleanupCandidate> candidates, bool afterUninstall, bool recycleBinDefault)
    {
        var viewModel = new CleanupViewModel(application, candidates, afterUninstall, recycleBinDefault);
        var window = new CleanupWindow(viewModel) { Owner = Owner };
        return window.ShowDialog() == true ? viewModel.BuildPlan() : null;
    }

    public void ShowOperationLog()
    {
        var log = (Core.Logging.IOperationLog)_services.GetService(typeof(Core.Logging.IOperationLog))!;
        var window = new OperationLogWindow(log) { Owner = Owner };
        window.ShowDialog();
    }
}
