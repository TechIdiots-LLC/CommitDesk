using CommitDesk.Core.Hosting;

namespace CommitDesk.Core.Git;

public sealed partial class GitRepository
{
    public async Task<bool> BranchExistsAsync(string name)
        => UseLibGit2 ? await Lg2Async(r => r.Branches[name] is { IsRemote: false }) : (await Run(["rev-parse", "--verify", "--quiet", $"refs/heads/{name}"], throwOnError: false)).Success;

    /// <summary>
    /// Checks out an open merge/pull request and returns the local branch:
    /// a branch of the project itself is fetched and tracked under its own name; a fork is added as a remote
    /// ("fork-owner") and its branch tracked as "pr/12" ("mr/12" on GitLab), so pulls get the author's new commits.
    /// When the fork is gone, the request's own ref on the project (refs/pull/12/head, refs/merge-requests/12/head)
    /// is fetched instead.
    /// </summary>
    public async Task<string> CheckoutChangeRequestAsync(string remote, OpenChangeRequest request, bool preferSsh,
        CancellationToken ct = default)
    {
        if (UseLibGit2) return await Lg2Async(r => Lg2CheckoutChangeRequest(r, remote, request, preferSsh, ct), ct);
        if (!request.FromFork)
            return await CheckoutRemoteBranchAsync(remote, request.SourceBranch, request.SourceBranch, ct);

        var local = request.ForkBranchName;
        var url = preferSsh ? request.SourceSshUrl ?? request.SourceHttpUrl : request.SourceHttpUrl ?? request.SourceSshUrl;
        if (url is not null && request.SourceBranch.Length > 0)
        {
            var forkRemote = await EnsureRemoteAsync(ForkRemoteName(request.SourceRepository!), url);
            return await CheckoutRemoteBranchAsync(forkRemote, request.SourceBranch, local, ct);
        }

        var requestRef = request.Kind == HostingKind.GitHub
            ? $"refs/pull/{request.Number}/head"
            : $"refs/merge-requests/{request.Number}/head";
        if (await BranchExistsAsync(local))
        {
            await Run(["fetch", remote, requestRef], ct: ct);
            await RunMovingHeadAsync("switch", local);
            await RunMovingHeadAsync("merge", "--ff-only", "FETCH_HEAD");
        }
        else
        {
            await Run(["fetch", remote, $"{requestRef}:refs/heads/{local}"], ct: ct);
            await RunMovingHeadAsync("switch", local);
        }
        return local;
    }

    /// <summary>Fetches <paramref name="branch"/> from <paramref name="remote"/> and switches to it as <paramref name="local"/>.</summary>
    async Task<string> CheckoutRemoteBranchAsync(string remote, string branch, string local, CancellationToken ct)
    {
        await Run(["fetch", remote, $"+refs/heads/{branch}:refs/remotes/{remote}/{branch}"], ct: ct);
        if (await BranchExistsAsync(local))
            await RunMovingHeadAsync("switch", local);   // already there: leave its own commits alone, the toolbar offers Pull if behind
        else
            await RunMovingHeadAsync("switch", "-c", local, "--track", $"{remote}/{branch}");
        return local;
    }

    /// <summary>"fork-octocat" for "octocat/project"; GitLab groups ("a/b/project") become "fork-a-b".</summary>
    public static string ForkRemoteName(string sourceRepository)
    {
        var owner = sourceRepository.Contains('/') ? sourceRepository[..sourceRepository.LastIndexOf('/')] : sourceRepository;
        var safe = new string(owner.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-').ToArray());
        return "fork-" + safe;
    }

    /// <summary>Adds the remote, or points an existing one with that name at <paramref name="url"/>.</summary>
    async Task<string> EnsureRemoteAsync(string name, string url)
    {
        var existing = await Run(["remote", "get-url", name], throwOnError: false);
        if (!existing.Success) await Run("remote", "add", name, url);
        else if (existing.StdOut.Trim() != url) await Run("remote", "set-url", name, url);
        return name;
    }
}
