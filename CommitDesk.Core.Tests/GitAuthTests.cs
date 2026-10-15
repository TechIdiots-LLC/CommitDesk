using CommitDesk.Core.Git;

namespace CommitDesk.Core.Tests;

public class GitAuthTests
{
    [Theory]
    [InlineData("warning: missing OAuth configuration for gitlab.example.com\nfatal: Cannot prompt because user interactivity has been disabled.\nfatal: could not read Username for 'https://gitlab.example.com': terminal prompts disabled")]
    [InlineData("remote: HTTP Basic: Access denied. If a password was provided for Git authentication, the password was incorrect\nfatal: Authentication failed for 'https://gitlab.example.com/g/p.git/'")]
    [InlineData("remote: Permission to Org/Repo.git denied to someone.\nfatal: unable to access 'https://github.com/Org/Repo.git/': The requested URL returned error: 403")]
    [InlineData("remote: Invalid username or token. Password authentication is not supported for Git operations.")]
    public void RecognisesAuthenticationFailures(string output) => Assert.True(GitAuth.IsAuthenticationFailure(output));

    [Theory]
    [InlineData("error: failed to push some refs to 'https://example.com/g/p.git'\nhint: Updates were rejected because the remote contains work that you do not have locally.")]
    [InlineData("fatal: unable to access 'https://example.com/': Could not resolve host: example.com")]
    [InlineData("git@github.com: Permission denied (publickey).")]
    [InlineData(null)]
    public void IgnoresOtherFailures(string? output) => Assert.False(GitAuth.IsAuthenticationFailure(output));

    [Theory]
    [InlineData("https://gitlab.example.com/g/p.git", "https://gitlab.example.com")]
    [InlineData("https://acalcutt@github.com/Org/Repo.git", "https://github.com")]
    [InlineData("https://example.com:8443/gitlab/g/p.git", "https://example.com:8443")]
    [InlineData("http://intranet/g/p.git", "http://intranet")]
    [InlineData("git@github.com:Org/Repo.git", null)]
    [InlineData("ssh://git@example.com:2222/g/p.git", null)]
    public void HttpServer(string url, string? expected) => Assert.Equal(expected, GitAuth.HttpServer(url));

    [Fact]
    public void UserNameFromUrl()
    {
        Assert.Equal("acalcutt", GitAuth.UserName("https://acalcutt@github.com/Org/Repo.git"));
        Assert.Null(GitAuth.UserName("https://github.com/Org/Repo.git"));
    }

    [Theory]
    [InlineData("glpat-abc123", true)]
    [InlineData("github_pat_11ABC", true)]
    [InlineData("ghp_abc", true)]
    [InlineData("hunter2", false)]
    public void TokenShapes(string secret, bool token) => Assert.Equal(token, GitAuth.LooksLikeToken(secret));

    static string Basic(string user, string secret)
        => "Authorization: Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{user}:{secret}"));

    [Fact]
    public void ExtraHeaderConfigScopesEachSecretToItsServer()
    {
        var config = GitAuth.ExtraHeaderConfig(
        [
            new GitCredential("https://gitlab.example.com/gitlab", false, "oauth2", "glpat-a"),
            new GitCredential("https://github.com", false, "x-access-token", "ghp_b"),
        ]);
        Assert.Equal(
        [
            new("http.https://gitlab.example.com/.extraHeader", Basic("oauth2", "glpat-a")),
            new("http.https://github.com/.extraHeader", Basic("x-access-token", "ghp_b")),
        ], config);
    }

    [Fact]
    public void ExtraHeaderConfigPutsRepositoryCredentialsFirst()
    {
        var config = GitAuth.ExtraHeaderConfig(
        [
            new GitCredential("https://gitlab.example.com", false, "me", "server-secret"),
            new GitCredential("https://gitlab.example.com/g/p.git", true, "bot", "repo-secret"),
        ]);
        Assert.Equal("http.https://gitlab.example.com/g/p.git.extraHeader", config[0].Key);
        Assert.Equal(Basic("bot", "repo-secret"), config[0].Value);
        Assert.Equal("http.https://gitlab.example.com/.extraHeader", config[1].Key);
    }

    [Fact]
    public void ExtraHeaderConfigSkipsEmptySecretsAndKeepsTheLastPerServer()
    {
        var config = GitAuth.ExtraHeaderConfig(
        [
            new GitCredential("https://gitlab.example.com", false, "oauth2", ""),
            new GitCredential("https://GITLAB.example.com", false, "old", "1"),
            new GitCredential("https://gitlab.example.com", false, "new", "2"),
            new GitCredential("git@gitlab.example.com:g/p.git", false, "ssh", "3"),
        ]);
        var only = Assert.Single(config);
        Assert.Equal(Basic("new", "2"), only.Value);
    }
}
