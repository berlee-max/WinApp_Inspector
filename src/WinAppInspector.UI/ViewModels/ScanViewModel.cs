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

    public ObservableCollection<ScanError> Errors { get; } = [];

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

    public void BeginScan()
    {
        IsScanning = true;
        ProgressIndeterminate = true;
        ProgressValue = 0;
        CurrentItem = string.Empty;
        Errors.Clear();
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
            Errors.Add(error);
        }

        _allRows.Clear();
        foreach (var item in items)
        {
            foreach (var directory in item.Entity.Directories)
            {
                _allRows.Add(ToRow(item, directory));
            }
        }

        RefreshRows();
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
