using CommunityToolkit.Mvvm.Input;
using CommitDesk.Core.Hosting;
using CommitDesk.Services;

namespace CommitDesk.ViewModels;

// Branch › Publish branch to…: choosing which remote (the project, or your fork) a branch is published to.
public sealed partial class MainViewModel
{
    /// <summary>Your fork of the project, not yet a remote of this repository: added when it's picked.</summary>
    sealed record ForkCandidate(string Login, string ProjectPath, string Url);

    /// <summary>
    /// Publishes (or moves) the current branch to a remote chosen from the repository's remotes, plus your fork on the
    /// server when there's an account for it and no remote points at it yet. The choice is kept for the branch (git's
    /// branch.&lt;name&gt;.pushRemote), so later pushes go there too.
    /// </summary>
    [RelayCommand]
    async Task PublishBranchTo()
    {
        if (Repo is null || !await RequireBranchAsync()) return;
        var repo = Repo;
        var branch = Status!.Branch!;
        var remotes = await repo.GetRemotesAsync();
        var current = PushRemote;

        var items = new List<PickerItem>();
        foreach (var name in remotes)
        {
            var url = await repo.GetRemoteUrlAsync(name);
            var badge = name == current ? (IsPublished ? "published here" : "goes here now")
                : name == ForkRemote ? "default" : null;
            items.Add(new PickerItem(name, HostedRemote.Parse(url)?.ProjectPath ?? url, name, badge));
        }
        if (await FindForkCandidateAsync(remotes) is { } fork)
            items.Insert(0, new PickerItem($"Your fork: {fork.ProjectPath}", $"Adds it as the remote \"{RemoteNameFor(fork, remotes)}\"", fork));

        var picked = await _dialogs.PickAsync<object>($"Publish {branch} to", items, "This repository has no remotes");
        var target = picked switch
        {
            string name => name,
            ForkCandidate f => await AddForkRemoteAsync(f, remotes),
            _ => null,
        };
        if (target is null || Repo != repo) return;

        await repo.SetLocalConfigAsync($"branch.{branch}.pushRemote", target);
        await RefreshAsync();
        await PushAsync(force: false);
    }

    /// <summary>
    /// The signed-in user's fork of this project (same name, under their login), when the server has an account, the
    /// project isn't theirs, and no remote points at the fork already.
    /// </summary>
    async Task<ForkCandidate?> FindForkCandidateAsync(IReadOnlyList<string> remotes)
    {
        if (Repo is not { } repo || HostingService is not { } service || Remote is not { } project) return null;
        try
        {
            var login = (await service.GetUserAsync()).Login;
            if (string.IsNullOrEmpty(login) || string.Equals(project.Owner, login, StringComparison.OrdinalIgnoreCase)) return null;
            var path = $"{login}/{project.Name}";
            foreach (var name in remotes)
                if (string.Equals(HostedRemote.Parse(await repo.GetRemoteUrlAsync(name))?.ProjectPath, path, StringComparison.OrdinalIgnoreCase))
                    return null;
            var baseUrl = await repo.GetRemoteUrlAsync(BaseRemoteName);
            return baseUrl is not null && project.ForkUrl(baseUrl, login) is { } url ? new ForkCandidate(login, path, url) : null;
        }
        catch (Exception)
        {
            return null;   // offline, or the token can't read the user: the existing remotes are still offered
        }
    }

    /// <summary>The remote is named after your login, as GitHub Desktop does, unless that name is taken.</summary>
    static string RemoteNameFor(ForkCandidate fork, IReadOnlyList<string> remotes)
        => remotes.Contains(fork.Login) ? "fork" : fork.Login;

    /// <summary>
    /// Adds your fork as a remote once the server confirms it exists (else offers its fork page), and offers to make it
    /// where new branches are published. Returns the new remote's name, or null.
    /// </summary>
    async Task<string?> AddForkRemoteAsync(ForkCandidate fork, IReadOnlyList<string> remotes)
    {
        if (Repo is not { } repo || HostingService is not { } service || Remote is not { } project) return null;
        try
        {
            await service.GetProjectDetailsAsync(fork.ProjectPath);
        }
        catch (Exception)
        {
            if (await _dialogs.ConfirmAsync("No fork yet",
                    $"{project.ProviderName} has no {fork.ProjectPath}. Fork {project.ProjectPath} on {project.ProviderName}, " +
                    "then choose Publish branch to… again.", "Open fork page") && project.ForkLink is { } link)
                await _platform.OpenUrlAsync(link);
            return null;
        }

        var name = RemoteNameFor(fork, remotes);
        await repo.SetRemoteUrlAsync(name, fork.Url);
        if (ForkRemote is null &&
            await _dialogs.ConfirmAsync("Publish to your fork by default?",
                $"Publish new branches to {fork.ProjectPath} from now on, instead of to {project.ProjectPath}? " +
                $"{DefaultBranch} still goes to {project.ProjectPath}, and Branch › Publish branch to… still lets you choose " +
                "for a single branch. You can change this in Repository › Repository settings.", "Yes", "Not now"))
            await repo.SetLocalConfigAsync("remote.pushDefault", name);
        return name;
    }
}
