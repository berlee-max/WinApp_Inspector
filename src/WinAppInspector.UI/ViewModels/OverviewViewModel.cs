using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using WinAppInspector.Analysis.Search;
using WinAppInspector.Core.Models;

namespace WinAppInspector.UI.ViewModels;

/// <summary>§13.3 filters.</summary>
public enum AppFilter
{
    All = 0,
    ThirdParty = 1,
    Microsoft = 2,
    Portable = 3,
    UserLevel = 4,
    Residue = 5,
    Driver = 6,
    Runtime = 7,
    Undetermined = 8,
    Store = 9,
}

public sealed record FilterOption(AppFilter Filter, string Label)
{
    public bool IsDefault => Filter == AppFilter.All;
}

public sealed record StatTile(string Label, int Count);

/// <summary>Page one, 软件总览 (§13): statistics, list, filter, search and selection.</summary>
public sealed partial class OverviewViewModel : ObservableObject
{
    private readonly DetailViewModel _detail;

    [ObservableProperty]
    private AppFilter _filter = AppFilter.All;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _showSystemComponents;

    [ObservableProperty]
    private ApplicationItemViewModel? _selectedItem;

    [ObservableProperty]
    private string _sortProperty = nameof(ApplicationItemViewModel.Name);

    [ObservableProperty]
    private bool _sortDescending;

    [ObservableProperty]
    private IReadOnlyList<StatTile> _stats = [];

    [ObservableProperty]
    private int _visibleCount;

    public OverviewViewModel(DetailViewModel detail)
    {
        _detail = detail;
        View = CollectionViewSource.GetDefaultView(Items);
        View.Filter = Accept;
        ApplySort();
    }

    public ObservableCollection<ApplicationItemViewModel> Items { get; } = [];

    public ICollectionView View { get; }

    public IReadOnlyList<FilterOption> Filters { get; } =
        Enum.GetValues<AppFilter>().Select(f => new FilterOption(f, Localization.Localize.Get("Filter." + f))).ToArray();

    /// <summary>Replaces the list after a scan. Keeps the selection when the same application is still present.</summary>
    public void Load(IEnumerable<ApplicationItemViewModel> items)
    {
        var selectedId = SelectedItem?.Id;
        using (View.DeferRefresh())
        {
            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }
        }

        SelectedItem = selectedId is null ? null : Items.FirstOrDefault(i => i.Id == selectedId);
        RefreshStats();
    }

    public void RefreshStats()
    {
        var all = Items.Select(i => i.Entity).ToList();
        Stats =
        [
            new StatTile(Localization.Localize.Get("Stats.Identified"), all.Count(a => !a.AppType.IsSystemLike())),
            new StatTile(Localization.Localize.Get("Stats.ThirdParty"), all.Count(a => a.AppType is AppType.ThirdPartyInstalled or AppType.UserLevel or AppType.StoreApp)),
            new StatTile(Localization.Localize.Get("Stats.Portable"), all.Count(a => a.AppType == AppType.Portable)),
            new StatTile(Localization.Localize.Get("Stats.Residue"), all.Count(a => a.AppType == AppType.SuspectedResidue)),
            new StatTile(Localization.Localize.Get("Stats.System"), all.Count(a => a.AppType.IsSystemLike())),
            new StatTile(Localization.Localize.Get("Stats.Undetermined"), all.Count(a => a.AppType == AppType.Undetermined)),
        ];
        VisibleCount = View.Cast<object>().Count();
    }

    public void SortBy(string property)
    {
        if (SortProperty == property)
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortProperty = property;
            SortDescending = false;
        }

        ApplySort();
    }

    partial void OnFilterChanged(AppFilter value) => Refresh();

    partial void OnSearchTextChanged(string value) => Refresh();

    partial void OnShowSystemComponentsChanged(bool value) => Refresh();

    partial void OnSelectedItemChanged(ApplicationItemViewModel? value) => _detail.Current = value;

    private void Refresh()
    {
        View.Refresh();
        VisibleCount = View.Cast<object>().Count();
    }

    private void ApplySort()
    {
        View.SortDescriptions.Clear();
        View.SortDescriptions.Add(new SortDescription(SortProperty, SortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending));
        if (SortProperty != nameof(ApplicationItemViewModel.Name))
        {
            View.SortDescriptions.Add(new SortDescription(nameof(ApplicationItemViewModel.Name), ListSortDirection.Ascending));
        }
    }

    private bool Accept(object item)
    {
        if (item is not ApplicationItemViewModel vm)
        {
            return false;
        }

        var app = vm.Entity;
        if (!ShowSystemComponents && app.AppType == AppType.SystemComponent && Filter != AppFilter.Microsoft)
        {
            return false; // §9.9 hidden by default
        }

        var passesFilter = Filter switch
        {
            AppFilter.All => true,
            AppFilter.ThirdParty => !app.IsMicrosoft && app.AppType is AppType.ThirdPartyInstalled or AppType.UserLevel or AppType.Portable or AppType.StoreApp,
            AppFilter.Microsoft => app.IsMicrosoft,
            AppFilter.Portable => app.AppType == AppType.Portable,
            AppFilter.UserLevel => app.AppType == AppType.UserLevel,
            AppFilter.Residue => app.AppType == AppType.SuspectedResidue,
            AppFilter.Driver => app.AppType == AppType.HardwareOrDriver,
            AppFilter.Runtime => app.AppType == AppType.SharedRuntime,
            AppFilter.Undetermined => app.AppType == AppType.Undetermined,
            AppFilter.Store => app.AppType == AppType.StoreApp,
            _ => true,
        };

        return passesFilter && SearchMatcher.Matches(app, SearchText);
    }
}
