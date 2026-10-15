using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommitDesk.Core.Hosting;

namespace CommitDesk.Core.GitLab;

public sealed class GitLabApiException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>Minimal GitLab REST API v4 client authenticated with a personal access token.</summary>
public sealed class GitLabClient(HttpClient http) : IHostingService
{
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string? BaseUrl { get; private set; }
    string? _token;

    public bool IsConfigured => !string.IsNullOrEmpty(BaseUrl) && !string.IsNullOrEmpty(_token);

    public void Configure(string? baseUrl, string? token)
    {
        BaseUrl = baseUrl?.Trim().TrimEnd('/');
        _token = token?.Trim();
    }

    static string Id(string projectPath) => Uri.EscapeDataString(projectPath);

    HttpRequestMessage Request(HttpMethod method, string relative)
    {
        if (!IsConfigured) throw new InvalidOperationException("GitLab is not configured. Set the instance URL and access token in Options.");
        var req = new HttpRequestMessage(method, $"{BaseUrl}/api/v4/{relative}");
        req.Headers.Add("PRIVATE-TOKEN", _token);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return req;
    }

    async Task<T> SendAsync<T>(HttpRequestMessage req, CancellationToken ct)
    {
        using (req)
        using (var resp = await http.SendAsync(req, ct))
        {
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                throw new GitLabApiException(resp.StatusCode, $"GitLab API {(int)resp.StatusCode} {resp.ReasonPhrase}: {ExtractMessage(body)}");
            }
            return (await resp.Content.ReadFromJsonAsync<T>(Json, ct))!;
        }
    }

    static string ExtractMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            foreach (var key in new[] { "message", "error_description", "error" })
                if (doc.RootElement.TryGetProperty(key, out var v))
                    return v.ValueKind == JsonValueKind.String ? v.GetString()! : v.ToString();
        }
        catch (JsonException) { }
        return body.Length > 300 ? body[..300] : body;
    }

    Task<T> GetAsync<T>(string relative, CancellationToken ct = default)
        => SendAsync<T>(Request(HttpMethod.Get, relative), ct);

    public Task<GitLabUser> GetCurrentUserAsync(CancellationToken ct = default)
        => GetAsync<GitLabUser>("user", ct);

    public Task<List<GitLabProject>> GetProjectsAsync(string? search, int page = 1, CancellationToken ct = default)
    {
        var q = $"projects?membership=true&simple=true&order_by=last_activity_at&per_page=100&page={page}";
        if (!string.IsNullOrWhiteSpace(search)) q += "&search=" + Uri.EscapeDataString(search);
        return GetAsync<List<GitLabProject>>(q, ct);
    }

    public Task<GitLabProject> GetProjectAsync(string projectPath, CancellationToken ct = default)
        => GetAsync<GitLabProject>($"projects/{Id(projectPath)}", ct);

    public Task<List<GitLabBranch>> GetBranchesAsync(string projectPath, CancellationToken ct = default)
        => GetAsync<List<GitLabBranch>>($"projects/{Id(projectPath)}/repository/branches?per_page=100", ct);

    public async Task<GitLabMergeRequest?> GetOpenMergeRequestAsync(string projectPath, string sourceBranch, CancellationToken ct = default)
    {
        var list = await GetAsync<List<GitLabMergeRequest>>(
            $"projects/{Id(projectPath)}/merge_requests?state=opened&source_branch={Uri.EscapeDataString(sourceBranch)}", ct);
        return list.FirstOrDefault();
    }

    public async Task<GitLabPipeline?> GetLatestPipelineAsync(string projectPath, string refName, CancellationToken ct = default)
    {
        var list = await GetAsync<List<GitLabPipeline>>(
            $"projects/{Id(projectPath)}/pipelines?per_page=1&ref={Uri.EscapeDataString(refName)}", ct);
        return list.FirstOrDefault();
    }

    public Task<GitLabMergeRequest> CreateMergeRequestAsync(string projectPath, CreateMergeRequestRequest body, CancellationToken ct = default)
    {
        var req = Request(HttpMethod.Post, $"projects/{Id(projectPath)}/merge_requests");
        req.Content = JsonContent.Create(body, options: Json);
        return SendAsync<GitLabMergeRequest>(req, ct);
    }

    // ── IHostingService ──────────────────────────────────────────────────────

    public HostingKind Kind => HostingKind.GitLab;

    public bool Handles(HostedRemote remote) => remote.Kind == HostingKind.GitLab && remote.IsOn(BaseUrl);

    public async Task<HostedUser> GetUserAsync(CancellationToken ct = default)
    {
        var u = await GetCurrentUserAsync(ct);
        return new HostedUser(u.Username, u.Name);
    }

    public async Task<IReadOnlyList<HostedProject>> ListProjectsAsync(string? search, CancellationToken ct = default)
        => (await GetProjectsAsync(search, ct: ct))
            .Select(p => new HostedProject(p.PathWithNamespace, p.Description, p.HttpUrlToRepo, p.SshUrlToRepo, p.WebUrl))
            .ToList();

    public async Task<string?> GetDefaultBranchAsync(string projectPath, CancellationToken ct = default)
        => (await GetProjectAsync(projectPath, ct)).DefaultBranch;

    // The API follows a renamed project's redirect from its old path
    public async Task<ProjectDetails> GetProjectDetailsAsync(string projectPath, CancellationToken ct = default)
    {
        var p = await GetProjectAsync(projectPath, ct);
        return new ProjectDetails(p.Name, p.DefaultBranch);
    }

    public async Task<IReadOnlyList<string>> ListBranchesAsync(string projectPath, CancellationToken ct = default)
        => (await GetBranchesAsync(projectPath, ct))
            .OrderByDescending(b => b.Default).ThenBy(b => b.Name, StringComparer.Ordinal)
            .Select(b => b.Name)
            .ToList();

    public async Task<ChangeRequest?> FindOpenChangeRequestAsync(string projectPath, string sourceBranch, CancellationToken ct = default)
        => await GetOpenMergeRequestAsync(projectPath, sourceBranch, ct) is { } mr
            ? new ChangeRequest(HostingKind.GitLab, mr.Iid, mr.Title, mr.WebUrl, mr.Draft)
            : null;

    public async Task<ChangeRequest?> FindLatestChangeRequestAsync(string projectPath, string sourceBranch, CancellationToken ct = default)
    {
        var list = await GetAsync<List<GitLabMergeRequest>>(
            $"projects/{Id(projectPath)}/merge_requests?state=all&order_by=updated_at&sort=desc&per_page=1&source_branch={Uri.EscapeDataString(sourceBranch)}", ct);
        if (list.FirstOrDefault() is not { } mr) return null;
        // GitLab states: opened, merged, closed, locked (being merged)
        var state = mr.State switch { "merged" => ChangeRequestState.Merged, "closed" => ChangeRequestState.Closed, _ => ChangeRequestState.Open };
        return new ChangeRequest(HostingKind.GitLab, mr.Iid, mr.Title, mr.WebUrl, mr.Draft, state);
    }

    public async Task<CiStatus?> GetCiStatusAsync(string projectPath, string branch, CancellationToken ct = default)
        => await GetLatestPipelineAsync(projectPath, branch, ct) is { } p ? new CiStatus(p.Status, p.WebUrl) : null;

    public async Task<IReadOnlyList<OpenChangeRequest>> ListOpenChangeRequestsAsync(string projectPath, CancellationToken ct = default)
    {
        // Up to 300, newest first: a busy project can have more than one page open
        var list = new List<GitLabMergeRequest>();
        for (int page = 1; page <= 3; page++)
        {
            var batch = await GetAsync<List<GitLabMergeRequest>>(
                $"projects/{Id(projectPath)}/merge_requests?state=opened&order_by=created_at&sort=desc&per_page=100&page={page}", ct);
            list.AddRange(batch);
            if (batch.Count < 100) break;
        }

        // A merge request from a fork names its project only by id; look each fork up once for its clone URLs.
        var forks = new Dictionary<long, GitLabProject?>();
        foreach (var id in list.Where(m => m.SourceProjectId != m.TargetProjectId).Select(m => m.SourceProjectId).Distinct())
        {
            try { forks[id] = await GetAsync<GitLabProject>($"projects/{id}", ct); }
            catch (GitLabApiException) { forks[id] = null; }   // deleted, or not visible to this account
        }

        return list.Select(m =>
        {
            var fromFork = m.SourceProjectId != m.TargetProjectId;
            var fork = fromFork ? forks.GetValueOrDefault(m.SourceProjectId) : null;
            return new OpenChangeRequest(HostingKind.GitLab, m.Iid, m.Title, m.WebUrl, m.Draft, m.Author?.Username ?? "",
                m.CreatedAt, m.SourceBranch,
                fromFork ? fork?.PathWithNamespace ?? $"project {m.SourceProjectId}" : null,
                fork?.HttpUrlToRepo, fork?.SshUrlToRepo, m.Sha);
        }).ToList();
    }

    public async Task<CiStatus?> GetChangeRequestCiStatusAsync(string projectPath, OpenChangeRequest request, CancellationToken ct = default)
    {
        var list = await GetAsync<List<GitLabPipeline>>(
            $"projects/{Id(projectPath)}/merge_requests/{request.Number}/pipelines?per_page=1", ct);
        return list.FirstOrDefault() is { } p ? new CiStatus(p.Status, p.WebUrl) : null;
    }

    public async Task<ChangeRequest> CreateChangeRequestAsync(string projectPath, NewChangeRequest request, CancellationToken ct = default)
    {
        var title = request.Title.Trim();
        // GitLab marks drafts with a title prefix.
        if (request.Draft && !title.StartsWith("Draft:", StringComparison.OrdinalIgnoreCase)) title = "Draft: " + title;
        long? assignee = request.AssignToMe ? (await GetCurrentUserAsync(ct)).Id : null;

        var mr = await CreateMergeRequestAsync(projectPath, new CreateMergeRequestRequest
        {
            SourceBranch = request.SourceBranch,
            TargetBranch = request.TargetBranch,
            Title = title,
            Description = request.Description,
            AssigneeId = assignee,
            RemoveSourceBranch = request.RemoveSourceBranch,
            Squash = request.Squash,
        }, ct);
        return new ChangeRequest(HostingKind.GitLab, mr.Iid, mr.Title, mr.WebUrl, mr.Draft);
    }
}
