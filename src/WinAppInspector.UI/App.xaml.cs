using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinAppInspector.Actions;
using WinAppInspector.Analysis;
using WinAppInspector.Core;
using WinAppInspector.Core.Scanning;
using WinAppInspector.Scanners;
using WinAppInspector.UI.Services;
using WinAppInspector.UI.ViewModels;
using WinAppInspector.UI.Views;

namespace WinAppInspector.UI;

/// <summary>Application entry point: builds the generic host, wires DI and shows the main window.</summary>
public partial class App : Application
{
    /// <summary>
    /// Start-up self-test used by CI on a real Windows desktop: show the window, switch through every page and exit
    /// with 0. Any XAML or DI failure exits non-zero, so a build that cannot start never reaches a release.
    /// </summary>
    public const string SelfTestSwitch = "--self-test";

    /// <summary>Set by the portable launcher to its own path, so shell registration points at the launcher, not at app\.</summary>
    public const string LauncherVariable = "WINAPPINSPECTOR_LAUNCHER";

    private IHost? _host;
    private bool _selfTest;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            CrashReporter.Report(args.ExceptionObject as Exception ?? new InvalidOperationException(args.ExceptionObject?.ToString()), "AppDomain", _selfTest);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashReporter.WriteLog(args.Exception, "UnobservedTask");
            args.SetObserved();
        };
    }

    /// <summary>Folder for logs, settings and cached scan results, under the user's local app data (§25, §33).</summary>
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WinAppInspector");

    /// <summary>The executable Explorer should start: the portable launcher when we were started by it, otherwise this process.</summary>
    public static string ExecutableForShell
    {
        get
        {
            var launcher = Environment.GetEnvironmentVariable(LauncherVariable);
            if (!string.IsNullOrWhiteSpace(launcher) && File.Exists(launcher))
            {
                return launcher;
            }

            return Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "WinAppInspector.exe");
        }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _selfTest = e.Args.Any(a => a.Equals(SelfTestSwitch, StringComparison.OrdinalIgnoreCase));

        try
        {
            await StartAsync(e.Args).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            CrashReporter.Report(ex, "Startup", _selfTest);
            Environment.Exit(CrashReporter.SelfTestCrashExitCode);
        }
    }

    private async Task StartAsync(string[] args)
    {
        Directory.CreateDirectory(DataDirectory);

        _host = Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton(sp => new SettingsService(DataDirectory, sp.GetRequiredService<ILogger<SettingsService>>()));
                services.AddSingleton<IScanOptionsProvider>(sp => sp.GetRequiredService<SettingsService>());

                services.AddWinAppInspectorCore();
                services.AddWinAppInspectorAnalysis();
                services.AddWinAppInspectorScanners();
                services.AddWinAppInspectorActions(DataDirectory);

                services.AddSingleton<IScanService, ScanService>();
                services.AddSingleton<IconService>();
                services.AddSingleton<IDialogService, DialogService>();
                services.AddSingleton(sp => new ScanCache(DataDirectory, sp.GetRequiredService<ILogger<ScanCache>>()));

                services.AddSingleton<DetailViewModel>();
                services.AddSingleton<AppManagerViewModel>();
                services.AddSingleton<UninstallFlowViewModel>();
                services.AddSingleton<MainViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync().ConfigureAwait(true);

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        var viewModel = _host.Services.GetRequiredService<MainViewModel>();
        if (_selfTest)
        {
            window.ContentRendered += async (_, _) => await RunSelfTestAsync(viewModel).ConfigureAwait(true);
        }

        window.Show();

        // §19: launched from the Explorer context menu.
        var analyzeIndex = Array.FindIndex(args, a => a.Equals(Actions.Platform.ShellIntegration.AnalyzeSwitch, StringComparison.OrdinalIgnoreCase));
        if (analyzeIndex >= 0 && analyzeIndex + 1 < args.Length)
        {
            viewModel.RequestAnalyzeOnStartup(args[analyzeIndex + 1]);
        }
    }

    /// <summary>Renders every shell stage once (templates are only applied to visible elements), then exits 0.</summary>
    private async Task RunSelfTestAsync(MainViewModel viewModel)
    {
        try
        {
            foreach (var stage in new[] { ShellStage.Manager, ShellStage.Scanning, ShellStage.Summary, ShellStage.Home })
            {
                viewModel.ShowStageForSelfTest(stage);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            }

            Shutdown(0);
        }
        catch (Exception ex)
        {
            CrashReporter.Report(ex, "SelfTest", selfTest: true);
            Environment.Exit(CrashReporter.SelfTestCrashExitCode);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashReporter.Report(e.Exception, "Dispatcher", _selfTest);
        e.Handled = true;
        if (_selfTest)
        {
            Environment.Exit(CrashReporter.SelfTestCrashExitCode);
        }

        Shutdown(1);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
