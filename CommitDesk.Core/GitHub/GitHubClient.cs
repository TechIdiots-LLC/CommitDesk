using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommitDesk.Core.Hosting;

namespace CommitDesk.Core.GitHub;

public sealed class GitHubApiException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>Minimal GitHub REST API client (github.com) authenticated with a personal access token.</summary>
public sealed class GitHubClient(HttpClient http) : IHostingService
{
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    const string WebBase = "https://github.com";
    const string ApiBase = "https://api.github.com";

    string? _token;
    string? _login;

    public HostingKind Kind => HostingKind.GitHub;
    public bool IsConfigured => !string.IsNullOrEmpty(_token);

    bool _anonymous;

    /// <summary>
    /// A client without an account, for reading public repositories (e.g. listing their pull requests). GitHub allows
    /// about 60 such requests an hour per address, so callers should keep to one or two calls.
    /// </summary>
    public static GitHubClient Anonymous(HttpClient http) => new(http) { _anonymous = true };

    public void Configure(string? token)
    {
        _token = token?.Trim();
        _login = null;
    }

    public bool Handles(HostedRemote remote) => remote.Kind == HostingKind.GitHub && remote.IsOn(WebBase);

    // ── HTTP ─────────────────────────────────────────────────────────────────

    HttpRequestMessage Request(HttpMethod method, string relative)
    {
        if (!IsConfigured && !_anonymous) throw new InvalidOperationException("GitHub is not configured. Add a GitHub account in Options.");
        var req = new HttpRequestMessage(method, $"{ApiBase}/{relative}");
        if (IsConfigured) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        req.Headers.UserAgent.ParseAdd("CommitDesk/0.1");
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
                throw new GitHubApiException(resp.StatusCode, $"GitHub API {(int)resp.StatusCode} {resp.ReasonPhrase}: {ExtractMessage(body)}");
            }
            return (await resp.Content.ReadFromJsonAsync<T>(Json, ct))!;
        }
    }

    static string ExtractMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
            // Validation failures put the useful part in errors[].message.
            if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            {
                var details = errors.EnumerateArray()
                    .Select(e => e.TryGetProperty("message", out var em) ? em.GetString() : null)
                    .OfType<string>();
                msg = string.Join(" ", new[] { msg }.Concat(details).OfType<string>());
            }
            if (!string.IsNullOrEmpty(msg)) return msg;
        }
        catch (JsonException) { }
        return body.Length > 300 ? body[..300] : body;
    }

    Task<T> GetAsync<T>(string relative, CancellationToken ct) => SendAsync<T>(Request(HttpMethod.Get, relative), ct);

    async Task<T> PostAsync<T>(string relative, object body, CancellationToken ct)
    {
        var req = Request(HttpMethod.Post, relative);
        req.Content = JsonContent.Create(body, options: Json);
        return await SendAsync<T>(req, ct);
    }

    static string Repo(string projectPath) => string.Join('/', projectPath.Split('/').Select(Uri.EscapeDataString));

    // ── Models (subset of the REST resources) ────────────────────────────────

    sealed class User { public string Login { get; set; } = ""; public string? Name { get; set; } }
    sealed class RepoInfo
    {
        public string Name { get; set; } = "";
        public string FullName { get; set; } = "";
        public string? Description { get; set; }
        public string CloneUrl { get; set; } = "";
        public string SshUrl { get; set; } = "";
        public string HtmlUrl { get; set; } = "";
        public string? DefaultBranch { get; set; }
    }
    sealed class Branch { public string Name { get; set; } = ""; }
    sealed class Pull
    {
        public long Number { get; set; }
        public string Title { get; set; } = "";
        public string HtmlUrl { get; set; } = "";
        public bool Draft { get; set; }
        public string State { get; set; } = "";
        public DateTimeOffset? MergedAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public User? User { get; set; }
        public PullRef? Head { get; set; }
        public PullRef? Base { get; set; }
    }
    sealed class PullRef { public string Ref { get; set; } = ""; public string Sha { get; set; } = ""; public RepoInfo? Repo { get; set; } }
    sealed class CheckRunList { [JsonPropertyName("check_runs")] public List<CheckRun> Runs { get; set; } = []; }
    sealed class CheckRun { public string Status { get; set; } = ""; public string? Conclusion { get; set; } }

    // ── IHostingService ──────────────────────────────────────────────────────

    public async Task<HostedUser> GetUserAsync(CancellationToken ct = default)
    {
        var u = await GetAsync<User>("user", ct);
        _login = u.Login;
        return new HostedUser(u.Login, u.Name ?? u.Login);
    }

    public async Task<IReadOnlyList<HostedProject>> ListProjectsAsync(string? search, CancellationToken ct = default)
    {
        // /user/repos has no text search; fetch the most recently updated repos and filter locally.
        var all = new List<RepoInfo>();
        for (int page = 1; page <= 3; page++)
        {
            var batch = await GetAsync<List<RepoInfo>>(
                $"user/repos?per_page=100&page={page}&sort=updated&affiliation=owner,collaborator,organization_member", ct);
            all.AddRange(batch);
            if (batch.Count < 100) break;
        }
        return all
            .Where(r => string.IsNullOrWhiteSpace(search) ||
                        r.FullName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        (r.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
            .Select(r => new HostedProject(r.FullName, r.Description, r.CloneUrl, r.SshUrl, r.HtmlUrl))
            .ToList();
    }

    public async Task<string?> GetDefaultBranchAsync(string projectPath, CancellationToken ct = default)
        => (await GetAsync<RepoInfo>($"repos/{Repo(projectPath)}", ct)).DefaultBranch;

    // GitHub redirects a renamed repository's old path to it
    public async Task<ProjectDetails> GetProjectDetailsAsync(string projectPath, CancellationToken ct = default)
    {
        var r = await GetAsync<RepoInfo>($"repos/{Repo(projectPath)}", ct);
        return new ProjectDetails(r.Name, r.DefaultBranch);
    }

    public async Task<IReadOnlyList<string>> ListBranchesAsync(string projectPath, CancellationToken ct = default)
    {
        var branches = await GetAsync<List<Branch>>($"repos/{Repo(projectPath)}/branches?per_page=100", ct);
        var def = await GetDefaultBranchAsync(projectPath, ct);
        return branches.Select(b => b.Name).OrderByDescending(n => n == def).ThenBy(n => n, StringComparer.Ordinal).ToList();
    }

    public async Task<ChangeRequest?> FindOpenChangeRequestAsync(string projectPath, string sourceBranch, CancellationToken ct = default)
    {
        var owner = projectPath.Split('/')[0];
        var pulls = await GetAsync<List<Pull>>(
            $"repos/{Repo(projectPath)}/pulls?state=open&head={Uri.EscapeDataString(owner + ":" + sourceBranch)}", ct);
        return pulls.FirstOrDefault() is { } p ? new ChangeRequest(HostingKind.GitHub, p.Number, p.Title, p.HtmlUrl, p.Draft) : null;
    }

    public async Task<ChangeRequest?> FindLatestChangeRequestAsync(string projectPath, string sourceBranch, CancellationToken ct = default)
    {
        var owner = projectPath.Split('/')[0];
        var pulls = await GetAsync<List<Pull>>(
            $"repos/{Repo(projectPath)}/pulls?state=all&sort=updated&direction=desc&per_page=1&head={Uri.EscapeDataString(owner + ":" + sourceBranch)}", ct);
        if (pulls.FirstOrDefault() is not { } p) return null;
        var state = p.State == "open" ? ChangeRequestState.Open : p.MergedAt is not null ? ChangeRequestState.Merged : ChangeRequestState.Closed;
        return new ChangeRequest(HostingKind.GitHub, p.Number, p.Title, p.HtmlUrl, p.Draft, state);
    }

    public async Task<IReadOnlyList<OpenChangeRequest>> ListOpenChangeRequestsAsync(string projectPath, CancellationToken ct = default)
    {
        // Up to 300, newest first: a busy project can have more than one page open
        var pulls = new List<Pull>();
        for (int page = 1; page <= 3; page++)
        {
            var batch = await GetAsync<List<Pull>>($"repos/{Repo(projectPath)}/pulls?state=open&sort=created&direction=desc&per_page=100&page={page}", ct);
            pulls.AddRange(batch);
            if (batch.Count < 100) break;
        }
        return pulls.Select(p =>
        {
            // A deleted fork leaves head.repo null; the request can still be fetched from the base repository.
            var headRepo = p.Head?.Repo;
            var fromFork = headRepo is null || !string.Equals(headRepo.FullName, p.Base?.Repo?.FullName, StringComparison.OrdinalIgnoreCase);
            return new OpenChangeRequest(HostingKind.GitHub, p.Number, p.Title, p.HtmlUrl, p.Draft, p.User?.Login ?? "", p.CreatedAt,
                p.Head?.Ref ?? "", fromFork ? headRepo?.FullName ?? "a deleted fork" : null,
                fromFork ? headRepo?.CloneUrl : null, fromFork ? headRepo?.SshUrl : null, p.Head?.Sha);
        }).ToList();
    }

    public Task<CiStatus?> GetChangeRequestCiStatusAsync(string projectPath, OpenChangeRequest request, CancellationToken ct = default)
        => request.HeadSha is { Length: > 0 } sha ? GetCiStatusAsync(projectPath, sha, ct) : Task.FromResult<CiStatus?>(null);

    /// <summary>Combines the branch head's check runs (GitHub Actions and other apps) into one status.</summary>
    public async Task<CiStatus?> GetCiStatusAsync(string projectPath, string branch, CancellationToken ct = default)
    {
        var list = await GetAsync<CheckRunList>(
            $"repos/{Repo(projectPath)}/commits/{Uri.EscapeDataString(branch)}/check-runs?per_page=100", ct);
        if (list.Runs.Count == 0) return null;

        string status;
        if (list.Runs.Any(r => r.Conclusion is "failure" or "timed_out" or "startup_failure" or "action_required")) status = "failed";
        else if (list.Runs.Any(r => r.Status is "in_progress")) status = "running";
        else if (list.Runs.Any(r => r.Status is "queued" or "waiting" or "requested" or "pending")) status = "pending";
        else if (list.Runs.All(r => r.Conclusion is "cancelled")) status = "canceled";
        else if (list.Runs.All(r => r.Conclusion is "skipped" or "neutral")) status = "skipped";
        else status = "success";

        var link = $"{WebBase}/{projectPath}/actions?query={Uri.EscapeDataString("branch:" + branch)}";
        return new CiStatus(status, link);
    }

    public async Task<ChangeRequest> CreateChangeRequestAsync(string projectPath, NewChangeRequest request, CancellationToken ct = default)
    {
        var pull = await PostAsync<Pull>($"repos/{Repo(projectPath)}/pulls", new
        {
            title = request.Title,
            head = request.SourceBranch,
            @base = request.TargetBranch,
            body = request.Description,
            draft = request.Draft,
        }, ct);

        if (request.AssignToMe)
        {
            var login = _login ?? (await GetUserAsync(ct)).Login;
            await PostAsync<JsonElement>($"repos/{Repo(projectPath)}/issues/{pull.Number}/assignees", new { assignees = new[] { login } }, ct);
        }
        return new ChangeRequest(HostingKind.GitHub, pull.Number, pull.Title, pull.HtmlUrl, pull.Draft);
    }
}
