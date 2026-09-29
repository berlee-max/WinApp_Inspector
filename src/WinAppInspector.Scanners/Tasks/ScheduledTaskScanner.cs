using Microsoft.Extensions.Logging;
using Microsoft.Win32.TaskScheduler;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Scanning;

namespace WinAppInspector.Scanners.Tasks;

/// <summary>Enumerates the Task Scheduler library (§7.8). Tasks with several exec actions produce one record per action.</summary>
public sealed class ScheduledTaskScanner : IScanner<ScheduledTaskRecord>
{
    private readonly WindowsKnownFolders _folders;
    private readonly ILogger<ScheduledTaskScanner> _logger;

    public ScheduledTaskScanner(WindowsKnownFolders folders, ILogger<ScheduledTaskScanner> logger)
    {
        _folders = folders;
        _logger = logger;
    }

    public string Name => nameof(ScheduledTaskScanner);

    public System.Threading.Tasks.Task<ScanResult<ScheduledTaskRecord>> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken) =>
        System.Threading.Tasks.Task.Run(() => Scan(progress, cancellationToken), cancellationToken);

    private ScanResult<ScheduledTaskRecord> Scan(IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(new ScanProgress(ScanStages.ScheduledTasks));
        var items = new List<ScheduledTaskRecord>();
        var errors = new List<ScanError>();

        try
        {
            using var service = new TaskService();
            foreach (var task in service.AllTasks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (task)
                {
                    try
                    {
                        items.AddRange(Convert(task));
                    }
                    catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or FileNotFoundException or InvalidOperationException)
                    {
                        errors.Add(new ScanError(Name, task.Path, ex.Message, ex));
                    }
                }
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or TypeInitializationException)
        {
            _logger.LogWarning(ex, "Task Scheduler enumeration failed");
            errors.Add(new ScanError(Name, "TaskService", ex.Message, ex));
        }

        _logger.LogInformation("Scheduled task scan found {Count} exec actions with {Errors} errors", items.Count, errors.Count);
        return new ScanResult<ScheduledTaskRecord>(items, errors);
    }

    private IEnumerable<ScheduledTaskRecord> Convert(Microsoft.Win32.TaskScheduler.Task task)
    {
        var definition = task.Definition;
        var triggers = definition.Triggers.Count == 0 ? null : string.Join("; ", definition.Triggers.Select(t => t.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var author = definition.RegistrationInfo.Author;
        var state = task.State.ToString();
        var folder = WindowsPath.GetDirectoryName(task.Path) ?? "\\";

        var execActions = definition.Actions.OfType<ExecAction>().ToArray();
        if (execActions.Length == 0)
        {
            yield return new ScheduledTaskRecord
            {
                TaskName = task.Name,
                TaskPath = folder,
                Trigger = triggers,
                State = state,
                Author = author,
            };
            yield break;
        }

        foreach (var action in execActions)
        {
            var execute = action.Path;
            yield return new ScheduledTaskRecord
            {
                TaskName = task.Name,
                TaskPath = folder,
                Execute = string.IsNullOrWhiteSpace(execute) ? null : Core.Parsing.CommandLine.ResolveCommandExecutable(execute, _folders) ?? execute,
                Arguments = string.IsNullOrWhiteSpace(action.Arguments) ? null : action.Arguments,
                WorkingDirectory = string.IsNullOrWhiteSpace(action.WorkingDirectory) ? null : action.WorkingDirectory,
                Trigger = triggers,
                State = state,
                Author = author,
            };
        }
    }
}
