using CommitDesk.Core.Git;

namespace CommitDesk.Core.Tests;

public class RepositorySettingsTests
{
    [Fact]
    public async Task GitIgnoreKeepsTheFilesLineEndings()
    {
        using var t = await TempRepo.CreateAsync();
        Assert.Equal("", await t.Repo.ReadGitIgnoreAsync());

        // A text box on Windows hands back \r line breaks; a new file gets \n
        await t.Repo.WriteGitIgnoreAsync("bin/\robj/");
        Assert.Equal("bin/\nobj/\n", t.Read(".gitignore"));

        // An existing CRLF file stays CRLF
        File.WriteAllText(Path.Combine(t.Dir, ".gitignore"), "bin/\r\n");
        await t.Repo.WriteGitIgnoreAsync("bin/\r\nnode_modules/\r\n\r\n");
        Assert.Equal("bin/\r\nnode_modules/\r\n", t.Read(".gitignore"));

        // Cleared: an empty file, not a deleted one
        await t.Repo.WriteGitIgnoreAsync("");
        Assert.Equal("", t.Read(".gitignore"));
    }

    [Fact]
    public async Task NoGitIgnoreIsCreatedForEmptyText()
    {
        using var t = await TempRepo.CreateAsync();
        await t.Repo.WriteGitIgnoreAsync("  \r");
        Assert.False(File.Exists(Path.Combine(t.Dir, ".gitignore")));
    }

    [Fact]
    public async Task LocalConfigIsSetAndRemoved()
    {
        using var t = await TempRepo.CreateAsync();   // sets a local user.name of "Test"
        Assert.Equal("Test", await t.Repo.GetLocalConfigAsync("user.name"));

        await t.Repo.SetLocalConfigAsync("user.name", "  Alex Example ");
        Assert.Equal("Alex Example", await t.Repo.GetLocalConfigAsync("user.name"));

        await t.Repo.SetLocalConfigAsync("user.name", "");
        Assert.Null(await t.Repo.GetLocalConfigAsync("user.name"));
        await t.Repo.SetLocalConfigAsync("user.name", null);   // already gone: no error
    }
}
