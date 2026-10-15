using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommitDesk.Core.Git;
using CommitDesk.Services;

namespace CommitDesk.ViewModels;

// History tab: commit list, commit details, and the commit context menu.
public sealed partial class MainViewModel
{
    const int HistoryPageSize = 100;

    bool _historyComplete;
    bool _loadingMoreHistory;

    public ObservableCollection<CommitInfo> Commits { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedCommit), nameof(SelectedCommitMeta))]
    private CommitInfo? _selectedCommit;

    [ObservableProperty] private IReadOnlyList<FileChange>? _commitFiles;
    [ObservableProperty] private FileChange? _selectedCommitFile;
    [ObservableProperty] private IReadOnlyList<DiffLine>? _commitDiffLines;
    [ObservableProperty] private string? _commitDiffMessage;

    /// <summary>Set instead of the text diff when the selected file in the commit is an image.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCommitImage), nameof(IsCommitText))]
    private ImageDiff? _commitImageDiff;

    public bool IsCommitImage => CommitImageDiff is not null;
    public bool IsCommitText => CommitImageDiff is null;

    public bool HasSelectedCommit => SelectedCommit is not null;

    public string? SelectedCommitMeta => SelectedCommit is { } c
        ? $"{c.AuthorName}  •  {c.AuthorDate.LocalDateTime:g}  •  {c.ShortSha}" + (c.IsMerge ? "  •  merge" : "")
        : null;

    async Task LoadHistoryAsync()
    {
        if (Repo is null) return;
        var selectedSha = SelectedCommit?.Sha;
        var commits = await Repo.GetLogAsync(0, HistoryPageSize);
        await MarkUnpushedAsync(commits);

        Commits.Clear();
        foreach (var c in commits) Commits.Add(c);
        _historyComplete = commits.Count < HistoryPageSize;
        SelectedCommit = Commits.FirstOrDefault(c => c.Sha == selectedSha) ?? Commits.FirstOrDefault();
    }

    async Task MarkUnpushedAsync(IReadOnlyList<CommitInfo> commits)
    {
        if (Repo is null || commits.Count == 0) return;
        if (Status?.Upstream is { } upstream)
        {
            var unpushed = await Repo.GetUnpushedShasAsync(upstream);
            foreach (var c in commits) c.IsUnpushed = unpushed.Contains(c.Sha);
        }
        else if (Status?.Branch is not null)
        {
            foreach (var c in commits) c.IsUnpushed = true;   // unpublished branch
        }
    }

    [RelayCommand]
    async Task LoadMoreHistory()
    {
        if (Repo is null || _historyComplete || _loadingMoreHistory) return;
        _loadingMoreHistory = true;
        try
        {
            var more = await Repo.GetLogAsync(Commits.Count, HistoryPageSize);
            await MarkUnpushedAsync(more);
            foreach (var c in more) Commits.Add(c);
            _historyComplete = more.Count < HistoryPageSize;
        }
        finally
        {
            _loadingMoreHistory = false;
        }
    }

    partial void OnSelectedCommitChanged(CommitInfo? value) => _ = LoadCommitFilesAsync(value);

    async Task LoadCommitFilesAsync(CommitInfo? commit)
    {
        CommitFiles = null;
        // FileChange is a record: the new commit's first file often *equals* the old selection (same path and kind),
        // and setting an equal value is no change, so its diff would never load. Clearing first makes it one.
        SelectedCommitFile = null;
        CommitDiffLines = null;
        CommitDiffMessage = null;
        CommitImageDiff = null;
        if (commit is null || Repo is null) return;
        try
        {
            var files = await Repo.GetCommitFilesAsync(commit);
            if (SelectedCommit != commit) return;
            CommitFiles = files;
            SelectedCommitFile = files.FirstOrDefault();
            if (files.Count == 0) CommitDiffMessage = "No file changes in this commit.";
        }
        catch (Exception ex)
        {
            CommitDiffMessage = ex.Message;
        }
    }

    partial void OnSelectedCommitFileChanged(FileChange? value) => _ = LoadCommitDiffAsync(value);

    async Task LoadCommitDiffAsync(FileChange? file)
    {
        if (file is null || SelectedCommit is not { } commit || Repo is null)
        {
            CommitDiffLines = null;
            CommitImageDiff = null;
            return;
        }
        try
        {
            if (ImageDiff.IsImage(file.Path))
            {
                var image = await Repo.GetCommitImageDiffAsync(commit, file);
                if (SelectedCommitFile != file) return;
                CommitDiffLines = null;
                CommitDiffMessage = null;
                CommitImageDiff = image;
                return;
            }
            CommitImageDiff = null;
            var diff = await Repo.GetCommitDiffAsync(commit, file);
            if (SelectedCommitFile != file) return;
            ShowDiff(diff, lines => CommitDiffLines = lines, msg => CommitDiffMessage = msg);
        }
        catch (Exception ex)
        {
            CommitDiffLines = null;
            CommitImageDiff = null;
            CommitDiffMessage = ex.Message;
        }
    }

    // ── Commit context menu ──────────────────────────────────────────────────

    public async Task AmendCommitAsync(CommitInfo commit)
    {
        if (Repo is null) return;
        if (commit.Sha != Status?.HeadSha)
        {
            await _dialogs.AlertAsync("Amend commit", "Only the most recent commit on the branch can be amended.");
            return;
        }
        if (!commit.IsUnpushed &&
            !await _dialogs.ConfirmAsync("Amend commit",
                "This commit has already been pushed. Amending it rewrites history and will need a force push. Continue?", "Amend"))
            return;

        SetCommitMessage(await Repo.GetHeadMessageAsync());
        IsAmending = true;
        IsHistoryTab = false;
    }

    public async Task ResetToCommitAsync(CommitInfo commit)
    {
        if (!await _dialogs.ConfirmAsync("Reset to commit",
                $"Reset the current branch to {commit.ShortSha}? Later commits are undone and their changes kept in the working tree.", "Reset"))
            return;
        await RunAsync("Resetting…", () => Repo!.ResetToAsync(commit.Sha));
    }

    public async Task CheckoutCommitAsync(CommitInfo commit)
    {
        if (!await _dialogs.ConfirmAsync("Checkout commit",
                $"Check out {commit.ShortSha}? HEAD will be detached; create a branch to keep new commits.", "Checkout"))
            return;
        await RunAsync("Checking out…", () => Repo!.CheckoutCommitAsync(commit.Sha));
    }

    public Task RevertCommitAsync(CommitInfo commit) => RunAsync("Reverting…", () => Repo!.RevertAsync(commit));

    public Task CreateBranchFromCommitAsync(CommitInfo commit) => NewBranchAsync(commit.Sha, $"from {commit.ShortSha}");

    public async Task CreateTagAsync(CommitInfo commit)
    {
        var name = await _dialogs.PromptAsync("Create tag", $"Tag name for {commit.ShortSha}:", accept: "Create");
        if (string.IsNullOrWhiteSpace(name)) return;
        await RunAsync("Creating tag…", () => Repo!.CreateTagAsync(name.Trim(), commit.Sha));
        await LoadHistoryAsync();
    }

    /// <summary>
    /// Like GitHub Desktop's "Cherry-pick commit…": choose the branch to apply the commit to (the current one is listed
    /// first), switch there if it's another (asking about uncommitted changes as usual), then apply it.
    /// </summary>
    public async Task CherryPickCommitAsync(CommitInfo commit)
    {
        if (Repo is null) return;
        var branches = await Repo.GetBranchesAsync();
        var localNames = branches.Where(b => !b.IsRemote).Select(b => b.Name).ToHashSet();
        static string? Age(BranchInfo b) => b.LastCommitDate is { } d ? Converters.RelativeTimeConverter.Format(d) : null;
        var items = branches
            .Where(b => !b.IsRemote || !localNames.Contains(b.NameWithoutRemote))
            .OrderByDescending(b => b.IsCurrent).ThenBy(b => b.IsRemote)
            .Select(b => new PickerItem(b.Name,
                b.IsCurrent ? "current branch" : b.IsRemote ? "remote branch" : b.Upstream is { } u ? $"tracks {u}" : null, b, Age(b)));

        var target = await _dialogs.PickAsync<BranchInfo>($"Cherry-pick {commit.ShortSha} \"{commit.Summary}\" to a branch", items);
        if (target is null) return;

        var targetName = target.IsRemote ? target.NameWithoutRemote : target.Name;
        if (!target.IsCurrent)
        {
            await SwitchToAsync(targetName, () => Repo.CheckoutAsync(target));
            if (Status?.Branch != targetName) return;   // cancelled, or the switch failed
        }
        await RunAsync($"Cherry-picking onto {targetName}…", () => Repo.CherryPickAsync(commit.Sha));
    }

    public Task CopyShaAsync(CommitInfo commit) => _platform.CopyAsync(commit.Sha);

    public Task CopyTagAsync(CommitInfo commit)
        => commit.Tags.Count > 0 ? _platform.CopyAsync(commit.Tags[0]) : Task.CompletedTask;

    public async Task ViewCommitOnHostAsync(CommitInfo commit)
    {
        if (await RequireRemoteAsync()) await OpenLinkAsync(Remote!.CommitLink(commit.Sha));
    }

    // ── A file in the selected commit (right-click, like GitHub Desktop) ─────

    /// <summary>Runs the action on the file in the working folder, or says it isn't there any more.</summary>
    async Task WithWorkingFileAsync(FileChange file, Action<string> action)
    {
        if (Repo is null) return;
        var path = Path.GetFullPath(Path.Combine(Repo.Path, file.Path));
        if (!File.Exists(path))
        {
            await _dialogs.AlertAsync(file.FileName, $"{file.Path} isn't in your working folder (it has been deleted or renamed since).");
            return;
        }
        await TryPlatform(() => action(path));
    }

    public Task ShowCommitFileInFolderAsync(FileChange file) => WithWorkingFileAsync(file, _platform.ShowInFileManager);
    public Task OpenCommitFileInEditorAsync(FileChange file) => WithWorkingFileAsync(file, _platform.OpenInEditor);
    public Task OpenCommitFileWithDefaultAppAsync(FileChange file) => WithWorkingFileAsync(file, _platform.OpenWithDefaultApp);

    public Task CopyCommitFilePathAsync(FileChange file, bool relative)
        => Repo is null ? Task.CompletedTask
            : CopyAsync(relative ? file.Path : Path.GetFullPath(Path.Combine(Repo.Path, file.Path)), relative ? "the relative path" : "the path");

    /// <summary>The file as it was in the commit; for a file the commit deleted, the commit itself.</summary>
    public async Task ViewCommitFileOnHostAsync(FileChange file)
    {
        if (SelectedCommit is not { } commit || !await RequireRemoteAsync()) return;
        await OpenLinkAsync(file.Kind == FileChangeKind.Deleted ? Remote!.CommitLink(commit.Sha) : Remote!.FileLink(commit.Sha, file.Path));
    }

    public async Task CopyCommitFileDiffAsync(FileChange file)
    {
        if (Repo is null || SelectedCommit is not { } commit) return;
        await CopyAsync(await Repo.GetCommitFilePatchAsync(commit.Sha, file), $"the diff of {file.FileName} in {commit.ShortSha}");
    }
}
