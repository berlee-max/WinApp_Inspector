using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WinAppInspector.Analysis.Resolution;
using WinAppInspector.Core.Evidence;
using WinAppInspector.Core.Models;

namespace WinAppInspector.Analysis.Tests.Resolution;

public class ApplicationResolverTests
{
    private static IApplicationResolver CreateResolver()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddWinAppInspectorAnalysis();
        return services.BuildServiceProvider().GetRequiredService<IApplicationResolver>();
    }

    private static ResolutionResult Resolve(SnapshotBuilder builder) => CreateResolver().Resolve(builder.Build());

    // ---- §39: unregistered user-level program ---------------------------------------------------------------------

    [Fact]
    public void Section_39_unregistered_user_app_is_described_from_its_executable()
    {
        var b = new SnapshotBuilder();
        var dir = $@"{b.Local}\Foo";
        var exe = $@"{dir}\Foo.exe";
        b.Directory(dir, executables: [exe])
         .Exe(exe, productName: "Foo Desktop", companyName: "Foo Technologies", version: "2.1.0", signer: "Foo Technologies Ltd.")
         .Startup("Foo", exe)
         .Process(exe, pid: 4242);

        var app = Resolve(b).Applications.Should().ContainSingle().Subject;

        app.Name.Should().Be("Foo Desktop");
        app.Publisher.Should().Be("Foo Technologies");
        app.Version.Should().Be("2.1.0");
        app.AppType.Should().Be(AppType.UserLevel);
        app.PreferredUninstallMethod.Should().Be(UninstallMethod.None);
        app.HasOfficialUninstaller.Should().BeFalse();
        app.IsRunning.Should().BeTrue();
        app.MainExecutable.Should().Be(exe);
        app.Signature.IsValid.Should().BeTrue();
        app.DetectionConfidence.Should().Be(ConfidenceLevel.High);
        app.StartupItems.Should().ContainSingle();
        app.Sources.Should().HaveFlag(DiscoverySource.Directory).And.HaveFlag(DiscoverySource.Process).And.HaveFlag(DiscoverySource.Startup);
        app.Evidence.Select(e => e.Kind).Should().Contain([
            EvidenceKind.MainExecutableProductNameMatch,
            EvidenceKind.SignaturePublisherMatch,
            EvidenceKind.RunningProcessInDirectory,
            EvidenceKind.StartupItemPointsToDirectory,
        ]);
        app.Reasons.Select(r => r.Kind).Should().Contain([
            ReasonKind.UninstallEntryMissing,
            ReasonKind.MainExecutableFound,
            ReasonKind.RunningProcessFound,
            ReasonKind.StartupItemFound,
            ReasonKind.SignatureValid,
            ReasonKind.LocatedInUserProfile,
            ReasonKind.OfficialUninstallerMissing,
        ]);
        app.RiskLevel.Should().Be(RiskLevel.Medium, "it is running and has no uninstaller");
    }

    // ---- §40: residue ---------------------------------------------------------------------------------------------

    [Fact]
    public void Section_40_stale_cache_only_folder_is_suspected_residue()
    {
        var b = new SnapshotBuilder();
        var dir = $@"{b.Local}\Foo";
        b.Directory(dir, lastWrite: SnapshotBuilder.Now.AddMonths(-8), children: ["Cache", "Logs"], fileCount: 1);

        var app = Resolve(b).Applications.Should().ContainSingle().Subject;

        app.Name.Should().Be("Foo");
        app.AppType.Should().Be(AppType.SuspectedResidue);
        app.Reasons.Select(r => r.Kind).Should().Contain([
            ReasonKind.UninstallEntryMissing,
            ReasonKind.MainExecutableMissing,
            ReasonKind.NoRunningProcess,
            ReasonKind.NoService,
            ReasonKind.NoStartupItem,
            ReasonKind.NoScheduledTask,
            ReasonKind.LongUnmodified,
            ReasonKind.OnlyCacheOrLogContent,
        ]);
        app.Reasons.Single(r => r.Kind == ReasonKind.LongUnmodified).Detail.Should().Be("2026-01-29");
        app.Directories.Should().ContainSingle().Which.Role.Should().Be(DirectoryRole.Data);
        app.DetectionConfidence.Should().Be(ConfidenceLevel.High, "stale and cache-only are both established");
        app.RiskLevel.Should().Be(RiskLevel.Low);
    }

    [Fact]
    public void Recently_modified_unowned_data_folder_is_undetermined_not_residue()
    {
        var b = new SnapshotBuilder();
        b.Directory($@"{b.Roaming}\Mystery", lastWrite: SnapshotBuilder.Now.AddDays(-2), children: ["profiles"], fileCount: 5);

        var app = Resolve(b).Applications.Should().ContainSingle().Subject;

        app.AppType.Should().Be(AppType.Undetermined);
        app.RiskLevel.Should().Be(RiskLevel.High);
        app.AppType.AllowsSafeToDeleteLabel().Should().BeFalse();
    }

    [Fact]
    public void Stale_folder_without_cache_markers_is_still_residue_when_nothing_references_it()
    {
        var b = new SnapshotBuilder();
        b.Directory($@"{b.Roaming}\OldTool", lastWrite: SnapshotBuilder.Now.AddDays(-400), children: ["settings"], fileCount: 2);

        Resolve(b).Applications.Single().AppType.Should().Be(AppType.SuspectedResidue);
    }

    [Fact]
    public void Stale_folder_without_executables_under_a_custom_root_is_undetermined_not_residue()
    {
        var b = new SnapshotBuilder();
        b.Directory(@"D:\Tools\Notes", ScanRoot.Custom, lastWrite: SnapshotBuilder.Now.AddDays(-400), children: ["2019"], fileCount: 2);

        var app = Resolve(b).Applications.Single();

        app.AppType.Should().Be(AppType.Undetermined, "user data in a user-added folder must never be offered as residue");
        app.Reasons.Should().Contain(r => r.Kind == ReasonKind.MainExecutableMissing);
    }

    // ---- §41: shared runtime --------------------------------------------------------------------------------------

    [Fact]
    public void Section_41_edge_webview_is_a_protected_shared_runtime()
    {
        var b = new SnapshotBuilder();
        var dir = $@"{b.ProgramFiles}\Microsoft\EdgeWebView";
        var exe = $@"{dir}\Application\msedgewebview2.exe";
        b.Registry("Microsoft Edge WebView2 Runtime", "Microsoft Corporation", installLocation: $@"{dir}\Application",
              uninstallString: $@"""{dir}\Application\128.0\Installer\setup.exe"" --uninstall --msedgewebview")
         .Directory($@"{b.ProgramFiles}\Microsoft", executables: [exe])
         .Exe(exe, productName: "Microsoft Edge WebView2", companyName: "Microsoft Corporation", signer: "Microsoft Corporation");

        var apps = Resolve(b).Applications;
        var webview = apps.Should().ContainSingle(a => a.Name == "Microsoft Edge WebView2 Runtime").Subject;

        webview.AppType.Should().Be(AppType.SharedRuntime);
        webview.IsMicrosoft.Should().BeTrue();
        webview.RiskLevel.Should().Be(RiskLevel.Protected);
        webview.Reasons.Should().Contain(r => r.Kind == ReasonKind.KnownSharedRuntime);
    }

    // ---- Registered third-party app with directory, process, service, task ----------------------------------------

    [Fact]
    public void Registered_app_aggregates_directory_runtime_links_and_evidence()
    {
        var b = new SnapshotBuilder();
        var install = $@"{b.ProgramFiles}\Google\Chrome\Application";
        var exe = $@"{install}\chrome.exe";
        var updater = $@"{b.ProgramFiles}\Google\Update\GoogleUpdate.exe";
        b.Registry("Google Chrome", "Google LLC", installLocation: install, version: "128.0",
              uninstallString: $@"""{install}\128.0\Installer\setup.exe"" --uninstall", displayIcon: exe + ",0")
         .Directory($@"{b.ProgramFiles}\Google", executables: [exe, updater])
         .Directory($@"{b.Local}\Google", children: ["Chrome"], fileCount: 0)
         .Exe(exe, productName: "Google Chrome", companyName: "Google LLC", version: "128.0.6613.120", signer: "Google LLC")
         .Exe(updater, productName: "Google Update", companyName: "Google LLC", signer: "Google LLC")
         .Process(exe, 100).Process(exe, 101)
         .Service("gupdate", updater, state: "Stopped")
         .Task("GoogleUpdateTaskMachineUA", updater)
         .Shortcut("Google Chrome", exe);

        var apps = Resolve(b).Applications;
        var chrome = apps.Should().ContainSingle().Subject;

        chrome.AppType.Should().Be(AppType.ThirdPartyInstalled);
        chrome.PreferredUninstallMethod.Should().Be(UninstallMethod.UninstallString);
        chrome.MainExecutable.Should().Be(exe);
        chrome.Processes.Should().HaveCount(2);
        chrome.Services.Should().ContainSingle().Which.Name.Should().Be("gupdate");
        chrome.ScheduledTasks.Should().ContainSingle();
        chrome.Directories.Should().HaveCount(2);
        chrome.Directories.Should().Contain(d => d.Path == $@"{b.ProgramFiles}\Google" && d.Role == DirectoryRole.SharedParent);
        chrome.Directories.Should().Contain(d => d.Path == $@"{b.Local}\Google" && d.Role == DirectoryRole.SharedParent, "a folder named after the vendor is shared");
        chrome.Evidence.Select(e => e.Kind).Should().Contain([
            EvidenceKind.ParentOfInstallLocation,
            EvidenceKind.SignaturePublisherMatch,
            EvidenceKind.RunningProcessInDirectory,
            EvidenceKind.ServiceExecutableInDirectory,
            EvidenceKind.FolderNameExactMatch,
        ]);
        chrome.Evidence.Should().NotContain(e => e.Kind == EvidenceKind.UninstallStringPointsToDirectory, "the uninstaller lives in the registered install location, which explains it, not the vendor folder");
        chrome.Directories.Single(d => d.Path == $@"{b.ProgramFiles}\Google").AttributionEvidence.Should().NotBeEmpty();
        chrome.DetectionConfidence.Should().Be(ConfidenceLevel.High);
        chrome.RiskLevel.Should().Be(RiskLevel.Medium, "processes are running");
        chrome.Signature.IsValid.Should().BeTrue();
        chrome.Sources.Should().HaveFlag(DiscoverySource.Registry).And.HaveFlag(DiscoverySource.Shortcut);
    }

    [Fact]
    public void Microsoft_platform_folders_are_not_attached_to_every_microsoft_package()
    {
        var b = new SnapshotBuilder();
        b.Package("Microsoft.GetStarted", "Microsoft Corporation", displayName: "Get Started")
         .Package("Microsoft.LockApp", "Microsoft Corporation", displayName: "Lock App")
         .Registry("Microsoft Edge", "Microsoft Corporation", installLocation: $@"{b.ProgramFiles}\Microsoft\Edge\Application")
         .Directory($@"{b.Local}\Microsoft", children: ["Windows", "Edge"])
         .Directory($@"{b.Roaming}\Microsoft", children: ["Windows"])
         .Directory($@"{b.ProgramFiles}\Microsoft", children: ["Edge"]);

        var apps = Resolve(b).Applications;

        foreach (var package in apps.Where(a => a.Packages.Count > 0))
        {
            package.Directories.Should().BeEmpty($"{package.Name} has no folder of its own; the Microsoft folders are platform data");
        }

        apps.Where(a => a.Name == "Microsoft Edge").Should().ContainSingle().Which.Directories
            .Should().OnlyContain(d => d.Role == DirectoryRole.SharedParent && d.Path == $@"{b.ProgramFiles}\Microsoft", "Edge is registered beneath Program Files/Microsoft");
        apps.Where(a => a.Directories.Any(d => d.Path == $@"{b.Local}\Microsoft")).Should().ContainSingle()
            .Which.AppType.Should().Be(AppType.SystemComponent, "AppData/Local/Microsoft is a known platform folder");
    }

    [Fact]
    public void Vendor_folder_shared_by_two_products_is_attached_to_both_as_shared_parent()
    {
        var b = new SnapshotBuilder();
        var vendor = $@"{b.ProgramFiles}\Contoso";
        b.Registry("Contoso Editor", "Contoso", installLocation: $@"{vendor}\Editor", uninstallString: $@"""{vendor}\Editor\unins.exe""")
         .Registry("Contoso Viewer", "Contoso", installLocation: $@"{vendor}\Viewer", uninstallString: $@"""{vendor}\Viewer\unins.exe""")
         .Directory(vendor, children: ["Editor", "Viewer"]);

        var apps = Resolve(b).Applications;

        apps.Should().HaveCount(2);
        apps.Should().OnlyContain(a => a.Directories.Count == 1 && a.Directories[0].Role == DirectoryRole.SharedParent);
    }

    // ---- User-level installed app registered under HKCU -----------------------------------------------------------

    [Fact]
    public void Hkcu_registered_app_under_appdata_is_user_level()
    {
        var b = new SnapshotBuilder();
        var dir = $@"{b.Roaming}\Spotify";
        var exe = $@"{dir}\Spotify.exe";
        b.Registry("Spotify", "Spotify AB", installLocation: dir, uninstallString: $@"{exe} /UNINSTALL /SILENT", scope: RegistryScope.CurrentUser, version: "1.2.40")
         .Directory(dir, executables: [exe])
         .Directory($@"{b.Local}\Spotify", children: ["Browser", "Data", "Storage"], fileCount: 3)
         .Exe(exe, productName: "Spotify", companyName: "Spotify AB", signer: "Spotify AB");

        var app = Resolve(b).Applications.Should().ContainSingle().Subject;

        app.AppType.Should().Be(AppType.UserLevel);
        app.Directories.Should().HaveCount(2);
        var data = app.Directories.Single(d => d.Path == $@"{b.Local}\Spotify");
        data.Role.Should().Be(DirectoryRole.Data, "the folder name equals the application name");
        data.AttributionEvidence.Should().Contain(e => e.Kind == EvidenceKind.FolderNameExactMatch);
        app.RiskLevel.Should().Be(RiskLevel.Low);
    }

    // ---- Portable app ---------------------------------------------------------------------------------------------

    [Fact]
    public void Unregistered_program_files_directory_with_exe_is_portable()
    {
        var b = new SnapshotBuilder();
        var dir = $@"{b.ProgramFiles}\Tools\SuperTool";
        var exe = $@"{dir}\SuperTool.exe";
        b.Directory(dir, root: ScanRoot.ProgramFiles, executables: [exe])
         .Exe(exe, productName: "SuperTool", companyName: "Super Software");

        var app = Resolve(b).Applications.Should().ContainSingle().Subject;

        app.AppType.Should().Be(AppType.Portable);
        app.Signature.Status.Should().Be(SignatureStatus.NotSigned);
        app.Reasons.Should().Contain(r => r.Kind == ReasonKind.SignatureMissing);
        app.Reasons.Should().Contain(r => r.Kind == ReasonKind.FilesInSingleDirectory);
    }

    [Fact]
    public void Directory_with_only_metadata_free_exe_falls_back_to_folder_name_and_lower_confidence()
    {
        var b = new SnapshotBuilder();
        var dir = $@"{b.Local}\Programs\mystery-app";
        var exe = $@"{dir}\run.exe";
        b.Directory(dir, executables: [exe]).Exe(exe);

        var app = Resolve(b).Applications.Should().ContainSingle().Subject;

        app.Name.Should().Be("mystery-app");
        app.AppType.Should().Be(AppType.UserLevel);
        app.DetectionConfidence.Should().Be(ConfidenceLevel.Low, "only the folder name agrees with the derived name");
        app.RiskLevel.Should().Be(RiskLevel.High);
        app.Reasons.Should().Contain(r => r.Kind == ReasonKind.LowAttributionConfidence);
    }

    // ---- Drivers, runtimes, system components ---------------------------------------------------------------------

    [Theory]
    [InlineData("NVIDIA Graphics Driver 551.23", "NVIDIA Corporation", AppType.HardwareOrDriver)]
    [InlineData("Intel(R) Chipset Device Software", "Intel Corporation", AppType.HardwareOrDriver)]
    [InlineData("Realtek Audio Console", "Realtek Semiconductor Corp.", AppType.HardwareOrDriver)]
    [InlineData("Microsoft Visual C++ 2015-2022 Redistributable (x64) - 14.38", "Microsoft Corporation", AppType.SharedRuntime)]
    [InlineData("Java 8 Update 401", "Oracle Corporation", AppType.SharedRuntime)]
    [InlineData("Microsoft .NET Runtime - 8.0.8 (x64)", "Microsoft Corporation", AppType.SharedRuntime)]
    [InlineData("Visual Studio Code", "Microsoft Corporation", AppType.ThirdPartyInstalled)]
    [InlineData("7-Zip 23.01 (x64)", "Igor Pavlov", AppType.ThirdPartyInstalled)]
    public void Registry_entries_are_classified_by_name_and_publisher(string name, string publisher, AppType expected)
    {
        var b = new SnapshotBuilder();
        b.Registry(name, publisher, installLocation: $@"{b.ProgramFiles}\X", uninstallString: $@"{b.ProgramFiles}\X\unins.exe");

        var app = Resolve(b).Applications.Should().ContainSingle().Subject;

        app.AppType.Should().Be(expected);
        app.RiskLevel.Should().Be(expected.IsProtectedByDefault() ? RiskLevel.Protected : RiskLevel.Low);
        app.IsMicrosoft.Should().Be(publisher.Contains("Microsoft", StringComparison.Ordinal));
    }

    [Fact]
    public void System_component_flag_and_update_children_are_hidden_system_entries()
    {
        var b = new SnapshotBuilder();
        b.Registry("Microsoft Update Health Tools", "Microsoft Corporation", systemComponent: true)
         .Registry("Office 16 Click-to-Run Licensing Component", "Microsoft Corporation", systemComponent: true)
         .Registry("Security Update for Microsoft Office (KB123)", "Microsoft Corporation", releaseType: "Security Update");

        var apps = Resolve(b).Applications;

        apps.Should().HaveCount(3);
        apps.Should().OnlyContain(a => a.AppType == AppType.SystemComponent && a.RiskLevel == RiskLevel.Protected);
        apps.Should().OnlyContain(a => a.Reasons.Any(r => r.Kind == ReasonKind.RegistrySystemComponentFlag));
    }

    [Fact]
    public void Child_update_entries_are_folded_into_their_parent()
    {
        var b = new SnapshotBuilder();
        b.Registry("Contoso Suite", "Contoso", keyName: "ContosoSuite", uninstallString: "C:\\x\\u.exe")
         .Registry("Contoso Suite Update 1", "Contoso", keyName: "KB1", parentKeyName: "ContosoSuite", releaseType: "Update");

        var app = Resolve(b).Applications.Should().ContainSingle().Subject;

        app.RegistryEntries.Should().HaveCount(2);
        app.AppType.Should().Be(AppType.ThirdPartyInstalled);
    }

    [Fact]
    public void Same_product_in_two_hives_is_merged()
    {
        var b = new SnapshotBuilder();
        b.Registry("Contoso App", "Contoso Ltd", version: "1.0", scope: RegistryScope.MachineNative, uninstallString: "C:\\x\\u.exe")
         .Registry("Contoso App", "Contoso Ltd", version: "1.0", scope: RegistryScope.MachineWow6432, uninstallString: "C:\\x\\u.exe");

        Resolve(b).Applications.Should().ContainSingle().Which.RegistryEntries.Should().HaveCount(2);
    }

    [Fact]
    public void Msi_entries_prefer_msi_uninstall_and_expose_the_product_code()
    {
        var b = new SnapshotBuilder();
        b.Registry("Contoso MSI", "Contoso", keyName: "{11111111-2222-3333-4444-555555555555}", windowsInstaller: true,
            uninstallString: "MsiExec.exe /X{11111111-2222-3333-4444-555555555555}");

        var app = Resolve(b).Applications.Single();

        app.PreferredUninstallMethod.Should().Be(UninstallMethod.Msi);
        app.MsiProductCode.Should().Be("{11111111-2222-3333-4444-555555555555}");
        app.Reasons.Should().Contain(r => r.Kind == ReasonKind.InstalledViaMsi);
    }

    [Fact]
    public void Unregistered_microsoft_folder_under_program_files_is_a_system_component()
    {
        var b = new SnapshotBuilder();
        var dir = $@"{b.ProgramFiles}\Windows Defender";
        b.Directory(dir, root: ScanRoot.ProgramFiles, executables: [$@"{dir}\MsMpEng.exe"])
         .Exe($@"{dir}\MsMpEng.exe", productName: "Microsoft Defender", companyName: "Microsoft Corporation", signer: "Microsoft Windows");

        var app = Resolve(b).Applications.Single();

        app.AppType.Should().Be(AppType.SystemComponent);
        app.IsMicrosoft.Should().BeTrue();
    }

    [Fact]
    public void Known_windows_folders_under_appdata_are_system_components()
    {
        var b = new SnapshotBuilder();
        b.Directory($@"{b.Local}\Microsoft", children: ["Windows", "Edge"], fileCount: 0)
         .Directory($@"{b.Local}\Temp", children: [], fileCount: 40)
         .Directory($@"{b.Local}\Packages", children: ["Microsoft.Foo_8wekyb3d8bbwe"], fileCount: 0);

        Resolve(b).Applications.Should().OnlyContain(a => a.AppType == AppType.SystemComponent);
    }

    // ---- Packages -------------------------------------------------------------------------------------------------

    [Fact]
    public void Store_framework_and_system_packages_are_classified()
    {
        var b = new SnapshotBuilder();
        b.Package("Microsoft.WindowsCalculator", "Microsoft Corporation", installLocation: $@"{b.ProgramFiles}\WindowsApps\Microsoft.WindowsCalculator_x", displayName: "Windows Calculator")
         .Package("Microsoft.VCLibs.140.00", "Microsoft Corporation", isFramework: true)
         .Package("Microsoft.Windows.ShellExperienceHost", "Microsoft Corporation", signatureKind: "System")
         .Package("SpotifyAB.SpotifyMusic", "Spotify AB", installLocation: $@"{b.ProgramFiles}\WindowsApps\SpotifyAB.SpotifyMusic_x");

        var apps = Resolve(b).Applications;

        apps.Should().HaveCount(4);
        apps.Single(a => a.Name == "Windows Calculator").AppType.Should().Be(AppType.StoreApp);
        apps.Single(a => a.Name == "Microsoft.VCLibs.140.00").AppType.Should().Be(AppType.SharedRuntime);
        apps.Single(a => a.Name == "Microsoft.Windows.ShellExperienceHost").AppType.Should().Be(AppType.SystemComponent);
        var spotify = apps.Single(a => a.Name == "SpotifyAB.SpotifyMusic");
        spotify.AppType.Should().Be(AppType.StoreApp);
        spotify.Publisher.Should().Be("Spotify AB");
        spotify.PreferredUninstallMethod.Should().Be(UninstallMethod.Appx);
        spotify.AppxPackageFullName.Should().StartWith("SpotifyAB.SpotifyMusic_");
        spotify.IsMicrosoft.Should().BeFalse();
    }

    [Fact]
    public void Package_data_folder_in_local_is_attached_by_name()
    {
        var b = new SnapshotBuilder();
        b.Package("SpotifyAB.SpotifyMusic", "Spotify AB", installLocation: $@"{b.ProgramFiles}\WindowsApps\SpotifyAB.SpotifyMusic_x", displayName: "Spotify")
         .Directory($@"{b.ProgramFiles}\WindowsApps\SpotifyAB.SpotifyMusic_x", executables: [$@"{b.ProgramFiles}\WindowsApps\SpotifyAB.SpotifyMusic_x\Spotify.exe"])
         .Exe($@"{b.ProgramFiles}\WindowsApps\SpotifyAB.SpotifyMusic_x\Spotify.exe", productName: "Spotify", companyName: "Spotify AB", signer: "Spotify AB");

        var app = Resolve(b).Applications.Should().ContainSingle().Subject;

        app.Directories.Should().ContainSingle().Which.Role.Should().Be(DirectoryRole.Program);
        app.Evidence.Should().Contain(e => e.Kind == EvidenceKind.PackageInstallLocationMatch);
        app.MainExecutable.Should().EndWith("Spotify.exe");
    }

    // ---- Orphans --------------------------------------------------------------------------------------------------

    [Fact]
    public void Running_program_outside_the_scan_scope_becomes_a_portable_entity_without_deletable_folders()
    {
        var b = new SnapshotBuilder();
        var exe = @"D:\Tools\Everything\Everything.exe";
        b.Process(exe, 20).Process(exe, 21)
         .Exe(exe, productName: "Everything", companyName: "voidtools", version: "1.4.1", signer: "voidtools")
         .Process($@"{b.Folders.SystemRoot}\explorer.exe", 30);

        var apps = Resolve(b).Applications;

        var tool = apps.Should().ContainSingle().Subject;
        tool.Name.Should().Be("Everything");
        tool.Publisher.Should().Be("voidtools");
        tool.Version.Should().Be("1.4.1");
        tool.AppType.Should().Be(AppType.Portable);
        tool.IsRunning.Should().BeTrue();
        tool.Processes.Should().HaveCount(2);
        tool.MainExecutable.Should().Be(exe);
        tool.InstallLocation.Should().BeNull("the folder a stray program runs from is never offered for deletion");
        tool.Directories.Should().BeEmpty();
        tool.HasOfficialUninstaller.Should().BeFalse();
        tool.Reasons.Should().Contain(r => r.Kind == ReasonKind.RunningProcessFound);
    }

    [Fact]
    public void Running_processes_outside_every_known_directory_are_reported_as_orphans()
    {
        var b = new SnapshotBuilder();
        var chrome = $@"{b.ProgramFiles}\Google\Chrome\Application\chrome.exe";
        b.Registry("Google Chrome", "Google LLC", installLocation: $@"{b.ProgramFiles}\Google\Chrome\Application")
         .Directory($@"{b.ProgramFiles}\Google\Chrome\Application", ScanRoot.ProgramFiles, executables: [chrome])
         .Process(chrome, 10)
         .Process(@"D:\Tools\Everything\Everything.exe", 20)
         .Process(@"D:\Tools\Everything\Everything.exe", 21)
         .Process($@"{b.Folders.SystemRoot}\explorer.exe", 30)
         .Process($@"{b.Folders.SystemRoot}\System32\svchost.exe", 31);
        b.ProcessWithoutPath("Protected", 40);

        var result = Resolve(b);

        result.Applications.Should().ContainSingle(a => a.Name == "Google Chrome").Which.Processes.Should().ContainSingle().Which.ProcessId.Should().Be(10);
        result.OrphanProcesses.Select(p => p.ProcessId).Should().BeEquivalentTo([20, 21], "only the portable tool outside the scan scope is unattributed");
        result.OrphanProcesses.Should().OnlyContain(p => p.ExecutablePath == @"D:\Tools\Everything\Everything.exe");
    }

    [Fact]
    public void Runtime_items_pointing_nowhere_are_reported_as_orphans_except_windows_ones()
    {
        var b = new SnapshotBuilder();
        b.Startup("Gone", $@"{b.Local}\Gone\gone.exe")
         .Startup("SecurityHealth", $@"{b.Folders.SystemRoot}\System32\SecurityHealthSystray.exe")
         .Service("GoneSvc", $@"{b.ProgramFiles}\Gone\svc.exe")
         .Service("Schedule", $@"{b.Folders.SystemRoot}\System32\svchost.exe")
         .Task("GoneUpdate", $@"{b.ProgramFiles}\Gone\update.exe");

        var result = Resolve(b);

        result.Applications.Should().BeEmpty();
        result.OrphanStartupItems.Should().ContainSingle().Which.Name.Should().Be("Gone");
        result.OrphanServices.Should().ContainSingle().Which.Name.Should().Be("GoneSvc");
        result.OrphanScheduledTasks.Should().ContainSingle().Which.TaskName.Should().Be("GoneUpdate");
    }

    // ---- Attribution safety ----------------------------------------------------------------------------------------

    [Fact]
    public void Folder_name_alone_does_not_attach_a_directory_to_an_app()
    {
        var b = new SnapshotBuilder();
        b.Registry("Chrome Remote Desktop Host", "Google LLC", installLocation: $@"{b.ProgramFiles}\Google\Chrome Remote Desktop", uninstallString: "MsiExec.exe /X{1}")
         .Directory($@"{b.Local}\Chrome", lastWrite: SnapshotBuilder.Now.AddDays(-1), children: ["User Data"], fileCount: 1);

        var apps = Resolve(b).Applications;

        apps.Should().HaveCount(2, "the folder name hint (low weight) is not enough evidence");
        var folder = apps.Single(a => a.Id.StartsWith("dir:", StringComparison.Ordinal));
        folder.AppType.Should().Be(AppType.Undetermined);
        folder.Evidence.Should().Contain(e => e.Kind == EvidenceKind.FolderNameSimilar, "the weak hint is still shown in 'why'");
    }

    [Fact]
    public void Resolution_is_deterministic_and_sorted_by_name()
    {
        var b = new SnapshotBuilder();
        b.Registry("Zeta", "Z", uninstallString: "z.exe").Registry("alpha", "A", uninstallString: "a.exe").Registry("Mid", "M", uninstallString: "m.exe");

        var names = Resolve(b).Applications.Select(a => a.Name).ToList();

        names.Should().Equal("alpha", "Mid", "Zeta");
    }
}
