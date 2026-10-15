using CommitDesk.Core.Git;

namespace CommitDesk.Core.Tests;

public class PartialCommitTests
{
    static string Lines(int count, Func<int, string>? map = null, string eol = "\n")
        => string.Concat(Enumerable.Range(1, count).Select(i => (map?.Invoke(i) ?? $"line {i}") + eol));

    [Fact]
    public async Task CommitsOnlySecondHunk()
    {
        using var t = await TempRepo.CreateAsync();
        t.Write("a.txt", Lines(20));
        await t.CommitAllAsync("init");

        t.Write("a.txt", Lines(20, i => i is 2 or 15 ? $"changed {i}" : $"line {i}"));
        var diff = await t.DiffAsync("a.txt");
        Assert.Equal(2, diff.Hunks.Count);
        foreach (var l in diff.Hunks[1].Lines.Where(l => l.IsChange)) l.IsSelected = true;

        await t.CommitPartialAsync("a.txt", diff);

        Assert.Equal(Lines(20, i => i == 15 ? "changed 15" : $"line {i}"), await t.ShowHeadAsync("a.txt"));
        Assert.Equal(Lines(20, i => i is 2 or 15 ? $"changed {i}" : $"line {i}"), t.Read("a.txt"));
    }

    [Fact]
    public async Task CommitsOnlyFirstHunkWithLineCountChange()
    {
        using var t = await TempRepo.CreateAsync();
        t.Write("a.txt", Lines(20));
        await t.CommitAllAsync("init");

        // First hunk inserts two lines, second hunk deletes one.
        var lines = Enumerable.Range(1, 20).Select(i => $"line {i}").ToList();
        lines.RemoveAt(16);
        lines.InsertRange(2, ["new a", "new b"]);
        t.Write("a.txt", string.Concat(lines.Select(l => l + "\n")));

        var diff = await t.DiffAsync("a.txt");
        Assert.Equal(2, diff.Hunks.Count);
        diff.Hunks[0].Lines.Single(l => l.Text == "new b").IsSelected = true;

        await t.CommitPartialAsync("a.txt", diff);

        var expected = Enumerable.Range(1, 20).Select(i => $"line {i}").ToList();
        expected.Insert(2, "new b");
        Assert.Equal(string.Concat(expected.Select(l => l + "\n")), await t.ShowHeadAsync("a.txt"));
    }

    [Fact]
    public async Task PartialReplacementKeepsUnselectedRemovalAsContext()
    {
        using var t = await TempRepo.CreateAsync();
        t.Write("a.txt", Lines(5));
        await t.CommitAllAsync("init");

        t.Write("a.txt", Lines(5, i => i == 3 ? "three" : $"line {i}"));
        var diff = await t.DiffAsync("a.txt");
        // Select only the addition: line 3 stays, "three" is inserted after it.
        diff.ChangeLines.Single(l => l.Kind == DiffLineKind.Add).IsSelected = true;

        await t.CommitPartialAsync("a.txt", diff);

        Assert.Equal("line 1\nline 2\nline 3\nthree\nline 4\nline 5\n", await t.ShowHeadAsync("a.txt"));
    }

    [Fact]
    public async Task PartialUntrackedFile()
    {
        using var t = await TempRepo.CreateAsync();
        t.Write("keep.txt", "x\n");
        await t.CommitAllAsync("init");

        t.Write("new.txt", Lines(5));
        var diff = await t.DiffAsync("new.txt");
        Assert.True(diff.IsNewFile);
        foreach (var l in diff.ChangeLines.Where(l => l.NewLineNumber is 1 or 3)) l.IsSelected = true;

        await t.CommitPartialAsync("new.txt", diff);

        Assert.Equal("line 1\nline 3\n", await t.ShowHeadAsync("new.txt"));
    }

    [Fact]
    public async Task PartialDeletedFile()
    {
        using var t = await TempRepo.CreateAsync();
        t.Write("gone.txt", Lines(5));
        await t.CommitAllAsync("init");

        File.Delete(Path.Combine(t.Dir, "gone.txt"));
        var diff = await t.DiffAsync("gone.txt");
        Assert.True(diff.IsDeletedFile);
        foreach (var l in diff.ChangeLines.Where(l => l.OldLineNumber is 2 or 4)) l.IsSelected = true;

        await t.CommitPartialAsync("gone.txt", diff);

        Assert.Equal("line 1\nline 3\nline 5\n", await t.ShowHeadAsync("gone.txt"));
    }

    [Fact]
    public async Task PartialCrlfFile()
    {
        using var t = await TempRepo.CreateAsync();
        t.Write("crlf.txt", Lines(20, eol: "\r\n"));
        await t.CommitAllAsync("init");

        t.Write("crlf.txt", Lines(20, i => i is 2 or 15 ? $"changed {i}" : $"line {i}", "\r\n"));
        var diff = await t.DiffAsync("crlf.txt");
        foreach (var l in diff.Hunks[0].Lines.Where(l => l.IsChange)) l.IsSelected = true;
        Assert.DoesNotContain(diff.AllLines, l => l.Text.EndsWith('\r'));

        await t.CommitPartialAsync("crlf.txt", diff);

        Assert.Equal(Lines(20, i => i == 2 ? "changed 2" : $"line {i}", "\r\n"), await t.ShowHeadAsync("crlf.txt"));
    }

    [Fact]
    public async Task MissingNewlineAtEndOfFile()
    {
        using var t = await TempRepo.CreateAsync();
        t.Write("a.txt", "one\ntwo");
        await t.CommitAllAsync("init");

        t.Write("a.txt", "one\ntwo\nthree");
        var diff = await t.DiffAsync("a.txt");
        diff.SelectAll(true);
        await t.CommitPartialAsync("a.txt", diff);

        Assert.Equal("one\ntwo\nthree", await t.ShowHeadAsync("a.txt"));
    }

    [Fact]
    public async Task CommitExcludesUnselectedFilesAndPreviouslyStagedChanges()
    {
        using var t = await TempRepo.CreateAsync();
        t.Write("a.txt", "a\n");
        t.Write("b.txt", "b\n");
        await t.CommitAllAsync("init");

        t.Write("a.txt", "a2\n");
        t.Write("b.txt", "b2\n");
        await t.RunAsync("add", "b.txt");   // staged outside the app; should not be committed

        var status = await t.Repo.GetStatusAsync();
        var a = status.Files.Single(f => f.Path == "a.txt");
        await t.Repo.CommitAsync("only a", [new CommitFileSelection(a, null)], amend: false, headIsUnborn: false);

        Assert.Equal("a2\n", await t.ShowHeadAsync("a.txt"));
        Assert.Equal("b\n", await t.ShowHeadAsync("b.txt"));
        Assert.Equal("b2\n", t.Read("b.txt"));
    }

    [Fact]
    public async Task FirstCommitOnUnbornBranch()
    {
        using var t = await TempRepo.CreateAsync();
        t.Write("a.txt", "a\n");
        t.Write("b.txt", "b\n");

        var status = await t.Repo.GetStatusAsync();
        Assert.True(status.IsUnborn);
        Assert.Equal("main", status.Branch);
        var a = status.Files.Single(f => f.Path == "a.txt");
        await t.Repo.CommitAsync("first", [new CommitFileSelection(a, null)], amend: false, headIsUnborn: true);

        var log = await t.Repo.GetLogAsync(0, 10);
        Assert.Single(log);
        Assert.Equal("first", log[0].Summary);
        var files = await t.Repo.GetCommitFilesAsync(log[0]);
        Assert.Equal("a.txt", Assert.Single(files).Path);
    }

    [Fact]
    public async Task LogParsesBodiesTagsAndMerges()
    {
        using var t = await TempRepo.CreateAsync();
        t.Write("a.txt", "a\n");
        await t.CommitAllAsync("init");
        await t.RunAsync("switch", "-c", "feature");
        t.Write("b.txt", "b\n");
        await t.RunAsync("add", "-A");
        await t.RunAsync("commit", "-m", "feature work", "-m", "body line 1\nbody line 2");
        await t.RunAsync("switch", "main");
        t.Write("c.txt", "c\n");
        await t.CommitAllAsync("main work");
        await t.Repo.MergeAsync("feature");
        await t.Repo.CreateTagAsync("v1.0", "HEAD");

        var log = await t.Repo.GetLogAsync(0, 10);
        Assert.Equal(4, log.Count);
        Assert.True(log[0].IsMerge);
        Assert.Contains("v1.0", log[0].Tags);
        var feature = log.Single(c => c.Summary == "feature work");
        Assert.Equal("body line 1\nbody line 2", feature.Body);

        var mergeFiles = await t.Repo.GetCommitFilesAsync(log[0]);
        Assert.Equal("b.txt", Assert.Single(mergeFiles).Path);

        var branches = await t.Repo.GetBranchesAsync();
        Assert.Contains(branches, b => b.Name == "main" && b.IsCurrent);
        Assert.Contains(branches, b => b.Name == "feature" && !b.IsCurrent);
    }

    [Fact]
    public async Task StatusReportsRenameAndUntracked()
    {
        using var t = await TempRepo.CreateAsync();
        t.Write("old name.txt", Lines(10));
        await t.CommitAllAsync("init");
        await t.RunAsync("mv", "old name.txt", "new name.txt");
        t.Write("ünïcode.txt", "x\n");

        var status = await t.Repo.GetStatusAsync();
        var rename = status.Files.Single(f => f.Kind == FileChangeKind.Renamed);
        Assert.Equal("new name.txt", rename.Path);
        Assert.Equal("old name.txt", rename.OldPath);
        Assert.Contains(status.Files, f => f.Path == "ünïcode.txt" && f.Kind == FileChangeKind.Untracked);

        await t.Repo.DiscardAsync(status.Files);
        Assert.Empty((await t.Repo.GetStatusAsync()).Files);
        Assert.True(File.Exists(Path.Combine(t.Dir, "old name.txt")));
    }
}
