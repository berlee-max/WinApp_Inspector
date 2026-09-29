using Microsoft.Extensions.Logging.Abstractions;
using WinAppInspector.Actions.Cleanup;
using WinAppInspector.Actions.Logging;
using WinAppInspector.Actions.Platform;
using WinAppInspector.Actions.Uninstall;
using WinAppInspector.Core.Actions;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Logging;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Rules;

namespace WinAppInspector.Scanners.Tests;

/// <summary>Windows-only checks for the Actions project that touch nothing outside a temp folder and HKCU reads.</summary>
public class ActionsSmokeTests
{
    [WindowsFact]
    public async Task Operation_log_round_trips_entries_newest_first()
    {
        var dir = Directory.CreateTempSubdirectory("wai-log-").FullName;
        try
        {
            using var log = new FileOperationLog(dir, NullLogger<FileOperationLog>.Instance);
            await log.AppendAsync(new OperationLogEntry(DateTimeOffset.Now.AddMinutes(-1), OperationKind.Scan, "first", OperationResult.Succeeded));
            await log.AppendAsync(new OperationLogEntry(DateTimeOffset.Now, OperationKind.DeleteDirectory, @"C:\x", OperationResult.Failed, "denied"));

            var entries = await log.ReadAsync(10);

            entries.Should().HaveCount(2);
            entries[0].Operation.Should().Be(OperationKind.DeleteDirectory);
            entries[0].Error.Should().Be("denied");
            entries[1].Target.Should().Be("first");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [WindowsFact]
    public void Registry_probe_finds_existing_and_missing_keys()
    {
        var probe = new RegistryKeyProbe();

        probe.Exists(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion").Should().BeTrue();
        probe.Exists(@"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall").Should().BeTrue();
        probe.Exists(@"HKEY_CURRENT_USER\Software\WinAppInspector\DoesNotExist\" + Guid.NewGuid()).Should().BeFalse();
        probe.Exists("NotAHive").Should().BeFalse();
    }

    [WindowsFact]
    public async Task Cleanup_manager_refuses_unconfirmed_plans_and_recycles_a_temp_directory()
    {
        var folders = WindowsKnownFolders.FromEnvironment();
        var guard = new DeletionGuard(new ProtectedPathRule(folders));
        var logDir = Directory.CreateTempSubdirectory("wai-cleanup-log-").FullName;
        var target = Directory.CreateTempSubdirectory("wai-cleanup-target-").FullName;
        await File.WriteAllTextAsync(Path.Combine(target, "leftover.txt"), "x");

        try
        {
            using var log = new FileOperationLog(logDir, NullLogger<FileOperationLog>.Instance);
            var manager = new CleanupManager(new ProtectedPathRule(folders), guard, log, NullLogger<CleanupManager>.Instance);
            var app = new ApplicationEntity
            {
                Id = "t",
                Name = "Temp",
                AppType = AppType.SuspectedResidue,
                DetectionConfidence = ConfidenceLevel.High,
                Directories = [new AppDirectory { Path = WindowsPath.Normalize(target), Root = ScanRoot.Custom, Role = DirectoryRole.Data }],
            };
            var item = new CleanupCandidate { Kind = CleanupItemKind.Directory, Target = WindowsPath.Normalize(target), CanRecycle = true };

            var refused = await manager.ExecuteAsync(new CleanupPlan { Application = app, Items = [item], UserConfirmed = false }, null, CancellationToken.None);
            refused.AllSucceeded.Should().BeFalse();
            Directory.Exists(target).Should().BeTrue("nothing may be touched without confirmation");

            var result = await manager.ExecuteAsync(new CleanupPlan { Application = app, Items = [item], UserConfirmed = true, UseRecycleBin = true }, null, CancellationToken.None);

            result.AllSucceeded.Should().BeTrue(result.Items[0].Error);
            result.Items[0].PermanentlyDeleted.Should().BeFalse();
            Directory.Exists(target).Should().BeFalse();
            (await log.ReadAsync(5)).Should().ContainSingle(e => e.Operation == OperationKind.DeleteDirectory && e.Result == OperationResult.Succeeded);
        }
        finally
        {
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            Directory.Delete(logDir, recursive: true);
        }
    }

    [WindowsFact]
    public async Task Uninstall_manager_refuses_unconfirmed_requests_and_reports_missing_uninstaller()
    {
        var logDir = Directory.CreateTempSubdirectory("wai-uninstall-log-").FullName;
        try
        {
            using var log = new FileOperationLog(logDir, NullLogger<FileOperationLog>.Instance);
            var manager = new UninstallManager(log, NullLogger<UninstallManager>.Instance);
            var app = new ApplicationEntity { Id = "x", Name = "X", UninstallCommand = @"""C:\definitely\missing\unins000.exe"" /SILENT", PreferredUninstallMethod = UninstallMethod.UninstallString };

            var unconfirmed = await manager.UninstallAsync(new UninstallRequest { Application = app, UserConfirmed = false }, null, CancellationToken.None);
            unconfirmed.Outcome.Should().Be(UninstallOutcome.NotConfirmed);

            var missing = await manager.UninstallAsync(new UninstallRequest { Application = app, UserConfirmed = true }, null, CancellationToken.None);
            missing.Outcome.Should().Be(UninstallOutcome.Failed);
            missing.Error.Should().Contain("not found");

            var none = await manager.UninstallAsync(new UninstallRequest { Application = new ApplicationEntity { Id = "y", Name = "Y" }, UserConfirmed = true }, null, CancellationToken.None);
            none.Outcome.Should().Be(UninstallOutcome.NoUninstaller);
        }
        finally
        {
            Directory.Delete(logDir, recursive: true);
        }
    }

    [Fact]
    public void ChooseRoute_follows_the_section_5_2_preference_order()
    {
        var msi = new ApplicationEntity { Id = "1", Name = "A", MsiProductCode = "{1}", UninstallCommand = "MsiExec.exe /X{1}" };
        UninstallManager.ChooseRoute(msi, preferQuiet: false).Method.Should().Be(UninstallMethod.Msi);
        UninstallManager.ChooseRoute(msi, preferQuiet: true).Command.Should().EndWith("/qb-");

        var quiet = new ApplicationEntity { Id = "2", Name = "B", UninstallCommand = "u.exe", QuietUninstallCommand = "u.exe /S" };
        UninstallManager.ChooseRoute(quiet, preferQuiet: false).Method.Should().Be(UninstallMethod.UninstallString);
        UninstallManager.ChooseRoute(quiet, preferQuiet: true).Method.Should().Be(UninstallMethod.QuietUninstallString);

        var appx = new ApplicationEntity { Id = "3", Name = "C", AppxPackageFullName = "C_1.0_x64__abc" };
        UninstallManager.ChooseRoute(appx, preferQuiet: false).Method.Should().Be(UninstallMethod.Appx);

        UninstallManager.ChooseRoute(new ApplicationEntity { Id = "4", Name = "D" }, preferQuiet: false).Method.Should().Be(UninstallMethod.None);
    }
}
