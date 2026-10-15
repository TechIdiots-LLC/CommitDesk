namespace CommitDesk.Core.Hosting;

/// <summary>
/// A git remote URL resolved to its hosting server and project path ("group/subgroup/project" or "owner/repo"),
/// with web links and wording for that kind of server.
/// </summary>
public sealed record HostedRemote(HostingKind Kind, string Host, string ProjectPath, string WebUrl)
{
    /// <summary>
    /// Parses https://host/group/proj.git, git@host:group/proj.git and ssh://git@host:port/group/proj.git.
    /// The kind comes from a matching <paramref name="knownHosts"/> entry (which also supplies the scheme and any
    /// relative path, e.g. https://host/gitlab), else from the hostname: github.com or names containing
    /// "github"/"gitlab".
    /// </summary>
    public static HostedRemote? Parse(string? remoteUrl, IEnumerable<KnownHost>? knownHosts = null)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl)) return null;
        string host, path;

        if (Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "ssh" or "git")
        {
            host = uri.Host;
            path = Uri.UnescapeDataString(uri.AbsolutePath);
        }
        else
        {
            // scp-like syntax: [user@]host:path
            var colon = remoteUrl.IndexOf(':');
            if (colon <= 0) return null;
            var userHost = remoteUrl[..colon];
            host = userHost.Contains('@') ? userHost[(userHost.IndexOf('@') + 1)..] : userHost;
            path = remoteUrl[(colon + 1)..];
        }

        path = path.Trim('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path[..^4];

        var kind = GuessKind(host);
        string webBase = uri is { Scheme: "http" } ? $"http://{uri.Authority}" : $"https://{host}";

        var known = knownHosts?.FirstOrDefault(k => SameHost(k.BaseUrl, host));
        if (known is not null && Uri.TryCreate(known.BaseUrl, UriKind.Absolute, out var inst))
        {
            kind = known.Kind;
            var relative = inst.AbsolutePath.Trim('/');
            webBase = inst.GetLeftPart(UriPartial.Authority);
            // An instance at https://host/gitlab serves clones as https://host/gitlab/group/proj.git.
            if (relative.Length > 0)
            {
                if (path.StartsWith(relative + "/", StringComparison.OrdinalIgnoreCase)) path = path[(relative.Length + 1)..];
                webBase += "/" + relative;
            }
        }

        if (path.Length == 0) return null;
        return new HostedRemote(kind, host, path, $"{webBase}/{path}");
    }

    static HostingKind GuessKind(string host)
    {
        if (host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || host.Contains("github", StringComparison.OrdinalIgnoreCase))
            return HostingKind.GitHub;
        if (host.Contains("gitlab", StringComparison.OrdinalIgnoreCase))
            return HostingKind.GitLab;
        return HostingKind.Unknown;
    }

    static bool SameHost(string? baseUrl, string host)
        => Uri.TryCreate(baseUrl, UriKind.Absolute, out var u) && string.Equals(u.Host, host, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the remote lives on the server at <paramref name="baseUrl"/>.</summary>
    public bool IsOn(string? baseUrl) => SameHost(baseUrl, Host);

    // ── Wording ──────────────────────────────────────────────────────────────

    public string ProviderName => Kind switch
    {
        HostingKind.GitLab => "GitLab",
        HostingKind.GitHub => "GitHub",
        _ => Host,
    };

    public string ChangeRequestName => Kind == HostingKind.GitHub ? "pull request" : "merge request";
    public string ChangeRequestsName => ChangeRequestName + "s";

    /// <summary>"pipelines" / "Actions".</summary>
    public string CiListName => Kind == HostingKind.GitHub ? "Actions" : "pipelines";

    /// <summary>"pipeline" / "checks".</summary>
    public string CiName => Kind == HostingKind.GitHub ? "checks" : "pipeline";

    // ── Web links (null when the host's URL layout is unknown) ───────────────

    static string Ref(string name) => string.Join('/', name.Split('/').Select(Uri.EscapeDataString));

    bool Known => Kind != HostingKind.Unknown;

    /// <summary>"/-" separates GitLab's project path from its pages; GitHub has no separator.</summary>
    string Pages => Kind == HostingKind.GitLab ? $"{WebUrl}/-" : WebUrl;

    public string ProjectLink => WebUrl;
    public string? BranchLink(string branch) => Known ? $"{Pages}/tree/{Ref(branch)}" : null;
    public string? CommitLink(string sha) => Known ? $"{Pages}/commit/{sha}" : null;
    /// <summary>A file as it was at a commit.</summary>
    public string? FileLink(string sha, string path) => Known ? $"{Pages}/blob/{sha}/{Ref(path)}" : null;
    public string? CompareLink(string from, string to) => Known ? $"{Pages}/compare/{Ref(from)}...{Ref(to)}" : null;
    public string? NewIssueLink => Known ? $"{Pages}/issues/new" : null;
    public string? IssuesLink => Known ? $"{Pages}/issues" : null;

    public string? ChangeRequestsLink => Kind switch
    {
        HostingKind.GitLab => $"{Pages}/merge_requests",
        HostingKind.GitHub => $"{WebUrl}/pulls",
        _ => null,
    };

    /// <summary>The open merge/pull requests from a branch (the list, when the app can't tell which one).</summary>
    public string? ChangeRequestsForBranchLink(string branch) => Kind switch
    {
        HostingKind.GitLab => $"{Pages}/merge_requests?state=opened&source_branch={Uri.EscapeDataString(branch)}",
        HostingKind.GitHub => $"{WebUrl}/pulls?q={Uri.EscapeDataString($"is:pr is:open head:{branch}")}",
        _ => null,
    };

    public string? CiLink(string branch) => Kind switch
    {
        HostingKind.GitLab => $"{Pages}/pipelines?ref={Uri.EscapeDataString(branch)}",
        HostingKind.GitHub => $"{WebUrl}/actions?query={Uri.EscapeDataString("branch:" + branch)}",
        _ => null,
    };

    /// <summary>The host's own "new merge/pull request" page, prefilled with the branches.</summary>
    public string? NewChangeRequestLink(string source, string target) => Kind switch
    {
        HostingKind.GitLab => $"{Pages}/merge_requests/new?merge_request%5Bsource_branch%5D={Uri.EscapeDataString(source)}" +
                              $"&merge_request%5Btarget_branch%5D={Uri.EscapeDataString(target)}",
        HostingKind.GitHub => $"{WebUrl}/compare/{Ref(target)}...{Ref(source)}?expand=1",
        _ => null,
    };
}
