using CommitDesk.Core.Git;
using CommitDesk.Core.Hosting;

namespace CommitDesk.Core.Tests;

/// <summary>
/// The libgit2 versions of merging, rebasing, conflicts, revert, cherry-pick, reset, tags, patches and checking out
/// merge/pull requests (as on Android), checked with the git command line.
/// </summary>
public class LibGit2OperationTests
{
    static GitRepository Lg2(TempRepo t) => new(new GitRunner { UseLibGit2 = true }, t.Dir);

    static async Task<string> Git(TempRepo t, params string[] args) => (await t.RunAsync(args)).StdOut.Trim().Replace("\r", "");

    /// <summary>main with one commit, and a branch "other" with a commit of its own.</summary>
    static async Task<TempRepo> TwoBranchesAsync(string otherContent = "a\nB from other\nc\n", string? mainContent = null)
    {
        var t = await TempRepo.CreateAsync();
        t.Write("file.txt", "a\nb\nc\n");
        await t.CommitAllAsync("base");
        await t.RunAsync("switch", "-c", "other");
        t.Write("file.txt", otherContent);
        t.Write("other.txt", "only on other\n");
        await t.CommitAllAsync("on other");
        await t.RunAsync("switch", "main");
        if (mainContent is not null)
        {
            t.Write("file.txt", mainContent);
            await t.CommitAllAsync("on main");
        }
        return t;
    }

    [Fact]
    public async Task MergeFastForwardsOrCommits()
    {
        using (var t = await TwoBranchesAsync())
        {
            await Lg2(t).MergeAsync("other");
            Assert.Equal(await Git(t, "rev-parse", "other"), await Git(t, "rev-parse", "HEAD"));   // fast-forward
        }
        using (var t = await TwoBranchesAsync(mainContent: "a\nb\nc\nd on main\n", otherContent: "a\nb\nc\n"))
        {
            await Lg2(t).MergeAsync("other");
            Assert.Equal(2, (await Git(t, "log", "-1", "--format=%P")).Split(' ').Length);       // a merge commit
            Assert.True(File.Exists(Path.Combine(t.Dir, "other.txt")));
            Assert.StartsWith("Merge branch 'other'", await Git(t, "log", "-1", "--format=%s"));
        }
    }

    [Fact]
    public async Task ConflictsCanBeResolvedAndContinued()
    {
        using var t = await TwoBranchesAsync(mainContent: "a\nB from main\nc\n");
        var lg2 = Lg2(t);
        var ex = await Assert.ThrowsAsync<GitException>(() => lg2.MergeAsync("other"));
        Assert.True(GitErrors.StoppedOnConflicts(ex.Message), ex.Message);

        Assert.Equal(RepositoryOperation.Merge, await lg2.GetOperationAsync());
        Assert.Equal(RepositoryOperation.Merge, await t.Repo.GetOperationAsync());
        var conflict = await lg2.GetConflictAsync("file.txt");
        Assert.Equal((1, true, true, true), (conflict.Markers, conflict.HasOurs, conflict.HasTheirs, conflict.Unmerged));
        Assert.Equal(new ConflictSides("main", "other"), await lg2.GetConflictSidesAsync(RepositoryOperation.Merge));
        Assert.Equal("Conflicted:file.txt", string.Join(",", (await lg2.GetStatusAsync()).Files.Where(f => f.Kind == FileChangeKind.Conflicted).Select(f => $"{f.Kind}:{f.Path}")));

        await lg2.ResolveConflictAsync("file.txt", ConflictChoice.Theirs);
        Assert.Equal("a\nB from other\nc\n", t.Read("file.txt").Replace("\r\n", "\n"));
        Assert.False((await lg2.GetConflictAsync("file.txt")).Unmerged);

        await lg2.ContinueAsync(RepositoryOperation.Merge);
        Assert.Equal(RepositoryOperation.None, await t.Repo.GetOperationAsync());
        Assert.Equal(2, (await Git(t, "log", "-1", "--format=%P")).Split(' ').Length);
        Assert.StartsWith("Merge branch 'other'", await Git(t, "log", "-1", "--format=%s"));
    }

    [Fact]
    public async Task AbortingAMergePutsEverythingBack()
    {
        using var t = await TwoBranchesAsync(mainContent: "a\nB from main\nc\n");
        var head = await Git(t, "rev-parse", "HEAD");
        await Assert.ThrowsAsync<GitException>(() => Lg2(t).MergeAsync("other"));
        await Lg2(t).AbortAsync(RepositoryOperation.Merge);
        Assert.Equal(RepositoryOperation.None, await t.Repo.GetOperationAsync());
        Assert.Equal(head, await Git(t, "rev-parse", "HEAD"));
        Assert.Empty((await t.Repo.GetStatusAsync()).Files);
    }

    [Fact]
    public async Task SquashMergeLeavesOneCommitToMake()
    {
        using var t = await TwoBranchesAsync(mainContent: "a\nb\nc\nd on main\n", otherContent: "a\nb\nc\n");
        var lg2 = Lg2(t);
        await lg2.SquashMergeAsync("other");
        Assert.Equal(RepositoryOperation.None, await t.Repo.GetOperationAsync());   // no merge in progress, as git --squash
        Assert.StartsWith("Squashed commit of the following:", await lg2.GetPreparedCommitMessageAsync());
        Assert.Equal("Added:other.txt", string.Join(",", (await t.Repo.GetStatusAsync()).Files.Select(f => $"{f.Kind}:{f.Path}")));

        var files = (await lg2.GetStatusAsync()).Files.Select(f => new CommitFileSelection(f, null)).ToList();
        await lg2.CommitAsync("squashed other", files, false, false);
        Assert.Single((await Git(t, "log", "-1", "--format=%P")).Split(' '));
    }

    [Fact]
    public async Task RebaseReplaysTheBranch()
    {
        using var t = await TwoBranchesAsync(mainContent: "a\nb\nc\nd on main\n", otherContent: "a\nb\nc\n");
        await t.RunAsync("switch", "other");
        await Lg2(t).RebaseAsync("main");
        Assert.Equal(RepositoryOperation.None, await t.Repo.GetOperationAsync());
        Assert.Equal(await Git(t, "rev-parse", "main"), await Git(t, "rev-parse", "HEAD~1"));   // now on top of main
        Assert.Equal("other", (await t.Repo.GetStatusAsync()).Branch);
    }

    [Fact]
    public async Task RebaseConflictsCanBeResolvedAndContinued()
    {
        using var t = await TwoBranchesAsync(mainContent: "a\nB from main\nc\n");
        await t.RunAsync("switch", "other");
        var lg2 = Lg2(t);
        var ex = await Assert.ThrowsAsync<GitException>(() => lg2.RebaseAsync("main"));
        Assert.True(GitErrors.StoppedOnConflicts(ex.Message), ex.Message);
        Assert.Equal(RepositoryOperation.Rebase, await t.Repo.GetOperationAsync());
        Assert.Equal(new ConflictSides("main", "other"), await lg2.GetConflictSidesAsync(RepositoryOperation.Rebase));

        await lg2.ResolveConflictAsync("file.txt", ConflictChoice.Theirs);
        await lg2.ContinueAsync(RepositoryOperation.Rebase);
        Assert.Equal(RepositoryOperation.None, await t.Repo.GetOperationAsync());
        Assert.Equal(await Git(t, "rev-parse", "main"), await Git(t, "rev-parse", "HEAD~1"));
        Assert.Equal("a\nB from other\nc\n", t.Read("file.txt").Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task RevertCherryPickResetAndTag()
    {
        using var t = await TwoBranchesAsync();
        var lg2 = Lg2(t);
        var otherTip = await Git(t, "rev-parse", "other");

        await lg2.CherryPickAsync(otherTip);
        Assert.Equal("on other", await Git(t, "log", "-1", "--format=%s"));
        Assert.Equal("a\nB from other\nc\n", t.Read("file.txt").Replace("\r\n", "\n"));

        var picked = (await lg2.GetLogAsync(0, 1))[0];
        await lg2.RevertAsync(picked);
        Assert.StartsWith("Revert \"on other\"", await Git(t, "log", "-1", "--format=%s"));
        Assert.Equal("a\nb\nc\n", t.Read("file.txt").Replace("\r\n", "\n"));

        await lg2.CreateTagAsync("v2", picked.Sha);
        Assert.Equal("tag", await Git(t, "cat-file", "-t", "v2"));   // annotated, as git tag -a
        Assert.Contains("v2", (await lg2.GetLogAsync(0, 5)).Single(c => c.Sha == picked.Sha).Tags);

        await lg2.ResetToAsync(picked.Sha);
        Assert.Equal(picked.Sha, await Git(t, "rev-parse", "HEAD"));
        Assert.Contains((await t.Repo.GetStatusAsync()).Files, f => f.Path == "file.txt");   // the revert's change, kept
    }

    [Fact]
    public async Task PatchesMatchGit()
    {
        using var t = await TwoBranchesAsync();
        await t.RunAsync("switch", "other");
        var head = (await t.Repo.GetLogAsync(0, 1))[0];
        var lg2Patch = await Lg2(t).GetCommitPatchAsync(head.Sha);
        Assert.StartsWith($"commit {head.Sha}\nAuthor: Test <test@example.com>\n", lg2Patch);
        Assert.Contains("    on other\n", lg2Patch);
        Assert.Contains("+B from other", lg2Patch);

        var file = (await t.Repo.GetCommitFilesAsync(head)).Single(f => f.Path == "file.txt");
        var cli = DiffParser.Parse("file.txt", await t.Repo.GetCommitFilePatchAsync(head.Sha, file), false, false);
        var lg2 = DiffParser.Parse("file.txt", await Lg2(t).GetCommitFilePatchAsync(head.Sha, file), false, false);
        Assert.Equal(cli.AllLines.Select(l => (l.Kind, l.Text)), lg2.AllLines.Select(l => (l.Kind, l.Text)));

        t.Write("file.txt", "changed\n");
        t.Write("new.txt", "new\n");
        var working = await Lg2(t).GetWorkingPatchAsync();
        Assert.Contains("+changed", working);
        Assert.Contains("+++ b/new.txt", working);
    }

    [Fact]
    public async Task ChangeRequestsAreCheckedOut()
    {
        var root = Path.Combine(Path.GetTempPath(), "gld-lg2cr-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var bare = Path.Combine(root, "remote.git");
            Directory.CreateDirectory(bare);
            await new GitRunner().RunAsync(bare, ["init", "--bare", "-b", "main"]);
            using var t = await TwoBranchesAsync();
            await t.RunAsync("remote", "add", "origin", bare);
            await t.RunAsync("push", "origin", "main", "other");
            await t.RunAsync("push", "origin", "other:refs/pull/7/head");   // as GitHub keeps a pull request's commits
            var otherTip = await Git(t, "rev-parse", "other");
            await t.RunAsync("branch", "-D", "other");

            OpenChangeRequest Request(string? fork) => new(HostingKind.GitHub, 7, "PR", "https://example.com", false, "alice",
                DateTimeOffset.UnixEpoch, "other", fork, null, null, otherTip);

            // From the project's own branch: a local branch tracking it
            Assert.Equal("other", await Lg2(t).CheckoutChangeRequestAsync("origin", Request(null), false));
            Assert.Equal(("other", "origin/other"), ((await t.Repo.GetStatusAsync()).Branch, (await t.Repo.GetStatusAsync()).Upstream));

            // From a fork that no longer exists: the request's own ref
            await t.RunAsync("switch", "main");
            Assert.Equal("pr/7", await Lg2(t).CheckoutChangeRequestAsync("origin", Request("alice/project"), false));
            Assert.Equal(otherTip, await Git(t, "rev-parse", "HEAD"));
            Assert.Equal("pr/7", (await t.Repo.GetStatusAsync()).Branch);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(root, true);
            }
        }
    }
}
