using CommitDesk.Core.Git;

namespace CommitDesk.Core.Tests;

public sealed class RepositoryScannerTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "gld-scan-" + Guid.NewGuid().ToString("N")[..8]);

    string Make(string relative, bool repo = false, bool gitFile = false)
    {
        var dir = Path.GetFullPath(Path.Combine(_root, relative));
        Directory.CreateDirectory(dir);
        if (repo) Directory.CreateDirectory(Path.Combine(dir, ".git"));
        if (gitFile) File.WriteAllText(Path.Combine(dir, ".git"), "gitdir: ../elsewhere");
        return dir;
    }

    [Fact]
    public void FindsDirectAndGroupedRepositories()
    {
        var a = Make("alpha", repo: true);
        var b = Make("group/beta", repo: true);
        var w = Make("worktree", gitFile: true);
        Make("alpha/sub", repo: true);                 // nested in a repo: not listed
        Make("deep/one/two", repo: true);              // beyond depth 2
        Make("plain-folder");
        Make("node_modules/pkg", repo: true);
        Make(".hidden", repo: true);

        var found = RepositoryScanner.FindRepositories(_root);

        Assert.Equal(new[] { a, b, w }.OrderBy(p => p, StringComparer.OrdinalIgnoreCase), found);
    }

    [Fact]
    public void MissingFolderReturnsEmpty()
        => Assert.Empty(RepositoryScanner.FindRepositories(Path.Combine(_root, "nope")));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
