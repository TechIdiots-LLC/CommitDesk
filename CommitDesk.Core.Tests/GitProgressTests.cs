using CommitDesk.Core.Git;

namespace CommitDesk.Core.Tests;

public class GitProgressTests
{
    [Theory]
    [InlineData("remote: Counting objects:  50% (5/10)", "Counting objects:  50% (5/10)", 0.04)]
    [InlineData("remote: Compressing objects: 100% (8/8), done.", "Compressing objects: 100% (8/8)", 0.10)]
    [InlineData("Receiving objects:  45% (450/1000), 1.20 MiB | 2.00 MiB/s", "Receiving objects:  45% (450/1000), 1.20 MiB | 2.00 MiB/s", 0.37)]
    [InlineData("Resolving deltas:  50% (2/4)", "Resolving deltas:  50% (2/4)", 0.80)]
    [InlineData("Updating files: 100% (120/120), done.", "Updating files: 100% (120/120)", 1.00)]
    public void ParsesPhasesIntoOneOverallBar(string line, string status, double fraction)
    {
        var p = GitProgress.Parse(line)!;
        Assert.Equal(status, p.Status);
        Assert.Equal(fraction, p.Fraction!.Value, 2);
    }

    [Fact]
    public void OtherLines()
    {
        Assert.Equal(("Cloning into 'project'…", 0.0), (GitProgress.Parse("Cloning into 'project'...")!.Status, GitProgress.Parse("Cloning into 'project'...")!.Fraction!.Value));
        Assert.Equal("Enumerating objects: 1234", GitProgress.Parse("remote: Enumerating objects: 1234, done.")!.Status);
        Assert.Null(GitProgress.Parse("Submodule 'lib' (https://example.com/lib.git) registered for path 'lib'")!.Fraction);
        Assert.Null(GitProgress.Parse("warning: redirecting to https://example.com/project.git/"));
        Assert.Null(GitProgress.Parse("   "));
    }

    [Fact]
    public async Task CloneReportsProgressAndKeepsOnlyFinalLines()
    {
        using var source = await TempRepo.CreateAsync();
        for (int i = 0; i < 20; i++) source.Write($"f{i}.txt", new string('x', 1000 + i));
        await source.CommitAllAsync("files");

        var target = Path.Combine(Path.GetTempPath(), "gld-progress-" + Guid.NewGuid().ToString("N")[..8], "copy");
        var lines = new List<string>();
        try
        {
            // file:// so git takes the regular transport and reports objects, like a clone from a server
            await GitRepository.CloneAsync(new GitRunner(), new Uri(source.Dir).AbsoluteUri, target, recurseSubmodules: false,
                progress: lines.Add);
            Assert.Contains(lines, l => l.StartsWith("Cloning into", StringComparison.Ordinal));
            Assert.Contains(lines, l => GitProgress.Parse(l)?.Fraction > 0.1);
            Assert.True(File.Exists(Path.Combine(target, "f19.txt")));
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(target)!, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
