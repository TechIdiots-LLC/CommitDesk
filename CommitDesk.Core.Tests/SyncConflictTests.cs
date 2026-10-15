using CommitDesk.Core.Git;

namespace CommitDesk.Core.Tests;

/// <summary>Pushing and pulling when someone else pushed first: a bare "server" and two clones of it.</summary>
public class SyncConflictTests
{
    sealed class Server : IDisposable
    {
        public string Dir { get; } = Path.Combine(Path.GetTempPath(), "gld-remote-" + Guid.NewGuid().ToString("N")[..8]);
        public TempRepo Mine { get; private set; } = null!;
        public TempRepo Theirs { get; private set; } = null!;

        public static async Task<Server> CreateAsync()
        {
            var s = new Server();
            Directory.CreateDirectory(s.Dir);
            await new GitRunner().RunAsync(s.Dir, ["init", "--bare", "-b", "main"]);

            s.Mine = await TempRepo.CreateAsync();
            s.Mine.Write("readme.txt", "one\n");
            await s.Mine.CommitAllAsync("first");
            await s.Mine.RunAsync("remote", "add", "origin", s.Dir);
            await s.Mine.Repo.PushAsync("origin", "main", setUpstream: true);

            s.Theirs = await TempRepo.CreateAsync();
            await s.Theirs.RunAsync("remote", "add", "origin", s.Dir);
            await s.Theirs.RunAsync("fetch", "origin");
            await s.Theirs.RunAsync("reset", "--hard", "origin/main");
            await s.Theirs.RunAsync("branch", "--set-upstream-to=origin/main");
            return s;
        }

        /// <summary>The other clone commits a change to readme.txt and pushes it.</summary>
        public async Task TheyPushAsync(string readme = "one\ntheirs\n")
        {
            Theirs.Write("readme.txt", readme);
            await Theirs.CommitAllAsync("their change");
            await Theirs.Repo.PushAsync("origin", "main", setUpstream: false);
        }

        public void Dispose()
        {
            Mine?.Dispose();
            Theirs?.Dispose();
            try
            {
                foreach (var f in Directory.EnumerateFiles(Dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(Dir, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task PushBehindTheRemoteIsRecognisedAndFetchingShowsBothSides()
    {
        using var s = await Server.CreateAsync();
        await s.TheyPushAsync();
        s.Mine.Write("other.txt", "mine\n");
        await s.Mine.CommitAllAsync("my change");

        var ex = await Assert.ThrowsAsync<GitException>(() => s.Mine.Repo.PushAsync("origin", "main", setUpstream: false));
        Assert.True(GitErrors.IsPushRejectedAsBehind(ex.Message), ex.Message);
        Assert.False(GitAuth.IsAuthenticationFailure(ex.Message));

        await s.Mine.Repo.FetchAsync();
        var status = await s.Mine.Repo.GetStatusAsync();
        Assert.Equal((1, 1), (status.Ahead, status.Behind));
    }

    [Fact]
    public async Task PullBlockedByLocalChangesIsRecognisedAndPullingAroundThemKeepsThem()
    {
        using var s = await Server.CreateAsync();
        await s.TheyPushAsync();
        s.Mine.Write("readme.txt", "one\nlocal edit\n");
        await s.Mine.Repo.FetchAsync();

        var ex = await Assert.ThrowsAsync<GitException>(() => s.Mine.Repo.PullAsync());
        Assert.True(GitErrors.IsBlockedByLocalChanges(ex.Message), ex.Message);

        // A change that merges cleanly with theirs comes back as it was, on top of their commit.
        s.Mine.Write("readme.txt", "zero\none\n");
        Assert.True(await s.Mine.Repo.PullAroundLocalChangesAsync("test: pull"));
        Assert.Equal("zero\none\ntheirs\n", s.Mine.Read("readme.txt").Replace("\r\n", "\n"));
        Assert.Equal("one\ntheirs\n", (await s.Mine.ShowHeadAsync("readme.txt")).Replace("\r\n", "\n"));
        Assert.Empty(await s.Mine.Repo.GetStashesAsync());
    }

    [Fact]
    public async Task ChangesThatConflictWithThePullAreKeptInTheStash()
    {
        using var s = await Server.CreateAsync();
        await s.TheyPushAsync("theirs\n");
        s.Mine.Write("readme.txt", "mine\n");
        await s.Mine.Repo.FetchAsync();

        Assert.False(await s.Mine.Repo.PullAroundLocalChangesAsync("test: pull"));
        Assert.Equal("theirs\n", (await s.Mine.ShowHeadAsync("readme.txt")).Replace("\r\n", "\n"));
        Assert.Contains(await s.Mine.Repo.GetStashesAsync(), st => st.Message.Contains("test: pull"));
    }

    [Theory]
    [InlineData(" ! [remote rejected] main -> main (pre-receive hook declined)", false)]
    [InlineData(" ! [rejected]        main -> main (stale info)", true)]
    [InlineData(" ! [rejected]        main -> main (non-fast-forward)", true)]
    public void RejectionKinds(string output, bool behind) => Assert.Equal(behind, GitErrors.IsPushRejectedAsBehind(output));
}
