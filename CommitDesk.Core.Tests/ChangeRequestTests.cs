using System.Net;
using System.Text;
using CommitDesk.Core.Git;
using CommitDesk.Core.GitHub;
using CommitDesk.Core.GitLab;
using CommitDesk.Core.Hosting;

namespace CommitDesk.Core.Tests;

public class ChangeRequestTests
{
    sealed class FakeHandler(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(respond(request), Encoding.UTF8, "application/json"),
            });
    }

    static OpenChangeRequest Request(HostingKind kind, long number, string branch, string? fork = null, string? forkUrl = null)
        => new(kind, number, "Title", "https://example.com", false, "alice", DateTimeOffset.UnixEpoch, branch, fork, forkUrl, null, null);

    [Fact]
    public async Task GitHubPullsFromTheProjectAndFromForks()
    {
        const string json = """
        [
          { "number": 12, "title": "Own branch", "html_url": "https://github.com/o/p/pull/12", "draft": true,
            "created_at": "2026-09-01T10:00:00Z", "user": { "login": "bob" },
            "head": { "ref": "feature", "sha": "abc", "repo": { "full_name": "o/p", "clone_url": "https://github.com/o/p.git", "ssh_url": "git@github.com:o/p.git" } },
            "base": { "ref": "main", "sha": "def", "repo": { "full_name": "o/p" } } },
          { "number": 13, "title": "From a fork", "html_url": "https://github.com/o/p/pull/13", "draft": false,
            "created_at": "2026-09-02T10:00:00Z", "user": { "login": "alice" },
            "head": { "ref": "fix", "sha": "123", "repo": { "full_name": "alice/p", "clone_url": "https://github.com/alice/p.git", "ssh_url": "git@github.com:alice/p.git" } },
            "base": { "ref": "main", "sha": "def", "repo": { "full_name": "o/p" } } },
          { "number": 14, "title": "Fork deleted", "html_url": "https://github.com/o/p/pull/14", "draft": false,
            "created_at": "2026-09-03T10:00:00Z", "user": { "login": "carol" },
            "head": { "ref": "gone", "sha": "456", "repo": null }, "base": { "ref": "main", "sha": "def", "repo": { "full_name": "o/p" } } }
        ]
        """;
        string? url = null;
        var gh = new GitHubClient(new HttpClient(new FakeHandler(r => { url = r.RequestUri!.ToString(); return json; })));
        gh.Configure("token");

        var list = await gh.ListOpenChangeRequestsAsync("o/p");

        Assert.StartsWith("https://api.github.com/repos/o/p/pulls?state=open", url);
        Assert.Equal(new[] { 12L, 13, 14 }, list.Select(r => r.Number));
        Assert.Equal(("#12", true, "bob", "feature", false), (list[0].Reference, list[0].Draft, list[0].Author, list[0].SourceBranch, list[0].FromFork));
        Assert.Equal(("alice/p", "https://github.com/alice/p.git", "pr/13", "123"),
            (list[1].SourceRepository, list[1].SourceHttpUrl, list[1].ForkBranchName, list[1].HeadSha));
        Assert.True(list[2].FromFork);
        Assert.Null(list[2].SourceHttpUrl);
    }

    [Fact]
    public async Task GitLabMergeRequestsLookUpForkProjects()
    {
        var gl = new GitLabClient(new HttpClient(new FakeHandler(r => r.RequestUri!.AbsolutePath switch
        {
            var p when p.EndsWith("/merge_requests") => """
                [
                  { "iid": 5, "title": "Own branch", "web_url": "https://gitlab.example.com/g/p/-/merge_requests/5", "draft": false,
                    "source_branch": "feature", "source_project_id": 1, "target_project_id": 1, "sha": "abc",
                    "created_at": "2026-09-01T10:00:00Z", "author": { "username": "bob" } },
                  { "iid": 6, "title": "From a fork", "web_url": "https://gitlab.example.com/g/p/-/merge_requests/6", "draft": true,
                    "source_branch": "fix", "source_project_id": 2, "target_project_id": 1, "sha": "123",
                    "created_at": "2026-09-02T10:00:00Z", "author": { "username": "alice" } }
                ]
                """,
            var p when p.EndsWith("/projects/2") => """
                { "id": 2, "path_with_namespace": "alice/p", "http_url_to_repo": "https://gitlab.example.com/alice/p.git",
                  "ssh_url_to_repo": "git@gitlab.example.com:alice/p.git", "web_url": "https://gitlab.example.com/alice/p" }
                """,
            var p => throw new InvalidOperationException(p),
        })));
        gl.Configure("https://gitlab.example.com", "token");

        var list = await gl.ListOpenChangeRequestsAsync("g/p");

        Assert.Equal(("!5", false, "bob"), (list[0].Reference, list[0].FromFork, list[0].Author));
        Assert.Equal(("alice/p", "git@gitlab.example.com:alice/p.git", "mr/6", true),
            (list[1].SourceRepository, list[1].SourceSshUrl, list[1].ForkBranchName, list[1].Draft));
    }

    [Theory]
    [InlineData("open", null, ChangeRequestState.Open)]
    [InlineData("closed", "2026-09-05T10:00:00Z", ChangeRequestState.Merged)]
    [InlineData("closed", null, ChangeRequestState.Closed)]
    public async Task GitHubLatestPullInAnyState(string state, string? mergedAt, ChangeRequestState expected)
    {
        var json = $$"""
            [ { "number": 21, "title": "Done", "html_url": "https://github.com/o/p/pull/21", "draft": false, "state": "{{state}}",
                "merged_at": {{(mergedAt is null ? "null" : $"\"{mergedAt}\"")}} } ]
            """;
        string? url = null;
        var gh = new GitHubClient(new HttpClient(new FakeHandler(r => { url = r.RequestUri!.ToString(); return json; })));
        gh.Configure("token");

        var cr = await gh.FindLatestChangeRequestAsync("o/p", "feature/x");

        Assert.Contains("state=all", url);
        Assert.Contains("head=o%3Afeature%2Fx", url);
        Assert.Equal((21L, "https://github.com/o/p/pull/21", expected), (cr!.Number, cr.WebUrl, cr.State));
    }

    [Theory]
    [InlineData("opened", ChangeRequestState.Open)]
    [InlineData("merged", ChangeRequestState.Merged)]
    [InlineData("closed", ChangeRequestState.Closed)]
    [InlineData("locked", ChangeRequestState.Open)]
    public async Task GitLabLatestMergeRequestInAnyState(string state, ChangeRequestState expected)
    {
        string? url = null;
        var gl = new GitLabClient(new HttpClient(new FakeHandler(r =>
        {
            url = r.RequestUri!.ToString();
            return $$"""[ { "iid": 7, "title": "Done", "web_url": "https://gitlab.example.com/g/p/-/merge_requests/7", "state": "{{state}}" } ]""";
        })));
        gl.Configure("https://gitlab.example.com", "token");

        var cr = await gl.FindLatestChangeRequestAsync("g/p", "feature");

        Assert.Contains("state=all", url);
        Assert.Contains("source_branch=feature", url);
        Assert.Equal(("!7", expected), (cr!.Reference, cr.State));
    }

    [Fact]
    public async Task NoRequestFromTheBranch()
    {
        var gh = new GitHubClient(new HttpClient(new FakeHandler(_ => "[]")));
        gh.Configure("token");
        Assert.Null(await gh.FindLatestChangeRequestAsync("o/p", "feature"));
    }

    [Theory]
    [InlineData("alice/project", "fork-alice")]
    [InlineData("group/sub group/project", "fork-group-sub-group")]
    public void ForkRemoteNames(string repository, string expected) => Assert.Equal(expected, GitRepository.ForkRemoteName(repository));

    /// <summary>An "upstream" bare repo with a feature branch, a "fork" with a fix branch, and a clone of upstream.</summary>
    sealed class Repos : IDisposable
    {
        public string Upstream { get; } = Temp("up");
        public string Fork { get; } = Temp("fork");
        public TempRepo Clone { get; private set; } = null!;

        static string Temp(string kind)
        {
            var d = Path.Combine(Path.GetTempPath(), $"gld-{kind}-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(d);
            return d;
        }

        public static async Task<Repos> CreateAsync()
        {
            var r = new Repos();
            var git = new GitRunner();
            await git.RunAsync(r.Upstream, ["init", "--bare", "-b", "main"]);
            await git.RunAsync(r.Fork, ["init", "--bare", "-b", "main"]);

            var work = await TempRepo.CreateAsync();
            work.Write("a.txt", "base\n");
            await work.CommitAllAsync("base");
            await work.RunAsync("remote", "add", "origin", r.Upstream);
            await work.RunAsync("remote", "add", "fork", r.Fork);
            await work.RunAsync("push", "origin", "main");
            await work.RunAsync("switch", "-c", "feature");
            work.Write("a.txt", "feature\n");
            await work.CommitAllAsync("feature");
            await work.RunAsync("push", "origin", "feature");
            await work.RunAsync("switch", "-c", "fix", "main");
            work.Write("a.txt", "fix\n");
            await work.CommitAllAsync("fix from the fork");
            await work.RunAsync("push", "fork", "fix");
            // What GitHub keeps on the project for every pull request, even when its fork is deleted
            await work.RunAsync("push", "origin", "fix:refs/pull/9/head");
            work.Dispose();

            r.Clone = await TempRepo.CreateAsync();
            await r.Clone.RunAsync("remote", "add", "origin", r.Upstream);
            await r.Clone.RunAsync("fetch", "origin");
            await r.Clone.RunAsync("reset", "--hard", "origin/main");
            return r;
        }

        public void Dispose()
        {
            Clone?.Dispose();
            foreach (var d in new[] { Upstream, Fork })
            {
                try
                {
                    foreach (var f in Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                    Directory.Delete(d, true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    [Fact]
    public async Task ChecksOutARequestFromTheProject()
    {
        using var r = await Repos.CreateAsync();
        var branch = await r.Clone.Repo.CheckoutChangeRequestAsync("origin", Request(HostingKind.GitHub, 12, "feature"), preferSsh: false);

        Assert.Equal("feature", branch);
        var status = await r.Clone.Repo.GetStatusAsync();
        Assert.Equal(("feature", "origin/feature"), (status.Branch, status.Upstream));
        Assert.Equal("feature\n", r.Clone.Read("a.txt").Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task ChecksOutARequestFromAForkThroughAForkRemote()
    {
        using var r = await Repos.CreateAsync();
        var request = Request(HostingKind.GitHub, 13, "fix", "alice/project", r.Fork);

        Assert.Equal("pr/13", await r.Clone.Repo.CheckoutChangeRequestAsync("origin", request, preferSsh: false));
        var status = await r.Clone.Repo.GetStatusAsync();
        Assert.Equal(("pr/13", "fork-alice/fix"), (status.Branch, status.Upstream));
        Assert.Equal("fix\n", r.Clone.Read("a.txt").Replace("\r\n", "\n"));

        // Again, from another branch: switches back to the same local branch
        await r.Clone.RunAsync("switch", "main");
        Assert.Equal("pr/13", await r.Clone.Repo.CheckoutChangeRequestAsync("origin", request, preferSsh: false));
        Assert.Equal("pr/13", (await r.Clone.Repo.GetStatusAsync()).Branch);
    }

    [Fact]
    public async Task ChecksOutARequestWhoseForkIsGoneFromTheProjectsRequestRef()
    {
        using var r = await Repos.CreateAsync();
        var request = Request(HostingKind.GitHub, 9, "fix", "a deleted fork");

        Assert.Equal("pr/9", await r.Clone.Repo.CheckoutChangeRequestAsync("origin", request, preferSsh: false));
        Assert.Equal("fix\n", r.Clone.Read("a.txt").Replace("\r\n", "\n"));
    }
}

public class ChangeRequestForBranchTests
{
    static OpenChangeRequest R(long n, string branch, string? fork = null, string? sha = null)
        => new(HostingKind.GitHub, n, $"#{n}", $"https://github.com/o/p/pull/{n}", false, "a", DateTimeOffset.UnixEpoch,
            branch, fork, null, null, sha);

    [Fact]
    public void PrefersTheRequestWhoseLatestCommitIsTheBranchTip()
    {
        // Two forks with a same-named branch; the tip decides
        var list = new[] { R(1, "fix", "alice/p", "aaa"), R(2, "fix", "bob/p", "bbb"), R(3, "other", null, "bbb") };
        Assert.Equal(2, OpenChangeRequest.ForBranch(list, "fix", ["bbb"])!.Number);
    }

    [Fact]
    public void ElseTheProjectsOwnBranchElseTheOnlyOne()
    {
        Assert.Equal(5, OpenChangeRequest.ForBranch([R(4, "feature", "alice/p"), R(5, "feature")], "feature", ["zzz"])!.Number);
        Assert.Equal(6, OpenChangeRequest.ForBranch([R(6, "feature", "alice/p")], "feature", [null])!.Number);
        Assert.Null(OpenChangeRequest.ForBranch([R(7, "feature", "alice/p"), R(8, "feature", "bob/p")], "feature", ["zzz"]));
        Assert.Null(OpenChangeRequest.ForBranch([R(9, "main")], "feature", ["zzz"]));
    }

    [Fact]
    public void ListLinksForABranch()
    {
        Assert.Equal("https://github.com/o/p/pulls?q=is%3Apr%20is%3Aopen%20head%3Afeature%2Fterrain-3d",
            HostedRemote.Parse("https://github.com/o/p.git")!.ChangeRequestsForBranchLink("feature/terrain-3d"));
        Assert.Equal("https://gitlab.com/g/p/-/merge_requests?state=opened&source_branch=feature%2Fx",
            HostedRemote.Parse("https://gitlab.com/g/p.git")!.ChangeRequestsForBranchLink("feature/x"));
    }
}
