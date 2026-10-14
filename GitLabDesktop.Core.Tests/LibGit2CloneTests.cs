using GitLabDesktop.Core.Git;

namespace GitLabDesktop.Core.Tests;

/// <summary>The libgit2 clone (used where there is no git command line), run here with LibGit2Sharp's Windows build.</summary>
public class LibGit2CloneTests
{
    [Fact]
    public async Task ClonesARepositoryWithGitStyleProgress()
    {
        using var source = await TempRepo.CreateAsync();
        source.Write("readme.md", "hello\n");
        Directory.CreateDirectory(Path.Combine(source.Dir, "src"));
        source.Write("src/app.txt", "app\n");
        await source.CommitAllAsync("first");
        var head = (await source.RunAsync("rev-parse", "HEAD")).StdOut.Trim();

        var target = Path.Combine(Path.GetTempPath(), "gld-lg2-" + Guid.NewGuid().ToString("N")[..8], "clone");
        var lines = new List<string>();
        try
        {
            await LibGit2Clone.CloneAsync(source.Dir, target, recurseSubmodules: false, config: null, progress: lines.Add);

            // Line endings follow core.autocrlf in the user's git config, as with git
            Assert.Equal("hello", File.ReadAllText(Path.Combine(target, "readme.md")).TrimEnd());
            Assert.True(File.Exists(Path.Combine(target, "src", "app.txt")));
            // The git command line reads it like any other clone
            var cloned = await new GitRunner().RunAsync(target, ["rev-parse", "HEAD"]);
            Assert.Equal(head, cloned.StdOut.Trim());
            Assert.StartsWith("Cloning into 'clone'", lines[0]);
            // Every progress line is one the clone dialog's parser understands
            Assert.All(lines, l => Assert.NotNull(GitProgress.Parse(l)));
            Assert.Contains(lines, l => l.StartsWith("Updating files: 100%", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTree(Path.GetDirectoryName(target)!);
        }
    }

    [Fact]
    public async Task AMissingRepositoryFailsLikeGit()
    {
        var target = Path.Combine(Path.GetTempPath(), "gld-lg2-" + Guid.NewGuid().ToString("N")[..8]);
        var missing = Path.Combine(Path.GetTempPath(), "gld-missing-" + Guid.NewGuid().ToString("N")[..8]);
        var ex = await Assert.ThrowsAsync<GitException>(() => LibGit2Clone.CloneAsync(missing, target, false, null));
        Assert.StartsWith("fatal: ", ex.Message);
        DeleteTree(target);
    }

    [Fact]
    public async Task CancellingStopsTheClone()
    {
        using var source = await TempRepo.CreateAsync();
        source.Write("a.txt", "a\n");
        await source.CommitAllAsync("first");
        var target = Path.Combine(Path.GetTempPath(), "gld-lg2-" + Guid.NewGuid().ToString("N")[..8]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LibGit2Clone.CloneAsync(source.Dir, target, false, null, cts.Token));
        DeleteTree(target);
    }

    [Theory]
    [InlineData("too many redirects or authentication replays")]
    [InlineData("unexpected http status code: 401")]
    public void RefusedSignInsAreRecognised(string libgit2Message)
        => Assert.True(GitAuth.IsAuthenticationFailure(LibGit2Clone.Describe(libgit2Message)));

    [Fact]
    public void UntrustedCertificatesAreRecognised()
        => Assert.True(GitCertificates.IsCertificateProblem(LibGit2Clone.Describe("the SSL certificate is invalid")));

    [Fact]
    public void ExtraHeadersPickTheMostSpecificSetting()
    {
        var config = GitAuth.ExtraHeaderConfig(
        [
            new GitCredential("https://git.example.com", false, "server-user", "s"),
            new GitCredential("https://git.example.com/group/project", true, "repo-user", "r"),
        ]);
        string? Header(string url) => GitAuth.ExtraHeadersFor(url, config).SingleOrDefault();

        var repoHeader = config.Single(c => c.Key.Contains("/group/project")).Value;
        var serverHeader = config.Single(c => !c.Key.Contains("/group/project")).Value;
        Assert.Equal(repoHeader, Header("https://git.example.com/group/project.git"));
        Assert.Equal(repoHeader, Header("https://git.example.com/group/project"));
        Assert.Equal(serverHeader, Header("https://git.example.com/group/other.git"));
        Assert.Equal(serverHeader, Header("https://someone@git.example.com/group/project2.git"));
        Assert.Null(Header("https://github.com/group/project.git"));
        Assert.Null(Header("https://git.example.com.evil.net/x.git"));
    }

    static void DeleteTree(string dir)
    {
        if (!Directory.Exists(dir)) return;
        // libgit2 marks pack files read-only
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(dir, true);
    }
}
