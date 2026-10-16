using CommitDesk.Core.Git;

namespace CommitDesk.Core.Tests;

public class WorktreeTests
{
    [Fact]
    public async Task FindsTheOtherWorktreeThatHasABranchCheckedOut()
    {
        using var t = await TempRepo.CreateAsync();
        t.Write("a.txt", "a\n");
        await t.CommitAllAsync("first");
        await t.RunAsync("branch", "topic");
        var other = t.Dir + "-wt";
        try
        {
            await t.RunAsync("worktree", "add", other, "topic");

            Assert.Equal(Path.GetFullPath(other), await t.Repo.GetOtherWorktreeOfBranchAsync("topic"));
            // This folder's own branch, and branches checked out nowhere, aren't in another worktree
            var current = (await t.Repo.GetStatusAsync()).Branch!;
            Assert.Null(await t.Repo.GetOtherWorktreeOfBranchAsync(current));
            Assert.Null(await t.Repo.GetOtherWorktreeOfBranchAsync("missing"));
        }
        finally
        {
            await t.RunAsync("worktree", "remove", "--force", other);
        }
    }
}
