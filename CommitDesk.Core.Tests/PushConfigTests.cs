using CommitDesk.Core.Git;

namespace CommitDesk.Core.Tests;

/// <summary>Reading git's push settings (remote.pushDefault, branch.&lt;name&gt;.pushRemote), with git and with libgit2.</summary>
public class PushConfigTests
{
    public static TheoryData<bool> Engines => new() { false, true };

    static GitRepository RepoFor(TempRepo t, bool libGit2) => libGit2 ? new(new GitRunner { UseLibGit2 = true }, t.Dir) : t.Repo;

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task ReadsRemotesAndPushSettings(bool libGit2)
    {
        using var t = await TempRepo.CreateAsync();
        var repo = RepoFor(t, libGit2);

        var empty = await repo.GetPushConfigAsync("main");
        Assert.Empty(empty.Remotes);
        Assert.Null(empty.PushDefault);
        Assert.Null(empty.BranchPushRemote);

        await t.Repo.SetRemoteUrlAsync("origin", "https://github.com/maplibre/maplibre-native.git");
        await t.Repo.SetRemoteUrlAsync("MyFork", "https://github.com/acalcutt/maplibre-native.git");
        await t.Repo.SetLocalConfigAsync("remote.pushDefault", "MyFork");
        await t.Repo.SetLocalConfigAsync("branch.feature/Terrain-3d.pushRemote", "origin");

        var c = await repo.GetPushConfigAsync("feature/Terrain-3d");
        Assert.Equal(["MyFork", "origin"], c.Remotes.Order(StringComparer.Ordinal));   // names keep their case
        Assert.Equal("MyFork", c.PushDefault);
        Assert.Equal("origin", c.BranchPushRemote);

        Assert.Null((await repo.GetPushConfigAsync("other")).BranchPushRemote);
        Assert.Null((await repo.GetPushConfigAsync(null)).BranchPushRemote);
    }

    // origin is the project (maplibre/maplibre-native), "acalcutt" is your fork
    static PushConfig ForkOfOrigin(string? pushDefault = "acalcutt", string? own = null)
        => new(["origin", "acalcutt", "github-desktop-calibermaps"], pushDefault, own);

    [Fact]
    public void WithoutAForkBranchesGoWhereTheyCameFrom()
    {
        var c = ForkOfOrigin(pushDefault: null);
        Assert.Null(c.Fork);
        Assert.Equal("origin", c.PushRemote("feature/terrain-3d", "origin", hasUpstream: true, "main"));
        Assert.Equal("origin", c.PushRemote("new-work", "origin", hasUpstream: false, "main"));
        Assert.Equal("origin", c.BaseRemote("origin"));
    }

    [Fact]
    public void WithAForkProjectBranchesArePublishedToIt()
    {
        var c = ForkOfOrigin();
        Assert.Equal(("acalcutt", "origin"), (c.Fork, c.Parent));
        // A branch made from origin/feature/terrain-3d, and a brand new one
        Assert.Equal("acalcutt", c.PushRemote("feature/terrain-3d", "origin", hasUpstream: true, "main"));
        Assert.Equal("acalcutt", c.PushRemote("new-work", "origin", hasUpstream: false, "main"));
        // Once published there it stays, and the project is still origin
        Assert.Equal("acalcutt", c.PushRemote("feature/terrain-3d", "acalcutt", hasUpstream: true, "main"));
        Assert.Equal("origin", c.BaseRemote("acalcutt"));
        Assert.Equal("origin", c.BaseRemote("origin"));
    }

    [Fact]
    public void TheDefaultBranchAndOtherPeoplesBranchesKeepTheirRemote()
    {
        var c = ForkOfOrigin();
        Assert.Equal("origin", c.PushRemote("main", "origin", hasUpstream: true, "main"));
        Assert.Equal("github-desktop-calibermaps", c.PushRemote("pr-4558", "github-desktop-calibermaps", hasUpstream: true, "main"));
        Assert.Equal("github-desktop-calibermaps", c.BaseRemote("github-desktop-calibermaps"));
    }

    [Fact]
    public void ABranchsOwnChoiceWins()
    {
        Assert.Equal("origin", ForkOfOrigin(own: "origin").PushRemote("feature/terrain-3d", "origin", hasUpstream: true, "main"));
        Assert.Equal("acalcutt", ForkOfOrigin(pushDefault: null, own: "acalcutt").PushRemote("main", "origin", hasUpstream: true, "main"));
        // A choice or default naming a remote that was since removed is ignored
        Assert.Equal("origin", ForkOfOrigin(pushDefault: "gone", own: "gone too").PushRemote("x", "origin", hasUpstream: true, "main"));
    }

    [Fact]
    public void ForkAsOriginWithTheProjectAsUpstream()
    {
        var c = new PushConfig(["origin", "upstream"], "origin", null);
        Assert.Equal("upstream", c.Parent);
        // A branch checked out from upstream/feature is published to origin, your fork
        Assert.Equal("origin", c.PushRemote("feature", "upstream", hasUpstream: true, "main"));
        Assert.Equal("upstream", c.BaseRemote("origin"));
        Assert.Equal("upstream", c.BaseRemote("upstream"));
    }
}
