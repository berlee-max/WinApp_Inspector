using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinAppInspector.Analysis.Search;
using WinAppInspector.Core.Models;
using WinAppInspector.UI.Localization;

namespace WinAppInspector.UI.ViewModels;

/// <summary>Sidebar categories of the app manager.</summary>
public enum CategoryKind
{
    All = 0,
    Running = 1,
    Portable = 2,
    Residue = 3,
    Undetermined = 4,
    Checked = 5,
    Store = 6,
    Installed = 7,
    Vendor = 8,
    System = 9,
}

public enum SortKey
{
    Name = 0,
    Size = 1,
    Date = 2,
}

public sealed record SortOption(SortKey Key, string Label);

/// <summary>One sidebar entry with a live count.</summary>
public sealed partial class CategoryItem : ObservableObject
{
    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private bool _isSelected;

    public CategoryItem(CategoryKind kind, string label, string? vendor = null)
    {
        Kind = kind;
        Label = label;
        Vendor = vendor;
    }

    public CategoryKind Kind { get; }
    public string Label { get; }

    /// <summary>The publisher for <see cref="CategoryKind.Vendor"/> entries.</summary>
    public string? Vendor { get; }
}

/// <summary>
/// The application manager: one row per application, sidebar categories with counts, search, sort and the tick boxes
/// that feed the bulk uninstall bar. Selection drives the detail panel through <see cref="DetailViewModel"/>.
/// </summary>
public sealed partial class AppManagerViewModel : ObservableObject
{
    private const int VendorGroups = 8;
    private readonly DetailViewModel _detail;
    private bool _suppressCategoryEvents;

    [ObservableProperty]
    private ApplicationItemViewModel? _selectedItem;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private SortOption _sort;

    [ObservableProperty]
    private bool _showSystemComponents;

    [ObservableProperty]
    private CategoryItem _selectedCategory;

    [ObservableProperty]
    private int _visibleCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChecked))]
    [NotifyPropertyChangedFor(nameof(CheckedSummaryText))]
    private int _checkedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CheckedSummaryText))]
    private string _checkedSizeText = string.Empty;

    public AppManagerViewModel(DetailViewModel detail)
    {
        _detail = detail;
        SortOptions =
        [
            new SortOption(SortKey.Name, Localize.Get("Sort.Name")),
            new SortOption(SortKey.Size, Localize.Get("Sort.Size")),
            new SortOption(SortKey.Date, Localize.Get("Sort.Date")),
        ];
        _sort = SortOptions[0];

        PrimaryCategories =
        [
            new CategoryItem(CategoryKind.All, Localize.Get("Category.All")),
            new CategoryItem(CategoryKind.Running, Localize.Get("Category.Running")),
            new CategoryItem(CategoryKind.Portable, Localize.Get("Category.Portable")),
            new CategoryItem(CategoryKind.Residue, Localize.Get("Category.Residue")),
            new CategoryItem(CategoryKind.Undetermined, Localize.Get("Category.Undetermined")),
            new CategoryItem(CategoryKind.Checked, Localize.Get("Category.Checked")),
        ];
        SourceCategories =
        [
            new CategoryItem(CategoryKind.Store, Localize.Get("Category.Store")),
            new CategoryItem(CategoryKind.Installed, Localize.Get("Category.Installed")),
            new CategoryItem(CategoryKind.System, Localize.Get("Category.System")),
        ];
        _selectedCategory = PrimaryCategories[0];
        _selectedCategory.IsSelected = true;

        // The filter reads SelectedCategory, so the view is wired up only once the categories exist.
        View = CollectionViewSource.GetDefaultView(Items);
        View.Filter = Accept;
        ApplySort();
    }

    public ObservableCollection<ApplicationItemViewModel> Items { get; } = [];

    public ICollectionView View { get; }

    public IReadOnlyList<SortOption> SortOptions { get; }

    public ObservableCollection<CategoryItem> PrimaryCategories { get; }

    public ObservableCollection<CategoryItem> SourceCategories { get; }

    public ObservableCollection<CategoryItem> VendorCategories { get; } = [];

    public bool HasChecked => CheckedCount > 0;

    public string CheckedSummaryText => CheckedCount == 0
        ? Localize.Get("Manager.NothingChecked")
        : Localize.Format("Manager.CheckedFormat", CheckedCount, CheckedSizeText);

    public IReadOnlyList<ApplicationItemViewModel> CheckedItems => Items.Where(i => i.IsChecked).ToList();

    /// <summary>Replaces the list after a scan. Keeps selection and ticks for applications that are still present.</summary>
    public void Load(IEnumerable<ApplicationItemViewModel> items)
    {
        var selectedId = SelectedItem?.Id;
        var checkedIds = Items.Where(i => i.IsChecked).Select(i => i.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var old in Items)
        {
            old.PropertyChanged -= OnItemPropertyChanged;
        }

        // Not inside View.DeferRefresh(): a sorted ListCollectionView throws when its source changes while deferred.
        Items.Clear();
        foreach (var item in items)
        {
            item.PropertyChanged += OnItemPropertyChanged;
            if (checkedIds.Contains(item.Id))
            {
                item.IsChecked = true;
            }

            Items.Add(item);
        }

        RebuildVendorCategories();
        RefreshCounts();
        SelectedItem = selectedId is null ? null : Items.FirstOrDefault(i => i.Id == selectedId);
    }

    /// <summary>Recomputes every count (after sizes arrive or ticks change).</summary>
    public void RefreshCounts()
    {
        foreach (var category in PrimaryCategories.Concat(SourceCategories).Concat(VendorCategories))
        {
            category.Count = Items.Count(i => Matches(i, category));
        }

        var ticked = Items.Where(i => i.IsChecked).ToList();
        CheckedCount = ticked.Count;
        var bytes = ticked.Sum(i => i.DiskUsageBytes ?? 0);
        CheckedSizeText = Localize.Bytes(bytes);
        VisibleCount = View.Cast<object>().Count();
    }

    [RelayCommand]
    private void SelectCategory(CategoryItem? category)
    {
        if (category is null || category == SelectedCategory)
        {
            return;
        }

        _suppressCategoryEvents = true;
        foreach (var c in PrimaryCategories.Concat(SourceCategories).Concat(VendorCategories))
        {
            c.IsSelected = c == category;
        }

        _suppressCategoryEvents = false;
        SelectedCategory = category;
    }

    [RelayCommand]
    private void CheckAllVisible()
    {
        foreach (var item in View.Cast<ApplicationItemViewModel>().Where(i => i.IsActionable))
        {
            item.IsChecked = true;
        }
    }

    [RelayCommand]
    private void UncheckAll()
    {
        foreach (var item in Items)
        {
            item.IsChecked = false;
        }
    }

    [RelayCommand]
    private void ShowDetail(ApplicationItemViewModel? item)
    {
        if (item is not null)
        {
            SelectedItem = item;
            _detail.Current = item;
        }
    }

    partial void OnSelectedCategoryChanged(CategoryItem value)
    {
        if (!_suppressCategoryEvents)
        {
            Refresh();
        }
    }

    partial void OnSearchTextChanged(string value) => Refresh();

    partial void OnShowSystemComponentsChanged(bool value) => Refresh();

    partial void OnSortChanged(SortOption value) => ApplySort();

    partial void OnSelectedItemChanged(ApplicationItemViewModel? value)
    {
        if (value is not null)
        {
            _detail.Current = value;
        }
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ApplicationItemViewModel.IsChecked))
        {
            var checkedCategory = PrimaryCategories.First(c => c.Kind == CategoryKind.Checked);
            checkedCategory.Count = Items.Count(i => i.IsChecked);
            var ticked = Items.Where(i => i.IsChecked).ToList();
            CheckedCount = ticked.Count;
            CheckedSizeText = Localize.Bytes(ticked.Sum(i => i.DiskUsageBytes ?? 0));
            if (SelectedCategory.Kind == CategoryKind.Checked)
            {
                Refresh();
            }
        }
    }

    private void Refresh()
    {
        View.Refresh();
        VisibleCount = View.Cast<object>().Count();
    }

    private void ApplySort()
    {
        View.SortDescriptions.Clear();
        switch (Sort.Key)
        {
            case SortKey.Size:
                View.SortDescriptions.Add(new SortDescription(nameof(ApplicationItemViewModel.SizeSortKey), ListSortDirection.Descending));
                break;
            case SortKey.Date:
                View.SortDescriptions.Add(new SortDescription(nameof(ApplicationItemViewModel.InstallDate), ListSortDirection.Descending));
                break;
            default:
                break;
        }

        View.SortDescriptions.Add(new SortDescription(nameof(ApplicationItemViewModel.Name), ListSortDirection.Ascending));
    }

    private void RebuildVendorCategories()
    {
        VendorCategories.Clear();
        var groups = Items
            .Where(IsListed)
            .GroupBy(i => i.VendorKey, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .Take(VendorGroups);
        foreach (var group in groups)
        {
            VendorCategories.Add(new CategoryItem(CategoryKind.Vendor, group.Key, group.Key));
        }
    }

    /// <summary>The main list: real applications. System pieces, undecided folders and idle portable folders live in their own categories.</summary>
    private bool IsListed(ApplicationItemViewModel item)
    {
        if (item.IsSystemLike)
        {
            return ShowSystemComponents;
        }

        return item.AppType != AppType.Undetermined && !item.IsIdlePortable;
    }

    private bool Matches(ApplicationItemViewModel item, CategoryItem category) => category.Kind switch
    {
        CategoryKind.All => IsListed(item),
        CategoryKind.Running => item.IsRunning && !item.IsSystemLike,
        CategoryKind.Portable => item.AppType is AppType.Portable or AppType.UserLevel && item.Entity.RegistryEntries.Count == 0,
        CategoryKind.Residue => item.AppType == AppType.SuspectedResidue,
        CategoryKind.Undetermined => item.AppType == AppType.Undetermined,
        CategoryKind.Checked => item.IsChecked,
        CategoryKind.Store => item.Source == SourceKind.Store && IsListed(item),
        CategoryKind.Installed => item.Source == SourceKind.Installed && IsListed(item),
        CategoryKind.Vendor => IsListed(item) && string.Equals(item.VendorKey, category.Vendor, StringComparison.OrdinalIgnoreCase),
        CategoryKind.System => item.IsSystemLike,
        _ => false,
    };

    private bool Accept(object item)
    {
        if (item is not ApplicationItemViewModel vm)
        {
            return false;
        }

        if (!Matches(vm, SelectedCategory))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(SearchText) || SearchMatcher.Matches(vm.Entity, SearchText);
    }
}
