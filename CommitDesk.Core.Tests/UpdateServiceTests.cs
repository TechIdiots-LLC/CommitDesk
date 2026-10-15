using CommitDesk.Core.Updates;

namespace CommitDesk.Core.Tests;

public class UpdateServiceTests
{
    static SemanticVersion V(string s) => SemanticVersion.TryParse(s, out var v) ? v : throw new ArgumentException(s);

    [Theory]
    [InlineData("0.1.0", "0.1.1")]
    [InlineData("0.1.1", "0.2.0")]
    [InlineData("0.2.0-rc.1", "0.2.0")]
    [InlineData("0.2.0-rc.1", "0.2.0-rc.2")]
    [InlineData("0.2.0-rc.2", "0.2.0-rc.10")]
    [InlineData("v0.9", "v1.0.0")]
    public void VersionOrdering(string lower, string higher) => Assert.True(V(lower).CompareTo(V(higher)) < 0);

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1")]
    [InlineData("1.2.3-")]
    public void RejectsBadVersions(string s) => Assert.False(SemanticVersion.TryParse(s, out _));

    static ReleaseInfo Release(string v) => new(V(v), "v" + v, "", "", []);

    [Fact]
    public void SelectsNewestStableUnlessPrereleasesAllowed()
    {
        var releases = new[] { Release("0.1.0"), Release("0.1.1"), Release("0.2.0-rc.1") };
        Assert.Equal("0.1.1", UpdateService.SelectUpdate(releases, V("0.1.0"), includePrereleases: false)!.Version.ToString());
        Assert.Equal("0.2.0-rc.1", UpdateService.SelectUpdate(releases, V("0.1.0"), includePrereleases: true)!.Version.ToString());
        Assert.Null(UpdateService.SelectUpdate(releases, V("0.1.1"), includePrereleases: false));
        // A pre-release user is offered newer pre-releases without opting in.
        Assert.NotNull(UpdateService.SelectUpdate([Release("0.2.0-rc.2")], V("0.2.0-rc.1"), includePrereleases: false));
    }

    [Fact]
    public void ParsesGitLabReleases()
    {
        const string json = """
            [{"tag_name":"v0.1.0","description":"Notes","upcoming_release":false,
              "assets":{"links":[{"name":"CommitDesk-v0.1.0-win-x64-setup.exe","url":"https://x/u","direct_asset_url":"https://x/d"}]},
              "_links":{"self":"https://x/-/releases/v0.1.0"}},
             {"tag_name":"nightly","assets":{"links":[]}}]
            """;
        var r = Assert.Single(UpdateService.ParseGitLabReleases(json));
        Assert.Equal("0.1.0", r.Version.ToString());
        Assert.Equal("https://x/d", r.FindAsset("-win-x64-setup.exe")!.Url);
        Assert.Equal("https://x/-/releases/v0.1.0", r.PageUrl);
    }

    [Fact]
    public void ParsesGitHubReleasesSkippingDrafts()
    {
        const string json = """
            [{"tag_name":"v0.1.1","body":"B","html_url":"https://gh/r","draft":false,
              "assets":[{"name":"CommitDesk-v0.1.1-macos.dmg","browser_download_url":"https://gh/dmg"}]},
             {"tag_name":"v0.2.0","draft":true,"assets":[]}]
            """;
        var r = Assert.Single(UpdateService.ParseGitHubReleases(json));
        Assert.Equal("https://gh/dmg", r.FindAsset("-macos.dmg")!.Url);
    }

    /// <summary>Against the real release feeds; run with GLD_LIVE_UPDATE_TEST=1.</summary>
    [Fact]
    public async Task LiveFeedsFindPublishedRelease()
    {
        if (Environment.GetEnvironmentVariable("GLD_LIVE_UPDATE_TEST") != "1") return;
        var service = new UpdateService(new HttpClient(), "CommitDesk", "0.0.1",
            "https://gitlab.techidiots.net", "techidiots-llc/commitdesk", "TechIdiots-LLC/CommitDesk");
        var result = await service.CheckAsync(includePrereleases: false, requiredAssetSuffix: "-win-x64-setup.exe");
        Assert.True(result.IsUpdateAvailable);
        Assert.NotNull(result.Update!.FindAsset("-win-x64-setup.exe"));
        Console.WriteLine($"{result.Source}: v{result.Update.Version} {result.Update.FindAsset("-win-x64-setup.exe")!.Url}");
    }
}
