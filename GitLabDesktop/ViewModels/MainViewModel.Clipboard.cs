using CommunityToolkit.Mvvm.Input;
using GitLabDesktop.Core.Git;

namespace GitLabDesktop.ViewModels;

// Copying names, paths, links and patches, e.g. to paste into an AI assistant, an issue or a chat:
// the repository and branch menus in the toolbar (right-click), and diffs from Changes and History.
public sealed partial class MainViewModel
{
    /// <summary>Copies the text and says so in the status bar (the clipboard itself gives no sign).</summary>
    async Task CopyAsync(string? text, string what)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            await _platform.CopyAsync(text);
            var lines = text.Count(c => c == '\n');
            StatusMessage = lines > 1 ? $"Copied {what} ({lines:N0} lines)." : $"Copied {what}.";
        }
        catch (Exception ex)
        {
            await _dialogs.AlertAsync("Copy", ex.Message);
        }
    }

    // ── Repository ───────────────────────────────────────────────────────────

    [RelayCommand]
    Task CopyRepositoryName() => CopyAsync(Repo?.Name, "the repository name");

    [RelayCommand]
    Task CopyRepositoryPath() => CopyAsync(Repo?.Path, "the repository path");

    [RelayCommand]
    async Task CopyRemoteUrl()
    {
        if (Repo is null) return;
        var url = await Repo.GetRemoteUrlAsync(RemoteName);
        if (url is null) await _dialogs.AlertAsync("Copy remote URL", $"This repository has no '{RemoteName}' remote.");
        else await CopyAsync(url, $"the {RemoteName} URL");
    }

    // ── Branch ───────────────────────────────────────────────────────────────

    [RelayCommand]
    Task CopyBranchName() => CopyAsync(Status?.Branch, "the branch name");

    [RelayCommand]
    async Task CopyBranchLink()
    {
        if (await RequireRemoteAsync() && await RequireBranchAsync())
            await CopyAsync(Remote!.BranchLink(Status!.Branch!), $"the branch's link on {ProviderName}");
    }

    [RelayCommand]
    async Task CopyChangeRequestLink()
    {
        if (ViewableChangeRequest is null) await LoadHostingInfoAsync();
        // The open request, else the branch's latest merged or closed one
        if (ViewableChangeRequest is { } request) await CopyAsync(request.WebUrl, $"the link to {request.Reference}");
        else await _dialogs.AlertAsync("Copy link", $"{BranchName} has no {RequestName}.");
    }

    // ── Patches ──────────────────────────────────────────────────────────────

    /// <summary>All uncommitted changes as one patch (Changes header › right-click).</summary>
    [RelayCommand]
    async Task CopyAllChangesAsPatch()
    {
        if (Repo is null || Status is null) return;
        await CopyAsync(await Repo.GetWorkingPatchAsync(isUnborn: Status.IsUnborn), "all changes as a patch");
    }

    public async Task CopyFileDiffAsync(ChangedFileViewModel file)
    {
        if (Repo is null || Status is null) return;
        await CopyAsync(await Repo.GetWorkingPatchAsync([file.Change], Status.IsUnborn), $"the diff of {file.Change.FileName}");
    }

    public async Task CopyCommitPatchAsync(CommitInfo commit)
    {
        if (Repo is null) return;
        await CopyAsync(await Repo.GetCommitPatchAsync(commit.Sha), $"commit {commit.ShortSha} as a patch");
    }
}
