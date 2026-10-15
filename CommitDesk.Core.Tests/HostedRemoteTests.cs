using System.Net;
using CommitDesk.Core.Hosting;

namespace CommitDesk.Core.Tests;

public class HostedRemoteTests
{
    static readonly KnownHost[] Known = [new(HostingKind.GitLab, "https://git.example.edu")];

    [Theory]
    [InlineData("https://git.example.edu/group/Project.git")]
    [InlineData("git@git.example.edu:group/Project.git")]
    [InlineData("ssh://git@git.example.edu:2222/group/Project.git")]
    [InlineData("https://user@git.example.edu/group/Project")]
    public void KnownHostDecidesKindForAnyUrlForm(string url)
    {
        var r = HostedRemote.Parse(url, Known)!;
        Assert.Equal(HostingKind.GitLab, r.Kind);
        Assert.Equal("git.example.edu", r.Host);
        Assert.Equal("group/Project", r.ProjectPath);
        Assert.Equal("https://git.example.edu/group/Project", r.WebUrl);
        Assert.True(r.IsOn("https://git.example.edu"));
    }

    [Fact]
    public void UnconfiguredHostWithoutHintIsUnknown()
    {
        var r = HostedRemote.Parse("https://git.example.edu/group/proj.git")!;
        Assert.Equal(HostingKind.Unknown, r.Kind);
        Assert.Equal("git.example.edu", r.ProviderName);
        Assert.Null(r.BranchLink("main"));
        Assert.Equal("https://git.example.edu/group/proj", r.ProjectLink);
    }

    [Theory]
    [InlineData("https://github.com/owner/repo.git", HostingKind.GitHub)]
    [InlineData("git@github.com:owner/repo.git", HostingKind.GitHub)]
    [InlineData("https://gitlab.example.com/g/p.git", HostingKind.GitLab)]
    [InlineData("https://github.example.com/o/r.git", HostingKind.GitHub)]
    public void HostnameHints(string url, HostingKind kind) => Assert.Equal(kind, HostedRemote.Parse(url)!.Kind);

    [Fact]
    public void KnownHostOverridesHostnameHint()
        => Assert.Equal(HostingKind.GitLab,
            HostedRemote.Parse("https://github.example.com/o/r.git", [new(HostingKind.GitLab, "https://github.example.com")])!.Kind);

    [Fact]
    public void InstanceWithRelativePath()
    {
        var r = HostedRemote.Parse("https://example.com/gitlab/group/sub/proj.git", [new(HostingKind.GitLab, "https://example.com/gitlab/")])!;
        Assert.Equal("group/sub/proj", r.ProjectPath);
        Assert.Equal("https://example.com/gitlab/group/sub/proj", r.WebUrl);
    }

    [Fact]
    public void GitLabLinks()
    {
        var r = HostedRemote.Parse("git@gitlab.example.com:g/p.git")!;
        Assert.Equal("merge request", r.ChangeRequestName);
        Assert.Equal("https://gitlab.example.com/g/p/-/tree/fix/a%20b", r.BranchLink("fix/a b"));
        Assert.Equal("https://gitlab.example.com/g/p/-/compare/main...fix/x", r.CompareLink("main", "fix/x"));
        Assert.Equal("https://gitlab.example.com/g/p/-/merge_requests", r.ChangeRequestsLink);
        Assert.Equal("https://gitlab.example.com/g/p/-/blob/abc/docs/read%20me.md", r.FileLink("abc", "docs/read me.md"));
        Assert.Equal("https://gitlab.example.com/g/p/-/merge_requests/new?merge_request%5Bsource_branch%5D=fix%2Fx&merge_request%5Btarget_branch%5D=main",
            r.NewChangeRequestLink("fix/x", "main"));
    }

    [Fact]
    public void GitHubLinks()
    {
        var r = HostedRemote.Parse("https://github.com/owner/repo.git")!;
        Assert.Equal("GitHub", r.ProviderName);
        Assert.Equal("pull request", r.ChangeRequestName);
        Assert.Equal("https://github.com/owner/repo/tree/fix/x", r.BranchLink("fix/x"));
        Assert.Equal("https://github.com/owner/repo/commit/abc", r.CommitLink("abc"));
        Assert.Equal("https://github.com/owner/repo/blob/abc/docs/maintainers.md", r.FileLink("abc", "docs/maintainers.md"));
        Assert.Equal("https://github.com/owner/repo/issues/new", r.NewIssueLink);
        Assert.Equal("https://github.com/owner/repo/pulls", r.ChangeRequestsLink);
        Assert.Equal("https://github.com/owner/repo/compare/main...fix/x?expand=1", r.NewChangeRequestLink("fix/x", "main"));
        Assert.Equal("https://github.com/owner/repo/actions?query=branch%3Afix%2Fx", r.CiLink("fix/x"));
    }

    [Fact]
    public void RejectsGarbage() => Assert.Null(HostedRemote.Parse("not a url"));
}

public class HostProbeTests
{
    sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(respond(request));
    }

    static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond) => new(new FakeHandler(respond));

    [Fact]
    public async Task DetectsGitLabFromUnauthorizedApiResponse()
    {
        var http = Client(req =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.Unauthorized);
            if (req.RequestUri!.AbsolutePath == "/api/v4/version") r.Headers.Add("X-Gitlab-Meta", "{\"version\":\"1\"}");
            return r;
        });
        Assert.Equal(HostingKind.GitLab, await HostProbe.DetectAsync(http, "https://git.example.edu"));
    }

    [Fact]
    public async Task DetectsGitHubEnterprise()
    {
        var http = Client(req =>
        {
            if (req.RequestUri!.AbsolutePath != "/api/v3/meta") return new HttpResponseMessage(HttpStatusCode.NotFound);
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Headers.Add("X-GitHub-Enterprise-Version", "3.12.0");
            return r;
        });
        Assert.Equal(HostingKind.GitHub, await HostProbe.DetectAsync(http, "https://ghe.example.com"));
    }

    [Fact]
    public async Task NotFoundWithGitHubHeaderIsNotGitHub()
    {
        // github.com's website sends X-GitHub-Request-Id even on 404 pages.
        var http = Client(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.NotFound);
            r.Headers.Add("X-GitHub-Request-Id", "x");
            return r;
        });
        Assert.Equal(HostingKind.Unknown, await HostProbe.DetectAsync(http, "https://example.com"));
    }

    [Fact]
    public async Task UnreachableHostIsUnknown()
    {
        var http = Client(_ => throw new HttpRequestException("no route"));
        Assert.Equal(HostingKind.Unknown, await HostProbe.DetectAsync(http, "https://offline.example.com"));
    }
}
