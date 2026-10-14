using System.Text;

namespace GitLabDesktop.Core.Git;

/// <summary>A username and password or token for git over HTTPS, for a whole server or one repository URL.</summary>
public sealed record GitCredential(string Url, bool ForRepository, string UserName, string Secret);

/// <summary>Recognising git authentication failures and the HTTPS server they came from.</summary>
public static class GitAuth
{
    /// <summary>
    /// Git config that authenticates HTTPS remotes with an Authorization header scoped by http.&lt;url&gt;.extraHeader,
    /// so a secret is only ever sent to its own server (or repository). A later credential for the same server wins.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> ExtraHeaderConfig(IEnumerable<GitCredential> credentials)
    {
        static string Header(GitCredential c)
            => "Authorization: Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{c.UserName}:{c.Secret}"));

        var list = credentials.Where(c => c.Secret.Length > 0).ToList();
        var servers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in list.Where(c => !c.ForRepository))
        {
            if (HttpServer(c.Url) is { } server) servers[server] = Header(c);
        }

        // Repository credentials first. For each URL git keeps the most specific match seen so far and skips later, less
        // specific ones, so a repository's header shuts out its server's header instead of both being sent. (Resetting
        // with an empty value isn't an option: Windows drops environment variables with empty values.)
        var config = list
            .Where(c => c.ForRepository)
            .Select(c => new KeyValuePair<string, string>($"http.{c.Url}.extraHeader", Header(c)))
            .ToList();
        config.AddRange(servers.Select(s => new KeyValuePair<string, string>($"http.{s.Key}/.extraHeader", s.Value)));
        return config;
    }

    /// <summary>
    /// The headers git would send to <paramref name="url"/> from <see cref="ExtraHeaderConfig"/>'s settings, for clients
    /// that take headers directly (libgit2): the most specific matching http.&lt;url&gt;.extraHeader, as git picks it.
    /// A setting's URL matches the URL itself, anything below it, and the same path with ".git".
    /// </summary>
    public static IEnumerable<string> ExtraHeadersFor(string url, IEnumerable<KeyValuePair<string, string>>? config)
    {
        const string prefix = "http.", suffix = ".extraHeader";
        var target = StripUserInfo(url).TrimEnd('/');
        string? best = null;
        var bestLength = -1;
        foreach (var (key, value) in config ?? [])
        {
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
            var scope = key[prefix.Length..^suffix.Length].TrimEnd('/');
            var matches = target.Equals(scope, StringComparison.OrdinalIgnoreCase)
                || target.StartsWith(scope + "/", StringComparison.OrdinalIgnoreCase)
                || target.Equals(scope + ".git", StringComparison.OrdinalIgnoreCase)
                || target.StartsWith(scope + ".git/", StringComparison.OrdinalIgnoreCase);
            if (matches && scope.Length > bestLength)
            {
                best = value;
                bestLength = scope.Length;
            }
        }
        return best is null ? [] : [best];
    }

    static string StripUserInfo(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.UserInfo.Length > 0
            ? new UriBuilder(uri) { UserName = "", Password = "" }.Uri.ToString()
            : url;

    // What git, Git Credential Manager, GitLab and GitHub print when a login is missing, wrong or not allowed.
    static readonly string[] Markers =
    [
        "Authentication failed",
        "could not read Username",
        "could not read Password",
        "terminal prompts disabled",
        "Cannot prompt because user interactivity has been disabled",
        "HTTP Basic: Access denied",
        "Invalid username or password",
        "Invalid username or token",
        "The requested URL returned error: 401",
        "The requested URL returned error: 403",
        "Write access to repository not granted",
        "You are not allowed to push code",
    ];

    public static bool IsAuthenticationFailure(string? gitOutput)
        => gitOutput is not null &&
           (Markers.Any(m => gitOutput.Contains(m, StringComparison.OrdinalIgnoreCase)) ||
            // GitHub: "remote: Permission to owner/repo.git denied to user."
            (gitOutput.Contains("Permission to ", StringComparison.Ordinal) && gitOutput.Contains(" denied to ", StringComparison.Ordinal)));

    /// <summary>
    /// "https://host[:port]" for an HTTP(S) remote, without any user name in the URL; null for SSH and local remotes,
    /// which a username/password cannot fix.
    /// </summary>
    public static string? HttpServer(string? remoteUrl)
    {
        if (!Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        return uri.IsDefaultPort ? $"{uri.Scheme}://{uri.Host}" : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
    }

    /// <summary>The user name embedded in a remote URL (https://user@host/...), if any.</summary>
    public static string? UserName(string? remoteUrl)
    {
        if (!Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.UserInfo)) return null;
        var user = uri.UserInfo.Split(':')[0];
        return user.Length == 0 ? null : Uri.UnescapeDataString(user);
    }

    /// <summary>Whether a secret looks like a GitLab or GitHub access token rather than a password.</summary>
    public static bool LooksLikeToken(string? secret)
        => secret is not null &&
           (secret.StartsWith("glpat-", StringComparison.Ordinal) || secret.StartsWith("gloas-", StringComparison.Ordinal) ||
            secret.StartsWith("ghp_", StringComparison.Ordinal) || secret.StartsWith("github_pat_", StringComparison.Ordinal) ||
            secret.StartsWith("gho_", StringComparison.Ordinal));
}
