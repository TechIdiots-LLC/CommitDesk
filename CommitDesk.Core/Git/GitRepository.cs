using System.Globalization;
using LibGit2Sharp;

namespace CommitDesk.Core.Git;

/// <summary>A selected file for the next commit; <see cref="PartialDiff"/> is set when only some lines are included.</summary>
public sealed record CommitFileSelection(FileChange Change, FileDiff? PartialDiff);

/// <summary>Local repository operations, implemented on top of the git CLI.</summary>
public sealed partial class GitRepository(GitRunner git, string path)
{
    const string RecordSep = "\x1e";
    const string FieldSep = "\x1f";

    string? _emptyTree;

    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\', '/'));
    public GitRunner Git => git;

    Task<GitResult> Run(params string[] args) => git.RunAsync(Path, args);
    Task<GitResult> Run(IEnumerable<string> args, string? stdin = null, bool throwOnError = true, CancellationToken ct = default)
        => git.RunAsync(Path, args, stdin, throwOnError, ct);

    // ── Repository discovery / creation ──────────────────────────────────────

    /// <summary>Returns the top level of the work tree containing <paramref name="dir"/>, or null when it is not in a repository.</summary>
    public static async Task<string?> FindRootAsync(GitRunner git, string dir)
    {
        if (git.UseLibGit2) return await Task.Run(() => Lg2FindRoot(dir));
        var r = await git.RunAsync(dir, ["rev-parse", "--show-toplevel"], throwOnError: false);
        return r.Success ? System.IO.Path.GetFullPath(r.StdOut.Trim()) : null;
    }

    public static Task InitAsync(GitRunner git, string dir, string defaultBranch = "main")
        => git.UseLibGit2 ? Task.Run(() => Lg2Init(dir, defaultBranch)) : git.RunAsync(dir, ["init", "-b", defaultBranch]);

    /// <param name="recurseSubmodules">
    /// Also clone the submodules. git's own submodule.recurse setting doesn't apply to clone, so this is passed explicitly.
    /// </param>
    /// <param name="progress">
    /// Receives git's progress lines as they arrive (see <see cref="GitProgress.Parse"/>); git only writes them when asked.
    /// </param>
    public static Task CloneAsync(GitRunner git, string url, string targetDir, bool recurseSubmodules = true,
        CancellationToken ct = default, Action<string>? progress = null)
    {
        if (git.UseLibGit2)
            return LibGit2Clone.CloneAsync(url, targetDir, recurseSubmodules, git.ConfigProvider?.Invoke(), ct, progress);
        var parent = System.IO.Path.GetDirectoryName(targetDir)!;
        Directory.CreateDirectory(parent);
        List<string> args = ["clone"];
        if (progress is not null) args.Add("--progress");
        if (recurseSubmodules) args.Add("--recurse-submodules");
        args.AddRange(["--", url, targetDir]);
        return git.RunAsync(parent, args, ct: ct, progress: progress);
    }

    /// <summary>
    /// A boolean as git uses it for every repository: from the user's global config, or else the system config (Git for
    /// Windows sets core.longpaths there). Null when neither sets it. A repository's own config can still override it.
    /// </summary>
    public static async Task<bool?> GetDefaultBoolAsync(GitRunner git, string key)
    {
        if (git.UseLibGit2) return await Task.Run(() => Lg2DefaultBool(key));
        // Run outside any repository, so only the system and global config apply
        var r = await git.RunAsync(System.IO.Path.GetTempPath(), ["config", "--type=bool", "--get", key], throwOnError: false);
        return r.Success ? r.StdOut.Trim() == "true" : null;
    }

    /// <summary>
    /// Turns a boolean on or off for every repository, in the user's global config. Off removes the global value, and
    /// writes an explicit false only when the system config would otherwise still turn it on.
    /// </summary>
    public static async Task SetDefaultBoolAsync(GitRunner git, string key, bool value)
    {
        if (git.UseLibGit2)
        {
            await Task.Run(() => Lg2SetDefaultBool(key, value));
            return;
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (value)
        {
            await git.RunAsync(home, ["config", "--global", key, "true"]);
            return;
        }
        await git.RunAsync(home, ["config", "--global", "--unset", key], throwOnError: false);   // exit 5 when not set
        if (await GetDefaultBoolAsync(git, key) == true)
            await git.RunAsync(home, ["config", "--global", key, "false"]);
    }

    // ── Status ───────────────────────────────────────────────────────────────

    public async Task<RepositoryStatus> GetStatusAsync()
    {
        if (UseLibGit2) return await Lg2Async(Lg2Status);
        string[] args = ["status", "--porcelain=v2", "--branch", "-z", "--untracked-files=all"];
        var r = await Run(args, throwOnError: false);
        // Stubs left by a failed submodule checkout break even git status, with wording that varies by git version
        // ("not a git repository: …/.git/modules/…", "gitfile does not point to a valid repository"). The repair only
        // ever removes such empty stubs, so try it on any failure and retry if it found some.
        if (!r.Success && await RemoveBrokenSubmoduleStubsAsync())
            r = await Run(args, throwOnError: false);
        if (!r.Success) throw new GitException(r.StdErr.Trim(), r);
        return StatusParser.Parse(r.StdOut);
    }

    public async Task<RepositoryOperation> GetOperationAsync()
    {
        if (UseLibGit2) return await Lg2Async(Lg2Operation);
        var r = await Run("rev-parse", "--absolute-git-dir");
        var gitDir = r.StdOut.Trim();
        if (Directory.Exists(System.IO.Path.Combine(gitDir, "rebase-merge")) ||
            Directory.Exists(System.IO.Path.Combine(gitDir, "rebase-apply")))
            return RepositoryOperation.Rebase;
        if (File.Exists(System.IO.Path.Combine(gitDir, "MERGE_HEAD"))) return RepositoryOperation.Merge;
        if (File.Exists(System.IO.Path.Combine(gitDir, "CHERRY_PICK_HEAD"))) return RepositoryOperation.CherryPick;
        if (File.Exists(System.IO.Path.Combine(gitDir, "REVERT_HEAD"))) return RepositoryOperation.Revert;
        return RepositoryOperation.None;
    }

    /// <summary>The message git prepared for a squash merge or a conflicted merge, if any.</summary>
    public async Task<string?> GetPreparedCommitMessageAsync()
    {
        var gitDir = UseLibGit2 ? await Lg2Async(r => r.Info.Path) : (await Run("rev-parse", "--absolute-git-dir")).StdOut.Trim();
        foreach (var name in new[] { "MERGE_MSG", "SQUASH_MSG" })
        {
            var f = System.IO.Path.Combine(gitDir, name);
            if (File.Exists(f))
            {
                var lines = (await File.ReadAllLinesAsync(f)).Where(l => !l.StartsWith('#'));
                return string.Join('\n', lines).Trim();
            }
        }
        return null;
    }

    async Task<string> EmptyTreeAsync()
        => _emptyTree ??= (await Run(["hash-object", "-t", "tree", "--stdin"], stdin: "")).StdOut.Trim();

    // ── Diffs ────────────────────────────────────────────────────────────────

    /// <summary>Diff of the working tree against HEAD for one changed file.</summary>
    public async Task<FileDiff> GetWorkingDiffAsync(FileChange change, bool headIsUnborn, bool initiallySelected)
    {
        if (UseLibGit2)
            return DiffParser.Parse(change.Path, await Lg2Async(r => Lg2WorkingDiffText(r, Path, change)), selectable: true, initiallySelected);
        GitResult r;
        if (change.Kind == FileChangeKind.Untracked)
        {
            // Exit code 1 just means "differences found" for --no-index.
            r = await Run(["diff", "--no-index", "--no-ext-diff", "--", "/dev/null", change.Path], throwOnError: false);
        }
        else
        {
            var baseRev = headIsUnborn ? await EmptyTreeAsync() : "HEAD";
            var args = new List<string> { "--literal-pathspecs", "diff", "--no-ext-diff", "-M", baseRev, "--", change.Path };
            if (change.OldPath is not null) args.Add(change.OldPath);
            r = await Run(args);
        }
        return DiffParser.Parse(change.Path, r.StdOut, selectable: true, initiallySelected);
    }

    public async Task<IReadOnlyList<FileChange>> GetCommitFilesAsync(CommitInfo commit)
    {
        if (UseLibGit2) return await Lg2Async(r => Lg2CommitFiles(r, commit.Sha));
        var parent = commit.Parents.Count > 0 ? commit.Parents[0] : await EmptyTreeAsync();
        var r = await Run("diff", "--name-status", "-z", "-M", parent, commit.Sha);
        var parts = r.StdOut.Split('\0');
        var files = new List<FileChange>();
        for (int i = 0; i + 1 < parts.Length; i++)
        {
            var status = parts[i];
            if (status.Length == 0) continue;
            switch (status[0])
            {
                case 'R':
                case 'C':
                    var oldPath = parts[++i];
                    var newPath = parts[++i];
                    files.Add(new FileChange(newPath, oldPath, status[0] == 'R' ? FileChangeKind.Renamed : FileChangeKind.Copied));
                    break;
                default:
                    var p = parts[++i];
                    files.Add(new FileChange(p, null, status[0] switch
                    {
                        'A' => FileChangeKind.Added,
                        'D' => FileChangeKind.Deleted,
                        'T' => FileChangeKind.TypeChanged,
                        _ => FileChangeKind.Modified,
                    }));
                    break;
            }
        }
        return files;
    }

    public async Task<FileDiff> GetCommitDiffAsync(CommitInfo commit, FileChange change)
    {
        if (UseLibGit2)
            return DiffParser.Parse(change.Path, await Lg2Async(r => Lg2CommitDiffText(r, commit.Sha, change)), selectable: false, initiallySelected: false);
        var parent = commit.Parents.Count > 0 ? commit.Parents[0] : await EmptyTreeAsync();
        var args = new List<string> { "--literal-pathspecs", "diff", "--no-ext-diff", "-M", parent, commit.Sha, "--", change.Path };
        if (change.OldPath is not null) args.Add(change.OldPath);
        var r = await Run(args);
        return DiffParser.Parse(change.Path, r.StdOut, selectable: false, initiallySelected: false);
    }

    // ── Images ───────────────────────────────────────────────────────────────

    /// <summary>A file's bytes at a revision ("HEAD", a commit sha), or null if it isn't there or is too large to preview.</summary>
    async Task<ImageVersion?> ReadBlobAsync(string revision, string path)
    {
        if (UseLibGit2) return await Lg2Async(r => Lg2ReadBlob(r, revision, path));
        // Check the size first so a huge blob isn't read into memory just to be refused.
        var size = await Run(["cat-file", "-s", $"{revision}:{path}"], throwOnError: false);
        if (!size.Success || !long.TryParse(size.StdOut.Trim(), out var bytes) || bytes > ImageDiff.MaxPreviewBytes) return null;
        var data = await git.ReadBytesAsync(Path, ["cat-file", "blob", $"{revision}:{path}"]);
        return data is null ? null : ImageVersion.From(data);
    }

    ImageVersion? ReadWorkingFile(string path)
    {
        var file = new FileInfo(System.IO.Path.Combine(Path, path));
        return file.Exists && file.Length <= ImageDiff.MaxPreviewBytes ? ImageVersion.From(File.ReadAllBytes(file.FullName)) : null;
    }

    /// <summary>An image's version in HEAD and in the working tree, for the Changes tab.</summary>
    public async Task<ImageDiff> GetWorkingImageDiffAsync(FileChange change, bool headIsUnborn)
    {
        bool inHead = !headIsUnborn && change.Kind is not (FileChangeKind.Untracked or FileChangeKind.Added);
        var old = inHead ? await ReadBlobAsync("HEAD", change.OldPath ?? change.Path) : null;
        var current = change.Kind == FileChangeKind.Deleted ? null : ReadWorkingFile(change.Path);
        return new ImageDiff(old, current);
    }

    /// <summary>An image's version before and in a commit (compared with its first parent), for the History tab.</summary>
    public async Task<ImageDiff> GetCommitImageDiffAsync(CommitInfo commit, FileChange change)
    {
        var old = commit.Parents.Count > 0 && change.Kind != FileChangeKind.Added
            ? await ReadBlobAsync(commit.Parents[0], change.OldPath ?? change.Path)
            : null;
        var current = change.Kind == FileChangeKind.Deleted ? null : await ReadBlobAsync(commit.Sha, change.Path);
        return new ImageDiff(old, current);
    }

    // ── Commit ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Commits exactly the selected files/lines. Like GitHub Desktop, the index is rebuilt from HEAD so
    /// whatever was staged outside the app does not leak into the commit.
    /// </summary>
    public async Task CommitAsync(string message, IReadOnlyList<CommitFileSelection> files, bool amend, bool headIsUnborn)
    {
        if (UseLibGit2)
        {
            await Lg2Async(r => Lg2Commit(r, message, files, amend, headIsUnborn));
            return;
        }
        var op = await GetOperationAsync();
        // Resetting the index would drop merge state and conflict resolutions, so only rebuild it otherwise.
        if (op == RepositoryOperation.None)
        {
            if (headIsUnborn)
                await Run("rm", "-r", "-q", "--cached", "--ignore-unmatch", ".");
            else
                await Run("reset", "-q", "HEAD", "--", ".");
        }

        var full = new List<string>();
        foreach (var f in files.Where(f => f.PartialDiff is null))
        {
            full.Add(f.Change.Path);
            if (f.Change.OldPath is not null) full.Add(f.Change.OldPath);
        }
        if (full.Count > 0)
        {
            await Run(["--literal-pathspecs", "add", "-A", "--pathspec-from-file=-", "--pathspec-file-nul"],
                stdin: string.Join('\0', full) + "\0");
        }

        foreach (var f in files.Where(f => f.PartialDiff is not null))
        {
            var patch = PartialPatchBuilder.Build(f.PartialDiff!);
            if (patch is null) continue;
            await Run(["apply", "--cached", "--recount", "--whitespace=nowarn", "-"], stdin: patch);
        }

        var args = new List<string> { "commit", "-F", "-" };
        if (amend) args.Add("--amend");
        await Run(args, stdin: message);
    }

    public async Task<string> GetHeadMessageAsync()
        => UseLibGit2 ? await Lg2Async(r => r.Head.Tip.Message.TrimEnd()) : (await Run("log", "-1", "--format=%B")).StdOut.TrimEnd();

    public async Task DiscardAsync(IReadOnlyList<FileChange> changes)
    {
        if (UseLibGit2)
        {
            await Lg2Async(r => Lg2Discard(r, changes));
            return;
        }
        var restore = new List<string>();
        foreach (var c in changes)
        {
            switch (c.Kind)
            {
                case FileChangeKind.Untracked:
                    DeleteWorkingFile(c.Path);
                    break;
                case FileChangeKind.Added:
                    await Run("--literal-pathspecs", "rm", "-q", "--cached", "-f", "--", c.Path);
                    DeleteWorkingFile(c.Path);
                    break;
                case FileChangeKind.Renamed or FileChangeKind.Copied:
                    await Run("--literal-pathspecs", "rm", "-q", "--cached", "-f", "--", c.Path);
                    DeleteWorkingFile(c.Path);
                    if (c.Kind == FileChangeKind.Renamed && c.OldPath is not null) restore.Add(c.OldPath);
                    break;
                default:
                    restore.Add(c.Path);
                    break;
            }
        }
        if (restore.Count > 0)
        {
            await Run(["--literal-pathspecs", "restore", "--source=HEAD", "--staged", "--worktree", "--pathspec-from-file=-", "--pathspec-file-nul"],
                stdin: string.Join('\0', restore) + "\0");
        }
    }

    void DeleteWorkingFile(string relative)
    {
        var full = System.IO.Path.Combine(Path, relative);
        if (File.Exists(full)) File.Delete(full);
    }

    public async Task AddToGitIgnoreAsync(string pattern)
    {
        var file = System.IO.Path.Combine(Path, ".gitignore");
        var existing = File.Exists(file) ? await File.ReadAllTextAsync(file) : "";
        var prefix = existing.Length > 0 && !existing.EndsWith('\n') ? "\n" : "";
        await File.AppendAllTextAsync(file, prefix + pattern + "\n");
    }

    // ── History ──────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<CommitInfo>> GetLogAsync(int skip, int count, string revision = "HEAD")
    {
        if (UseLibGit2) return await Lg2Async(r => Lg2Log(r, skip, count, revision));
        var format = string.Join(FieldSep, "%H", "%h", "%P", "%an", "%ae", "%aI", "%D", "%s", "%b") + RecordSep;
        var r = await Run(["log", $"--max-count={count}", $"--skip={skip}", $"--format={format}", "--decorate=full", revision, "--"], throwOnError: false);
        if (!r.Success) return [];   // unborn branch

        var list = new List<CommitInfo>();
        foreach (var rec in r.StdOut.Split(RecordSep))
        {
            var f = rec.TrimStart('\n', '\r').Split(FieldSep);
            if (f.Length < 9) continue;
            var tags = f[6].Split(", ", StringSplitOptions.RemoveEmptyEntries)
                .Where(d => d.StartsWith("tag: refs/tags/"))
                .Select(d => d["tag: refs/tags/".Length..])
                .ToList();
            list.Add(new CommitInfo(
                f[0], f[1],
                f[2].Split(' ', StringSplitOptions.RemoveEmptyEntries),
                f[3], f[4],
                DateTimeOffset.Parse(f[5], CultureInfo.InvariantCulture),
                f[7], f[8].Trim(), tags));
        }
        return list;
    }

    /// <summary>Commits on HEAD that are not on the upstream branch.</summary>
    public async Task<HashSet<string>> GetUnpushedShasAsync(string upstream)
    {
        if (UseLibGit2) return await Lg2Async(r => Lg2Unpushed(r, upstream));
        var r = await Run(["rev-list", $"{upstream}..HEAD"], throwOnError: false);
        return r.Success
            ? r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet()
            : [];
    }

    // ── Branches ─────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<BranchInfo>> GetBranchesAsync()
    {
        if (UseLibGit2) return await Lg2Async(Lg2Branches);
        var format = string.Join("%1f", "%(refname)", "%(refname:short)", "%(upstream:short)", "%(objectname)", "%(committerdate:iso8601-strict)", "%(HEAD)");
        var r = await Run("for-each-ref", $"--format={format}", "--sort=-committerdate", "refs/heads", "refs/remotes");
        var list = new List<BranchInfo>();
        foreach (var line in r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split(FieldSep);
            if (f.Length < 6 || f[0].EndsWith("/HEAD")) continue;
            DateTimeOffset? date = DateTimeOffset.TryParse(f[4], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
            list.Add(new BranchInfo(f[0], f[1], f[0].StartsWith("refs/remotes/"),
                string.IsNullOrEmpty(f[2]) ? null : f[2], f[3], date, f[5] == "*"));
        }
        return list;
    }

    public Task CreateBranchAsync(string name, string? startPoint = null)
        => UseLibGit2 ? Lg2Async(r => Lg2CreateBranch(r, name, startPoint)) : startPoint is null ? RunMovingHeadAsync("switch", "-c", name) : RunMovingHeadAsync("switch", "-c", name, startPoint);

    public Task CheckoutAsync(BranchInfo branch)
        => UseLibGit2 ? Lg2Async(r => Lg2Checkout(r, branch)) : branch.IsRemote
            ? RunMovingHeadAsync("switch", "-c", branch.NameWithoutRemote, "--track", branch.Name)
            : RunMovingHeadAsync("switch", branch.Name);

    public Task CheckoutCommitAsync(string sha)
        => UseLibGit2 ? Lg2Async(r => { Commands.Checkout(r, r.Lookup<LibGit2Sharp.Commit>(sha)); }) : RunMovingHeadAsync("switch", "--detach", sha);

    public Task RenameBranchAsync(string oldName, string newName)
        => UseLibGit2 ? Lg2Async(r => { r.Branches.Rename(oldName, newName); }) : Run("branch", "-m", oldName, newName);

    public Task DeleteBranchAsync(string name) => UseLibGit2 ? Lg2Async(r => r.Branches.Remove(name)) : Run("branch", "-D", name);

    public Task DeleteRemoteBranchAsync(string remote, string name)
        => UseLibGit2 ? Lg2Async(r => Lg2DeleteRemoteBranch(r, remote, name)) : Run("push", remote, "--delete", name);

    public Task MergeAsync(string branch) => RunMovingHeadAsync("merge", "--no-edit", branch);

    public Task SquashMergeAsync(string branch) => RunMovingHeadAsync("merge", "--squash", branch);

    public Task RebaseAsync(string onto) => RunMovingHeadAsync("rebase", onto);

    public Task AbortAsync(RepositoryOperation op) => op switch
    {
        RepositoryOperation.Merge => Run("merge", "--abort"),
        RepositoryOperation.Rebase => Run("rebase", "--abort"),
        RepositoryOperation.CherryPick => Run("cherry-pick", "--abort"),
        RepositoryOperation.Revert => Run("revert", "--abort"),
        _ => Task.CompletedTask,
    };

    public async Task ContinueAsync(RepositoryOperation op)
    {
        await Run("add", "-A");
        var verb = op switch
        {
            RepositoryOperation.Rebase => "rebase",
            RepositoryOperation.CherryPick => "cherry-pick",
            RepositoryOperation.Revert => "revert",
            _ => null,
        };
        // core.editor=true keeps git from opening an editor for the message.
        if (verb is not null) await Run("-c", "core.editor=true", verb, "--continue");
        else await Run("commit", "--no-edit");
    }

    public Task RevertAsync(CommitInfo c)
        => c.IsMerge ? Run("revert", "--no-edit", "-m", "1", c.Sha) : Run("revert", "--no-edit", c.Sha);

    public Task CherryPickAsync(string sha) => Run("cherry-pick", sha);

    public Task ResetToAsync(string sha) => Run("reset", "--mixed", sha);

    public Task CreateTagAsync(string name, string sha) => Run("tag", "-a", name, "-m", name, sha);

    // ── Stash ────────────────────────────────────────────────────────────────

    public Task StashAsync(string message)
        => UseLibGit2
            ? Lg2Async(r => { r.Stashes.Add(StashSignature(r), message, StashModifiers.IncludeUntracked); })
            : Run("stash", "push", "--include-untracked", "-m", message);

    public async Task<IReadOnlyList<StashEntry>> GetStashesAsync()
    {
        if (UseLibGit2)
            return await Lg2Async(r => (IReadOnlyList<StashEntry>)r.Stashes.Select((s, i) => new StashEntry($"stash@{{{i}}}", s.Message)).ToList());
        var r = await Run("stash", "list", "--format=%gd%x1f%gs");
        return r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split(FieldSep))
            .Where(p => p.Length == 2)
            .Select(p => new StashEntry(p[0], p[1]))
            .ToList();
    }

    public Task PopStashAsync(string name)
        => UseLibGit2 ? Lg2Async(r => Lg2PopStash(r, StashIndex(name)), conflictAction: "merge") : Run("stash", "pop", name);

    public Task DropStashAsync(string name)
        => UseLibGit2 ? Lg2Async(r => r.Stashes.Remove(StashIndex(name))) : Run("stash", "drop", name);

    // ── Remotes ──────────────────────────────────────────────────────────────

    public async Task<string?> GetRemoteUrlAsync(string remote = "origin")
    {
        if (UseLibGit2) return await Lg2Async(r => r.Network.Remotes[remote]?.Url);
        var r = await Run(["remote", "get-url", remote], throwOnError: false);
        return r.Success ? r.StdOut.Trim() : null;
    }

    public async Task<IReadOnlyList<string>> GetRemotesAsync()
        => UseLibGit2 ? await Lg2Async(r => (IReadOnlyList<string>)r.Network.Remotes.Select(x => x.Name).ToList()) : (await Run("remote")).StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public async Task SetRemoteUrlAsync(string remote, string url)
    {
        if (UseLibGit2)
        {
            await Lg2Async(r =>
            {
                if (r.Network.Remotes[remote] is null) r.Network.Remotes.Add(remote, url);
                else r.Network.Remotes.Update(remote, u => u.Url = url);
            });
            return;
        }
        var remotes = await GetRemotesAsync();
        if (remotes.Contains(remote)) await Run("remote", "set-url", remote, url);
        else await Run("remote", "add", remote, url);
    }

    /// <summary>The remote's default branch as a short ref such as "origin/main", if known locally.</summary>
    public async Task<string?> GetRemoteDefaultBranchAsync(string remote = "origin")
    {
        if (UseLibGit2)
            return await Lg2Async(r => r.Refs[$"refs/remotes/{remote}/HEAD"] is SymbolicReference s
                ? s.TargetIdentifier.Replace("refs/remotes/", "")
                : null);
        var r = await Run(["symbolic-ref", "--short", $"refs/remotes/{remote}/HEAD"], throwOnError: false);
        return r.Success ? r.StdOut.Trim() : null;
    }

    public Task FetchAsync(string remote = "origin", CancellationToken ct = default)
        => UseLibGit2 ? Lg2Async(r => Lg2Fetch(r, remote, ct), ct) : Run(["fetch", "--prune", "--tags", remote], ct: ct);

    public async Task PullAsync(CancellationToken ct = default)
    {
        if (UseLibGit2)
        {
            await Lg2Async(r => Lg2Pull(r, ct), ct, conflictAction: "merge");
            return;
        }
        // Without a pull.rebase setting modern git refuses to pull a diverged branch; default to merging.
        var cfg = await Run(["config", "--get", "pull.rebase"], throwOnError: false);
        var args = new List<string> { "pull" };
        if (!cfg.Success) args.Add("--no-rebase");
        await RunMovingHeadAsync(args, ct);
    }

    /// <summary>
    /// Pulls with uncommitted changes in the way: stashes them (untracked files too), pulls, and puts them back.
    /// Returns false when they came back with conflicts; the stash is then kept, as git does, so nothing is lost.
    /// If the pull itself fails, the changes stay in the stash named <paramref name="stashMessage"/> and the error is
    /// thrown.
    /// </summary>
    public async Task<bool> PullAroundLocalChangesAsync(string stashMessage, CancellationToken ct = default)
    {
        await StashAsync(stashMessage);
        await PullAsync(ct);
        if (UseLibGit2)
        {
            try { await PopStashAsync("stash@{0}"); return true; }
            catch (GitException) { return false; }
        }
        var pop = await Run(["stash", "pop"], throwOnError: false);
        return pop.Success;
    }

    public Task PushAsync(string remote, string branch, bool setUpstream, bool force = false, CancellationToken ct = default)
    {
        if (UseLibGit2) return Lg2Async(r => Lg2Push(r, remote, branch, setUpstream, force), ct);
        var args = new List<string> { "push", "--follow-tags" };
        if (setUpstream) args.Add("--set-upstream");
        if (force) args.Add("--force-with-lease");
        args.Add(remote);
        args.Add($"refs/heads/{branch}:refs/heads/{branch}");
        return Run(args, ct: ct);
    }
}
