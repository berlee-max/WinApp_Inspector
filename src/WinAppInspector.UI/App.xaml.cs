using System.IO;
using System.Windows;
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
    private IHost? _host;

    /// <summary>Folder for logs, settings and cached scan results, under the user's local app data (§25, §33).</summary>
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WinAppInspector");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Directory.CreateDirectory(DataDirectory);

        _host = Host.CreateDefaultBuilder(e.Args)
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

                services.AddSingleton<DetailViewModel>();
                services.AddSingleton<OverviewViewModel>();
                services.AddSingleton<ScanViewModel>();
                services.AddSingleton<UninstallViewModel>();
                services.AddSingleton<MainViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync().ConfigureAwait(true);

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
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
