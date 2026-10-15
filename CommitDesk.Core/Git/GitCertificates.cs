namespace CommitDesk.Core.Git;

/// <summary>
/// How git checks HTTPS servers' certificates, in the user's global config: which certificate store it trusts, and the
/// servers whose certificates it doesn't check at all.
/// </summary>
public static class GitCertificates
{
    static readonly string[] Markers =
    [
        // OpenSSL (Git for Windows' default, macOS, Linux)
        "SSL certificate problem",
        "unable to get local issuer certificate",
        "certificate verify failed",
        "server certificate verification failed",
        // Schannel (the Windows certificate store)
        "SEC_E_UNTRUSTED_ROOT",
        "SEC_E_CERT_UNKNOWN",
        "CERT_TRUST_",
        "certificate chain was issued by an authority that is not trusted",
    ];

    /// <summary>
    /// A connection git refused because it doesn't trust the server's certificate: a self-signed one, or one issued by a
    /// company's own certificate authority that git's CA list doesn't have.
    /// </summary>
    public static bool IsCertificateProblem(string? gitOutput)
        => gitOutput is not null && Markers.Any(m => gitOutput.Contains(m, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether git checks certificates against the Windows certificate store (http.sslBackend schannel), which has the
    /// certificate authorities Windows trusts, including ones a domain adds. Git for Windows otherwise uses OpenSSL with
    /// its own CA list. Git for Windows' installer sets this in the system config; the global config overrides it.
    /// </summary>
    public static async Task<bool> UsesWindowsCertificateStoreAsync(GitRunner git)
    {
        var r = await git.RunAsync(Path.GetTempPath(), ["config", "--get", "http.sslBackend"], throwOnError: false);
        return r.Success && r.StdOut.Trim().Equals("schannel", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Turns the Windows certificate store on or off for every repository. Off removes the global value, and writes
    /// openssl only when the system config would otherwise still choose schannel.
    /// </summary>
    public static async Task SetUseWindowsCertificateStoreAsync(GitRunner git, bool value)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (value)
        {
            await git.RunAsync(home, ["config", "--global", "http.sslBackend", "schannel"]);
            return;
        }
        await git.RunAsync(home, ["config", "--global", "--unset", "http.sslBackend"], throwOnError: false);
        if (await UsesWindowsCertificateStoreAsync(git))
            await git.RunAsync(home, ["config", "--global", "http.sslBackend", "openssl"]);
    }

    /// <summary>
    /// "https://host[:port]/" for a server or repository URL, or a bare host name: the form of http.&lt;url&gt;.* keys
    /// that applies to everything on that server. Null if it isn't an HTTP(S) server.
    /// </summary>
    public static string? ServerKey(string? urlOrHost)
    {
        var text = urlOrHost?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.Host.Length == 0)
            return null;
        return uri.IsDefaultPort ? $"{uri.Scheme}://{uri.Host}/" : $"{uri.Scheme}://{uri.Host}:{uri.Port}/";
    }

    /// <summary>The servers whose certificates git doesn't check (http.&lt;url&gt;.sslVerify false in the global config).</summary>
    public static async Task<IReadOnlyList<string>> GetUntrustedServersAsync(GitRunner git)
    {
        var r = await git.RunAsync(Path.GetTempPath(),
            ["config", "--global", "--type=bool", "--get-regexp", @"^http\..+\.sslverify$"], throwOnError: false);
        if (!r.Success) return [];   // exit 1: none
        var servers = new List<string>();
        foreach (var line in r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // "http.https://git.example.com/.sslverify false": the URL keeps its case, the rest is lower-cased by git
            var space = line.LastIndexOf(' ');
            if (space < 0 || line[(space + 1)..] != "false") continue;
            var key = line[..space];
            const string prefix = "http.", suffix = ".sslverify";
            if (key.Length > prefix.Length + suffix.Length)
                servers.Add(key[prefix.Length..^suffix.Length]);
        }
        return servers;
    }

    /// <summary>Stops git checking certificates for one server (and only that server).</summary>
    public static async Task AddUntrustedServerAsync(GitRunner git, string server)
    {
        var key = ServerKey(server) ?? throw new ArgumentException($"{server} isn't an HTTPS server address.", nameof(server));
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        await git.RunAsync(home, ["config", "--global", $"http.{key}.sslVerify", "false"]);
    }

    /// <summary>Checks a server's certificates again.</summary>
    public static async Task RemoveUntrustedServerAsync(GitRunner git, string server)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // The key as listed, so entries written by hand in another form are removed too
        await git.RunAsync(home, ["config", "--global", "--unset-all", $"http.{server}.sslVerify"], throwOnError: false);
    }
}
