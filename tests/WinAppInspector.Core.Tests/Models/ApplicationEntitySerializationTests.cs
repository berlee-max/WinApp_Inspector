using System.Text.Json;
using System.Text.Json.Serialization;
using WinAppInspector.Core.Evidence;
using WinAppInspector.Core.Models;

namespace WinAppInspector.Core.Tests.Models;

/// <summary>The UI caches resolved entities as JSON (§33); the records must survive a System.Text.Json round trip.</summary>
public class ApplicationEntitySerializationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void Entity_round_trips_through_json()
    {
        var entity = new ApplicationEntity
        {
            Id = "dir:C:\\Users\\U\\AppData\\Local\\Foo",
            Name = "Foo Desktop",
            Version = "2.1.0",
            Publisher = "Foo Technologies",
            AppType = AppType.UserLevel,
            InstallLocation = @"C:\Users\U\AppData\Local\Foo",
            MainExecutable = @"C:\Users\U\AppData\Local\Foo\Foo.exe",
            PreferredUninstallMethod = UninstallMethod.None,
            Signature = new SignatureInfo { Status = SignatureStatus.Valid, Publisher = "Foo Technologies Ltd.", NotAfter = DateTimeOffset.UtcNow },
            IsMicrosoft = false,
            RiskLevel = RiskLevel.Medium,
            DetectionConfidence = ConfidenceLevel.High,
            Sources = DiscoverySource.Directory | DiscoverySource.Process | DiscoverySource.Startup,
            InstallDate = new DateOnly(2024, 9, 15),
            LastModified = DateTimeOffset.UtcNow,
            DiskUsageBytes = 850L * 1024 * 1024,
            Directories =
            [
                new AppDirectory
                {
                    Path = @"C:\Users\U\AppData\Local\Foo", Root = ScanRoot.LocalAppData, Role = DirectoryRole.Program, SizeBytes = 100,
                    ExecutablePaths = [@"C:\Users\U\AppData\Local\Foo\Foo.exe"], ChildDirectoryNames = ["Cache"],
                    AttributionEvidence = [new EvidenceItem(EvidenceKind.MainExecutableProductNameMatch, "Foo.exe: Foo Desktop")],
                },
            ],
            Processes = [new ProcessRecord { ProcessId = 4242, Name = "Foo.exe", ExecutablePath = @"C:\Users\U\AppData\Local\Foo\Foo.exe" }],
            StartupItems = [new StartupItemRecord { Name = "Foo", Kind = StartupItemKind.RegistryRun, Location = "HKCU\\...\\Run", Command = "Foo.exe", IsDisabled = false }],
            Services = [new ServiceRecord { Name = "FooSvc", State = "Stopped" }],
            ScheduledTasks = [new ScheduledTaskRecord { TaskName = "FooUpdate", TaskPath = "\\", Execute = "x.exe" }],
            RegistryEntries = [new RegistryUninstallEntry { Scope = RegistryScope.CurrentUser, KeyName = "Foo", KeyPath = "HKCU\\...\\Foo", DisplayName = "Foo", EstimatedSizeKb = 12 }],
            Packages = [new AppxPackageRecord { Name = "P", PackageFullName = "P_1", PackageFamilyName = "P_x", NonRemovable = null }],
            Executables = [new ExecutableMetadata { Path = @"C:\Users\U\AppData\Local\Foo\Foo.exe", ProductName = "Foo Desktop" }],
            Evidence = [new EvidenceItem(EvidenceKind.SignaturePublisherMatch, "Foo.exe: Foo Technologies Ltd."), new EvidenceItem(EvidenceKind.RunningProcessInDirectory, "Foo.exe (PID 4242)")],
            Reasons = [new Reason(ReasonKind.UninstallEntryMissing), new Reason(ReasonKind.LongUnmodified, "2025-11-21")],
        };

        var json = JsonSerializer.Serialize(entity, Options);
        var back = JsonSerializer.Deserialize<ApplicationEntity>(json, Options);

        back.Should().NotBeNull();
        back!.Id.Should().Be(entity.Id);
        back.Name.Should().Be(entity.Name);
        back.AppType.Should().Be(AppType.UserLevel);
        back.Sources.Should().Be(entity.Sources);
        back.InstallDate.Should().Be(new DateOnly(2024, 9, 15));
        back.Signature.Status.Should().Be(SignatureStatus.Valid);
        back.Signature.Publisher.Should().Be("Foo Technologies Ltd.");
        back.IsRunning.Should().BeTrue("derived from the deserialized process list");
        back.HasOfficialUninstaller.Should().BeFalse();
        back.Directories.Should().ContainSingle().Which.AttributionEvidence.Should().ContainSingle().Which.Kind.Should().Be(EvidenceKind.MainExecutableProductNameMatch);
        back.Directories[0].ExecutablePaths.Should().ContainSingle();
        back.Processes.Should().ContainSingle().Which.ProcessId.Should().Be(4242);
        back.StartupItems.Should().ContainSingle().Which.IsDisabled.Should().BeFalse();
        back.Services.Should().ContainSingle();
        back.ScheduledTasks.Should().ContainSingle();
        back.RegistryEntries.Should().ContainSingle().Which.EstimatedSizeKb.Should().Be(12);
        back.Packages.Should().ContainSingle().Which.NonRemovable.Should().BeNull();
        back.Executables.Should().ContainSingle();
        back.Evidence.Should().HaveCount(2);
        back.Evidence[0].Weight.Should().Be(EvidenceWeight.High, "the weight is derived from the kind, not stored");
        back.Reasons.Should().HaveCount(2);
        back.Reasons[1].Detail.Should().Be("2025-11-21");
        back.Reasons[0].IsPositive.Should().BeFalse();
        back.DiskUsageBytes.Should().Be(entity.DiskUsageBytes);
    }

    [Fact]
    public void List_of_entities_round_trips()
    {
        var list = new List<ApplicationEntity>
        {
            new() { Id = "a", Name = "A" },
            new() { Id = "b", Name = "B", AppType = AppType.SystemComponent, RiskLevel = RiskLevel.Protected },
        };

        var back = JsonSerializer.Deserialize<List<ApplicationEntity>>(JsonSerializer.Serialize(list, Options), Options);

        back.Should().HaveCount(2);
        back![1].RiskLevel.Should().Be(RiskLevel.Protected);
        back[0].Directories.Should().BeEmpty();
        back[0].Signature.Status.Should().Be(SignatureStatus.NotChecked);
    }
}
