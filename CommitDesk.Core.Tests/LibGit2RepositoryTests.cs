using CommitDesk.Core.Git;

namespace CommitDesk.Core.Tests;

/// <summary>
/// The libgit2 versions of the repository operations (GitRunner.UseLibGit2, as on Android) against the git command line
/// on the same repositories: they must report and do the same things.
/// </summary>
public class LibGit2RepositoryTests
{
    /// <summary>The same repository through libgit2.</summary>
    static GitRepository Lg2(TempRepo t) => new(new GitRunner { UseLibGit2 = true }, t.Dir);

    static string Describe(IEnumerable<FileChange> files)
        => string.Join(", ", files.OrderBy(f => f.Path).Select(f => $"{f.Kind}:{f.Path}{(f.OldPath is null ? "" : "<-" + f.OldPath)}"));

    static async Task<TempRepo> RepoWithHistoryAsync()
    {
        var t = await TempRepo.CreateAsync();
        t.Write("keep.txt", "one\ntwo\nthree\n");
        t.Write("edit.txt", "a\nb\nc\n");
        t.Write("gone.txt", "bye\n");
        t.Write("move.txt", "moving\nalong\nnicely\n");
        await t.CommitAllAsync("first");
        await t.RunAsync("tag", "-a", "v1", "-m", "v1");
        t.Write("keep.txt", "one\ntwo\nthree\nfour\n");
        await t.RunAsync("commit", "-am", "second\n\nWith a body\nof two lines");
        return t;
    }

    [Fact]
    public async Task StatusMatchesGit()
    {
        using var t = await RepoWithHistoryAsync();
        t.Write("edit.txt", "a\nB\nc\n");                          // modified
        File.Delete(Path.Combine(t.Dir, "gone.txt"));               // deleted
        t.Write("new.txt", "new\n");                                // untracked
        t.Write("staged.txt", "staged\n");
        await t.RunAsync("add", "staged.txt");                      // added
        await t.RunAsync("mv", "move.txt", "moved.txt");            // renamed

        var cli = await t.Repo.GetStatusAsync();
        var lg2 = await Lg2(t).GetStatusAsync();

        Assert.Equal(Describe(cli.Files), Describe(lg2.Files));
        Assert.Equal((cli.Branch, cli.HeadSha, cli.Upstream), (lg2.Branch, lg2.HeadSha, lg2.Upstream));
    }

    [Fact]
    public async Task DiffsMatchGit()
    {
        using var t = await RepoWithHistoryAsync();
        t.Write("edit.txt", "a\nB\nc\nd\n");
        t.Write("new.txt", "first\nsecond\n");
        var status = await t.Repo.GetStatusAsync();
        foreach (var change in status.Files)
        {
            var cli = await t.Repo.GetWorkingDiffAsync(change, false, false);
            var lg2 = await Lg2(t).GetWorkingDiffAsync(change, false, false);
            Assert.Equal(cli.AllLines.Select(l => (l.Kind, l.Text)), lg2.AllLines.Select(l => (l.Kind, l.Text)));
        }

        var head = (await t.Repo.GetLogAsync(0, 1))[0];
        var cliFiles = await t.Repo.GetCommitFilesAsync(head);
        Assert.Equal(Describe(cliFiles), Describe(await Lg2(t).GetCommitFilesAsync(head)));
        var cliDiff = await t.Repo.GetCommitDiffAsync(head, cliFiles[0]);
        var lg2Diff = await Lg2(t).GetCommitDiffAsync(head, cliFiles[0]);
        Assert.Equal(cliDiff.AllLines.Select(l => (l.Kind, l.Text)), lg2Diff.AllLines.Select(l => (l.Kind, l.Text)));
    }

    [Fact]
    public async Task LogAndBranchesMatchGit()
    {
        using var t = await RepoWithHistoryAsync();
        await t.RunAsync("branch", "feature");
        var cliLog = await t.Repo.GetLogAsync(0, 10);
        var lg2Log = await Lg2(t).GetLogAsync(0, 10);
        Assert.Equal(cliLog.Select(c => (c.Sha, string.Join(",", c.Parents), c.AuthorName, c.Summary, c.Body, string.Join(",", c.Tags))),
            lg2Log.Select(c => (c.Sha, string.Join(",", c.Parents), c.AuthorName, c.Summary, c.Body, string.Join(",", c.Tags))));
        Assert.Equal("With a body\nof two lines", lg2Log[0].Body);
        Assert.Equal(cliLog.Skip(1).Select(c => c.Sha), (await Lg2(t).GetLogAsync(1, 10)).Select(c => c.Sha));

        var cliBranches = await t.Repo.GetBranchesAsync();
        var lg2Branches = await Lg2(t).GetBranchesAsync();
        Assert.Equal(cliBranches.Select(b => (b.FullName, b.Name, b.Sha, b.IsCurrent)).OrderBy(b => b.FullName),
            lg2Branches.Select(b => (b.FullName, b.Name, b.Sha, b.IsCurrent)).OrderBy(b => b.FullName));
    }

    [Fact]
    public async Task CommitsTheSelectedFilesOnly()
    {
        using var t = await RepoWithHistoryAsync();
        t.Write("edit.txt", "a\nB\nc\n");
        t.Write("new.txt", "new\n");
        t.Write("keep.txt", "changed but not committed\n");
        var status = await Lg2(t).GetStatusAsync();
        var picked = status.Files.Where(f => f.Path is "edit.txt" or "new.txt").Select(f => new CommitFileSelection(f, null)).ToList();

        await Lg2(t).CommitAsync("third", picked, amend: false, headIsUnborn: false);

        Assert.Equal("third", (await t.RunAsync("log", "-1", "--format=%s")).StdOut.Trim());
        Assert.Equal("edit.txt\nnew.txt", (await t.RunAsync("show", "--name-only", "--format=", "HEAD")).StdOut.Trim().Replace("\r", ""));
        Assert.Equal("Modified:keep.txt", Describe((await t.Repo.GetStatusAsync()).Files));

        await Lg2(t).CommitAsync("third, amended", [], amend: true, headIsUnborn: false);
        Assert.Equal("third, amended", (await t.RunAsync("log", "-1", "--format=%s")).StdOut.Trim());
        Assert.Equal("third, amended", await Lg2(t).GetHeadMessageAsync());
    }

    [Fact]
    public async Task SelectedLinesAreNotSupportedYet()
    {
        using var t = await RepoWithHistoryAsync();
        t.Write("edit.txt", "a\nB\nc\nD\n");
        var change = (await t.Repo.GetStatusAsync()).Files.Single();
        var diff = await t.Repo.GetWorkingDiffAsync(change, false, true);
        var ex = await Assert.ThrowsAsync<GitException>(() => Lg2(t).CommitAsync("x", [new CommitFileSelection(change, diff)], false, false));
        Assert.Contains("selected lines", ex.Message);
    }

    [Fact]
    public async Task DiscardPutsFilesBack()
    {
        using var t = await RepoWithHistoryAsync();
        t.Write("edit.txt", "a\nB\nc\n");
        t.Write("new.txt", "new\n");
        File.Delete(Path.Combine(t.Dir, "gone.txt"));
        await t.RunAsync("mv", "move.txt", "moved.txt");

        await Lg2(t).DiscardAsync((await Lg2(t).GetStatusAsync()).Files);

        Assert.Empty((await t.Repo.GetStatusAsync()).Files);
        Assert.Equal("a\nb\nc\n", t.Read("edit.txt").Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task BranchesCanBeCreatedSwitchedRenamedAndDeleted()
    {
        using var t = await RepoWithHistoryAsync();
        var lg2 = Lg2(t);
        await lg2.CreateBranchAsync("topic");
        Assert.Equal("topic", (await t.Repo.GetStatusAsync()).Branch);

        var main = (await lg2.GetBranchesAsync()).Single(b => b.Name == "main");
        await lg2.CheckoutAsync(main);
        Assert.Equal("main", (await t.Repo.GetStatusAsync()).Branch);

        await lg2.RenameBranchAsync("topic", "topic2");
        await lg2.DeleteBranchAsync("topic2");
        Assert.DoesNotContain(await t.Repo.GetBranchesAsync(), b => b.Name.StartsWith("topic"));
    }

    [Fact]
    public async Task SwitchingWithChangesInTheWayFailsLikeGit()
    {
        using var t = await RepoWithHistoryAsync();
        await t.RunAsync("switch", "-c", "other");
        t.Write("edit.txt", "on other\n");
        await t.RunAsync("commit", "-am", "other");
        await t.RunAsync("switch", "main");
        t.Write("edit.txt", "local change\n");

        var other = (await Lg2(t).GetBranchesAsync()).Single(b => b.Name == "other");
        var ex = await Assert.ThrowsAsync<GitException>(() => Lg2(t).CheckoutAsync(other));
        Assert.True(GitErrors.IsBlockedByLocalChanges(ex.Message), ex.Message);
    }

    [Fact]
    public async Task StashAndPop()
    {
        using var t = await RepoWithHistoryAsync();
        t.Write("edit.txt", "stashed\n");
        t.Write("new.txt", "untracked too\n");
        var lg2 = Lg2(t);
        await lg2.StashAsync("my stash");
        Assert.Empty((await t.Repo.GetStatusAsync()).Files);
        Assert.Contains("my stash", (await lg2.GetStashesAsync()).Single().Message);

        await lg2.PopStashAsync("stash@{0}");
        Assert.Equal("Modified:edit.txt, Untracked:new.txt", Describe((await t.Repo.GetStatusAsync()).Files));
        Assert.Empty(await lg2.GetStashesAsync());
    }

    [Fact]
    public async Task PushFetchAndPullThroughARemote()
    {
        var root = Path.Combine(Path.GetTempPath(), "gld-lg2remote-" + Guid.NewGuid().ToString("N")[..8]);
        var bare = Path.Combine(root, "remote.git");
        try
        {
            var git = new GitRunner();
            Directory.CreateDirectory(bare);
            await git.RunAsync(bare, ["init", "--bare", "-b", "main"]);

            using var a = await RepoWithHistoryAsync();
            await a.RunAsync("remote", "add", "origin", bare);
            var lgA = Lg2(a);
            await lgA.PushAsync("origin", "main", setUpstream: true);
            Assert.Equal("origin/main", (await a.Repo.GetStatusAsync()).Upstream);

            // A second copy, which pushes a new commit
            using var b = await TempRepo.CreateAsync();
            await b.RunAsync("remote", "add", "origin", bare);
            await Lg2(b).FetchAsync();
            var remoteMain = (await Lg2(b).GetBranchesAsync()).Single(x => x.Name == "origin/main");
            await Lg2(b).CheckoutAsync(remoteMain);
            b.Write("from-b.txt", "b\n");
            var change = (await Lg2(b).GetStatusAsync()).Files.Single();
            await Lg2(b).CommitAsync("from b", [new CommitFileSelection(change, null)], false, false);
            await Lg2(b).PushAsync("origin", "main", setUpstream: false);

            // A is now behind: its push is rejected the way the app recognises, and a pull brings it up to date
            a.Write("keep.txt", "conflict-free change in a\n");
            await a.RunAsync("commit", "-am", "from a");
            var rejected = await Assert.ThrowsAsync<GitException>(() => lgA.PushAsync("origin", "main", false));
            Assert.True(GitErrors.IsPushRejectedAsBehind(rejected.Message), rejected.Message);

            await lgA.PullAsync();
            Assert.True(File.Exists(Path.Combine(a.Dir, "from-b.txt")));
            var status = await a.Repo.GetStatusAsync();
            Assert.Equal((2, 0), (status.Ahead, status.Behind));   // a's commit and the merge of b's
            await lgA.PushAsync("origin", "main", false);
            Assert.Equal(0, (await a.Repo.GetStatusAsync()).Ahead);
        }
        finally
        {
            DeleteTree(root);
        }
    }

    [Fact]
    public async Task OperationsNotPortedYetSaySo()
    {
        using var t = await RepoWithHistoryAsync();
        var ex = await Assert.ThrowsAsync<GitException>(() => Lg2(t).RebaseAsync("main"));
        Assert.Equal("This isn't available on this device yet (git rebase).", ex.Message);
    }

    [Fact]
    public async Task FindRootInitAndConfig()
    {
        using var t = await RepoWithHistoryAsync();
        var lg2Runner = new GitRunner { UseLibGit2 = true };
        Directory.CreateDirectory(Path.Combine(t.Dir, "sub"));
        Assert.Equal(Path.GetFullPath(t.Dir).TrimEnd('\\'), await GitRepository.FindRootAsync(lg2Runner, Path.Combine(t.Dir, "sub")));

        await Lg2(t).SetLocalConfigAsync("user.name", "Someone Else");
        Assert.Equal("Someone Else", (await t.RunAsync("config", "--local", "--get", "user.name")).StdOut.Trim());
        Assert.Equal("Someone Else", await Lg2(t).GetLocalConfigAsync("user.name"));

        var dir = Path.Combine(Path.GetTempPath(), "gld-lg2init-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            await GitRepository.InitAsync(lg2Runner, dir);
            Assert.Equal("main", (await new GitRepository(lg2Runner, dir).GetStatusAsync()).Branch);
            Assert.True((await new GitRepository(lg2Runner, dir).GetStatusAsync()).IsUnborn);
        }
        finally
        {
            DeleteTree(dir);
        }
    }

    static void DeleteTree(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(dir, true);
    }
}
