namespace CommitDesk.Core.Hosting;

/// <summary>Which kind of server hosts a repository.</summary>
public enum HostingKind { Unknown, GitLab, GitHub }

/// <summary>A server the user has an account on; used to recognise remotes whose hostname alone is ambiguous.</summary>
public sealed record KnownHost(HostingKind Kind, string BaseUrl);

public sealed record HostedUser(string Login, string Name);

/// <summary>A project (GitLab) or repository (GitHub) that can be cloned.</summary>
public sealed record HostedProject(string FullName, string? Description, string HttpUrl, string SshUrl, string WebUrl);

public enum ChangeRequestState { Open, Merged, Closed }

/// <summary>A merge request (GitLab) or pull request (GitHub); open unless <paramref name="State"/> says otherwise.</summary>
public sealed record ChangeRequest(HostingKind Kind, long Number, string Title, string WebUrl, bool Draft,
    ChangeRequestState State = ChangeRequestState.Open)
{
    /// <summary>"!12" on GitLab, "#12" on GitHub.</summary>
    public string Reference => Kind == HostingKind.GitHub ? $"#{Number}" : $"!{Number}";

    public bool IsOpen => State == ChangeRequestState.Open;
}

/// <summary>An open merge/pull request as listed in the branch dropdown, with what checking it out needs.</summary>
/// <param name="SourceBranch">The branch the request comes from, in <paramref name="SourceRepository"/>.</param>
/// <param name="SourceRepository">
/// The fork the request comes from ("owner/project"), or null when it comes from a branch of the project itself.
/// </param>
/// <param name="SourceHttpUrl">Clone URLs of the fork, when the request comes from one that still exists.</param>
/// <param name="HeadSha">The request's latest commit, for its CI status.</param>
public sealed record OpenChangeRequest(
    HostingKind Kind, long Number, string Title, string WebUrl, bool Draft, string Author, DateTimeOffset CreatedAt,
    string SourceBranch, string? SourceRepository, string? SourceHttpUrl, string? SourceSshUrl, string? HeadSha)
{
    /// <summary>"!12" on GitLab, "#12" on GitHub.</summary>
    public string Reference => Kind == HostingKind.GitHub ? $"#{Number}" : $"!{Number}";

    public bool FromFork => SourceRepository is not null;

    /// <summary>The local branch for a request from a fork, like GitHub Desktop's "pr/12" (GitLab: "mr/12").</summary>
    public string ForkBranchName => Kind == HostingKind.GitHub ? $"pr/{Number}" : $"mr/{Number}";

    public ChangeRequest ToChangeRequest() => new(Kind, Number, Title, WebUrl, Draft);

    /// <summary>
    /// The open request a local branch belongs to: one whose latest commit is one of <paramref name="commits"/> (the
    /// branch's tip, so a request from a fork is found too), else one from the project's own branch of that name, else
    /// the only request from a branch of that name. Null when none, or when forks' same-named branches make it a guess.
    /// </summary>
    public static OpenChangeRequest? ForBranch(IEnumerable<OpenChangeRequest> requests, string branch, IEnumerable<string?> commits)
    {
        var named = requests.Where(r => r.SourceBranch == branch).ToList();
        var shas = commits.Where(c => !string.IsNullOrEmpty(c)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return named.FirstOrDefault(r => r.HeadSha is { } sha && shas.Contains(sha))
               ?? named.FirstOrDefault(r => !r.FromFork)
               ?? (named.Count == 1 ? named[0] : null);
    }
}

/// <summary>
/// CI state of a branch: the latest GitLab pipeline, or GitHub's check runs combined.
/// <see cref="Status"/> uses GitLab's vocabulary: success, failed, running, pending, canceled, skipped.
/// </summary>
public sealed record CiStatus(string Status, string WebUrl);

public sealed class NewChangeRequest
{
    public string SourceBranch { get; set; } = "";
    public string TargetBranch { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public bool Draft { get; set; }
    public bool AssignToMe { get; set; }
    /// <summary>GitLab only: delete the source branch when merged.</summary>
    public bool RemoveSourceBranch { get; set; }
    /// <summary>GitLab only: squash commits when merged.</summary>
    public bool Squash { get; set; }
}

/// <summary>The API operations the app needs, implemented for GitLab and GitHub.</summary>
public interface IHostingService
{
    HostingKind Kind { get; }

    /// <summary>Has a server URL and an access token.</summary>
    bool IsConfigured { get; }

    /// <summary>Whether this account's server hosts the given remote.</summary>
    bool Handles(HostedRemote remote);

    Task<HostedUser> GetUserAsync(CancellationToken ct = default);
    Task<IReadOnlyList<HostedProject>> ListProjectsAsync(string? search, CancellationToken ct = default);
    Task<string?> GetDefaultBranchAsync(string projectPath, CancellationToken ct = default);

    /// <summary>Branch names with the default branch first.</summary>
    Task<IReadOnlyList<string>> ListBranchesAsync(string projectPath, CancellationToken ct = default);

    Task<ChangeRequest?> FindOpenChangeRequestAsync(string projectPath, string sourceBranch, CancellationToken ct = default);

    /// <summary>
    /// The most recently updated request from a branch of the project, in any state: open, merged or closed.
    /// </summary>
    Task<ChangeRequest?> FindLatestChangeRequestAsync(string projectPath, string sourceBranch, CancellationToken ct = default);

    /// <summary>Open merge/pull requests into the project, newest first.</summary>
    Task<IReadOnlyList<OpenChangeRequest>> ListOpenChangeRequestsAsync(string projectPath, CancellationToken ct = default);

    /// <summary>CI state of a request's latest commit (GitLab: its latest merge request pipeline).</summary>
    Task<CiStatus?> GetChangeRequestCiStatusAsync(string projectPath, OpenChangeRequest request, CancellationToken ct = default);
    Task<CiStatus?> GetCiStatusAsync(string projectPath, string branch, CancellationToken ct = default);
    Task<ChangeRequest> CreateChangeRequestAsync(string projectPath, NewChangeRequest request, CancellationToken ct = default);
}
