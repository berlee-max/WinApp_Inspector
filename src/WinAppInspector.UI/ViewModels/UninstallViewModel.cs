using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WinAppInspector.Core.Models;

namespace WinAppInspector.UI.ViewModels;

/// <summary>
/// Page three, 卸载与清理 (§20–22). This phase lists the candidates and their official uninstall route;
/// the actions themselves (uninstall, residue rescan, manual removal) arrive with the Actions project in the next phase.
/// </summary>
public sealed partial class UninstallViewModel : ObservableObject
{
    private readonly DetailViewModel _detail;

    [ObservableProperty]
    private ApplicationItemViewModel? _selectedItem;

    [ObservableProperty]
    private bool _showManualCandidates;

    public UninstallViewModel(DetailViewModel detail)
    {
        _detail = detail;
    }

    /// <summary>Applications with an official uninstaller (§20.1).</summary>
    public ObservableCollection<ApplicationItemViewModel> Uninstallable { get; } = [];

    /// <summary>§22: portable apps, clear residue and user-level apps without an uninstaller.</summary>
    public ObservableCollection<ApplicationItemViewModel> ManualCandidates { get; } = [];

    public void Load(IEnumerable<ApplicationItemViewModel> items)
    {
        Uninstallable.Clear();
        ManualCandidates.Clear();
        foreach (var item in items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var app = item.Entity;
            if (app.AppType.IsProtectedByDefault())
            {
                continue; // §9.7–9.9: no removal entry point at all
            }

            if (app.HasOfficialUninstaller)
            {
                Uninstallable.Add(item);
            }
            else if (app.AppType.IsEligibleForManualRemoval())
            {
                ManualCandidates.Add(item);
            }
        }
    }

    partial void OnSelectedItemChanged(ApplicationItemViewModel? value)
    {
        if (value is not null)
        {
            _detail.Current = value;
        }
    }
}
