using GitLabDesktop.Core.Git;

namespace GitLabDesktop.Core.Tests;

[Collection(nameof(GlobalGitConfig))]
public class CertificateTests
{
    [Theory]
    [InlineData("fatal: unable to access 'https://git.example.edu/a/b.git/': SSL certificate problem: self-signed certificate in certificate chain")]
    [InlineData("fatal: unable to access 'https://x/': SSL certificate problem: unable to get local issuer certificate")]
    [InlineData("fatal: unable to access 'https://x/': schannel: SEC_E_UNTRUSTED_ROOT (0x80090325) - The certificate chain was issued by an authority that is not trusted.")]
    public void RecognisesCertificateFailures(string message) => Assert.True(GitCertificates.IsCertificateProblem(message));

    [Theory]
    [InlineData("fatal: Authentication failed for 'https://x/'")]
    [InlineData(null)]
    public void OtherFailuresAreNotCertificateProblems(string? message) => Assert.False(GitCertificates.IsCertificateProblem(message));

    [Theory]
    [InlineData("git.example.edu", "https://git.example.edu/")]
    [InlineData("https://git.example.edu/group/project.git", "https://git.example.edu/")]
    [InlineData("https://user@git.example.edu:8443/x", "https://git.example.edu:8443/")]
    [InlineData("http://git.example.edu", "http://git.example.edu/")]
    [InlineData("git@git.example.edu:group/project.git", null)]
    [InlineData("  ", null)]
    public void ServerKeyCoversTheWholeServer(string input, string? expected) => Assert.Equal(expected, GitCertificates.ServerKey(input));

    [Fact]
    public Task WindowsCertificateStoreOverridesTheSystemConfig()
        => SubmoduleTests.WithTemporaryConfigAsync("[http]\n\tsslBackend = openssl\n", async global =>
        {
            // Git for Windows' installer writes its choice of backend to the system config
            var git = new GitRunner();
            Assert.False(await GitCertificates.UsesWindowsCertificateStoreAsync(git));

            await GitCertificates.SetUseWindowsCertificateStoreAsync(git, true);
            Assert.True(await GitCertificates.UsesWindowsCertificateStoreAsync(git));

            await GitCertificates.SetUseWindowsCertificateStoreAsync(git, false);
            Assert.False(await GitCertificates.UsesWindowsCertificateStoreAsync(git));
            Assert.DoesNotContain("sslBackend", File.ReadAllText(global));   // the system's openssl applies again
        });

    [Fact]
    public Task TurningOffTheStoreWhenTheSystemChoseItWritesOpenSsl()
        => SubmoduleTests.WithTemporaryConfigAsync("[http]\n\tsslBackend = schannel\n", async global =>
        {
            var git = new GitRunner();
            Assert.True(await GitCertificates.UsesWindowsCertificateStoreAsync(git));
            await GitCertificates.SetUseWindowsCertificateStoreAsync(git, false);
            Assert.False(await GitCertificates.UsesWindowsCertificateStoreAsync(git));
            Assert.Contains("sslBackend = openssl", File.ReadAllText(global));
        });

    [Fact]
    public Task UntrustedServersRoundTrip() => SubmoduleTests.WithTemporaryConfigAsync("", async global =>
    {
        var git = new GitRunner();
        Assert.Empty(await GitCertificates.GetUntrustedServersAsync(git));

        await GitCertificates.AddUntrustedServerAsync(git, "https://Git.Example.edu/group/project.git");
        await GitCertificates.AddUntrustedServerAsync(git, "other.example.com:8443");
        var servers = await GitCertificates.GetUntrustedServersAsync(git);
        Assert.Equal(["https://git.example.edu/", "https://other.example.com:8443/"], servers);
        Assert.Contains("[http \"https://git.example.edu/\"]", File.ReadAllText(global));

        // Applies to that server only
        var r = await git.RunAsync(Path.GetTempPath(), ["config", "--get-urlmatch", "http.sslVerify", "https://git.example.edu/a/b.git"]);
        Assert.Equal("false", r.StdOut.Trim());
        r = await git.RunAsync(Path.GetTempPath(), ["config", "--get-urlmatch", "http.sslVerify", "https://github.com/a/b.git"], throwOnError: false);
        Assert.NotEqual("false", r.StdOut.Trim());

        await GitCertificates.RemoveUntrustedServerAsync(git, servers[0]);
        Assert.Equal(["https://other.example.com:8443/"], await GitCertificates.GetUntrustedServersAsync(git));
    });
}
