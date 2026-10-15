using CommitDesk.Core.Git;

namespace CommitDesk.Core.Tests;

public class ConflictTests
{
    /// <summary>main and feature both change a.txt from "base"; returns with main checked out.</summary>
    static async Task<TempRepo> DivergedAsync(string mainText = "main\n", string? featureText = "feature\n")
    {
        var t = await TempRepo.CreateAsync();
        t.Write("a.txt", "base\n");
        await t.CommitAllAsync("base");
        await t.RunAsync("switch", "-c", "feature");
        if (featureText is null) await t.RunAsync("rm", "--quiet", "a.txt");
        else t.Write("a.txt", featureText);
        await t.CommitAllAsync("feature change");
        await t.RunAsync("switch", "main");
        t.Write("a.txt", mainText);
        await t.CommitAllAsync("main change");
        return t;
    }

    static string Text(TempRepo t, string file) => t.Read(file).Replace("\r\n", "\n");

    [Fact]
    public async Task MergeConflictNamesBothSidesAndCountsMarkers()
    {
        using var t = await DivergedAsync();
        await Assert.ThrowsAsync<GitException>(() => t.Repo.MergeAsync("feature"));

        Assert.Equal(new ConflictSides("main", "feature"), await t.Repo.GetConflictSidesAsync(RepositoryOperation.Merge));
        var info = await t.Repo.GetConflictAsync("a.txt");
        Assert.Equal(new ConflictInfo(1, true, true, true), info);
        Assert.Contains((await t.Repo.GetStatusAsync()).Files, f => f.Path == "a.txt" && f.Kind == FileChangeKind.Conflicted);
    }

    [Theory]
    [InlineData(ConflictChoice.Ours, "main\n")]
    [InlineData(ConflictChoice.Theirs, "feature\n")]
    public async Task ChoosingASideResolvesTheFile(ConflictChoice choice, string expected)
    {
        using var t = await DivergedAsync();
        await Assert.ThrowsAsync<GitException>(() => t.Repo.MergeAsync("feature"));

        await t.Repo.ResolveConflictAsync("a.txt", choice);

        Assert.Equal(expected, Text(t, "a.txt"));
        Assert.DoesNotContain((await t.Repo.GetStatusAsync()).Files, f => f.Kind == FileChangeKind.Conflicted);
        await t.Repo.ContinueAsync(RepositoryOperation.Merge);
        Assert.Equal(RepositoryOperation.None, await t.Repo.GetOperationAsync());
    }

    [Fact]
    public async Task AnEditedFileIsMarkedResolvedAsIs()
    {
        using var t = await DivergedAsync();
        await Assert.ThrowsAsync<GitException>(() => t.Repo.MergeAsync("feature"));
        t.Write("a.txt", "both\n");
        Assert.Equal(0, (await t.Repo.GetConflictAsync("a.txt")).Markers);

        await t.Repo.ResolveConflictAsync("a.txt", ConflictChoice.AsIs);

        Assert.Equal("both\n", Text(t, "a.txt"));
        Assert.DoesNotContain((await t.Repo.GetStatusAsync()).Files, f => f.Kind == FileChangeKind.Conflicted);
    }

    [Fact]
    public async Task ModifyDeleteConflictCanKeepTheDeletion()
    {
        using var t = await DivergedAsync(featureText: null);   // feature deleted a.txt, main changed it
        await Assert.ThrowsAsync<GitException>(() => t.Repo.MergeAsync("feature"));
        var info = await t.Repo.GetConflictAsync("a.txt");
        Assert.True(info.HasOurs);
        Assert.False(info.HasTheirs);

        await t.Repo.ResolveConflictAsync("a.txt", ConflictChoice.Theirs);

        Assert.False(File.Exists(Path.Combine(t.Dir, "a.txt")));
        Assert.DoesNotContain((await t.Repo.GetStatusAsync()).Files, f => f.Kind == FileChangeKind.Conflicted);
    }

    [Fact]
    public async Task RebaseSwapsTheSides()
    {
        using var t = await DivergedAsync();
        await t.RunAsync("switch", "feature");
        await Assert.ThrowsAsync<GitException>(() => t.Repo.RebaseAsync("main"));

        // Replaying feature's commit onto main: "ours" is main, "theirs" is your own branch
        Assert.Equal(new ConflictSides("main", "feature"), await t.Repo.GetConflictSidesAsync(RepositoryOperation.Rebase));
        await t.Repo.ResolveConflictAsync("a.txt", ConflictChoice.Theirs);
        Assert.Equal("feature\n", Text(t, "a.txt"));
    }
}
