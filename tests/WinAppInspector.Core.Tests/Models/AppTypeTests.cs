using WinAppInspector.Core.Evidence;
using WinAppInspector.Core.Models;

namespace WinAppInspector.Core.Tests.Models;

public class AppTypeTests
{
    [Fact]
    public void There_are_exactly_ten_categories()
    {
        Enum.GetValues<AppType>().Should().HaveCount(10, "§9 defines ten categories");
    }

    [Fact]
    public void Enum_values_are_stable_for_persistence()
    {
        ((int)AppType.Undetermined).Should().Be(0);
        ((int)AppType.SystemComponent).Should().Be(9);
        Enum.GetValues<AppType>().Cast<int>().Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(AppType.SystemComponent, true)]
    [InlineData(AppType.HardwareOrDriver, true)]
    [InlineData(AppType.SharedRuntime, true)]
    [InlineData(AppType.ThirdPartyInstalled, false)]
    [InlineData(AppType.Portable, false)]
    [InlineData(AppType.SuspectedResidue, false)]
    [InlineData(AppType.Undetermined, false)]
    public void Protected_by_default_matches_sections_9_7_to_9_9(AppType type, bool expected)
    {
        type.IsProtectedByDefault().Should().Be(expected);
    }

    [Fact]
    public void Undetermined_never_allows_the_safe_label()
    {
        AppType.Undetermined.AllowsSafeToDeleteLabel().Should().BeFalse();
        AppType.SuspectedResidue.AllowsSafeToDeleteLabel().Should().BeTrue();
        AppType.SystemComponent.AllowsSafeToDeleteLabel().Should().BeFalse();
    }

    [Theory]
    [InlineData(AppType.Portable, true)]
    [InlineData(AppType.SuspectedResidue, true)]
    [InlineData(AppType.UserLevel, true)]
    [InlineData(AppType.AppCache, true)]
    [InlineData(AppType.ThirdPartyInstalled, false)]
    [InlineData(AppType.StoreApp, false)]
    [InlineData(AppType.SystemComponent, false)]
    [InlineData(AppType.Undetermined, false)]
    public void Manual_removal_eligibility_follows_section_22(AppType type, bool expected)
    {
        type.IsEligibleForManualRemoval().Should().Be(expected);
    }

    [Fact]
    public void Entity_defaults_are_conservative()
    {
        var entity = new ApplicationEntity { Id = "x", Name = "X" };

        entity.AppType.Should().Be(AppType.Undetermined);
        entity.RiskLevel.Should().Be(RiskLevel.Unknown);
        entity.DetectionConfidence.Should().Be(ConfidenceLevel.Unknown);
        entity.IsRunning.Should().BeFalse();
        entity.HasOfficialUninstaller.Should().BeFalse();
        entity.Signature.Status.Should().Be(SignatureStatus.NotChecked);
        entity.Reasons.Should().BeEmpty();
    }

    [Fact]
    public void IsRunning_is_derived_from_processes()
    {
        var entity = new ApplicationEntity
        {
            Id = "x",
            Name = "X",
            Processes = [new ProcessRecord { ProcessId = 1, Name = "x.exe" }],
        };

        entity.IsRunning.Should().BeTrue();
        (entity with { Processes = [] }).IsRunning.Should().BeFalse();
    }

    [Fact]
    public void Negative_reasons_are_flagged()
    {
        new Reason(ReasonKind.UninstallEntryMissing).IsPositive.Should().BeFalse();
        new Reason(ReasonKind.MainExecutableMissing).IsPositive.Should().BeFalse();
        new Reason(ReasonKind.SignatureValid).IsPositive.Should().BeTrue();
        new Reason(ReasonKind.LongUnmodified, "2025-11-21").Detail.Should().Be("2025-11-21");
    }
}
