using Microsoft.Extensions.Logging.Abstractions;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Rules;
using WinAppInspector.Core.Scanning;
using WinAppInspector.Scanners.Appx;
using WinAppInspector.Scanners.Directories;
using WinAppInspector.Scanners.Executables;
using WinAppInspector.Scanners.Processes;
using WinAppInspector.Scanners.Registry;
using WinAppInspector.Scanners.Services;
using WinAppInspector.Scanners.Startup;
using WinAppInspector.Scanners.Tasks;

namespace WinAppInspector.Scanners.Tests;

/// <summary>Runs each scanner for real on the Windows CI runner. Assertions only rely on what every Windows 10 / 11 install has.</summary>
public class ScannerSmokeTests
{
    private static WindowsKnownFolders Folders => WindowsKnownFolders.FromEnvironment();

    [WindowsFact]
    public async Task RegistryScanner_finds_uninstall_entries()
    {
        var scanner = new RegistryScanner(NullLogger<RegistryScanner>.Instance);

        var result = await scanner.ScanAsync(null, CancellationToken.None);

        result.Items.Should().NotBeEmpty();
        result.Items.Should().Contain(e => e.DisplayName != null && e.UninstallString != null);
        result.Items.Should().Contain(e => e.Scope == RegistryScope.MachineNative);
        result.Items.Select(e => e.KeyPath).Should().OnlyHaveUniqueItems();
    }

    [WindowsFact]
    public async Task ProcessScanner_includes_the_current_process_with_its_path()
    {
        var scanner = new ProcessScanner(new ExeMetadataReader(), NullLogger<ProcessScanner>.Instance);

        var result = await scanner.ScanAsync(null, CancellationToken.None);

        var me = result.Items.Should().ContainSingle(p => p.ProcessId == Environment.ProcessId).Subject;
        me.ExecutablePath.Should().NotBeNullOrEmpty();
        WindowsPath.AreEqual(me.ExecutablePath, Environment.ProcessPath).Should().BeTrue();
    }

    [WindowsFact]
    public void ExeMetadataReader_reads_version_resource_of_a_system_binary()
    {
        var path = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        if (!File.Exists(path))
        {
            path = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        }

        var metadata = new ExeMetadataReader().Read(path);

        metadata.CompanyName.Should().Contain("Microsoft");
        metadata.FileVersion.Should().NotBeNullOrEmpty();
        metadata.FileSizeBytes.Should().BeGreaterThan(0);
    }

    [WindowsFact]
    public void ExeMetadataReader_throws_for_missing_file()
    {
        var act = () => new ExeMetadataReader().Read(@"C:\definitely\missing\file.exe");
        act.Should().Throw<FileNotFoundException>();
    }

    [WindowsFact]
    public void SignatureReader_validates_an_embedded_microsoft_signature()
    {
        // dotnet.exe carries an embedded Authenticode signature; System32 binaries are mostly catalog-signed.
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("DOTNET_ROOT") is { } root ? Path.Combine(root, "dotnet.exe") : null,
            @"C:\Program Files\dotnet\dotnet.exe",
            Environment.ProcessPath,
        };
        var path = candidates.FirstOrDefault(p => p is not null && File.Exists(p));
        path.Should().NotBeNull("a signed dotnet host must exist on the CI runner");

        var signature = new SignatureReader().Read(path!);

        signature.Status.Should().Be(SignatureStatus.Valid, signature.Error);
        signature.Publisher.Should().Contain("Microsoft");
        signature.SubjectName.Should().Contain("Microsoft");
        signature.Thumbprint.Should().NotBeNullOrEmpty();
        signature.NotAfter.Should().NotBeNull();
    }

    [WindowsFact]
    public void SignatureReader_reports_unsigned_files_without_throwing()
    {
        var unsigned = typeof(ScannerSmokeTests).Assembly.Location;

        var signature = new SignatureReader().Read(unsigned);

        signature.Status.Should().Be(SignatureStatus.NotSigned);
    }

    [WindowsFact]
    public void SignatureReader_reports_missing_file_as_read_failure()
    {
        new SignatureReader().Read(@"C:\definitely\missing\file.exe").Status.Should().Be(SignatureStatus.ReadFailed);
    }

    [WindowsFact]
    public async Task DirectoryScanner_discovers_program_files_children_without_WindowsApps()
    {
        var folders = Folders;
        var options = ScanOptions.Default with { DirectoryRoots = new HashSet<ScanRoot> { ScanRoot.ProgramFiles, ScanRoot.LocalAppData } };
        var scanner = new DirectoryScanner(folders, new ProtectedPathRule(folders), new StaticScanOptionsProvider(options), NullLogger<DirectoryScanner>.Instance);

        var result = await scanner.ScanAsync(null, CancellationToken.None);

        result.Items.Should().NotBeEmpty();
        result.Items.Should().OnlyContain(d => d.Root == ScanRoot.ProgramFiles || d.Root == ScanRoot.LocalAppData);
        result.Items.Should().NotContain(d => WindowsPath.AreEqual(d.Path, folders.WindowsApps));
        result.Items.Should().Contain(d => d.ContainsExecutables, "Program Files always holds at least one program with an exe");
        result.Items.Should().OnlyContain(d => d.SizeBytes == null, "discovery must not compute sizes (§32)");
        result.Items.Should().OnlyContain(d => d.ExecutablePaths.Count <= options.MaxExecutablesPerDirectory);
    }

    [WindowsFact]
    public async Task DirectoryScanner_scans_custom_directories_but_never_the_folder_itself_or_system_locations()
    {
        var folders = Folders;
        var root = Directory.CreateTempSubdirectory("wai-tools-").FullName;
        var tool = Path.Combine(root, "Everything");
        Directory.CreateDirectory(tool);
        await File.WriteAllBytesAsync(Path.Combine(tool, "Everything.exe"), new byte[] { 0x4D, 0x5A });
        await File.WriteAllTextAsync(Path.Combine(root, "stray.exe"), "not a program");
        var missing = Path.Combine(root, "does-not-exist");

        try
        {
            var options = ScanOptions.Default with
            {
                DirectoryRoots = new HashSet<ScanRoot>(),
                CustomDirectories = [root, missing, folders.SystemRoot, folders.SystemDrive, folders.UserProfile, folders.UsersRoot],
            };
            var scanner = new DirectoryScanner(folders, new ProtectedPathRule(folders), new StaticScanOptionsProvider(options), NullLogger<DirectoryScanner>.Instance);

            var result = await scanner.ScanAsync(null, CancellationToken.None);

            result.Items.Should().ContainSingle().Which.Should().Match<AppDirectory>(d =>
                WindowsPath.AreEqual(d.Path, tool) && d.Root == ScanRoot.Custom && d.ExecutablePaths.Count == 1);
            result.Items.Should().NotContain(d => WindowsPath.AreEqual(d.Path, root), "the custom folder itself is a container, not a program");
            result.Errors.Should().Contain(e => WindowsPath.AreEqual(e.Target, missing));
            result.Errors.Should().Contain(e => WindowsPath.AreEqual(e.Target, folders.SystemRoot), "the Windows directory is refused (§11)");
            result.Errors.Should().Contain(e => WindowsPath.AreEqual(e.Target, folders.SystemDrive), "a drive root is refused (§11)");
            result.Errors.Should().Contain(e => WindowsPath.AreEqual(e.Target, folders.UserProfile) && e.Code == ScanErrorCodes.CustomDirectoryProtected, "Desktop / Downloads must never become programs");
            result.Errors.Should().Contain(e => WindowsPath.AreEqual(e.Target, folders.UsersRoot) && e.Code == ScanErrorCodes.CustomDirectoryProtected, "other accounts must never become programs");
            result.Errors.Should().Contain(e => WindowsPath.AreEqual(e.Target, root) && e.Code == ScanErrorCodes.CustomDirectoryLooksLikeProgram, "the root holds stray.exe itself");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [WindowsFact]
    public async Task DirectorySizeCalculator_measures_a_temp_tree()
    {
        var root = Directory.CreateTempSubdirectory("wai-size-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            await File.WriteAllBytesAsync(Path.Combine(root, "a.bin"), new byte[1000]);
            await File.WriteAllBytesAsync(Path.Combine(root, "sub", "b.bin"), new byte[500]);

            var size = await new DirectorySizeCalculator().ComputeAsync(root, CancellationToken.None);

            size.Complete.Should().BeTrue();
            size.Bytes.Should().Be(1500);
            size.FileCount.Should().Be(2);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [WindowsFact]
    public async Task ServiceScanner_lists_services_with_resolved_image_paths()
    {
        var scanner = new ServiceScanner(Folders, NullLogger<ServiceScanner>.Instance);

        var result = await scanner.ScanAsync(null, CancellationToken.None);

        result.Errors.Should().BeEmpty();
        result.Items.Should().NotBeEmpty();
        result.Items.Should().Contain(s => s.ExecutablePath != null && s.ExecutablePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        result.Items.Should().Contain(s => s.Name.Equals("Schedule", StringComparison.OrdinalIgnoreCase) || s.Name.Equals("EventLog", StringComparison.OrdinalIgnoreCase));
    }

    [WindowsFact]
    public async Task StartupScanner_runs_without_errors()
    {
        var scanner = new StartupScanner(Folders, new ShortcutResolver(NullLogger<ShortcutResolver>.Instance), NullLogger<StartupScanner>.Instance);

        var result = await scanner.ScanAsync(null, CancellationToken.None);

        result.Errors.Should().BeEmpty();
        result.Items.Should().OnlyContain(i => i.Command != null);
    }

    [WindowsFact]
    public async Task ShortcutScanner_resolves_start_menu_shortcuts()
    {
        var scanner = new ShortcutScanner(Folders, new ShortcutResolver(NullLogger<ShortcutResolver>.Instance), NullLogger<ShortcutScanner>.Instance);

        var result = await scanner.ScanAsync(null, CancellationToken.None);

        result.Errors.Should().BeEmpty();
        result.Items.Should().OnlyContain(s => s.TargetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    [WindowsFact]
    public async Task ScheduledTaskScanner_lists_tasks()
    {
        var scanner = new ScheduledTaskScanner(Folders, NullLogger<ScheduledTaskScanner>.Instance);

        var result = await scanner.ScanAsync(null, CancellationToken.None);

        result.Items.Should().NotBeEmpty();
        result.Items.Should().Contain(t => t.Execute != null);
        result.Items.Should().OnlyContain(t => t.TaskPath.StartsWith('\\'));
    }

    [WindowsFact]
    public async Task PackageManager_provider_enumerates_packages()
    {
        var provider = new PackageManagerAppxProvider(NullLogger<PackageManagerAppxProvider>.Instance);

        var packages = await provider.GetPackagesAsync(CancellationToken.None);

        packages.Should().OnlyContain(p => p.PackageFullName.Length > 0 && p.PackageFamilyName.Length > 0);
    }

    [WindowsFact]
    public async Task PowerShell_provider_enumerates_packages()
    {
        var provider = new PowerShellAppxProvider(NullLogger<PowerShellAppxProvider>.Instance);

        var packages = await provider.GetPackagesAsync(CancellationToken.None);

        packages.Should().OnlyContain(p => p.PackageFullName.Length > 0 && p.PackageFamilyName.Length > 0);
    }

    [WindowsFact]
    public async Task AppxScanner_uses_the_first_working_provider()
    {
        var scanner = new AppxScanner(
            [new PackageManagerAppxProvider(NullLogger<PackageManagerAppxProvider>.Instance), new PowerShellAppxProvider(NullLogger<PowerShellAppxProvider>.Instance)],
            NullLogger<AppxScanner>.Instance);

        var result = await scanner.ScanAsync(null, CancellationToken.None);

        result.Errors.Should().BeEmpty();
    }
}
