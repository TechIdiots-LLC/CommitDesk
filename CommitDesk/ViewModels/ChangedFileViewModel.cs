using CommunityToolkit.Mvvm.ComponentModel;
using CommitDesk.Core.Git;

namespace CommitDesk.ViewModels;

/// <summary>
/// A changed file in the Changes tab. The checkbox includes or excludes the whole file; once the diff
/// has been loaded, individual lines can be toggled and the file becomes partially included.
/// </summary>
public sealed partial class ChangedFileViewModel(FileChange change) : ObservableObject
{
    bool _syncing;

    public FileChange Change { get; private set; } = change;

    /// <summary>Loaded when the file is first shown; carries the per-line selection.</summary>
    public FileDiff? Diff { get; private set; }

    [ObservableProperty] private bool _isIncluded = true;
    [ObservableProperty] private bool _isPartial;

    partial void OnIsIncludedChanged(bool value)
    {
        if (_syncing) return;
        Diff?.SelectAll(value);
        IsPartial = false;
    }

    public void Update(FileChange change) => Change = change;

    /// <summary>
    /// Attaches a freshly loaded diff. If it is identical to the previous one the line selection carries
    /// over; otherwise every line follows the file checkbox.
    /// </summary>
    public void SetDiff(FileDiff diff)
    {
        var old = Diff;
        if (old is not null && SameLines(old, diff))
        {
            foreach (var (o, n) in old.ChangeLines.Zip(diff.ChangeLines))
                n.IsSelected = o.IsSelected;
        }
        else
        {
            diff.SelectAll(IsIncluded);
        }
        Diff = diff;
        SyncFromDiff();
    }

    static bool SameLines(FileDiff a, FileDiff b)
        => a.AllLines.Select(l => l.Raw).SequenceEqual(b.AllLines.Select(l => l.Raw));

    /// <summary>Recomputes the checkbox state after lines were toggled.</summary>
    public void SyncFromDiff()
    {
        if (Diff is null) return;
        var lines = Diff.ChangeLines.ToList();
        if (lines.Count == 0) return;   // binary or mode-only change: the checkbox is all there is
        int selected = lines.Count(l => l.IsSelected);
        _syncing = true;
        IsIncluded = selected > 0;
        IsPartial = selected > 0 && selected < lines.Count;
        _syncing = false;
    }

    /// <summary>What to commit for this file, or null when excluded.</summary>
    public CommitFileSelection? ToSelection()
        => IsIncluded ? new CommitFileSelection(Change, IsPartial ? Diff : null) : null;
}
