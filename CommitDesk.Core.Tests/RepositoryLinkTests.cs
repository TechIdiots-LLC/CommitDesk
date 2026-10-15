using CommitDesk.Core.Hosting;

namespace CommitDesk.Core.Tests;

public class RepositoryLinkTests
{
    [Fact]
    public void ParsesGitHubsOpenWithDesktopLink()
    {
        var link = RepositoryLink.Parse("x-github-client://openRepo/https://github.com/maplibre/maplibre-style-spec")!;
        Assert.Equal(new RepositoryLink("https://github.com/maplibre/maplibre-style-spec", null, null), link);
    }

    [Fact]
    public void ReadsBranchAndFileAndEncodedUrls()
    {
        var link = RepositoryLink.Parse("x-github-client://openRepo/https%3A%2F%2Fgithub.com%2Fo%2Fr?branch=feature%2Fx&filepath=docs%2Fread+me.md")!;
        Assert.Equal(("https://github.com/o/r", "feature/x", "docs/read me.md"), (link.CloneUrl, link.Branch, link.FilePath));
    }

    [Theory]
    [InlineData("github-windows://openRepo/https://github.com/o/r")]
    [InlineData("commitdesk://openRepo/https://gitlab.example.com/group/project")]
    [InlineData("gitlab-desktop://openRepo/https://gitlab.example.com/group/project")]   // the app's scheme before the rename
    [InlineData("\"X-GITHUB-CLIENT://openRepo/https://github.com/o/r/\"")]
    public void OtherFormsAreUnderstood(string text) => Assert.NotNull(RepositoryLink.Parse(text));

    [Theory]
    [InlineData(null)]
    [InlineData("C:\\Users\\me\\repo")]
    [InlineData("x-github-desktop-auth://oauth?code=1")]            // GitHub Desktop's sign-in callback: not ours
    [InlineData("x-github-client://openRepo/file:///C:/evil")]       // only web URLs
    [InlineData("https://github.com/o/r")]
    public void IgnoresAnythingElse(string? text) => Assert.Null(RepositoryLink.Parse(text));

    [Fact]
    public void SplitsCommandLines()
    {
        Assert.Equal(["x-github-client://openRepo/https://github.com/o/r"], RepositoryLink.SplitCommandLine("\"x-github-client://openRepo/https://github.com/o/r\""));
        Assert.Equal(["C:\\My Repos\\project", "--flag"], RepositoryLink.SplitCommandLine("  \"C:\\My Repos\\project\"  --flag "));
        Assert.Empty(RepositoryLink.SplitCommandLine(""));
    }
}
