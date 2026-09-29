using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Rules;

namespace WinAppInspector.Core.Tests.Rules;

public class DeletionGuardTests
{
    private const string AppDir = @"C:\Users\Alice\AppData\Local\Foo";

    private readonly DeletionGuard _guard = new(new ProtectedPathRule(WindowsKnownFolders.CreateDefault("Alice")));

    private static ApplicationEntity Residue(Action<ApplicationEntityBuilder>? configure = null)
    {
        var builder = new ApplicationEntityBuilder
        {
            AppType = AppType.SuspectedResidue,
            Confidence = ConfidenceLevel.High,
        };
        configure?.Invoke(builder);
        return builder.Build();
    }

    private DeletionVerdict Evaluate(ApplicationEntity app, bool confirmed = true, params string[] targets) =>
        _guard.Evaluate(new DeletionRequest
        {
            Application = app,
            TargetPaths = targets.Length == 0 ? [AppDir] : targets,
            UserConfirmed = confirmed,
        });

    [Fact]
    public void Confirmed_high_confidence_residue_in_its_own_directory_is_allowed()
    {
        var verdict = Evaluate(Residue());

        verdict.IsAllowed.Should().BeTrue();
        verdict.Blockers.Should().BeEmpty();
        verdict.CanShowSafeToDeleteLabel.Should().BeTrue();
    }

    [Fact]
    public void Missing_user_confirmation_blocks_but_keeps_the_safe_label()
    {
        var verdict = Evaluate(Residue(), confirmed: false);

        verdict.IsAllowed.Should().BeFalse();
        verdict.Blockers.Should().ContainSingle(b => b.Kind == DeletionBlockerKind.UserConfirmationRequired);
        verdict.HardBlockers.Should().BeEmpty();
        verdict.CanShowSafeToDeleteLabel.Should().BeTrue("§5.1 confirmation is a UI step, not an attribution problem");
    }

    [Fact]
    public void Undetermined_entities_are_never_deletable_nor_labelled_safe()
    {
        var verdict = Evaluate(Residue(b => b.AppType = AppType.Undetermined));

        verdict.IsAllowed.Should().BeFalse();
        verdict.Blockers.Should().Contain(b => b.Kind == DeletionBlockerKind.UndeterminedAttribution);
        verdict.CanShowSafeToDeleteLabel.Should().BeFalse("§9.10");
    }

    [Fact]
    public void Acknowledged_attribution_lifts_only_the_attribution_blockers()
    {
        var app = Residue(b =>
        {
            b.AppType = AppType.Undetermined;
            b.Confidence = ConfidenceLevel.Unknown;
        });

        var verdict = _guard.Evaluate(new DeletionRequest
        {
            Application = app,
            TargetPaths = [AppDir],
            UserConfirmed = true,
            AttributionAcknowledged = true,
        });

        verdict.IsAllowed.Should().BeTrue();
        verdict.CanShowSafeToDeleteLabel.Should().BeFalse("§9.10 forbids the label even when the user takes responsibility");

        var protectedTarget = _guard.Evaluate(new DeletionRequest
        {
            Application = app,
            TargetPaths = [@"C:\Windows\System32"],
            UserConfirmed = true,
            AttributionAcknowledged = true,
        });
        protectedTarget.Blockers.Should().Contain(b => b.Kind == DeletionBlockerKind.ProtectedPath, "acknowledgement never reaches the path rules");
    }

    [Theory]
    [InlineData(AppType.SystemComponent)]
    [InlineData(AppType.HardwareOrDriver)]
    [InlineData(AppType.SharedRuntime)]
    public void Protected_application_types_are_blocked(AppType type)
    {
        var verdict = Evaluate(Residue(b => b.AppType = type));

        verdict.IsAllowed.Should().BeFalse();
        verdict.Blockers.Should().Contain(b => b.Kind == DeletionBlockerKind.ProtectedApplicationType);
        verdict.CanShowSafeToDeleteLabel.Should().BeFalse();
    }

    [Theory]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"C:\Program Files\WindowsApps\Foo")]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Users\Alice\AppData\Local")]
    public void Protected_paths_are_blocked_even_when_everything_else_is_fine(string target)
    {
        var verdict = Evaluate(Residue(), confirmed: true, target);

        verdict.IsAllowed.Should().BeFalse();
        verdict.Blockers.Should().Contain(b => b.Kind == DeletionBlockerKind.ProtectedPath && b.Target == target);
    }

    [Fact]
    public void Target_outside_the_applications_directories_is_blocked()
    {
        var verdict = Evaluate(Residue(), confirmed: true, @"C:\Users\Alice\AppData\Local\Bar");

        verdict.Blockers.Should().ContainSingle(b => b.Kind == DeletionBlockerKind.PathNotOwnedByApplication);
    }

    [Fact]
    public void Subdirectory_of_an_owned_directory_is_allowed()
    {
        var verdict = Evaluate(Residue(), confirmed: true, AppDir + @"\Cache");

        verdict.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Running_process_inside_target_blocks_deletion()
    {
        var app = Residue(b => b.Processes.Add(new ProcessRecord
        {
            ProcessId = 4242,
            Name = "foo.exe",
            ExecutablePath = AppDir + @"\foo.exe",
        }));

        var verdict = Evaluate(app);

        verdict.Blockers.Should().ContainSingle(b => b.Kind == DeletionBlockerKind.ApplicationRunning && b.Target!.Contains("4242"));
        verdict.CanShowSafeToDeleteLabel.Should().BeFalse();
    }

    [Fact]
    public void Process_outside_target_does_not_block()
    {
        var app = Residue(b => b.Processes.Add(new ProcessRecord
        {
            ProcessId = 1,
            Name = "other.exe",
            ExecutablePath = @"C:\Program Files\Other\other.exe",
        }));

        Evaluate(app).IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Service_referencing_target_blocks_until_handled()
    {
        var app = Residue(b => b.Services.Add(new ServiceRecord
        {
            Name = "FooSvc",
            ExecutablePath = AppDir + @"\svc.exe",
        }));

        Evaluate(app).Blockers.Should().ContainSingle(b => b.Kind == DeletionBlockerKind.ReferencedByService && b.Target == "FooSvc");

        var handled = _guard.Evaluate(new DeletionRequest
        {
            Application = app,
            TargetPaths = [AppDir],
            UserConfirmed = true,
            ServicesHandled = true,
        });
        handled.IsAllowed.Should().BeTrue();
    }

    [Theory]
    [InlineData(ConfidenceLevel.Unknown)]
    [InlineData(ConfidenceLevel.Low)]
    public void Low_confidence_blocks_deletion(ConfidenceLevel confidence)
    {
        var verdict = Evaluate(Residue(b => b.Confidence = confidence));

        verdict.Blockers.Should().Contain(b => b.Kind == DeletionBlockerKind.LowConfidence);
        verdict.CanShowSafeToDeleteLabel.Should().BeFalse();
    }

    [Fact]
    public void Medium_confidence_is_enough()
    {
        Evaluate(Residue(b => b.Confidence = ConfidenceLevel.Medium)).IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Installed_app_with_official_uninstaller_must_not_be_deleted_manually()
    {
        var app = Residue(b =>
        {
            b.AppType = AppType.ThirdPartyInstalled;
            b.UninstallMethod = UninstallMethod.UninstallString;
        });

        var verdict = Evaluate(app);

        verdict.Blockers.Should().ContainSingle(b => b.Kind == DeletionBlockerKind.OfficialUninstallerAvailable);
    }

    [Fact]
    public void Portable_app_without_uninstaller_can_be_removed_manually()
    {
        var app = Residue(b => b.AppType = AppType.Portable);

        Evaluate(app).IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Every_target_is_checked_and_reported()
    {
        var verdict = Evaluate(Residue(), confirmed: false, @"C:\Windows", AppDir, @"D:\Elsewhere");

        verdict.Blockers.Select(b => b.Kind).Should().BeEquivalentTo(
        [
            DeletionBlockerKind.UserConfirmationRequired,
            DeletionBlockerKind.ProtectedPath,
            DeletionBlockerKind.PathNotOwnedByApplication,
        ]);
    }

    /// <summary>Small mutable builder so tests read as scenarios.</summary>
    private sealed class ApplicationEntityBuilder
    {
        public AppType AppType { get; set; } = AppType.SuspectedResidue;
        public ConfidenceLevel Confidence { get; set; } = ConfidenceLevel.High;
        public UninstallMethod UninstallMethod { get; set; } = UninstallMethod.None;
        public List<ProcessRecord> Processes { get; } = [];
        public List<ServiceRecord> Services { get; } = [];

        public ApplicationEntity Build() => new()
        {
            Id = AppDir,
            Name = "Foo",
            AppType = AppType,
            DetectionConfidence = Confidence,
            PreferredUninstallMethod = UninstallMethod,
            Directories = [new AppDirectory { Path = AppDir, Root = ScanRoot.LocalAppData }],
            Processes = Processes,
            Services = Services,
        };
    }
}
