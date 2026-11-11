namespace CommitDesk.Core.Git;

public enum FileChangeKind { Modified, Added, Deleted, Renamed, Copied, Untracked, Conflicted, TypeChanged }

public sealed record FileChange(string Path, string? OldPath, FileChangeKind Kind)
{
    /// <summary>Single-letter badge shown next to the file.</summary>
    public string Badge => Kind switch
    {
        FileChangeKind.Added or FileChangeKind.Untracked => "+",
        FileChangeKind.Deleted => "−",
        FileChangeKind.Renamed => "→",
        FileChangeKind.Copied => "C",
        FileChangeKind.Conflicted => "!",
        _ => "•",
    };

    public string FileName => System.IO.Path.GetFileName(Path);

    public string? Directory
    {
        get
        {
            var dir = System.IO.Path.GetDirectoryName(Path)?.Replace('\\', '/');
            return string.IsNullOrEmpty(dir) ? null : dir + "/";
        }
    }
}

public sealed class RepositoryStatus
{
    public string? Branch { get; init; }              // null when HEAD is detached
    public string? HeadSha { get; init; }             // null on an unborn branch
    public string? Upstream { get; init; }
    public int Ahead { get; init; }
    public int Behind { get; init; }
    public IReadOnlyList<FileChange> Files { get; init; } = [];
    public bool IsDetached => Branch is null;
    public bool IsUnborn => HeadSha is null;
}

/// <summary>
/// Git's push settings for a branch: the repository's remotes, remote.pushDefault and the branch's
/// branch.&lt;name&gt;.pushRemote (null when unset).
/// </summary>
public sealed record PushConfig(IReadOnlyList<string> Remotes, string? PushDefault, string? BranchPushRemote)
{
    /// <summary>The repository's "Publish branches to" remote (usually your fork), when it names a remote that exists.</summary>
    public string? Fork => PushDefault is { } p && Remotes.Contains(p) ? p : null;

    /// <summary>With a fork set, the other remote that's the forked project: upstream, else origin.</summary>
    public string? Parent => Fork is { } fork ? new[] { "upstream", "origin" }.FirstOrDefault(r => r != fork && Remotes.Contains(r)) : null;

    /// <summary>
    /// The project's own remote, for links, merge/pull requests and updating from the default branch: the tracked
    /// remote, except on a branch that lives in the fork, where it's the forked project.
    /// </summary>
    public string BaseRemote(string trackedRemote) => Fork is { } fork && trackedRemote == fork ? Parent ?? trackedRemote : trackedRemote;

    /// <summary>
    /// Where a branch is published and pushed: its own choice (branch.&lt;name&gt;.pushRemote), else the fork for a branch
    /// that would otherwise go to the project (not the default branch, and not one tracking someone else's remote, such
    /// as a checked-out request from another fork), else the remote it tracks.
    /// </summary>
    /// <param name="trackedRemote">The remote of the branch's upstream; origin when it has none.</param>
    /// <param name="hasUpstream">Whether the branch tracks a remote branch at all.</param>
    public string PushRemote(string branch, string trackedRemote, bool hasUpstream, string defaultBranch)
    {
        if (BranchPushRemote is { } own && Remotes.Contains(own)) return own;
        if (Fork is { } fork && branch != defaultBranch && (!hasUpstream || trackedRemote == fork || trackedRemote == Parent))
            return fork;
        return trackedRemote;
    }
}

public sealed record BranchInfo(
    string FullName,
    string Name,
    bool IsRemote,
    string? Upstream,
    string Sha,
    DateTimeOffset? LastCommitDate,
    bool IsCurrent)
{
    /// <summary>"origin/feature/x" → "feature/x".</summary>
    public string NameWithoutRemote => IsRemote && Name.IndexOf('/') is var i and > 0 ? Name[(i + 1)..] : Name;
    public string? RemoteName => IsRemote && Name.IndexOf('/') is var i and > 0 ? Name[..i] : null;
}

public sealed record CommitInfo(
    string Sha,
    string ShortSha,
    IReadOnlyList<string> Parents,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthorDate,
    string Summary,
    string Body,
    IReadOnlyList<string> Tags)
{
    public bool IsMerge => Parents.Count > 1;
    public bool IsUnpushed { get; set; }
}

public sealed record StashEntry(string Name, string Message);

public enum RepositoryOperation { None, Merge, Rebase, CherryPick, Revert }
