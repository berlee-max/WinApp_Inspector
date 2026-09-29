using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinAppInspector.Core.Evidence;
using WinAppInspector.Core.Models;
using WinAppInspector.UI.Localization;

namespace WinAppInspector.UI.ViewModels;

public sealed record DetailRow(string Label, string Value);

/// <summary>The right-hand detail panel (§14): basic info, install info, directories, runtime, links, and "why".</summary>
public sealed partial class DetailViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(Entity))]
    [NotifyPropertyChangedFor(nameof(BasicInfo))]
    [NotifyPropertyChangedFor(nameof(InstallInfo))]
    [NotifyPropertyChangedFor(nameof(Directories))]
    [NotifyPropertyChangedFor(nameof(Processes))]
    [NotifyPropertyChangedFor(nameof(StartupItems))]
    [NotifyPropertyChangedFor(nameof(Services))]
    [NotifyPropertyChangedFor(nameof(ScheduledTasks))]
    [NotifyPropertyChangedFor(nameof(HasLinks))]
    [NotifyPropertyChangedFor(nameof(Reasons))]
    [NotifyPropertyChangedFor(nameof(Evidence))]
    [NotifyPropertyChangedFor(nameof(VerdictText))]
    [NotifyPropertyChangedFor(nameof(RecommendationText))]
    private ApplicationItemViewModel? _current;

    public bool HasSelection => Current is not null;

    public ApplicationEntity? Entity => Current?.Entity;

    public IReadOnlyList<DetailRow> BasicInfo => Entity is null ? [] : Rows(
        ("Detail.Name", Entity.Name),
        ("Detail.Version", Entity.Version),
        ("Detail.Publisher", Entity.Publisher),
        ("Detail.InstallDate", Entity.InstallDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
        ("Detail.LastModified", Entity.LastModified?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)),
        ("Detail.Signature", SignatureText(Entity.Signature)),
        ("Detail.DiskUsage", Entity.DiskUsageBytes is not null ? Localize.Bytes(Entity.DiskUsageBytes) : Entity.EstimatedSizeBytes is not null ? Localize.Bytes(Entity.EstimatedSizeBytes) + " " + Localize.Get("Size.RegistryEstimate") : null));

    public IReadOnlyList<DetailRow> InstallInfo => Entity is null ? [] : Rows(
        ("Detail.Type", Localize.AppType(Entity.AppType)),
        ("Detail.InstallLocation", Entity.InstallLocation),
        ("Detail.MainExecutable", Entity.MainExecutable),
        ("Detail.UninstallMethod", Entity.HasOfficialUninstaller ? Localize.UninstallMethod(Entity.PreferredUninstallMethod) : Localize.Get("UninstallMethod.None")),
        ("Detail.UninstallCommand", Entity.QuietUninstallCommand ?? Entity.UninstallCommand),
        ("Detail.MsiProductCode", Entity.MsiProductCode),
        ("Detail.AppxPackage", Entity.AppxPackageFullName),
        ("Detail.Confidence", Localize.Confidence(Entity.DetectionConfidence)),
        ("Detail.Sources", SourcesText(Entity.Sources)));

    public IReadOnlyList<AppDirectory> Directories => Entity?.Directories ?? [];
    public IReadOnlyList<ProcessRecord> Processes => Entity?.Processes ?? [];
    public IReadOnlyList<StartupItemRecord> StartupItems => Entity?.StartupItems ?? [];
    public IReadOnlyList<ServiceRecord> Services => Entity?.Services ?? [];
    public IReadOnlyList<ScheduledTaskRecord> ScheduledTasks => Entity?.ScheduledTasks ?? [];
    public bool HasLinks => StartupItems.Count > 0 || Services.Count > 0 || ScheduledTasks.Count > 0;
    public IReadOnlyList<Reason> Reasons => Entity?.Reasons ?? [];
    public IReadOnlyList<EvidenceItem> Evidence => Entity?.Evidence ?? [];

    public string VerdictText => Entity is null ? string.Empty : Localize.Format("Detail.VerdictFormat", Localize.AppType(Entity.AppType));

    /// <summary>§18 建议 / §41 建议: what the user can do, phrased conservatively.</summary>
    public string RecommendationText
    {
        get
        {
            if (Entity is null)
            {
                return string.Empty;
            }

            if (Entity.AppType.IsProtectedByDefault())
            {
                return Localize.Get("Recommend.Keep");
            }

            if (Entity.AppType == AppType.Undetermined)
            {
                return Localize.Get("Recommend.Undetermined");
            }

            if (Entity.HasOfficialUninstaller)
            {
                return Localize.Get("Recommend.OfficialUninstaller");
            }

            return Entity.AppType == AppType.SuspectedResidue
                ? Localize.Get("Recommend.Residue")
                : Localize.Get("Recommend.NoUninstaller");
        }
    }

    [RelayCommand]
    private void Close() => Current = null;

    [RelayCommand]
    private void OpenLocation(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var target = System.IO.Directory.Exists(path) ? path : System.IO.Path.GetDirectoryName(path);
            if (target is not null && System.IO.Directory.Exists(target))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{target}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Opening Explorer is a convenience; failing silently here is acceptable.
        }
    }

    private static DetailRow[] Rows(params (string Key, string? Value)[] rows) =>
        rows.Where(r => !string.IsNullOrWhiteSpace(r.Value)).Select(r => new DetailRow(Localize.Get(r.Key), r.Value!)).ToArray();

    private static string SignatureText(SignatureInfo signature)
    {
        var text = Localize.Signature(signature.Status);
        if (signature.Publisher is not null)
        {
            text += $"（{signature.Publisher}）";
        }

        if (signature.Error is not null && signature.Status is SignatureStatus.Invalid or SignatureStatus.ReadFailed)
        {
            text += " — " + signature.Error;
        }

        return text;
    }

    private static string SourcesText(DiscoverySource sources)
    {
        var parts = Enum.GetValues<DiscoverySource>()
            .Where(s => s != DiscoverySource.None && sources.HasFlag(s))
            .Select(s => Localize.Get("Source." + s));
        return string.Join(" + ", parts);
    }
}
