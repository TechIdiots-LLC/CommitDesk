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
