namespace CommitDesk.Core.Git;

/// <summary>Recognises git failures the app can offer a way out of, instead of showing git's message.</summary>
public static class GitErrors
{
    /// <summary>
    /// A push refused because the remote branch has commits the local one doesn't ("! [rejected] … (fetch first)" or
    /// "(non-fast-forward)"), or a force push whose lease is out of date ("(stale info)"). Fetching fixes all three.
    /// A "[remote rejected]" (server hooks, protected branches) is not this: fetching wouldn't help.
    /// </summary>
    public static bool IsPushRejectedAsBehind(string? gitOutput)
        => gitOutput is not null &&
           gitOutput.Contains("[rejected]", StringComparison.Ordinal) &&
           (gitOutput.Contains("fetch first", StringComparison.Ordinal) ||
            gitOutput.Contains("non-fast-forward", StringComparison.Ordinal) ||
            gitOutput.Contains("stale info", StringComparison.Ordinal));

    /// <summary>
    /// A merge, pull, rebase, cherry-pick, revert or stash restore that stopped because of conflicts, leaving files to
    /// resolve ("CONFLICT (content): Merge conflict in …").
    /// </summary>
    public static bool StoppedOnConflicts(string? gitOutput)
        => gitOutput is not null &&
           (gitOutput.Contains("CONFLICT (", StringComparison.Ordinal) ||
            gitOutput.Contains("Automatic merge failed", StringComparison.Ordinal) ||
            gitOutput.Contains("Resolve all conflicts manually", StringComparison.Ordinal));

    static readonly string[] LocalChangesMarkers =
    [
        // Tracked files the merge would change, or untracked files it would create
        "would be overwritten by merge",
        "would be overwritten by checkout",
        "commit your changes or stash them",
        "Please move or remove them before you merge",
        // pull.rebase
        "You have unstaged changes",
        "Your index contains uncommitted changes",
    ];

    /// <summary>A pull (or merge) that git refused to start because uncommitted changes are in the way.</summary>
    public static bool IsBlockedByLocalChanges(string? gitOutput)
        => gitOutput is not null && LocalChangesMarkers.Any(m => gitOutput.Contains(m, StringComparison.OrdinalIgnoreCase));
}
