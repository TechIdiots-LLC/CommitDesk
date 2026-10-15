using LibGit2Sharp;

namespace CommitDesk.Core.Git;

/// <summary>
/// The repository operations through libgit2 (LibGit2Sharp) instead of the git command line, used when
/// <see cref="GitRunner.UseLibGit2"/> is set: on Android, which has no git. Each returns what the command-line version
/// returns, so the app works the same either way; operations not ported yet fail with a clear message from
/// <see cref="GitRunner.RunAsync"/>. Signing in uses the same http.&lt;url&gt;.extraHeader settings git gets.
/// </summary>
public sealed partial class GitRepository
{
    bool UseLibGit2 => git.UseLibGit2;

    T Lg2<T>(Func<Repository, T> action, string conflictAction = "checkout")
    {
        try
        {
            using var repo = new Repository(Path);
            return action(repo);
        }
        catch (LibGit2SharpException ex)
        {
            throw LibGit2Errors.ToGitException(ex, conflictAction);
        }
    }

    Task<T> Lg2Async<T>(Func<Repository, T> action, CancellationToken ct = default, string conflictAction = "checkout")
        => Task.Run(() => Lg2(action, conflictAction), ct);

    Task Lg2Async(Action<Repository> action, CancellationToken ct = default, string conflictAction = "checkout")
        => Task.Run(() => Lg2(repo => { action(repo); return 0; }, conflictAction), ct);

    // ── Discovery / creation ─────────────────────────────────────────────────

    static string? Lg2FindRoot(string dir)
    {
        if (Repository.Discover(dir) is not { } gitDir) return null;
        using var repo = new Repository(gitDir);
        return repo.Info.WorkingDirectory is { } work ? System.IO.Path.GetFullPath(work).TrimEnd('\\', '/') : null;
    }

    static void Lg2Init(string dir, string defaultBranch)
    {
        Repository.Init(dir);
        using var repo = new Repository(dir);
        PointUnbornHead(repo, defaultBranch);
    }

    /// <summary>
    /// Makes a repository with no commits yet start on <paramref name="branch"/>. LibGit2Sharp only points references at
    /// existing objects, so write HEAD as git does (git init -b, git switch -c on an unborn branch).
    /// </summary>
    static void PointUnbornHead(Repository repo, string branch)
        => File.WriteAllText(System.IO.Path.Combine(repo.Info.Path, "HEAD"), $"ref: refs/heads/{branch}\n");

    // ── Status ───────────────────────────────────────────────────────────────

    static RepositoryStatus Lg2Status(Repository repo)
    {
        var options = new StatusOptions
        {
            IncludeUntracked = true,
            RecurseUntrackedDirs = true,
            DetectRenamesInIndex = true,
            DetectRenamesInWorkDir = true,
        };
        var files = new List<FileChange>();
        foreach (var entry in repo.RetrieveStatus(options))
        {
            if (entry.State is FileStatus.Ignored or FileStatus.Unaltered) continue;
            files.Add(ToFileChange(entry));
        }

        var head = repo.Head;
        var tracking = head.IsTracking ? head.TrackingDetails : null;
        return new RepositoryStatus
        {
            Branch = repo.Info.IsHeadDetached ? null : head.FriendlyName,
            HeadSha = head.Tip?.Sha,
            Upstream = head.IsTracking ? head.TrackedBranch?.FriendlyName : null,
            Ahead = tracking?.AheadBy ?? 0,
            Behind = tracking?.BehindBy ?? 0,
            Files = files,
        };
    }

    /// <summary>One file's state, as one kind of change the way the app lists it.</summary>
    static FileChange ToFileChange(StatusEntry entry)
    {
        var s = entry.State;
        if (s.HasFlag(FileStatus.Conflicted)) return new FileChange(entry.FilePath, null, FileChangeKind.Conflicted);
        if (s.HasFlag(FileStatus.RenamedInIndex) || s.HasFlag(FileStatus.RenamedInWorkdir))
        {
            var old = entry.HeadToIndexRenameDetails?.OldFilePath ?? entry.IndexToWorkDirRenameDetails?.OldFilePath;
            return new FileChange(entry.FilePath, old, FileChangeKind.Renamed);
        }
        var kind =
            s.HasFlag(FileStatus.NewInIndex) ? FileChangeKind.Added :
            s.HasFlag(FileStatus.NewInWorkdir) ? FileChangeKind.Untracked :
            s.HasFlag(FileStatus.DeletedFromIndex) || s.HasFlag(FileStatus.DeletedFromWorkdir) ? FileChangeKind.Deleted :
            s.HasFlag(FileStatus.TypeChangeInIndex) || s.HasFlag(FileStatus.TypeChangeInWorkdir) ? FileChangeKind.TypeChanged :
            FileChangeKind.Modified;
        return new FileChange(entry.FilePath, null, kind);
    }

    static RepositoryOperation Lg2Operation(Repository repo) => repo.Info.CurrentOperation switch
    {
        CurrentOperation.Merge => RepositoryOperation.Merge,
        CurrentOperation.Revert or CurrentOperation.RevertSequence => RepositoryOperation.Revert,
        CurrentOperation.CherryPick or CurrentOperation.CherryPickSequence => RepositoryOperation.CherryPick,
        CurrentOperation.Rebase or CurrentOperation.RebaseInteractive or CurrentOperation.RebaseMerge
            or CurrentOperation.ApplyMailboxOrRebase => RepositoryOperation.Rebase,
        _ => RepositoryOperation.None,
    };

    // ── Diffs ────────────────────────────────────────────────────────────────

    static readonly CompareOptions DiffOptions = new() { Similarity = SimilarityOptions.Renames, ContextLines = 3 };

    static string Lg2WorkingDiffText(Repository repo, string workDir, FileChange change)
    {
        if (change.Kind == FileChangeKind.Untracked) return NewFilePatch(workDir, change.Path);
        string[] paths = change.OldPath is null ? [change.Path] : [change.Path, change.OldPath];
        // HEAD (or nothing, on an unborn branch) against the working tree, through the index: git diff HEAD
        return repo.Diff.Compare<Patch>(repo.Head.Tip?.Tree, DiffTargets.Index | DiffTargets.WorkingDirectory, paths, null, DiffOptions).Content;
    }

    /// <summary>An untracked file as the patch git diff --no-index /dev/null &lt;file&gt; prints.</summary>
    static string NewFilePatch(string workDir, string path)
    {
        var header = $"diff --git a/{path} b/{path}\nnew file mode 100644\n";
        var full = System.IO.Path.Combine(workDir, path);
        if (!File.Exists(full)) return header;
        var bytes = File.ReadAllBytes(full);
        if (bytes.AsSpan(0, Math.Min(bytes.Length, 8000)).IndexOf((byte)0) >= 0)
            return header + $"Binary files /dev/null and b/{path} differ\n";
        if (bytes.Length == 0) return header;

        var text = System.Text.Encoding.UTF8.GetString(bytes);
        var endsWithNewline = text.EndsWith('\n');
        var lines = (endsWithNewline ? text[..^1] : text).Split('\n');
        var sb = new System.Text.StringBuilder(header)
            .Append($"--- /dev/null\n+++ b/{path}\n@@ -0,0 +1{(lines.Length == 1 ? "" : "," + lines.Length)} @@\n");
        foreach (var line in lines) sb.Append('+').Append(line).Append('\n');
        if (!endsWithNewline) sb.Append("\\ No newline at end of file\n");
        return sb.ToString();
    }

    static Tree? ParentTree(Repository repo, string sha) => repo.Lookup<Commit>(sha) is { } c ? c.Parents.FirstOrDefault()?.Tree : null;

    static IReadOnlyList<FileChange> Lg2CommitFiles(Repository repo, string sha)
    {
        var commit = repo.Lookup<Commit>(sha) ?? throw new NotFoundException($"Commit {sha} not found.");
        var files = new List<FileChange>();
        foreach (var c in repo.Diff.Compare<TreeChanges>(commit.Parents.FirstOrDefault()?.Tree, commit.Tree, DiffOptions))
        {
            var kind = c.Status switch
            {
                ChangeKind.Added => FileChangeKind.Added,
                ChangeKind.Deleted => FileChangeKind.Deleted,
                ChangeKind.Renamed => FileChangeKind.Renamed,
                ChangeKind.Copied => FileChangeKind.Copied,
                ChangeKind.TypeChanged => FileChangeKind.TypeChanged,
                ChangeKind.Modified => FileChangeKind.Modified,
                _ => (FileChangeKind?)null,
            };
            if (kind is { } k)
                files.Add(new FileChange(c.Path, k is FileChangeKind.Renamed or FileChangeKind.Copied ? c.OldPath : null, k));
        }
        return files;
    }

    static string Lg2CommitDiffText(Repository repo, string sha, FileChange change)
    {
        var commit = repo.Lookup<Commit>(sha) ?? throw new NotFoundException($"Commit {sha} not found.");
        string[] paths = change.OldPath is null ? [change.Path] : [change.Path, change.OldPath];
        return repo.Diff.Compare<Patch>(commit.Parents.FirstOrDefault()?.Tree, commit.Tree, paths, null, DiffOptions).Content;
    }

    static ImageVersion? Lg2ReadBlob(Repository repo, string revision, string path)
    {
        if (repo.Lookup<Blob>($"{revision}:{path}") is not { } blob || blob.Size > ImageDiff.MaxPreviewBytes) return null;
        using var stream = blob.GetContentStream();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ImageVersion.From(ms.ToArray());
    }

    // ── Commit ───────────────────────────────────────────────────────────────

    /// <summary>The user's name and email for commits, from the repository's config or the user's.</summary>
    static Signature RequireSignature(Repository repo)
        => repo.Config.BuildSignature(DateTimeOffset.Now)
           ?? throw new GitException(
               "Your name and email for commits aren't set. Add user.name and user.email in Repository › Repository settings › Git config.",
               new GitResult(128, "", "user.name or user.email not set"));

    void Lg2Commit(Repository repo, string message, IReadOnlyList<CommitFileSelection> files, bool amend, bool headIsUnborn)
    {
        if (files.Any(f => f.PartialDiff is not null))
            throw new GitException("Committing selected lines isn't available on this device yet: include or leave out whole files.",
                new GitResult(128, "", "partial commit"));

        // As on the command line: rebuild the index from HEAD so only what's selected goes in, except during a merge
        if (Lg2Operation(repo) == RepositoryOperation.None)
        {
            if (headIsUnborn) repo.Index.Clear();
            else repo.Index.Replace(repo.Head.Tip.Tree);
            repo.Index.Write();
        }
        var paths = files.SelectMany(f => f.Change.OldPath is null ? [f.Change.Path] : new[] { f.Change.Path, f.Change.OldPath }).ToList();
        if (paths.Count > 0) Commands.Stage(repo, paths);

        var signature = RequireSignature(repo);
        repo.Commit(message, signature, signature, new CommitOptions { AmendPreviousCommit = amend });
    }

    void Lg2Discard(Repository repo, IReadOnlyList<FileChange> changes)
    {
        var restore = new List<string>();
        foreach (var c in changes)
        {
            switch (c.Kind)
            {
                case FileChangeKind.Untracked:
                    DeleteWorkingFile(c.Path);
                    break;
                case FileChangeKind.Added or FileChangeKind.Copied:
                    repo.Index.Remove(c.Path);
                    DeleteWorkingFile(c.Path);
                    break;
                case FileChangeKind.Renamed:
                    repo.Index.Remove(c.Path);
                    DeleteWorkingFile(c.Path);
                    if (c.OldPath is not null) restore.Add(c.OldPath);
                    break;
                default:
                    restore.Add(c.Path);
                    break;
            }
        }
        repo.Index.Write();
        // Checking out from HEAD puts both the index and the working file back (git restore --staged --worktree)
        if (restore.Count > 0)
            repo.CheckoutPaths("HEAD", restore, new CheckoutOptions { CheckoutModifiers = CheckoutModifiers.Force });
    }

    // ── History ──────────────────────────────────────────────────────────────

    static IReadOnlyList<CommitInfo> Lg2Log(Repository repo, int skip, int count, string revision)
    {
        if (repo.Info.IsHeadUnborn && revision == "HEAD") return [];
        var tags = repo.Tags
            .Where(t => t.PeeledTarget is Commit)
            .GroupBy(t => t.PeeledTarget.Sha)
            .ToDictionary(g => g.Key, g => g.Select(t => t.FriendlyName).ToList());
        try
        {
            var filter = new CommitFilter { IncludeReachableFrom = revision, SortBy = CommitSortStrategies.Time };
            return repo.Commits.QueryBy(filter).Skip(skip).Take(count).Select(c =>
            {
                var (summary, body) = SplitMessage(c.Message);
                return new CommitInfo(c.Sha, c.Sha[..7], c.Parents.Select(p => p.Sha).ToList(),
                    c.Author.Name, c.Author.Email, c.Author.When, summary, body,
                    tags.TryGetValue(c.Sha, out var t) ? t : []);
            }).ToList();
        }
        catch (NotFoundException)
        {
            return [];   // as git log on a revision that doesn't exist
        }
    }

    /// <summary>git log's %s (the first paragraph, on one line) and %b (the rest).</summary>
    static (string Summary, string Body) SplitMessage(string message)
    {
        var text = message.Replace("\r\n", "\n").Trim('\n');
        var split = text.IndexOf("\n\n", StringComparison.Ordinal);
        var subject = split < 0 ? text : text[..split];
        var body = split < 0 ? "" : text[(split + 2)..].Trim();
        return (subject.Replace('\n', ' ').Trim(), body);
    }

    static HashSet<string> Lg2Unpushed(Repository repo, string upstream)
    {
        if (repo.Info.IsHeadUnborn || repo.Lookup<Commit>(upstream) is not { } theirs) return [];
        var filter = new CommitFilter { IncludeReachableFrom = repo.Head.Tip, ExcludeReachableFrom = theirs };
        return repo.Commits.QueryBy(filter).Select(c => c.Sha).ToHashSet();
    }

    // ── Branches ─────────────────────────────────────────────────────────────

    static IReadOnlyList<BranchInfo> Lg2Branches(Repository repo)
        => repo.Branches
            .Where(b => !b.CanonicalName.EndsWith("/HEAD", StringComparison.Ordinal) && b.Tip is not null)
            .Select(b => new BranchInfo(b.CanonicalName, b.FriendlyName, b.IsRemote,
                b.IsTracking ? b.TrackedBranch?.FriendlyName : null, b.Tip.Sha, b.Tip.Committer.When, b.IsCurrentRepositoryHead))
            .OrderByDescending(b => b.LastCommitDate)
            .ToList();

    static void Lg2CreateBranch(Repository repo, string name, string? startPoint)
    {
        var target = startPoint is null ? repo.Head.Tip : repo.Lookup<Commit>(startPoint);
        if (target is null)
        {
            if (startPoint is not null) throw new NotFoundException($"'{startPoint}' is not a commit.");
            // A new repository with no commits yet: the new branch is simply where the first commit will go
            PointUnbornHead(repo, name);
            return;
        }
        Commands.Checkout(repo, repo.CreateBranch(name, target));
    }

    static void Lg2Checkout(Repository repo, BranchInfo branch)
    {
        if (!branch.IsRemote)
        {
            Commands.Checkout(repo, repo.Branches[branch.Name] ?? throw new NotFoundException($"Branch {branch.Name} not found."));
            return;
        }
        // A remote branch: a local branch that tracks it, as git switch -c <name> --track <remote>/<name>
        var remote = repo.Branches[branch.Name] ?? throw new NotFoundException($"Branch {branch.Name} not found.");
        var local = repo.Branches[branch.NameWithoutRemote] ?? repo.CreateBranch(branch.NameWithoutRemote, remote.Tip);
        repo.Branches.Update(local, u => u.TrackedBranch = remote.CanonicalName);
        Commands.Checkout(repo, local);
    }

    // ── Stash ────────────────────────────────────────────────────────────────

    /// <summary>"stash@{2}" → 2.</summary>
    static int StashIndex(string name)
        => int.TryParse(name.AsSpan(name.IndexOf('{') + 1, name.Length - name.IndexOf('{') - 2), out var i) ? i : 0;

    static Signature StashSignature(Repository repo)
        => repo.Config.BuildSignature(DateTimeOffset.Now) ?? new Signature("CommitDesk", "commitdesk@localhost", DateTimeOffset.Now);

    static void ThrowIfConflicted(Repository repo, string what)
    {
        var paths = repo.Index.Conflicts.Select(c => (c.Ours ?? c.Theirs ?? c.Ancestor).Path).Distinct().ToList();
        if (paths.Count == 0) return;
        var lines = string.Join("\n", paths.Select(p => $"CONFLICT (content): Merge conflict in {p}"));
        throw new GitException($"{lines}\nAutomatic {what} failed; fix conflicts and then commit the result.", new GitResult(1, lines, ""));
    }

    static void Lg2PopStash(Repository repo, int index)
    {
        var status = repo.Stashes.Pop(index, new StashApplyOptions { ApplyModifiers = StashApplyModifiers.ReinstateIndex });
        if (status == StashApplyStatus.Conflicts) ThrowIfConflicted(repo, "merge");
    }

    // ── Remotes ──────────────────────────────────────────────────────────────

    /// <summary>The sign-in headers git would send to this remote (http.&lt;url&gt;.extraHeader).</summary>
    string[] HeadersFor(string url) => GitAuth.ExtraHeadersFor(url, git.ConfigProvider?.Invoke()).ToArray();

    void Lg2Fetch(Repository repo, string remote, CancellationToken ct)
    {
        var r = repo.Network.Remotes[remote] ?? throw new NotFoundException($"No remote named '{remote}'.");
        var options = new FetchOptions
        {
            Prune = true,
            TagFetchMode = TagFetchMode.All,
            CustomHeaders = HeadersFor(r.Url),
            CertificateCheck = GitCertificates.CertificateCheckFor(r.Url),
            OnTransferProgress = _ => !ct.IsCancellationRequested,
        };
        Commands.Fetch(repo, remote, r.FetchRefSpecs.Select(s => s.Specification), options, null);
    }

    /// <summary>Fetches the upstream and merges it, fast-forwarding when possible, as git pull --no-rebase.</summary>
    void Lg2Pull(Repository repo, CancellationToken ct)
    {
        var head = repo.Head;
        if (!head.IsTracking || head.TrackedBranch is not { } upstream)
            throw new GitException("There is no tracking information for the current branch. Publish it first.",
                new GitResult(1, "", "no upstream"));
        Lg2Fetch(repo, upstream.RemoteName, ct);
        upstream = repo.Head.TrackedBranch;
        var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
        var result = repo.Merge(upstream, signature ?? new Signature("CommitDesk", "commitdesk@localhost", DateTimeOffset.Now),
            new MergeOptions { FastForwardStrategy = signature is null ? FastForwardStrategy.FastForwardOnly : FastForwardStrategy.Default });
        if (result.Status == MergeStatus.Conflicts) ThrowIfConflicted(repo, "merge");
    }

    void Lg2Push(Repository repo, string remote, string branch, bool setUpstream, bool force)
    {
        var r = repo.Network.Remotes[remote] ?? throw new NotFoundException($"No remote named '{remote}'.");
        string? rejected = null;
        var options = new PushOptions
        {
            CustomHeaders = HeadersFor(r.Url),
            CertificateCheck = GitCertificates.CertificateCheckFor(r.Url),
            OnPushStatusError = e => rejected = $" ! [remote rejected] {e.Reference} ({e.Message})",
        };
        // libgit2 has no --force-with-lease; a force push is a plain force
        repo.Network.Push(r, $"{(force ? "+" : "")}refs/heads/{branch}:refs/heads/{branch}", options);
        if (rejected is not null) throw new GitException($"{rejected}\nerror: failed to push some refs", new GitResult(1, "", rejected));
        if (setUpstream && repo.Branches[branch] is { } local)
            repo.Branches.Update(local, u => { u.Remote = remote; u.UpstreamBranch = $"refs/heads/{branch}"; });
    }

    void Lg2DeleteRemoteBranch(Repository repo, string remote, string name)
    {
        var r = repo.Network.Remotes[remote] ?? throw new NotFoundException($"No remote named '{remote}'.");
        repo.Network.Push(r, $":refs/heads/{name}",
            new PushOptions { CustomHeaders = HeadersFor(r.Url), CertificateCheck = GitCertificates.CertificateCheckFor(r.Url) });
    }

    // ── Config ───────────────────────────────────────────────────────────────

    /// <summary>A boolean as git uses it for every repository: the user's (global) config, then the system's.</summary>
    static bool? Lg2DefaultBool(string key)
    {
        using var config = Configuration.BuildFrom(null);
        return config.Get<bool>(key) is { } entry ? entry.Value : null;
    }

    static void Lg2SetDefaultBool(string key, bool value)
    {
        using var config = Configuration.BuildFrom(null);
        if (value) config.Set(key, true, ConfigurationLevel.Global);
        else
        {
            config.Unset(key, ConfigurationLevel.Global);
            if (Lg2DefaultBool(key) == true) config.Set(key, false, ConfigurationLevel.Global);
        }
    }
}

/// <summary>Sets libgit2 up for a platform without git (where its user config lives).</summary>
public static class LibGit2Setup
{
    /// <summary>
    /// Keeps libgit2's user-level (global) config, git's ~/.gitconfig, in <paramref name="homeDirectory"/>, the app's own
    /// folder on Android, which has no home directory.
    /// </summary>
    public static void Configure(string homeDirectory)
    {
        Directory.CreateDirectory(homeDirectory);
        GlobalSettings.SetConfigSearchPaths(ConfigurationLevel.Global, homeDirectory);
    }
}
