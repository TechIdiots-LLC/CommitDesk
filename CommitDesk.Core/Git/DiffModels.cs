using CommunityToolkit.Mvvm.ComponentModel;

namespace CommitDesk.Core.Git;

public enum DiffLineKind { Context, Add, Delete, Hunk, NoNewline }

public sealed partial class DiffLine : ObservableObject
{
    public required DiffLineKind Kind { get; init; }

    /// <summary>The line exactly as git printed it, including the +/-/space prefix and any trailing \r.</summary>
    public required string Raw { get; init; }

    public int? OldLineNumber { get; init; }
    public int? NewLineNumber { get; init; }
    public int HunkIndex { get; init; }

    /// <summary>Whether this added/removed line is included in the next commit.</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>False for read-only diffs (history), so the gutter shows no selection state.</summary>
    public bool IsSelectable { get; set; }

    public bool IsChange => Kind is DiffLineKind.Add or DiffLineKind.Delete;

    public string Text => Kind == DiffLineKind.Hunk ? Raw.TrimEnd('\r') : Raw.Length > 0 ? Raw[1..].TrimEnd('\r') : "";
}

public sealed class DiffHunk
{
    public int Index { get; init; }
    public int OldStart { get; init; }
    public int OldCount { get; init; }
    public int NewStart { get; init; }
    public int NewCount { get; init; }
    public required DiffLine Header { get; init; }
    public List<DiffLine> Lines { get; } = [];
}

public sealed class FileDiff
{
    public required string Path { get; init; }
    public List<string> HeaderLines { get; } = [];
    public List<DiffHunk> Hunks { get; } = [];
    public bool IsBinary { get; set; }
    public bool IsNewFile { get; set; }
    public bool IsDeletedFile { get; set; }

    /// <summary>Hunk headers and body lines in display order.</summary>
    public IEnumerable<DiffLine> AllLines => Hunks.SelectMany(h => h.Lines.Prepend(h.Header));

    public IEnumerable<DiffLine> ChangeLines => Hunks.SelectMany(h => h.Lines).Where(l => l.IsChange);

    public bool HasChanges => ChangeLines.Any();

    public void SelectAll(bool selected)
    {
        foreach (var l in ChangeLines) l.IsSelected = selected;
    }
}
