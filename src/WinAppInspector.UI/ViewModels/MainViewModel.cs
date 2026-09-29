using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WinAppInspector.Analysis.Export;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Scanning;
using WinAppInspector.UI.Localization;
using WinAppInspector.UI.Services;

namespace WinAppInspector.UI.ViewModels;

/// <summary>What the window shows: the start page, the scan in progress, the result summary, or the app manager.</summary>
public enum ShellStage
{
    Home = 0,
    Scanning = 1,
    Summary = 2,
    Manager = 3,
}

/// <summary>Shell view model: stage, the scan lifecycle, the result summary and the bulk actions shared by the two pages.</summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IScanService _scanService;
    private readonly IconService _icons;
    private readonly SettingsService _settings;
    private readonly ScanCache _cache;
    private readonly IDialogService _dialogs;
    private readonly ILogger<MainViewModel> _logger;
    private readonly Dictionary<string, ApplicationItemViewModel> _itemsById = new(StringComparer.OrdinalIgnoreCase);
    private string? _pendingAnalyzePath;
    private DateTimeOffset? _lastScanTime;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _sizeCts;
    private ScanSnapshot? _lastSnapshot;
    private IReadOnlyList<ScanError> _lastErrors = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHome))]
    [NotifyPropertyChangedFor(nameof(IsScanning))]
    [NotifyPropertyChangedFor(nameof(IsSummary))]
    [NotifyPropertyChangedFor(nameof(IsManager))]
    [NotifyPropertyChangedFor(nameof(IsHeroVisible))]
    [NotifyCanExecuteChangedFor(nameof(StartScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenManagerCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallCheckedCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallCurrentCommand))]
    private ShellStage _stage = ShellStage.Home;

    // Scan progress (hero page)
    [ObservableProperty]
    private string _scanStageText = string.Empty;

    [ObservableProperty]
    private string _scanDetailText = string.Empty;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private bool _progressIndeterminate = true;

    // Summary (hero page after a scan)
    [ObservableProperty]
    private int _totalApps;

    [ObservableProperty]
    private int _runningCount;

    [ObservableProperty]
    private int _portableRunningCount;

    [ObservableProperty]
    private int _residueCount;

    [ObservableProperty]
    private string _residueSizeText = string.Empty;

    [ObservableProperty]
    private int _undeterminedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrors))]
    private int _errorCount;

    [ObservableProperty]
    private string _lastScanText = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public MainViewModel(
        IScanService scanService,
        IconService icons,
        SettingsService settings,
        ScanCache cache,
        IDialogService dialogs,
        AppManagerViewModel manager,
        UninstallFlowViewModel uninstall,
        DetailViewModel detail,
        ILogger<MainViewModel> logger)
    {
        _scanService = scanService;
        _icons = icons;
        _settings = settings;
        _cache = cache;
        _dialogs = dialogs;
        Manager = manager;
        UninstallFlow = uninstall;
        Detail = detail;
        _logger = logger;

        Manager.ShowSystemComponents = !settings.Current.HideSystemComponents;
        UninstallFlow.Completed += (_, _) =>
        {
            if (StartScanCommand.CanExecute(null))
            {
                StartScanCommand.Execute(null);
            }
        };
        UninstallFlow.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(UninstallFlowViewModel.IsBusy) or nameof(UninstallFlowViewModel.BusyText))
            {
                StatusText = UninstallFlow.IsBusy ? UninstallFlow.BusyText : string.Empty;
                UninstallCheckedCommand.NotifyCanExecuteChanged();
                UninstallCurrentCommand.NotifyCanExecuteChanged();
                StartScanCommand.NotifyCanExecuteChanged();
            }
        };
        Manager.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppManagerViewModel.CheckedCount))
            {
                UninstallCheckedCommand.NotifyCanExecuteChanged();
            }
        };
        Detail.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DetailViewModel.Current))
            {
                UninstallCurrentCommand.NotifyCanExecuteChanged();
            }
        };
    }

    public AppManagerViewModel Manager { get; }
    public UninstallFlowViewModel UninstallFlow { get; }
    public DetailViewModel Detail { get; }

    public bool IsHome => Stage == ShellStage.Home;
    public bool IsScanning => Stage == ShellStage.Scanning;
    public bool IsSummary => Stage == ShellStage.Summary;
    public bool IsManager => Stage == ShellStage.Manager;

    /// <summary>The hero (dark) page hosts home, scanning and summary; the manager is the light page.</summary>
    public bool IsHeroVisible => Stage != ShellStage.Manager;

    public bool HasErrors => ErrorCount > 0;

    public bool HasResults => _itemsById.Count > 0;

    /// <summary>Raised when the settings dialog should be shown; the window owns dialog creation.</summary>
    public event EventHandler? SettingsRequested;

    public event EventHandler? AboutRequested;

    /// <summary>§19: a path handed over by the Explorer context menu; analysed once a scan has finished.</summary>
    public void RequestAnalyzeOnStartup(string path)
    {
        _pendingAnalyzePath = path;
        if (StartScanCommand.CanExecute(null))
        {
            StartScanCommand.Execute(null);
        }
    }

    /// <summary>§33: show the previous results immediately; the user decides when to rescan.</summary>
    public async Task LoadCacheAsync()
    {
        var cached = await _cache.LoadAsync(CancellationToken.None);
        // Re-checked after the await: a scan started meanwhile (Explorer --analyze, or the button) must not be overwritten.
        if (cached is null || cached.Applications.Count == 0 || _itemsById.Count > 0 || _scanCts is not null || Stage == ShellStage.Scanning)
        {
            return;
        }

        LoadResults(cached.Applications, []);
        _lastScanTime = cached.ScanTime;
        LastScanText = Localize.Format("Status.CachedScanFormat", FormatTime(cached.ScanTime));
        Stage = ShellStage.Summary;
    }

    // ---- Navigation ----------------------------------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanOpenManager))]
    private void OpenManager(string? category)
    {
        if (category is not null && Enum.TryParse<CategoryKind>(category, out var kind))
        {
            var target = Manager.PrimaryCategories.Concat(Manager.SourceCategories).FirstOrDefault(c => c.Kind == kind);
            if (target is not null)
            {
                Manager.SelectCategoryCommand.Execute(target);
            }
        }

        Stage = ShellStage.Manager;
    }

    private bool CanOpenManager() => HasResults && Stage != ShellStage.Scanning;

    [RelayCommand]
    private void BackToSummary()
    {
        Detail.Current = null;
        Stage = HasResults ? ShellStage.Summary : ShellStage.Home;
    }

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenAbout() => AboutRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenOperationLog() => _dialogs.ShowOperationLog();

    [RelayCommand]
    private void ShowErrors()
    {
        if (_lastErrors.Count == 0)
        {
            return;
        }

        var lines = _lastErrors.Take(40).Select(e => $"{e.Source}: {e.Target}  —  {ScanErrorRow.From(e).Text}");
        _dialogs.Info(Localize.Get("Scan.Errors"), Localize.Format("Scan.ErrorsFormat", _lastErrors.Count), string.Join(Environment.NewLine, lines));
    }

    /// <summary>Called by the window after the settings dialog closes with Save.</summary>
    public void SettingsSaved()
    {
        Manager.ShowSystemComponents = !_settings.Current.HideSystemComponents;
        Manager.RefreshCounts();
    }

    // ---- Actions -------------------------------------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanUninstallChecked))]
    private async Task UninstallCheckedAsync()
    {
        var items = Manager.CheckedItems;
        if (items.Count == 0)
        {
            return;
        }

        await UninstallFlow.RunAsync(items);
    }

    private bool CanUninstallChecked() => Manager.CheckedCount > 0 && !UninstallFlow.IsBusy && Stage == ShellStage.Manager;

    [RelayCommand(CanExecute = nameof(CanUninstallCurrent))]
    private async Task UninstallCurrentAsync()
    {
        if (Detail.Current is { } item)
        {
            await UninstallFlow.RunAsync([item]);
        }
    }

    private bool CanUninstallCurrent() => Detail.Current is { IsActionable: true } && !UninstallFlow.IsBusy;

    [RelayCommand]
    private void AnalyzeFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Localize.Get("Manager.AnalyzeFolder") };
        if (dialog.ShowDialog() == true)
        {
            _ = AnalyzeFolderAsync(dialog.FolderName);
        }
    }

    [RelayCommand]
    private void ExportReport()
    {
        if (_itemsById.Count == 0)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Localize.Get("Export.Title"),
            FileName = "WinAppInspector-" + DateTime.Now.ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture),
            Filter = Localize.Get("Export.Filter"),
            DefaultExt = ".csv",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var format = System.IO.Path.GetExtension(dialog.FileName).ToUpperInvariant() switch
        {
            ".JSON" => ReportFormat.Json,
            ".HTML" or ".HTM" => ReportFormat.Html,
            _ => ReportFormat.Csv,
        };

        var labels = new ReportLabels
        {
            Title = Localize.Get("Export.ReportTitle"),
            Name = Localize.Get("Column.Name"),
            Version = Localize.Get("Column.Version"),
            Publisher = Localize.Get("Column.Publisher"),
            Type = Localize.Get("Column.Type"),
            InstallLocation = Localize.Get("Column.InstallLocation"),
            Status = Localize.Get("Column.Status"),
            IsRunning = Localize.Get("Column.Running"),
            UninstallMethod = Localize.Get("Column.UninstallMethod"),
            DiskUsage = Localize.Get("Column.DiskUsage"),
            Yes = Localize.Get("Value.Yes"),
            No = Localize.Get("Value.No"),
            ScanTime = Localize.Get("Export.ScanTime"),
            GeneratedBy = Localize.Get("Export.GeneratedBy"),
            TypeText = Localize.AppType,
            UninstallMethodText = Localize.UninstallMethod,
            StatusText = a => new ApplicationItemViewModel(a, _icons).StatusText,
            SizeText = b => b is null ? string.Empty : Localize.Bytes(b),
        };

        try
        {
            // Export what the user sees in the manager (category, search, sort); from the summary page, everything listed.
            var visible = Stage == ShellStage.Manager
                ? Manager.View.Cast<ApplicationItemViewModel>().Select(i => i.Entity).ToList()
                : _itemsById.Values.Where(i => !i.IsSystemLike).Select(i => i.Entity).ToList();
            var content = ReportExporter.Export(visible, format, labels, _lastScanTime ?? DateTimeOffset.Now);
            System.IO.File.WriteAllText(dialog.FileName, content, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            StatusText = Localize.Format("Export.DoneFormat", visible.Count, dialog.FileName);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            _dialogs.Warn(Localize.Get("Export.Title"), Localize.Format("Export.FailedFormat", dialog.FileName), ex.Message);
        }
    }

    // ---- Scan lifecycle ------------------------------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanStartScan))]
    private async Task StartScanAsync()
    {
        if (Stage == ShellStage.Scanning || _scanCts is not null)
        {
            return;
        }

        _sizeCts?.Cancel();
        var cts = new CancellationTokenSource();
        _scanCts = cts;
        Detail.Current = null;
        Stage = ShellStage.Scanning;
        ProgressIndeterminate = true;
        ProgressValue = 0;
        ScanStageText = Localize.Get("Status.Scanning");
        ScanDetailText = string.Empty;
        StatusText = string.Empty;

        var progress = new Progress<ScanProgress>(p =>
        {
            ScanStageText = Localize.Stage(p.Stage);
            ScanDetailText = p.CurrentItem ?? string.Empty;
            if (p.Completed is { } done && p.Total is { } total && total > 0)
            {
                ProgressIndeterminate = false;
                ProgressValue = 100.0 * done / total;
            }
            else
            {
                ProgressIndeterminate = true;
            }
        });

        try
        {
            var outcome = await _scanService.ScanAsync(progress, cts.Token);
            _lastSnapshot = outcome.Snapshot;
            _lastScanTime = outcome.Snapshot.ScanTime;
            LoadResults(outcome.Resolution.Applications, outcome.Snapshot.Errors);
            LastScanText = Localize.Format("Status.LastScanFormat", FormatTime(outcome.Snapshot.ScanTime));
            Stage = ShellStage.Summary;
            _ = ComputeSizesAsync(outcome.Resolution.Applications);

            if (_pendingAnalyzePath is { } pending)
            {
                _pendingAnalyzePath = null;
                Stage = ShellStage.Manager;
                await AnalyzeFolderAsync(System.IO.File.Exists(pending) ? System.IO.Path.GetDirectoryName(pending) ?? pending : pending);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = Localize.Get("Status.Cancelled");
            Stage = HasResults ? ShellStage.Summary : ShellStage.Home;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // §34: show the concrete cause.
            _logger.LogError(ex, "Scan failed");
            Stage = HasResults ? ShellStage.Summary : ShellStage.Home;
            _dialogs.Warn(Localize.Get("Home.Scan"), Localize.Get("Status.ScanFailed"), ex.Message);
        }
        finally
        {
            cts.Dispose();
            if (ReferenceEquals(_scanCts, cts))
            {
                _scanCts = null;
            }

            OpenManagerCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanStartScan() => Stage != ShellStage.Scanning && !UninstallFlow.IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancelScan))]
    private void CancelScan() => _scanCts?.Cancel();

    private bool CanCancelScan() => Stage == ShellStage.Scanning;

    private void LoadResults(IReadOnlyList<ApplicationEntity> applications, IReadOnlyList<ScanError> errors)
    {
        _itemsById.Clear();
        var items = applications.Select(a => new ApplicationItemViewModel(a, _icons)).ToList();
        foreach (var item in items)
        {
            _itemsById[item.Id] = item;
        }

        Manager.Load(items);
        _lastErrors = errors;
        ErrorCount = errors.Count;
        RefreshSummary();
        OnPropertyChanged(nameof(HasResults));
        OpenManagerCommand.NotifyCanExecuteChanged();
    }

    private void RefreshSummary()
    {
        var items = _itemsById.Values.ToList();
        TotalApps = items.Count(i => !i.IsSystemLike && i.AppType != AppType.Undetermined && !i.IsIdlePortable);
        RunningCount = items.Count(i => i.IsRunning && !i.IsSystemLike);
        PortableRunningCount = items.Count(i => i.IsRunning && i.AppType == AppType.Portable);
        var residue = items.Where(i => i.AppType == AppType.SuspectedResidue).ToList();
        ResidueCount = residue.Count;
        ResidueSizeText = residue.Any(i => i.Entity.DiskUsageBytes is not null)
            ? Localize.Bytes(residue.Sum(i => i.Entity.DiskUsageBytes ?? 0))
            : Localize.Get("Size.Pending");
        UndeterminedCount = items.Count(i => i.AppType == AppType.Undetermined);
    }

    private async Task ComputeSizesAsync(IReadOnlyList<ApplicationEntity> applications)
    {
        _sizeCts = new CancellationTokenSource();
        var token = _sizeCts.Token;
        var pending = new Dictionary<string, Dictionary<string, long>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            await _scanService.ComputeSizesAsync(applications, OnSize, null, token);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Manager.RefreshCounts();
                RefreshSummary();
            });
            var withSizes = Application.Current.Dispatcher.Invoke(() => _itemsById.Values.Select(i => i.Entity).ToList());
            await _cache.SaveAsync(new CachedScan(_lastScanTime ?? DateTimeOffset.Now, withSizes), CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // A new scan superseded the size pass.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "Size computation failed");
        }
        finally
        {
            _sizeCts.Dispose();
            _sizeCts = null;
        }

        void OnSize(string id, string path, DirectorySize size)
        {
            // Called on a pool thread; copy the per-application map before handing it to the UI thread.
            Dictionary<string, long> snapshot;
            lock (pending)
            {
                if (!pending.TryGetValue(id, out var sizes))
                {
                    sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                    pending[id] = sizes;
                }

                sizes[path] = size.Bytes;
                snapshot = new Dictionary<string, long>(sizes, StringComparer.OrdinalIgnoreCase);
            }

            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (!_itemsById.TryGetValue(id, out var item))
                {
                    return;
                }

                var entity = item.Entity;
                var directories = entity.Directories.Select(d => snapshot.TryGetValue(d.Path, out var bytes) ? d with { SizeBytes = bytes } : d).ToArray();
                var total = directories.Where(d => d.Role != DirectoryRole.SharedParent && d.SizeBytes is not null).Sum(d => d.SizeBytes!.Value);
                item.Update(entity with { Directories = directories, DiskUsageBytes = total });
                if (Detail.Current == item)
                {
                    Detail.Current = null;
                    Detail.Current = item;
                }
            });
        }
    }

    private async Task AnalyzeFolderAsync(string path)
    {
        StatusText = Localize.Format("Status.AnalyzingFormat", path);
        try
        {
            var entity = await _scanService.AnalyzeDirectoryAsync(path, _lastSnapshot, CancellationToken.None);
            if (entity is null)
            {
                StatusText = Localize.Format("Status.AnalyzeFailedFormat", path, Localize.Get("Value.Unknown"));
                return;
            }

            Stage = ShellStage.Manager;
            Detail.Current = new ApplicationItemViewModel(entity, _icons) { IsAdHoc = true };
            StatusText = Localize.Format("Status.AnalyzedFormat", path, Localize.AppType(entity.AppType));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "Folder analysis failed for {Path}", path);
            StatusText = Localize.Format("Status.AnalyzeFailedFormat", path, ex.Message);
        }
    }

    private static string FormatTime(DateTimeOffset time) => time.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    public void Dispose()
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _sizeCts?.Cancel();
        _sizeCts?.Dispose();
    }
}
