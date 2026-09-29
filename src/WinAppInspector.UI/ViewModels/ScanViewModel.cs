using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Scanning;
using WinAppInspector.UI.Localization;

namespace WinAppInspector.UI.ViewModels;

/// <summary>One of the §16 stages, shown as a checklist while scanning.</summary>
public sealed partial class ScanStageViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isDone;

    [ObservableProperty]
    private string _detail = string.Empty;

    public required string Key { get; init; }
    public required string Label { get; init; }
}

/// <summary>§17 result row: one discovered directory and what it was attributed to.</summary>
public sealed record ScanResultRow(
    ApplicationItemViewModel Application,
    AppDirectory Directory,
    string Folder,
    string Owner,
    string Publisher,
    string SizeText,
    string MainExecutable,
    string RegisteredText,
    string RunningText,
    string VerdictText,
    string Category);

/// <summary>Kind of a runtime item that no application claimed.</summary>
public enum UnattributedKind
{
    Process = 0,
    StartupItem = 1,
    Service = 2,
    ScheduledTask = 3,
}

/// <summary>
/// A running process, startup entry, service or scheduled task whose executable lies outside every discovered
/// application directory (§7.5–7.8). Typically portable software started from a folder outside the scan scope;
/// the user can analyse that folder or add it to the scan scope. Nothing here can be deleted directly.
/// </summary>
public sealed record UnattributedRow(UnattributedKind Kind, string KindText, string Name, string ExecutablePath, string Detail, bool TargetExists)
{
    /// <summary>The folder to analyse (§18) for this item.</summary>
    public string Folder => WindowsPath.GetDirectoryName(ExecutablePath) ?? ExecutablePath;

    /// <summary>Why it is unattributed: the target is gone (a dangling registration, likely residue) or it lives outside the scan scope.</summary>
    public string ReasonText => Localize.Get(TargetExists ? "Unattributed.OutOfScope" : "Unattributed.Missing");
}

/// <summary>§34 error row with the cause localised when the scanner supplied a code.</summary>
public sealed record ScanErrorRow(string Source, string Target, string Text)
{
    public static ScanErrorRow From(ScanError error) => new(error.Source, error.Target, error.Code switch
    {
        ScanErrorCodes.CustomDirectoryMissing => Localize.Get("ScanError.CustomDirectoryMissing"),
        ScanErrorCodes.CustomDirectoryProtected => Localize.Format("ScanError.CustomDirectoryProtected", Localize.Get("Protection." + error.Detail)),
        ScanErrorCodes.CustomDirectoryLooksLikeProgram => Localize.Get("ScanError.CustomDirectoryLooksLikeProgram"),
        _ => error.Message,
    });
}

/// <summary>§17 result categories.</summary>
public enum ScanCategory
{
    All = 0,
    Registered = 1,
    Unregistered = 2,
    Portable = 3,
    Residue = 4,
    Unknown = 5,
}

/// <summary>Page two, 程序扫描 (§15–18): progress, per-directory results and "analyse this folder".</summary>
public sealed partial class ScanViewModel : ObservableObject
{
    private readonly DetailViewModel _detail;
    private readonly List<ScanResultRow> _allRows = [];

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _currentItem = string.Empty;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private bool _progressIndeterminate = true;

    [ObservableProperty]
    private ScanCategory _category = ScanCategory.All;

    [ObservableProperty]
    private ScanResultRow? _selectedRow;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<StatTile> _categoryStats = [];

    public ScanViewModel(DetailViewModel detail)
    {
        _detail = detail;
        Stages =
        [
            Stage(ScanStages.Registry), Stage(ScanStages.Appx), Stage(ScanStages.Directories), Stage(ScanStages.Executables),
            Stage(ScanStages.Signatures), Stage(ScanStages.Processes), Stage(ScanStages.Services), Stage(ScanStages.Startup),
            Stage(ScanStages.ScheduledTasks), Stage(ScanStages.Resolving), Stage(ScanStages.Sizes),
        ];
    }

    public ObservableCollection<ScanStageViewModel> Stages { get; }

    public ObservableCollection<ScanResultRow> Rows { get; } = [];

    public ObservableCollection<ScanErrorRow> Errors { get; } = [];

    /// <summary>Runtime items no application claimed; empty until a fresh scan has run (not part of the cache).</summary>
    public ObservableCollection<UnattributedRow> Unattributed { get; } = [];

    public IReadOnlyList<KeyValuePair<ScanCategory, string>> Categories { get; } =
        Enum.GetValues<ScanCategory>().Select(c => new KeyValuePair<ScanCategory, string>(c, Localize.Get("ScanCategory." + c))).ToArray();

    /// <summary>Raised by the page's 开始扫描 button; MainViewModel wires the actual scan.</summary>
    public event EventHandler? ScanRequested;

    /// <summary>Raised when the user picks a folder to analyse (§18).</summary>
    public event EventHandler<string>? AnalyzeFolderRequested;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start() => ScanRequested?.Invoke(this, EventArgs.Empty);

    private bool CanStart() => !IsScanning;

    [RelayCommand]
    private void AnalyzeFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Localize.Get("Scan.AnalyzeFolder") };
        if (dialog.ShowDialog() == true)
        {
            AnalyzeFolderRequested?.Invoke(this, dialog.FolderName);
        }
    }

    /// <summary>§18 for an unattributed item: analyse the folder its executable lives in.</summary>
    [RelayCommand(CanExecute = nameof(CanAnalyzeUnattributed))]
    private void AnalyzeUnattributed(UnattributedRow? row)
    {
        if (row is not null)
        {
            AnalyzeFolderRequested?.Invoke(this, row.Folder);
        }
    }

    private static bool CanAnalyzeUnattributed(UnattributedRow? row) => row?.TargetExists == true;

    public void BeginScan()
    {
        IsScanning = true;
        ProgressIndeterminate = true;
        ProgressValue = 0;
        CurrentItem = string.Empty;
        Errors.Clear();
        Unattributed.Clear();
        foreach (var stage in Stages)
        {
            stage.IsActive = false;
            stage.IsDone = false;
            stage.Detail = string.Empty;
        }

        StartCommand.NotifyCanExecuteChanged();
    }

    public void Report(ScanProgress progress)
    {
        var stage = Stages.FirstOrDefault(s => s.Key == progress.Stage);
        if (stage is null)
        {
            return;
        }

        foreach (var s in Stages)
        {
            if (s != stage && s.IsActive)
            {
                s.IsActive = false;
                s.IsDone = true;
            }
        }

        stage.IsActive = true;
        if (progress.Completed is { } done && progress.Total is { } total && total > 0)
        {
            stage.Detail = $"{done}/{total}";
            ProgressIndeterminate = false;
            ProgressValue = 100.0 * done / total;
        }
        else
        {
            ProgressIndeterminate = true;
        }

        CurrentItem = progress.CurrentItem ?? string.Empty;
    }

    public void EndScan(IReadOnlyList<ApplicationItemViewModel> items, IReadOnlyList<ScanError> errors)
    {
        foreach (var stage in Stages)
        {
            if (stage.Key != ScanStages.Sizes)
            {
                stage.IsActive = false;
                stage.IsDone = true;
            }
        }

        IsScanning = false;
        ProgressIndeterminate = false;
        ProgressValue = 100;
        CurrentItem = string.Empty;
        StartCommand.NotifyCanExecuteChanged();

        Errors.Clear();
        foreach (var error in errors)
        {
            Errors.Add(ScanErrorRow.From(error));
        }

        _allRows.Clear();
        foreach (var item in items)
        {
            foreach (var directory in item.Entity.Directories.Where(d => d.Role != DirectoryRole.SharedParent))
            {
                _allRows.Add(ToRow(item, directory));
            }
        }

        RefreshRows();
    }

    /// <summary>Replaces the unattributed list from a resolution. The inspector's own process is not an unknown program.</summary>
    public void SetUnattributed(IReadOnlyList<ProcessRecord> processes, IReadOnlyList<StartupItemRecord> startupItems, IReadOnlyList<ServiceRecord> services, IReadOnlyList<ScheduledTaskRecord> tasks)
    {
        Unattributed.Clear();
        var self = Environment.ProcessPath;

        // One row per executable; several PIDs of the same program are one item.
        foreach (var group in processes
                     .Where(p => p.ExecutablePath is not null && (self is null || !WindowsPath.AreEqual(p.ExecutablePath, self)))
                     .GroupBy(p => p.ExecutablePath!, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => WindowsPath.GetFileName(g.Key), StringComparer.CurrentCultureIgnoreCase))
        {
            var first = group.First();
            var product = first.ProductName ?? first.CompanyName;
            var pids = string.Join(", ", group.Select(p => p.ProcessId));
            var detail = product is null ? Localize.Format("Unattributed.PidsFormat", pids) : $"{product}（{Localize.Format("Unattributed.PidsFormat", pids)}）";
            // A running process always exists; the check matters for the registrations below.
            Unattributed.Add(new UnattributedRow(UnattributedKind.Process, Localize.Get("Unattributed.Process"), first.Name, group.Key, detail, TargetExists: true));
        }

        foreach (var item in startupItems.Where(i => i.ExecutablePath is not null).OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Unattributed.Add(new UnattributedRow(UnattributedKind.StartupItem, Localize.Get("Unattributed.StartupItem"), item.Name, item.ExecutablePath!, item.Location, Exists(item.ExecutablePath!)));
        }

        foreach (var service in services.Where(s => s.ExecutablePath is not null).OrderBy(s => s.DisplayName ?? s.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Unattributed.Add(new UnattributedRow(UnattributedKind.Service, Localize.Get("Unattributed.Service"), service.DisplayName ?? service.Name, service.ExecutablePath!, service.State ?? string.Empty, Exists(service.ExecutablePath!)));
        }

        foreach (var task in tasks.Where(t => t.Execute is not null).OrderBy(t => t.FullPath, StringComparer.CurrentCultureIgnoreCase))
        {
            Unattributed.Add(new UnattributedRow(UnattributedKind.ScheduledTask, Localize.Get("Unattributed.ScheduledTask"), task.FullPath, task.Execute!, task.State ?? string.Empty, Exists(task.Execute!)));
        }

        static bool Exists(string path)
        {
            try
            {
                return System.IO.File.Exists(path);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException)
            {
                return false;
            }
        }
    }

    public void MarkSizesDone()
    {
        var sizes = Stages.First(s => s.Key == ScanStages.Sizes);
        sizes.IsActive = false;
        sizes.IsDone = true;
        RefreshSizes();
    }

    public void RefreshSizes()
    {
        for (var i = 0; i < _allRows.Count; i++)
        {
            var row = _allRows[i];
            var directory = row.Application.Entity.Directories.FirstOrDefault(d => WindowsPath.AreEqual(d.Path, row.Directory.Path));
            if (directory is not null && directory.SizeBytes != row.Directory.SizeBytes)
            {
                _allRows[i] = ToRow(row.Application, directory);
            }
        }

        RefreshRows();
    }

    partial void OnCategoryChanged(ScanCategory value) => RefreshRows();

    partial void OnSelectedRowChanged(ScanResultRow? value)
    {
        if (value is not null)
        {
            _detail.Current = value.Application;
        }
    }

    private void RefreshRows()
    {
        Rows.Clear();
        foreach (var row in _allRows.Where(r => Category == ScanCategory.All || r.Category == Localize.Get("ScanCategory." + Category)))
        {
            Rows.Add(row);
        }

        CategoryStats =
        [
            new StatTile(Localize.Get("ScanCategory.Registered"), _allRows.Count(r => r.Category == Localize.Get("ScanCategory.Registered"))),
            new StatTile(Localize.Get("ScanCategory.Unregistered"), _allRows.Count(r => r.Category == Localize.Get("ScanCategory.Unregistered"))),
            new StatTile(Localize.Get("ScanCategory.Portable"), _allRows.Count(r => r.Category == Localize.Get("ScanCategory.Portable"))),
            new StatTile(Localize.Get("ScanCategory.Residue"), _allRows.Count(r => r.Category == Localize.Get("ScanCategory.Residue"))),
            new StatTile(Localize.Get("ScanCategory.Unknown"), _allRows.Count(r => r.Category == Localize.Get("ScanCategory.Unknown"))),
        ];
        SummaryText = Localize.Format("Scan.SummaryFormat", _allRows.Count, Rows.Count);
    }

    private static ScanResultRow ToRow(ApplicationItemViewModel item, AppDirectory directory)
    {
        var app = item.Entity;
        var registered = app.RegistryEntries.Count > 0 || app.Packages.Count > 0;
        var category = app.AppType switch
        {
            AppType.SuspectedResidue => ScanCategory.Residue,
            AppType.Undetermined => ScanCategory.Unknown,
            AppType.Portable => ScanCategory.Portable,
            _ when registered => ScanCategory.Registered,
            _ => ScanCategory.Unregistered,
        };

        return new ScanResultRow(
            item,
            directory,
            directory.Path,
            app.Name,
            app.Publisher ?? Localize.Get("Value.Unknown"),
            directory.SizeBytes is null ? Localize.Get("Size.Pending") : Localize.Bytes(directory.SizeBytes),
            WindowsPath.GetFileName(app.MainExecutable) is { Length: > 0 } exe ? exe : Localize.Get("Value.None"),
            Localize.Get(registered ? "Value.Yes" : "Value.No"),
            Localize.Get(app.IsRunning ? "Status.Running" : "Status.NotRunning"),
            Localize.AppType(app.AppType),
            Localize.Get("ScanCategory." + category));
    }

    private static ScanStageViewModel Stage(string key) => new() { Key = key, Label = Localize.Stage(key) };
}
