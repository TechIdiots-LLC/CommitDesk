namespace CommitDesk.Core.GitLab;

// Subsets of the GitLab REST API v4 resources. Property names map with JsonNamingPolicy.SnakeCaseLower.

public sealed class GitLabUser
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string Name { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public string? WebUrl { get; set; }
}

public sealed class GitLabProject
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string NameWithNamespace { get; set; } = "";
    public string PathWithNamespace { get; set; } = "";
    public string? Description { get; set; }
    public string? DefaultBranch { get; set; }
    public string WebUrl { get; set; } = "";
    public string HttpUrlToRepo { get; set; } = "";
    public string SshUrlToRepo { get; set; } = "";
    public string? Visibility { get; set; }
    public DateTimeOffset? LastActivityAt { get; set; }
}

public sealed class GitLabMergeRequest
{
    public long Id { get; set; }
    public long Iid { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string State { get; set; } = "";
    public string SourceBranch { get; set; } = "";
    public string TargetBranch { get; set; } = "";
    public string WebUrl { get; set; } = "";
    public bool Draft { get; set; }
    public string? DetailedMergeStatus { get; set; }
    public GitLabUser? Author { get; set; }
    public long SourceProjectId { get; set; }
    public long TargetProjectId { get; set; }
    public string? Sha { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class GitLabPipeline
{
    public long Id { get; set; }
    public long? Iid { get; set; }
    public string Status { get; set; } = "";
    public string Ref { get; set; } = "";
    public string Sha { get; set; } = "";
    public string WebUrl { get; set; } = "";
}

public sealed class GitLabBranch
{
    public string Name { get; set; } = "";
    public bool Default { get; set; }
    public bool Protected { get; set; }
}

public sealed class CreateMergeRequestRequest
{
    public string SourceBranch { get; set; } = "";
    public string TargetBranch { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public long? AssigneeId { get; set; }
    public bool RemoveSourceBranch { get; set; }
    public bool Squash { get; set; }
}
