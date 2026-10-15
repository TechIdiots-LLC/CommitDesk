using LibGit2Sharp;

namespace CommitDesk.Core.Git;

/// <summary>
/// libgit2 versions of the operations that change history or resolve conflicts: merge (and squash merge), rebase,
/// revert, cherry-pick, reset, tags, continue/abort, the conflicts dialog, checking out merge/pull requests, and patches
/// for copying. See GitRepository.LibGit2.cs.
/// </summary>
public sealed partial class GitRepository
{
    /// <summary>A branch name, remote branch or commit as a commit.</summary>
    static Commit ResolveCommit(Repository repo, string revision)
        => repo.Lookup<Commit>(revision) ?? throw new NotFoundException($"'{revision}' is not a branch or commit.");

    static Identity RequireIdentity(Repository repo)
    {
        var signature = RequireSignature(repo);
        return new Identity(signature.Name, signature.Email);
    }

    // ── Merge ────────────────────────────────────────────────────────────────

    static void Lg2Merge(Repository repo, string branch)
    {
        // A branch by name, so the message reads "Merge branch 'x'" as git's does, not "Merge commit '<sha>'"
        var options = new MergeOptions { FastForwardStrategy = FastForwardStrategy.Default };
        var result = repo.Branches[branch] is { } b
            ? repo.Merge(b, RequireSignature(repo), options)
            : repo.Merge(ResolveCommit(repo, branch), RequireSignature(repo), options);
        if (result.Status == MergeStatus.Conflicts) ThrowIfConflicted(repo, "merge");
    }

    /// <summary>
    /// git merge --squash: the branch's changes in the index and working tree, uncommitted, with SQUASH_MSG listing its
    /// commits; no merge in progress afterwards, so the commit has one parent. libgit2 has no squash, so merge without
    /// committing, then drop the merge state.
    /// </summary>
    static void Lg2SquashMerge(Repository repo, string branch)
    {
        var theirs = ResolveCommit(repo, branch);
        var head = repo.Head.Tip;
        var squashed = repo.Commits.QueryBy(new CommitFilter { IncludeReachableFrom = theirs, ExcludeReachableFrom = head }).ToList();
        var result = repo.Merge(theirs, StashSignature(repo),
            new MergeOptions { FastForwardStrategy = FastForwardStrategy.NoFastForward, CommitOnSuccess = false });

        var gitDir = repo.Info.Path;
        foreach (var name in new[] { "MERGE_HEAD", "MERGE_MODE", "MERGE_MSG" })
            File.Delete(System.IO.Path.Combine(gitDir, name));
        var message = new System.Text.StringBuilder("Squashed commit of the following:\n");
        foreach (var c in squashed)
        {
            message.Append($"\ncommit {c.Sha}\nAuthor: {c.Author.Name} <{c.Author.Email}>\nDate:   {c.Author.When:ddd MMM d HH:mm:ss yyyy zzz}\n\n");
            foreach (var line in c.Message.TrimEnd().Split('\n')) message.Append("    ").Append(line).Append('\n');
        }
        File.WriteAllText(System.IO.Path.Combine(gitDir, "SQUASH_MSG"), message.ToString());

        if (result.Status == MergeStatus.Conflicts) ThrowIfConflicted(repo, "merge");
    }

    // ── Rebase ───────────────────────────────────────────────────────────────

    static void ThrowIfRebaseStopped(Repository repo, RebaseResult result)
    {
        if (result.Status == RebaseStatus.Conflicts) ThrowIfConflicted(repo, "rebase");
        if (result.Status == RebaseStatus.Stop)
            throw new GitException("The rebase stopped. Resolve the problem, then continue or abort it.", new GitResult(1, "", "rebase stopped"));
    }

    static void Lg2Rebase(Repository repo, string onto)
    {
        var upstream = repo.Branches[onto] ?? throw new NotFoundException($"'{onto}' is not a branch.");
        var result = repo.Rebase.Start(repo.Head, upstream, null, RequireIdentity(repo), new RebaseOptions());
        ThrowIfRebaseStopped(repo, result);
    }

    // ── Continue / abort ─────────────────────────────────────────────────────

    void Lg2Continue(Repository repo, RepositoryOperation op)
    {
        Commands.Stage(repo, "*");
        if (op == RepositoryOperation.Rebase)
        {
            ThrowIfRebaseStopped(repo, repo.Rebase.Continue(RequireIdentity(repo), new RebaseOptions()));
            return;
        }
        if (repo.Index.Conflicts.Any()) ThrowIfConflicted(repo, "merge");

        // Merge, cherry-pick or revert: commit with the message git prepared; a cherry-pick keeps its author
        var gitDir = repo.Info.Path;
        var message = File.Exists(System.IO.Path.Combine(gitDir, "MERGE_MSG"))
            ? string.Join('\n', File.ReadAllLines(System.IO.Path.Combine(gitDir, "MERGE_MSG")).Where(l => !l.StartsWith('#'))).Trim()
            : "Merge";
        var committer = RequireSignature(repo);
        var author = committer;
        if (op == RepositoryOperation.CherryPick && File.Exists(System.IO.Path.Combine(gitDir, "CHERRY_PICK_HEAD"))
            && repo.Lookup<Commit>(File.ReadAllText(System.IO.Path.Combine(gitDir, "CHERRY_PICK_HEAD")).Trim()) is { } picked)
            author = picked.Author;
        repo.Commit(message, author, committer);
        if (op != RepositoryOperation.Merge)
            foreach (var name in new[] { "CHERRY_PICK_HEAD", "REVERT_HEAD", "MERGE_MSG" })
                File.Delete(System.IO.Path.Combine(gitDir, name));
    }

    static void Lg2Abort(Repository repo, RepositoryOperation op)
    {
        if (op == RepositoryOperation.Rebase)
        {
            repo.Rebase.Abort();
            return;
        }
        // Back to the commit before it; libgit2's hard reset also clears the merge/cherry-pick/revert state
        repo.Reset(ResetMode.Hard, repo.Head.Tip);
    }

    // ── Revert, cherry-pick, reset, tags ─────────────────────────────────────

    static void Lg2Revert(Repository repo, CommitInfo c)
    {
        var result = repo.Revert(ResolveCommit(repo, c.Sha), RequireSignature(repo),
            new RevertOptions { Mainline = c.IsMerge ? 1 : 0 });
        if (result.Status == RevertStatus.Conflicts) ThrowIfConflicted(repo, "revert");
    }

    static void Lg2CherryPick(Repository repo, string sha)
    {
        var result = repo.CherryPick(ResolveCommit(repo, sha), RequireSignature(repo));
        if (result.Status == CherryPickStatus.Conflicts) ThrowIfConflicted(repo, "cherry-pick");
    }

    // ── Conflicts ────────────────────────────────────────────────────────────

    ConflictInfo Lg2Conflict(Repository repo, string path)
    {
        var conflict = repo.Index.Conflicts[path];
        var full = System.IO.Path.Combine(Path, path);
        var markers = File.Exists(full)
            ? File.ReadLines(full).Count(l => l.StartsWith("<<<<<<< ", StringComparison.Ordinal) || l == "<<<<<<<")
            : 0;
        return new ConflictInfo(markers, conflict?.Ours is not null, conflict?.Theirs is not null, File.Exists(full), Unmerged: conflict is not null);
    }

    /// <summary>The name git's name-rev would give a commit: a branch whose tip it is.</summary>
    static string NameOfCommit(Repository repo, string rev, string fallback)
    {
        if (rev.Length == 0 || repo.Lookup<Commit>(rev) is not { } commit) return fallback;
        var branch = repo.Branches.Where(b => b.Tip?.Sha == commit.Sha && !b.CanonicalName.EndsWith("/HEAD", StringComparison.Ordinal))
            .OrderBy(b => b.IsRemote).FirstOrDefault();
        return branch?.FriendlyName ?? fallback;
    }

    void Lg2ResolveConflict(Repository repo, string path, ConflictChoice choice)
    {
        var conflict = repo.Index.Conflicts[path];
        var side = choice switch
        {
            ConflictChoice.Ours => conflict?.Ours,
            ConflictChoice.Theirs => conflict?.Theirs,
            _ => null,
        };
        var keep = choice == ConflictChoice.AsIs ? File.Exists(System.IO.Path.Combine(Path, path)) : side is not null;
        if (!keep)
        {
            // The chosen side deleted the file
            DeleteWorkingFile(path);
            repo.Index.Remove(path);
            repo.Index.Write();
            return;
        }
        if (side is not null && repo.Lookup<Blob>(side.Id) is { } blob)
        {
            using var content = blob.GetContentStream();
            using var file = File.Create(System.IO.Path.Combine(Path, path));
            content.CopyTo(file);
        }
        repo.Index.Add(path);
        repo.Index.Write();
    }

    // ── Merge/pull requests ──────────────────────────────────────────────────

    void Lg2FetchSpecs(Repository repo, string remote, IEnumerable<string> specs, CancellationToken ct)
    {
        var r = repo.Network.Remotes[remote] ?? throw new NotFoundException($"No remote named '{remote}'.");
        var options = new FetchOptions
        {
            CustomHeaders = HeadersFor(r.Url),
            CertificateCheck = GitCertificates.CertificateCheckFor(r.Url),
            OnTransferProgress = _ => !ct.IsCancellationRequested,
        };
        Commands.Fetch(repo, remote, specs, options, null);
    }

    static void EnsureRemote(Repository repo, string name, string url)
    {
        if (repo.Network.Remotes[name] is null) repo.Network.Remotes.Add(name, url);
        else if (repo.Network.Remotes[name].Url != url) repo.Network.Remotes.Update(name, u => u.Url = url);
    }

    string Lg2CheckoutRemoteBranch(Repository repo, string remote, string branch, string local, CancellationToken ct)
    {
        Lg2FetchSpecs(repo, remote, [$"+refs/heads/{branch}:refs/remotes/{remote}/{branch}"], ct);
        if (repo.Branches[local] is { IsRemote: false } existing)
        {
            Commands.Checkout(repo, existing);   // already there: its own commits stay; the toolbar offers Pull if behind
            return local;
        }
        var tracked = repo.Branches[$"{remote}/{branch}"] ?? throw new NotFoundException($"{remote}/{branch} wasn't fetched.");
        var created = repo.CreateBranch(local, tracked.Tip);
        repo.Branches.Update(created, u => u.TrackedBranch = tracked.CanonicalName);
        Commands.Checkout(repo, created);
        return local;
    }

    string Lg2CheckoutChangeRequest(Repository repo, string remote, Hosting.OpenChangeRequest request, bool preferSsh, CancellationToken ct)
    {
        if (!request.FromFork) return Lg2CheckoutRemoteBranch(repo, remote, request.SourceBranch, request.SourceBranch, ct);

        var local = request.ForkBranchName;
        var url = preferSsh ? request.SourceSshUrl ?? request.SourceHttpUrl : request.SourceHttpUrl ?? request.SourceSshUrl;
        if (url is not null && request.SourceBranch.Length > 0)
        {
            var forkRemote = ForkRemoteName(request.SourceRepository!);
            EnsureRemote(repo, forkRemote, url);
            return Lg2CheckoutRemoteBranch(repo, forkRemote, request.SourceBranch, local, ct);
        }

        // The fork is gone: the request's own ref on the project, into a scratch ref, then onto the local branch
        var requestRef = request.Kind == Hosting.HostingKind.GitHub
            ? $"refs/pull/{request.Number}/head"
            : $"refs/merge-requests/{request.Number}/head";
        const string scratch = "refs/commitdesk/fetched-request";
        Lg2FetchSpecs(repo, remote, [$"+{requestRef}:{scratch}"], ct);
        var fetched = repo.Lookup<Commit>(scratch) ?? throw new NotFoundException($"{requestRef} wasn't fetched.");
        try
        {
            if (repo.Branches[local] is { IsRemote: false } existing)
            {
                Commands.Checkout(repo, existing);
                repo.Merge(fetched, StashSignature(repo), new MergeOptions { FastForwardStrategy = FastForwardStrategy.FastForwardOnly });
            }
            else
            {
                Commands.Checkout(repo, repo.CreateBranch(local, fetched));
            }
        }
        finally
        {
            repo.Refs.Remove(scratch);
        }
        return local;
    }

    // ── Patches for copying ──────────────────────────────────────────────────

    string Lg2WorkingPatch(Repository repo, IReadOnlyList<FileChange>? files)
    {
        var changes = files ?? Lg2Status(repo).Files;
        var parts = new List<string>();
        var tracked = changes.Where(f => f.Kind != FileChangeKind.Untracked)
            .SelectMany(f => f.OldPath is null ? [f.Path] : new[] { f.Path, f.OldPath }).ToList();
        if (tracked.Count > 0)
            parts.Add(repo.Diff.Compare<Patch>(repo.Head.Tip?.Tree, DiffTargets.Index | DiffTargets.WorkingDirectory, tracked, null, DiffOptions).Content);
        foreach (var untracked in changes.Where(f => f.Kind == FileChangeKind.Untracked))
            parts.Add(NewFilePatch(Path, untracked.Path));
        return string.Concat(parts.Where(p => p.Length > 0).Select(p => p.EndsWith('\n') ? p : p + "\n"));
    }

    /// <summary>A commit as git show --format=medium prints it: header, indented message, then its diff.</summary>
    static string Lg2CommitPatch(Repository repo, string sha)
    {
        var c = ResolveCommit(repo, sha);
        var sb = new System.Text.StringBuilder($"commit {c.Sha}\n");
        if (c.Parents.Count() > 1) sb.Append("Merge: ").Append(string.Join(' ', c.Parents.Select(p => p.Sha[..7]))).Append('\n');
        sb.Append($"Author: {c.Author.Name} <{c.Author.Email}>\nDate:   {c.Author.When:ddd MMM d HH:mm:ss yyyy zzz}\n\n");
        foreach (var line in c.Message.TrimEnd().Split('\n')) sb.Append("    ").Append(line.TrimEnd('\r')).Append('\n');
        sb.Append('\n');
        sb.Append(repo.Diff.Compare<Patch>(c.Parents.FirstOrDefault()?.Tree, c.Tree, DiffOptions).Content);
        return sb.ToString();
    }
}
