namespace CommitDesk.Core.Hosting;

/// <summary>
/// Works out what software a git host runs by asking it, so no server names need to be configured or built in.
/// </summary>
public static class HostProbe
{
    /// <summary>
    /// GitLab answers /api/v4/... with an "X-Gitlab-Meta" header even when the request is unauthenticated (401).
    /// GitHub Enterprise Server answers /api/v3/meta with "X-GitHub-Enterprise-Version" or a GitHub request id.
    /// Returns <see cref="HostingKind.Unknown"/> when neither matches or the host cannot be reached.
    /// </summary>
    public static async Task<HostingKind> DetectAsync(HttpClient http, string baseUrl, CancellationToken ct = default)
    {
        baseUrl = baseUrl.TrimEnd('/');
        if (await HasHeaderAsync(http, $"{baseUrl}/api/v4/version", ct, "X-Gitlab-Meta"))
            return HostingKind.GitLab;
        if (await HasHeaderAsync(http, $"{baseUrl}/api/v3/meta", ct, "X-GitHub-Enterprise-Version", "X-GitHub-Request-Id"))
            return HostingKind.GitHub;
        return HostingKind.Unknown;
    }

    static async Task<bool> HasHeaderAsync(HttpClient http, string url, CancellationToken ct, params string[] headers)
    {
        try
        {
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            // github.com's website also sends X-GitHub-Request-Id on 404 pages; require a real API answer there.
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
            return headers.Any(h => resp.Headers.Contains(h));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }
}
