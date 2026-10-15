using CommitDesk.Core.Git;

namespace CommitDesk.Core.Tests;

public class PatchTests
{
    [Fact]
    public async Task WorkingPatchCoversEditsStagedOrNotAndNewFiles()
    {
        using var t = await TempRepo.CreateAsync();
        t.Write("a.txt", "one\n");
        t.Write("b.txt", "bee\n");
        await t.CommitAllAsync("first");

        t.Write("a.txt", "one\ntwo\n");          // unstaged edit
        t.Write("b.txt", "bee\nbuzz\n");
        await t.RunAsync("add", "b.txt");        // staged edit
        t.Write("new.txt", "brand new\n");       // untracked

        var all = await t.Repo.GetWorkingPatchAsync();
        Assert.Contains("+++ b/a.txt", all);
        Assert.Contains("+two", all);
        Assert.Contains("+buzz", all);
        Assert.Contains("+++ b/new.txt", all);
        Assert.Contains("+brand new", all);

        // Just one file
        var status = await t.Repo.GetStatusAsync();
        var one = await t.Repo.GetWorkingPatchAsync(status.Files.Where(f => f.Path == "a.txt"));
        Assert.Contains("+two", one);
        Assert.DoesNotContain("buzz", one);
        Assert.DoesNotContain("new.txt", one);

        var onlyNew = await t.Repo.GetWorkingPatchAsync(status.Files.Where(f => f.Path == "new.txt"));
        Assert.Contains("+brand new", onlyNew);
        Assert.DoesNotContain("a.txt", onlyNew);
    }

    [Fact]
    public async Task CommitPatchHasMessageAndDiff()
    {
        using var t = await TempRepo.CreateAsync();
        t.Write("a.txt", "one\n");
        await t.CommitAllAsync("first");
        t.Write("a.txt", "one\ntwo\n");
        await t.CommitAllAsync("Add a second line");

        var head = (await t.Repo.GetLogAsync(0, 1))[0];
        var patch = await t.Repo.GetCommitPatchAsync(head.Sha);
        Assert.StartsWith($"commit {head.Sha}", patch);
        Assert.Contains("Add a second line", patch);
        Assert.Contains("+two", patch);
    }

    [Fact]
    public async Task CommitFilePatchHasOnlyThatFile()
    {
        using var t = await TempRepo.CreateAsync();
        t.Write("a.txt", "one\n");
        t.Write("b.txt", "bee\n");
        await t.CommitAllAsync("first");
        t.Write("a.txt", "one\ntwo\n");
        t.Write("b.txt", "bee\nbuzz\n");
        await t.CommitAllAsync("both");

        var head = (await t.Repo.GetLogAsync(0, 1))[0];
        var file = (await t.Repo.GetCommitFilesAsync(head)).Single(f => f.Path == "a.txt");
        var patch = await t.Repo.GetCommitFilePatchAsync(head.Sha, file);
        Assert.StartsWith("diff --git a/a.txt b/a.txt", patch);
        Assert.Contains("+two", patch);
        Assert.DoesNotContain("buzz", patch);
    }
}
