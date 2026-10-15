using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommitDesk.Core.Git;

namespace CommitDesk.ViewModels;

// Changes tab: changed files, per-line selection, commit.
public sealed partial class MainViewModel
{
    const int MaxDisplayLines = 20000;

    bool _settingAllIncluded;

    public ObservableCollection<ChangedFileViewModel> ChangedFiles { get; } = [];

    [ObservableProperty] private ChangedFileViewModel? _selectedChange;
    [ObservableProperty] private IReadOnlyList<DiffLine>? _changeDiffLines;
    [ObservableProperty] private string? _changeDiffMessage;

    /// <summary>Set instead of the text diff when the selected file is an image.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChangeImage), nameof(IsChangeText))]
    private ImageDiff? _changeImageDiff;

    /// <summary>Set instead of a diff when the selected file has merge conflicts.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChangeConflict), nameof(IsChangeText), nameof(ConflictStatusText),
        nameof(UseOursText), nameof(UseTheirsText))]
    private ConflictInfo? _changeConflict;

    ConflictSides _conflictSides = new("your branch", "the other branch");

    public bool IsChangeImage => ChangeImageDiff is not null;
    public bool IsChangeConflict => ChangeConflict is not null;
    public bool IsChangeText => ChangeImageDiff is null && ChangeConflict is null;

    public string ConflictStatusText => ChangeConflict switch
    {
        null => "",
        { HasOurs: false } => $"{_conflictSides.Ours} deleted this file, and {_conflictSides.Theirs} changed it.",
        { HasTheirs: false } => $"{_conflictSides.Theirs} deleted this file, and {_conflictSides.Ours} changed it.",
        { Markers: > 0 } c => $"{c.Markers} conflict{(c.Markers == 1 ? "" : "s")} left between {_conflictSides.Ours} and " +
                              $"{_conflictSides.Theirs}. Open the file in your editor and keep what you want between each " +
                              "<<<<<<< and >>>>>>> marker, removing the markers.",
        _ => HasOperation
            ? "No conflicts remaining. The file is included when you continue."
            : "No conflicts remaining. The file is included when you commit.",
    };

    public string UseOursText => ChangeConflict is { HasOurs: false } ? $"Delete it, as in {_conflictSides.Ours}" : $"Use {_conflictSides.Ours}'s version";
    public string UseTheirsText => ChangeConflict is { HasTheirs: false } ? $"Delete it, as in {_conflictSides.Theirs}" : $"Use {_conflictSides.Theirs}'s version";

    [RelayCommand]
    async Task ResolveConflict(ConflictChoice choice)
    {
        if (Repo is null || SelectedChange is not { } file || ChangeConflict is null) return;
        await RunAsync("Resolving…", () => Repo.ResolveConflictAsync(file.Change.Path, choice));
    }

    [RelayCommand]
    Task OpenConflictInEditor() => SelectedChange is { } file ? OpenFileInEditorAsync(file) : Task.CompletedTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCommit))]
    private string _commitSummary = "";

    [ObservableProperty] private string _commitDescription = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommitButtonText), nameof(CanCommit))]
    private bool _isAmending;

    [ObservableProperty] private bool _allIncluded = true;

    public string ChangedFilesHeader => ChangedFiles.Count switch
    {
        0 => "No changed files",
        1 => "1 changed file",
        var n => $"{n} changed files",
    };

    public bool HasChanges => ChangedFiles.Count > 0;

    /// <summary>The count shown on the Changes tab.</summary>
    public int ChangedFilesCount => ChangedFiles.Count;

    public string CommitButtonText => IsAmending ? "Amend last commit" : $"Commit to {Status?.Branch ?? "HEAD"}";

    public bool CanCommit => Repo is not null && !string.IsNullOrWhiteSpace(CommitSummary) &&
                             (IsAmending || ChangedFiles.Any(f => f.IsIncluded));

    async Task UpdateChangedFilesAsync(IReadOnlyList<FileChange> files)
    {
        var existing = ChangedFiles.ToDictionary(f => f.Change.Path + "|" + f.Change.Kind);
        var selectedKey = SelectedChange is { } s ? s.Change.Path + "|" + s.Change.Kind : null;

        foreach (var f in ChangedFiles) f.PropertyChanged -= OnChangedFilePropertyChanged;
        ChangedFiles.Clear();
        foreach (var change in files)
        {
            var key = change.Path + "|" + change.Kind;
            if (existing.TryGetValue(key, out var vm)) vm.Update(change);
            else vm = new ChangedFileViewModel(change) { IsIncluded = true };
            vm.PropertyChanged += OnChangedFilePropertyChanged;
            ChangedFiles.Add(vm);
        }
        OnPropertyChanged(nameof(ChangedFilesHeader));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(ChangedFilesCount));
        OnPropertyChanged(nameof(HasConflicts));
        OnPropertyChanged(nameof(ShowOperationBanner));
        OnPropertyChanged(nameof(OperationBanner));
        UpdateIncludedState();

        var reselect = ChangedFiles.FirstOrDefault(f => f.Change.Path + "|" + f.Change.Kind == selectedKey)
                       ?? ChangedFiles.FirstOrDefault();
        if (reselect == SelectedChange)
            await LoadChangeDiffAsync(reselect);   // same file, but its contents may have changed
        else
            SelectedChange = reselect;
    }

    void OnChangedFilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChangedFileViewModel.IsIncluded)) UpdateIncludedState();
    }

    void UpdateIncludedState()
    {
        _settingAllIncluded = true;
        AllIncluded = ChangedFiles.Count > 0 && ChangedFiles.All(f => f.IsIncluded);
        _settingAllIncluded = false;
        OnPropertyChanged(nameof(CanCommit));
    }

    partial void OnAllIncludedChanged(bool value)
    {
        if (_settingAllIncluded) return;
        foreach (var f in ChangedFiles) f.IsIncluded = value;
    }

    partial void OnSelectedChangeChanged(ChangedFileViewModel? value) => _ = LoadChangeDiffAsync(value);

    async Task LoadChangeDiffAsync(ChangedFileViewModel? file)
    {
        if (file is null || Repo is null || Status is null)
        {
            ChangeDiffLines = null;
            ChangeDiffMessage = null;
            ChangeImageDiff = null;
            ChangeConflict = null;
            return;
        }
        try
        {
            if (file.Change.Kind == FileChangeKind.Conflicted)
            {
                var sides = await Repo.GetConflictSidesAsync(Operation);
                var conflict = await Repo.GetConflictAsync(file.Change.Path);
                if (SelectedChange != file) return;
                _conflictSides = sides;
                ChangeDiffLines = null;
                ChangeDiffMessage = null;
                ChangeImageDiff = null;
                ChangeConflict = null;   // re-raise the texts even when the info is unchanged
                ChangeConflict = conflict;
                return;
            }
            ChangeConflict = null;
            if (ImageDiff.IsImage(file.Change.Path))
            {
                // Shown as pictures (old and new), like GitHub Desktop; images are committed whole.
                var image = await Repo.GetWorkingImageDiffAsync(file.Change, Status.IsUnborn);
                if (SelectedChange != file) return;
                ChangeDiffLines = null;
                ChangeDiffMessage = null;
                ChangeImageDiff = image;
                return;
            }
            ChangeImageDiff = null;
            var diff = await Repo.GetWorkingDiffAsync(file.Change, Status.IsUnborn, file.IsIncluded);
            if (SelectedChange != file) return;
            file.SetDiff(diff);
            ShowDiff(diff, lines => ChangeDiffLines = lines, msg => ChangeDiffMessage = msg);
        }
        catch (Exception ex)
        {
            ChangeDiffLines = null;
            ChangeImageDiff = null;
            ChangeConflict = null;
            ChangeDiffMessage = ex.Message;
        }
    }

    static void ShowDiff(FileDiff diff, Action<IReadOnlyList<DiffLine>?> setLines, Action<string?> setMessage)
    {
        if (diff.IsBinary)
        {
            setLines(null);
            setMessage("Binary file changed.");
            return;
        }
        var lines = diff.AllLines.ToList();
        if (lines.Count == 0)
        {
            setLines(null);
            setMessage("No content changes (empty file or file mode change).");
        }
        else if (lines.Count > MaxDisplayLines)
        {
            setLines(null);
            setMessage($"The diff is too large to display ({lines.Count:N0} lines).");
        }
        else
        {
            setLines(lines);
            setMessage(null);
        }
    }

    /// <summary>Click on a diff gutter: toggles one line, or a whole hunk when the hunk header is clicked.</summary>
    [RelayCommand]
    void ToggleLine(DiffLine? line)
    {
        if (line is null || !line.IsSelectable || SelectedChange?.Diff is not { } diff) return;
        if (line.Kind == DiffLineKind.Hunk)
        {
            var hunkLines = diff.Hunks[line.HunkIndex].Lines.Where(l => l.IsChange).ToList();
            bool select = !hunkLines.All(l => l.IsSelected);
            foreach (var l in hunkLines) l.IsSelected = select;
        }
        else if (line.IsChange)
        {
            line.IsSelected = !line.IsSelected;
        }
        SelectedChange.SyncFromDiff();
    }

    void SetCommitMessage(string message)
    {
        var parts = message.Replace("\r\n", "\n").Split('\n', 2);
        CommitSummary = parts[0].Trim();
        CommitDescription = parts.Length > 1 ? parts[1].Trim('\n') : "";
    }

    [RelayCommand]
    Task Commit() => RunAsync(IsAmending ? "Amending…" : "Committing…", async () =>
    {
        if (!CanCommit || Repo is null || Status is null) return;

        var selections = ChangedFiles.Select(f => f.ToSelection()).OfType<CommitFileSelection>().ToList();

        // A partially included file must still have the diff the user reviewed.
        foreach (var sel in selections.Where(s => s.PartialDiff is not null))
        {
            var current = await Repo.GetWorkingDiffAsync(sel.Change, Status.IsUnborn, false);
            if (!current.AllLines.Select(l => l.Raw).SequenceEqual(sel.PartialDiff!.AllLines.Select(l => l.Raw)))
            {
                await _dialogs.AlertAsync("File changed",
                    $"{sel.Change.Path} changed on disk since its lines were selected. Review the selection and commit again.");
                return;
            }
        }

        // Conflicted files whose markers are all gone are simply resolved; warn only about ones that still have markers.
        var withMarkers = new List<string>();
        foreach (var s in selections.Where(s => s.Change.Kind == FileChangeKind.Conflicted))
            if ((await Repo.GetConflictAsync(s.Change.Path)).Markers > 0) withMarkers.Add(s.Change.Path);
        if (withMarkers.Count > 0 &&
            !await _dialogs.ConfirmAsync("Conflicts remain",
                $"{string.Join(", ", withMarkers)} still {(withMarkers.Count == 1 ? "has" : "have")} conflict markers. Commit anyway?", "Commit"))
            return;

        var message = CommitSummary.Trim();
        if (!string.IsNullOrWhiteSpace(CommitDescription)) message += "\n\n" + CommitDescription.Trim();

        await Repo.CommitAsync(message, selections, IsAmending, Status.IsUnborn);
        CommitSummary = "";
        CommitDescription = "";
        IsAmending = false;
    });

    [RelayCommand]
    void CancelAmend()
    {
        IsAmending = false;
        CommitSummary = "";
        CommitDescription = "";
    }

    // ── File context menu ────────────────────────────────────────────────────

    public Task DiscardFileAsync(ChangedFileViewModel file) => RunAsync("Discarding…", async () =>
    {
        if (Repo is null) return;
        if (!await _dialogs.ConfirmAsync("Discard changes", $"Discard all changes to {file.Change.Path}? This cannot be undone.", "Discard"))
            return;
        await Repo.DiscardAsync([file.Change]);
    });

    public Task IgnoreFileAsync(ChangedFileViewModel file)
        => RunAsync("Updating .gitignore…", () => Repo!.AddToGitIgnoreAsync("/" + file.Change.Path));

    public Task IgnoreExtensionAsync(ChangedFileViewModel file)
    {
        var ext = Path.GetExtension(file.Change.Path);
        return string.IsNullOrEmpty(ext)
            ? _dialogs.AlertAsync("Ignore", "This file has no extension.")
            : RunAsync("Updating .gitignore…", () => Repo!.AddToGitIgnoreAsync("*" + ext));
    }

    string FullPath(ChangedFileViewModel file) => Path.GetFullPath(Path.Combine(Repo!.Path, file.Change.Path));

    public Task CopyFilePathAsync(ChangedFileViewModel file, bool relative)
        => _platform.CopyAsync(relative ? file.Change.Path : FullPath(file));

    public Task ShowFileInFolderAsync(ChangedFileViewModel file) => TryPlatform(() => _platform.ShowInFileManager(FullPath(file)));

    public Task OpenFileInEditorAsync(ChangedFileViewModel file) => TryPlatform(() => _platform.OpenInEditor(FullPath(file)));

    public Task OpenFileWithDefaultAppAsync(ChangedFileViewModel file) => TryPlatform(() => _platform.OpenWithDefaultApp(FullPath(file)));

    async Task TryPlatform(Action action)
    {
        try { action(); }
        catch (Exception ex) { await _dialogs.AlertAsync("Error", ex.Message); }
    }
}
