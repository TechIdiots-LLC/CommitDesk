using CommitDesk.Core.Git;

namespace CommitDesk.Core.Tests;

/// <summary>Undo commit / redo commit, with the git command line and with libgit2 (Android).</summary>
public class UndoCommitTests
{
    public static TheoryData<bool> Engines => new() { false, true };

    static GitRepository RepoFor(TempRepo t, bool libGit2) => libGit2 ? new(new GitRunner { UseLibGit2 = true }, t.Dir) : t.Repo;

    static async Task<string> Git(TempRepo t, params string[] args) => (await t.RunAsync(args)).StdOut.Trim().Replace("\r", "");

    static async Task<CommitInfo> HeadAsync(TempRepo t) => (await t.Repo.GetLogAsync(0, 1))[0];

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task UndoKeepsTheChangesAndRedoPutsTheCommitBack(bool libGit2)
    {
        using var t = await TempRepo.CreateAsync();
        var repo = RepoFor(t, libGit2);
        t.Write("file.txt", "one\n");
        await t.CommitAllAsync("first");
        var first = await Git(t, "rev-parse", "HEAD");
        t.Write("file.txt", "one\ntwo\n");
        t.Write("new.txt", "new\n");
        await t.CommitAllAsync("second");
        var second = await HeadAsync(t);

        await repo.UndoCommitAsync(second);
        Assert.Equal(first, await Git(t, "rev-parse", "HEAD"));
        Assert.Equal("one\ntwo\n", t.Read("file.txt").Replace("\r\n", "\n"));
        var files = (await t.Repo.GetStatusAsync()).Files.ToDictionary(f => f.Path, f => f.Kind);
        Assert.Equal(FileChangeKind.Modified, files["file.txt"]);
        Assert.Equal(FileChangeKind.Untracked, files["new.txt"]);   // unstaged again, as GitHub Desktop leaves it
        Assert.Equal("", await Git(t, "diff", "--cached", "--name-only"));

        // An edit made after undoing is kept when the commit is put back
        t.Write("file.txt", "one\ntwo\nthree\n");
        await repo.RedoCommitAsync(second);
        Assert.Equal(second.Sha, await Git(t, "rev-parse", "HEAD"));
        Assert.Equal("file.txt", string.Join(",", (await t.Repo.GetStatusAsync()).Files.Select(f => f.Path)));
        Assert.Equal("one\ntwo\nthree\n", t.Read("file.txt").Replace("\r\n", "\n"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task UndoingAMergeLeavesTheMergedChanges(bool libGit2)
    {
        using var t = await TempRepo.CreateAsync();
        var repo = RepoFor(t, libGit2);
        t.Write("file.txt", "base\n");
        await t.CommitAllAsync("base");
        await t.RunAsync("switch", "-c", "other");
        t.Write("other.txt", "from other\n");
        await t.CommitAllAsync("on other");
        await t.RunAsync("switch", "main");
        t.Write("main.txt", "from main\n");
        await t.CommitAllAsync("on main");
        var mainTip = await Git(t, "rev-parse", "HEAD");
        await t.RunAsync("merge", "--no-ff", "--no-edit", "other");
        var merge = await HeadAsync(t);
        Assert.True(merge.IsMerge);

        await repo.UndoCommitAsync(merge);
        Assert.Equal(mainTip, await Git(t, "rev-parse", "HEAD"));
        Assert.Equal(RepositoryOperation.None, await t.Repo.GetOperationAsync());
        Assert.Contains((await t.Repo.GetStatusAsync()).Files, f => f.Path == "other.txt" && f.Kind == FileChangeKind.Untracked);

        await repo.RedoCommitAsync(merge);
        Assert.Equal(merge.Sha, await Git(t, "rev-parse", "HEAD"));
        Assert.Empty((await t.Repo.GetStatusAsync()).Files);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task UndoingTheFirstCommitLeavesAnEmptyBranch(bool libGit2)
    {
        using var t = await TempRepo.CreateAsync();
        var repo = RepoFor(t, libGit2);
        t.Write("file.txt", "one\n");
        await t.CommitAllAsync("first");
        var first = await HeadAsync(t);

        await repo.UndoCommitAsync(first);
        var status = await t.Repo.GetStatusAsync();
        Assert.True(status.IsUnborn);
        Assert.Equal("main", status.Branch);
        Assert.Equal("file.txt", string.Join(",", status.Files.Select(f => f.Path)));
        Assert.Equal("one\n", t.Read("file.txt").Replace("\r\n", "\n"));

        await repo.RedoCommitAsync(first);
        status = await t.Repo.GetStatusAsync();
        Assert.Equal(first.Sha, status.HeadSha);
        Assert.Equal("main", status.Branch);
        Assert.Empty(status.Files);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task OnlyTheLatestCommitCanBeUndone(bool libGit2)
    {
        using var t = await TempRepo.CreateAsync();
        var repo = RepoFor(t, libGit2);
        t.Write("file.txt", "one\n");
        await t.CommitAllAsync("first");
        var first = await HeadAsync(t);
        t.Write("file.txt", "two\n");
        await t.CommitAllAsync("second");
        var head = await Git(t, "rev-parse", "HEAD");

        await Assert.ThrowsAsync<GitException>(() => repo.UndoCommitAsync(first));
        Assert.Equal(head, await Git(t, "rev-parse", "HEAD"));
        Assert.Empty((await t.Repo.GetStatusAsync()).Files);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task RedoIsRefusedOnceTheBranchHasMoved(bool libGit2)
    {
        using var t = await TempRepo.CreateAsync();
        var repo = RepoFor(t, libGit2);
        t.Write("file.txt", "one\n");
        await t.CommitAllAsync("first");
        t.Write("file.txt", "two\n");
        await t.CommitAllAsync("second");
        var second = await HeadAsync(t);
        await repo.UndoCommitAsync(second);
        await t.CommitAllAsync("made instead");
        var replacement = await Git(t, "rev-parse", "HEAD");

        await Assert.ThrowsAsync<GitException>(() => repo.RedoCommitAsync(second));
        Assert.Equal(replacement, await Git(t, "rev-parse", "HEAD"));
    }
}
