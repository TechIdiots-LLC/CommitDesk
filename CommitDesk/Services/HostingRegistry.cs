using CommitDesk.Core.GitHub;
using CommitDesk.Core.GitLab;
using CommitDesk.Core.Hosting;

namespace CommitDesk.Services;

/// <summary>
/// One API client per configured account, plus remote resolution: which kind of server a remote is on and
/// which account (if any) can talk to it.
/// </summary>
public sealed class HostingRegistry(AppSettings settings, IHttpClientFactory httpFactory)
{
    List<(HostAccount Account, IHostingService Service)> _services = [];

    // Hosts that recently gave no answer (offline, or not GitLab/GitHub), so each sign-in or refresh doesn't wait on
    // another probe. Unlike definite answers these aren't saved: the server is asked again after a while.
    readonly Dictionary<string, DateTime> _unanswered = new(StringComparer.OrdinalIgnoreCase);
    static readonly TimeSpan RetryUnansweredAfter = TimeSpan.FromMinutes(10);

    public IReadOnlyList<(HostAccount Account, IHostingService Service)> Services => _services;

    /// <summary>Recreates the clients from the accounts that use the API (GitLab servers and github.com).</summary>
    public void Rebuild() => _services = settings.Accounts.Where(a => a.UsesApi).Select(a => (a, Create(a))).ToList();

    public IHostingService Create(HostAccount account)
    {
        var http = httpFactory.CreateClient("hosting");
        if (account.Kind == HostingKind.GitHub)
        {
            var gh = new GitHubClient(http);
            gh.Configure(account.Secret);
            return gh;
        }
        var gl = new GitLabClient(http);
        gl.Configure(account.BaseUrl, account.Secret);
        return gl;
    }

    /// <summary>A client without an account for a public github.com repository, for a few read-only calls.</summary>
    public IHostingService? AnonymousFor(HostedRemote? remote)
        => remote is not null && remote.Kind == HostingKind.GitHub && remote.IsOn(AppSettings.GitHubUrl)
            ? GitHubClient.Anonymous(httpFactory.CreateClient("hosting"))
            : null;

    /// <summary>The configured account client for this remote, or null (links still work without one).</summary>
    public IHostingService? For(HostedRemote? remote)
        => remote is null ? null : _services.FirstOrDefault(s => s.Service.IsConfigured && s.Service.Handles(remote)).Service;

    /// <summary>
    /// Parses a remote URL. When neither an account nor an earlier probe covers its host, and the host is not
    /// github.com, asks the server what it runs and remembers the answer.
    /// </summary>
    public async Task<HostedRemote?> ResolveAsync(string? remoteUrl)
    {
        var remote = HostedRemote.Parse(remoteUrl, settings.KnownHosts);
        if (remote is null || remote.IsOn(AppSettings.GitHubUrl)) return remote;

        var detected = settings.DetectedHosts;
        bool known = settings.KnownHosts.Any(k => remote.IsOn(k.BaseUrl)) || detected.ContainsKey(remote.Host);
        if (known) return remote;
        lock (_unanswered)
            if (_unanswered.TryGetValue(remote.Host, out var asked) && DateTime.UtcNow - asked < RetryUnansweredAfter) return remote;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var kind = await HostProbe.DetectAsync(httpFactory.CreateClient("hosting"), $"https://{remote.Host}", cts.Token);
        // Save only definite answers, so an offline host is probed again later.
        if (kind != HostingKind.Unknown)
        {
            detected[remote.Host] = kind;
            settings.DetectedHosts = detected;
            remote = HostedRemote.Parse(remoteUrl, settings.KnownHosts);
        }
        else
        {
            lock (_unanswered) _unanswered[remote.Host] = DateTime.UtcNow;
        }
        return remote;
    }
}
