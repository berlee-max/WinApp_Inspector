using CommunityToolkit.Mvvm.ComponentModel;
using WinAppInspector.Core.Actions;
using WinAppInspector.Core.Models;
using WinAppInspector.UI.Localization;

namespace WinAppInspector.UI.ViewModels;

/// <summary>One checklist row (§22: every item has a checkbox).</summary>
public sealed partial class CleanupItemViewModel : ObservableObject
{
    private readonly CleanupViewModel _owner;

    [ObservableProperty]
    private bool _isSelected;

    public CleanupItemViewModel(CleanupCandidate candidate, CleanupViewModel owner)
    {
        Candidate = candidate;
        _owner = owner;
        _isSelected = candidate.Blocked is null;
    }

    public CleanupCandidate Candidate { get; }

    public string KindText => Localize.Get("CleanupKind." + Candidate.Kind);
    public string Target => Candidate.Target;
    public string? Detail => Candidate.Detail;
    public string SizeText => Candidate.SizeBytes is null ? string.Empty : Localize.Bytes(Candidate.SizeBytes);
    public bool IsBlocked => Candidate.Blocked is not null;
    public bool CanSelect => Candidate.Blocked is null;
    public string? BlockedText => Candidate.Blocked is null ? null : Localize.Format("Cleanup.BlockedFormat", Localize.Get("Blocker." + Candidate.Blocked.Split(',')[0].Trim()));
    public bool IsPermanent => !Candidate.CanRecycle;
    public bool RequiresElevation => Candidate.RequiresElevation;

    partial void OnIsSelectedChanged(bool value) => _owner.SelectionChanged();
}

/// <summary>The "即将移除" dialog (§21–§24). Builds a <see cref="CleanupPlan"/> only when confirmed.</summary>
public sealed partial class CleanupViewModel : ObservableObject
{
    private readonly ApplicationEntity _application;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExecute))]
    private bool _userConfirmed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExecute))]
    private bool _permanentAcknowledged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExecute))]
    [NotifyPropertyChangedFor(nameof(NeedsPermanentAcknowledgement))]
    [NotifyPropertyChangedFor(nameof(PermanentWarningText))]
    private bool _useRecycleBin;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private string _selectedSizeText = string.Empty;

    public CleanupViewModel(ApplicationEntity application, IReadOnlyList<CleanupCandidate> candidates, bool afterUninstall, bool recycleBinDefault)
    {
        _application = application;
        Items = candidates.Select(c => new CleanupItemViewModel(c, this)).ToArray();
        AfterUninstall = afterUninstall;
        _useRecycleBin = recycleBinDefault;
        Title = afterUninstall ? Localize.Format("Cleanup.TitleAfterUninstall", application.Name) : Localize.Format("Cleanup.TitleManual", application.Name);
        SelectionChanged();
    }

    public IReadOnlyList<CleanupItemViewModel> Items { get; }
    public string Title { get; }
    public bool AfterUninstall { get; }
    public string ApplicationName => _application.Name;
    public bool HasItems => Items.Count > 0;

    /// <summary>True when any selected item cannot go to the recycle bin (§24).</summary>
    public bool NeedsPermanentAcknowledgement => !UseRecycleBin || Items.Any(i => i.IsSelected && i.IsPermanent);

    public string PermanentWarningText => Localize.Get("Cleanup.PermanentWarning");

    public bool CanExecute => UserConfirmed && SelectedCount > 0 && (!NeedsPermanentAcknowledgement || PermanentAcknowledged);

    public void SelectionChanged()
    {
        SelectedCount = Items.Count(i => i.IsSelected);
        var bytes = Items.Where(i => i.IsSelected).Sum(i => i.Candidate.SizeBytes ?? 0);
        SelectedSizeText = bytes > 0 ? Localize.Bytes(bytes) : string.Empty;
        OnPropertyChanged(nameof(NeedsPermanentAcknowledgement));
        OnPropertyChanged(nameof(CanExecute));
    }

    public CleanupPlan BuildPlan() => new()
    {
        Application = _application,
        Items = Items.Where(i => i.IsSelected).Select(i => i.Candidate).ToArray(),
        UserConfirmed = UserConfirmed,
        UseRecycleBin = UseRecycleBin,
        PermanentDeletionAcknowledged = PermanentAcknowledged,
    };
}
