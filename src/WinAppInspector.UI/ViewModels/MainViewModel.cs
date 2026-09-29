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

/// <summary>The three top-level pages (§12).</summary>
public enum MainPage
{
    Overview = 0,
    Scan = 1,
    Uninstall = 2,
}

/// <summary>Shell view model: navigation, the top-right actions, and the scan lifecycle shared by all pages.</summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IScanService _scanService;
    private readonly IconService _icons;
    private readonly SettingsService _settings;
    private readonly ScanCache _cache;
    private readonly IDialogService _dialogs;
    private readonly ILogger<MainViewModel> _logger;
    private string? _pendingAnalyzePath;
    private DateTimeOffset? _lastScanTime;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _sizeCts;
    private ScanSnapshot? _lastSnapshot;
    private readonly Dictionary<string, ApplicationItemViewModel> _itemsById = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPageViewModel))]
    [NotifyPropertyChangedFor(nameof(IsOverviewSelected))]
    [NotifyPropertyChangedFor(nameof(IsScanSelected))]
    [NotifyPropertyChangedFor(nameof(IsUninstallSelected))]
    private MainPage _currentPage = MainPage.Overview;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RescanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelScanCommand))]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _lastScanText = string.Empty;

    public MainViewModel(
        IScanService scanService,
        IconService icons,
        SettingsService settings,
        ScanCache cache,
        IDialogService dialogs,
        OverviewViewModel overview,
        ScanViewModel scan,
        UninstallViewModel uninstall,
        DetailViewModel detail,
        ILogger<MainViewModel> logger)
    {
        _scanService = scanService;
        _icons = icons;
        _settings = settings;
        _cache = cache;
        _dialogs = dialogs;
        Overview = overview;
        Scan = scan;
        Uninstall = uninstall;
        Detail = detail;
        _logger = logger;

        Overview.ShowSystemComponents = !settings.Current.HideSystemComponents;
        Scan.ScanRequested += (_, _) => RescanCommand.Execute(null);
        Scan.AnalyzeFolderRequested += (_, path) => _ = AnalyzeFolderAsync(path);
        Uninstall.RescanRequested += (_, _) =>
        {
            if (RescanCommand.CanExecute(null))
            {
                RescanCommand.Execute(null);
            }
        };
        StatusText = Localize.Get("Status.Idle");
    }

    public OverviewViewModel Overview { get; }
    public ScanViewModel Scan { get; }
    public UninstallViewModel Uninstall { get; }
    public DetailViewModel Detail { get; }

    public object CurrentPageViewModel => CurrentPage switch
    {
        MainPage.Scan => Scan,
        MainPage.Uninstall => Uninstall,
        _ => Overview,
    };

    public bool IsOverviewSelected
    {
        get => CurrentPage == MainPage.Overview;
        set => SelectPage(MainPage.Overview, value);
    }

    public bool IsScanSelected
    {
        get => CurrentPage == MainPage.Scan;
        set => SelectPage(MainPage.Scan, value);
    }

    public bool IsUninstallSelected
    {
        get => CurrentPage == MainPage.Uninstall;
        set => SelectPage(MainPage.Uninstall, value);
    }

    /// <summary>Raised when the settings dialog should be shown; the window owns dialog creation.</summary>
    public event EventHandler? SettingsRequested;

    public event EventHandler? AboutRequested;

    /// <summary>§19: a path handed over by the Explorer context menu; analysed once the first scan has finished.</summary>
    public void RequestAnalyzeOnStartup(string path) => _pendingAnalyzePath = path;

    /// <summary>§31/§33: show the previous results immediately while the fresh scan runs.</summary>
    public async Task LoadCacheAsync()
    {
        var cached = await _cache.LoadAsync(CancellationToken.None);
        if (cached is null || cached.Applications.Count == 0 || _itemsById.Count > 0)
        {
            return;
        }

        LoadResults(cached.Applications, []);
        _lastScanTime = cached.ScanTime;
        LastScanText = Localize.Format("Status.CachedScanFormat", cached.ScanTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture));
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
            // Export what the user sees: the current filter and search, sorted as displayed.
            var visible = Overview.View.Cast<ApplicationItemViewModel>().Select(i => i.Entity).ToList();
            var content = ReportExporter.Export(visible, format, labels, _lastScanTime ?? DateTimeOffset.Now);
            System.IO.File.WriteAllText(dialog.FileName, content, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            StatusText = Localize.Format("Export.DoneFormat", visible.Count, dialog.FileName);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            _dialogs.Warn(Localize.Get("Export.Title"), Localize.Format("Export.FailedFormat", dialog.FileName), ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRescan))]
    private async Task RescanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        _sizeCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        IsScanning = true;
        Scan.BeginScan();
        StatusText = Localize.Get("Status.Scanning");

        var progress = new Progress<ScanProgress>(p =>
        {
            Scan.Report(p);
            StatusText = string.IsNullOrEmpty(p.CurrentItem) ? Localize.Stage(p.Stage) : $"{Localize.Stage(p.Stage)} — {p.CurrentItem}";
        });

        try
        {
            var outcome = await _scanService.ScanAsync(progress, _scanCts.Token);
            _lastSnapshot = outcome.Snapshot;
            _lastScanTime = outcome.Snapshot.ScanTime;
            LoadResults(outcome.Resolution.Applications, outcome.Snapshot.Errors);
            var r = outcome.Resolution;
            Scan.SetUnattributed(r.OrphanProcesses, r.OrphanStartupItems, r.OrphanServices, r.OrphanScheduledTasks);
            LastScanText = Localize.Format("Status.LastScanFormat", outcome.Snapshot.ScanTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture));
            StatusText = Localize.Format("Status.ScanDoneFormat", outcome.Resolution.Applications.Count, outcome.Snapshot.Errors.Count);
            _ = ComputeSizesAsync(outcome.Resolution.Applications);

            if (_pendingAnalyzePath is { } pending)
            {
                _pendingAnalyzePath = null;
                CurrentPage = MainPage.Scan;
                await AnalyzeFolderAsync(System.IO.File.Exists(pending) ? System.IO.Path.GetDirectoryName(pending) ?? pending : pending);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = Localize.Get("Status.Cancelled");
            Scan.EndScan(_itemsById.Values.ToArray(), []);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // §34: show the concrete cause.
            _logger.LogError(ex, "Scan failed");
            StatusText = Localize.Format("Status.ScanFailedFormat", ex.Message);
            Scan.EndScan(_itemsById.Values.ToArray(), [new ScanError("Scan", string.Empty, ex.Message, ex)]);
        }
        finally
        {
            IsScanning = false;
            _scanCts.Dispose();
            _scanCts = null;
        }
    }

    private bool CanRescan() => !IsScanning;

    [RelayCommand(CanExecute = nameof(CanCancelScan))]
    private void CancelScan() => _scanCts?.Cancel();

    private bool CanCancelScan() => IsScanning;

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenAbout() => AboutRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Called by the window after the settings dialog closes with Save.</summary>
    public void SettingsSaved()
    {
        Overview.ShowSystemComponents = !_settings.Current.HideSystemComponents;
    }

    private void LoadResults(IReadOnlyList<ApplicationEntity> applications, IReadOnlyList<ScanError> errors)
    {
        _itemsById.Clear();
        var items = applications.Select(a => new ApplicationItemViewModel(a, _icons)).ToList();
        foreach (var item in items)
        {
            _itemsById[item.Id] = item;
        }

        Overview.Load(items);
        Uninstall.Load(items);
        Scan.EndScan(items, errors);
    }

    private async Task ComputeSizesAsync(IReadOnlyList<ApplicationEntity> applications)
    {
        _sizeCts = new CancellationTokenSource();
        var token = _sizeCts.Token;
        var progress = new Progress<ScanProgress>(Scan.Report);
        var pending = new Dictionary<string, Dictionary<string, long>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            await _scanService.ComputeSizesAsync(applications, OnSize, progress, token);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Scan.MarkSizesDone();
                Overview.RefreshStats();
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

            var item = new ApplicationItemViewModel(entity, _icons);
            Detail.Current = item;
            StatusText = Localize.Format("Status.AnalyzedFormat", path, Localize.AppType(entity.AppType));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "Folder analysis failed for {Path}", path);
            StatusText = Localize.Format("Status.AnalyzeFailedFormat", path, ex.Message);
        }
    }

    private void SelectPage(MainPage page, bool selected)
    {
        if (selected)
        {
            CurrentPage = page;
        }
    }

    public void Dispose()
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _sizeCts?.Cancel();
        _sizeCts?.Dispose();
    }
}
