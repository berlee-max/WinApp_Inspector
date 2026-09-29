using WinAppInspector.Analysis.Cleanup;
using WinAppInspector.Core.Actions;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Rules;

namespace WinAppInspector.Analysis.Tests.Cleanup;

public class CleanupPlannerTests
{
    private static readonly WindowsKnownFolders Folders = WindowsKnownFolders.CreateDefault("Alice");
    private readonly CleanupPlanner _planner = new(new DeletionGuard(new ProtectedPathRule(Folders)), new ProtectedPathRule(Folders));

    private static ApplicationEntity App(AppType type = AppType.SuspectedResidue, UninstallMethod method = UninstallMethod.None, params AppDirectory[] directories) => new()
    {
        Id = "x",
        Name = "Foo",
        AppType = type,
        DetectionConfidence = ConfidenceLevel.High,
        PreferredUninstallMethod = method,
        Directories = directories,
    };

    private static AppDirectory Dir(string path, DirectoryRole role = DirectoryRole.Data, long? size = null) =>
        new() { Path = path, Root = Folders.RootOf(path), Role = role, SizeBytes = size };

    private static ResidueReport Report(IReadOnlyList<AppDirectory>? directories = null, IReadOnlyList<StartupItemRecord>? startup = null,
        IReadOnlyList<ServiceRecord>? services = null, IReadOnlyList<ScheduledTaskRecord>? tasks = null, IReadOnlyList<RegistryUninstallEntry>? registry = null, IReadOnlyList<string>? configKeys = null) =>
        new(directories ?? [], registry ?? [], configKeys ?? [], startup ?? [], services ?? [], tasks ?? []);

    [Fact]
    public void Residue_directories_under_the_profile_are_recyclable_unblocked_candidates()
    {
        var dir = Dir($@"{Folders.LocalAppData}\Foo", size: 420_000_000);
        var app = App(directories: dir);

        var candidates = _planner.Build(app, Report([dir]), afterUninstall: false);

        var c = candidates.Should().ContainSingle().Subject;
        c.Kind.Should().Be(CleanupItemKind.Directory);
        c.CanRecycle.Should().BeTrue();
        c.RequiresElevation.Should().BeFalse();
        c.Blocked.Should().BeNull();
        c.SizeBytes.Should().Be(420_000_000);
    }

    [Fact]
    public void Shared_parent_and_protected_directories_are_shown_but_blocked()
    {
        var shared = Dir($@"{Folders.ProgramFiles}\Vendor", DirectoryRole.SharedParent);
        var system = Dir($@"{Folders.SystemRoot}\System32");
        var app = App(directories: [shared, system]);

        var candidates = _planner.Build(app, Report([shared, system]), afterUninstall: false);

        candidates.Should().HaveCount(2);
        candidates[0].Blocked.Should().Be(nameof(DeletionBlockerKind.SharedDirectory));
        candidates[1].Blocked.Should().Contain(nameof(DeletionBlockerKind.ProtectedPath));
    }

    [Fact]
    public void Installed_app_with_uninstaller_is_blocked_before_uninstall_and_allowed_after()
    {
        var dir = Dir($@"{Folders.ProgramFiles}\Foo", DirectoryRole.Program);
        var app = App(AppType.ThirdPartyInstalled, UninstallMethod.UninstallString, dir);

        _planner.Build(app, Report([dir]), afterUninstall: false).Single().Blocked.Should().Contain(nameof(DeletionBlockerKind.OfficialUninstallerAvailable));
        _planner.Build(app, Report([dir]), afterUninstall: true).Single().Blocked.Should().BeNull();
    }

    [Fact]
    public void Running_processes_block_before_uninstall_only()
    {
        var dir = Dir($@"{Folders.LocalAppData}\Foo", DirectoryRole.Program);
        var app = App(AppType.UserLevel, directories: dir) with { Processes = [new ProcessRecord { ProcessId = 1, Name = "foo.exe", ExecutablePath = dir.Path + @"\foo.exe" }] };

        _planner.Build(app, Report([dir]), afterUninstall: false).Single().Blocked.Should().Contain(nameof(DeletionBlockerKind.ApplicationRunning));
        _planner.Build(app, Report([dir]), afterUninstall: true).Single().Blocked.Should().BeNull();
    }

    [Fact]
    public void Non_file_items_cannot_be_recycled_and_machine_items_need_elevation()
    {
        var app = App();
        var report = Report(
            startup:
            [
                new StartupItemRecord { Name = "Foo", Kind = StartupItemKind.RegistryRun, Location = @"HKEY_CURRENT_USER\...\Run", Command = "foo.exe" },
                new StartupItemRecord { Name = "FooAll", Kind = StartupItemKind.RegistryRun, Location = @"HKEY_LOCAL_MACHINE\...\Run", Command = "foo.exe", IsMachineWide = true },
                new StartupItemRecord { Name = "Foo", Kind = StartupItemKind.StartupFolder, Location = @"C:\Users\Alice\...\Startup", Command = @"C:\Users\Alice\...\Startup\Foo.lnk" },
            ],
            services: [new ServiceRecord { Name = "FooSvc", State = "Stopped" }, new ServiceRecord { Name = "FooLive", State = "Running" }],
            tasks: [new ScheduledTaskRecord { TaskName = "FooUpdate", TaskPath = "\\Foo", Execute = "x" }, new ScheduledTaskRecord { TaskName = "FooUpdate", TaskPath = "\\Foo", Execute = "y" }],
            registry: [new RegistryUninstallEntry { Scope = RegistryScope.CurrentUser, KeyName = "Foo", KeyPath = @"HKEY_CURRENT_USER\...\Uninstall\Foo" }],
            configKeys: [@"HKEY_CURRENT_USER\Software\Foo", @"HKEY_LOCAL_MACHINE\Software\Foo"]);

        var candidates = _planner.Build(app, report, afterUninstall: true);

        candidates.Where(c => c.Kind == CleanupItemKind.StartupRegistryValue).Should().HaveCount(2);
        candidates.Single(c => c.Target == @"HKEY_LOCAL_MACHINE\...\Run::FooAll").RequiresElevation.Should().BeTrue();
        candidates.Single(c => c.Kind == CleanupItemKind.StartupFolderItem).CanRecycle.Should().BeTrue();
        candidates.Where(c => c.Kind == CleanupItemKind.ScheduledTask).Should().ContainSingle("duplicate exec actions of one task collapse");
        candidates.Single(c => c.Target == "FooLive").Blocked.Should().NotBeNull("a running service must be stopped first");
        candidates.Single(c => c.Target == "FooSvc").Blocked.Should().BeNull();
        candidates.Where(c => c.Kind == CleanupItemKind.RegistryKey).Should().HaveCount(3).And.OnlyContain(c => !c.CanRecycle);
        candidates.Single(c => c.Target == @"HKEY_LOCAL_MACHINE\Software\Foo").RequiresElevation.Should().BeTrue();
        candidates.Single(c => c.Target == @"HKEY_CURRENT_USER\Software\Foo").RequiresElevation.Should().BeFalse();
    }

    [Fact]
    public void Validate_requires_confirmation_and_permanent_deletion_acknowledgement()
    {
        var dir = Dir($@"{Folders.LocalAppData}\Foo");
        var app = App(directories: dir);
        var items = _planner.Build(app, Report([dir], startup: [new StartupItemRecord { Name = "Foo", Kind = StartupItemKind.RegistryRun, Location = "HKCU\\Run" }]), afterUninstall: true);

        _planner.Validate(new CleanupPlan { Application = app, Items = items, UserConfirmed = false })
            .Should().Contain(nameof(DeletionBlockerKind.UserConfirmationRequired));

        _planner.Validate(new CleanupPlan { Application = app, Items = items, UserConfirmed = true })
            .Should().ContainSingle().Which.Should().Be("PermanentDeletionNotAcknowledged", "a registry value cannot go to the recycle bin");

        _planner.Validate(new CleanupPlan { Application = app, Items = items, UserConfirmed = true, PermanentDeletionAcknowledged = true })
            .Should().BeEmpty();

        _planner.Validate(new CleanupPlan { Application = app, Items = [items[0]], UserConfirmed = true, UseRecycleBin = false })
            .Should().ContainSingle().Which.Should().Be("PermanentDeletionNotAcknowledged");
    }

    [Fact]
    public void Validate_rejects_blocked_and_protected_items()
    {
        var app = App();
        var blocked = new CleanupCandidate { Kind = CleanupItemKind.Directory, Target = @"C:\Users\Alice\AppData\Local\Foo", CanRecycle = true, Blocked = "X" };
        var protectedDir = new CleanupCandidate { Kind = CleanupItemKind.Directory, Target = @"C:\Windows\Temp", CanRecycle = true };

        var problems = _planner.Validate(new CleanupPlan { Application = app, Items = [blocked, protectedDir], UserConfirmed = true, PermanentDeletionAcknowledged = true });

        problems.Should().HaveCount(2);
        problems.Should().Contain(p => p.Contains("Foo: X", StringComparison.Ordinal));
        problems.Should().Contain(p => p.Contains(nameof(DeletionBlockerKind.ProtectedPath), StringComparison.Ordinal));
    }
}
