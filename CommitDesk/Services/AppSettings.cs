using System.Text.Json;
using System.Text.Json.Serialization;
using CommitDesk.Core.Git;
using CommitDesk.Core.Hosting;

namespace CommitDesk.Services;

/// <summary>
/// A sign-in for a server, or for one repository on it: git uses it for HTTPS remotes there, and on a GitLab server or
/// github.com the app uses it for merge/pull requests and CI status. The secret is kept in SecureStorage, not in
/// Preferences.
/// </summary>
public sealed class HostAccount
{
    /// <summary>GitLab, GitHub, or Unknown for any other git server (git only, no API).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<HostingKind>))]
    public HostingKind Kind { get; set; }

    /// <summary>Server URL, e.g. https://gitlab.example.com (GitHub accounts are always https://github.com).</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>The repository URL (without user info) for a sign-in that is only for that repository.</summary>
    public string? Repository { get; set; }

    /// <summary>Username for git; empty for an access token, which git sends with the host's conventional username.</summary>
    public string? UserName { get; set; }

    /// <summary>Password or access token.</summary>
    [JsonIgnore] public string? Secret { get; set; }

    /// <summary>False for a sign-in kept only until the app exits.</summary>
    [JsonIgnore] public bool Remember { get; set; } = true;

    /// <summary>The URL this account applies to, and its storage key.</summary>
    [JsonIgnore] public string Scope => Repository ?? BaseUrl;

    [JsonIgnore] public string Host => Uri.TryCreate(BaseUrl, UriKind.Absolute, out var u) ? u.Host : BaseUrl;

    /// <summary>Whether the secret is also the API token: a whole-server account on a GitLab server or github.com.</summary>
    [JsonIgnore]
    public bool UsesApi => Repository is null &&
                           (Kind == HostingKind.GitLab || (Kind == HostingKind.GitHub && SameHost(BaseUrl, AppSettings.GitHubUrl)));

    [JsonIgnore]
    public string Title => Repository is not null
        ? $"{Repository[Math.Min(Repository.Length, BaseUrl.TrimEnd('/').Length + 1)..]} on {Host}"
        : Kind switch
        {
            HostingKind.GitHub when UsesApi => "GitHub",
            HostingKind.GitLab => $"GitLab ({Host})",
            _ => Host,
        };

    /// <summary>The username git sends: the account's, or for a token the one GitHub or GitLab conventionally expects.</summary>
    [JsonIgnore]
    public string GitUserName => string.IsNullOrWhiteSpace(UserName)
        ? (Kind == HostingKind.GitHub ? "x-access-token" : "oauth2")
        : UserName;

    public HostAccount Clone() => (HostAccount)MemberwiseClone();

    public static bool SameHost(string? a, string? b)
        => Uri.TryCreate(a, UriKind.Absolute, out var ua) && Uri.TryCreate(b, UriKind.Absolute, out var ub) &&
           string.Equals(ua.Host, ub.Host, StringComparison.OrdinalIgnoreCase) && ua.Port == ub.Port;
}

/// <summary>User settings. Access tokens live in SecureStorage; everything else in Preferences.</summary>
public sealed class AppSettings
{
    public const string GitHubUrl = "https://github.com";

    /// <summary>Where repositories live: the default clone and "new repository" location, and the folder scanned for existing checkouts.</summary>
    public string RepositoriesDirectory
    {
        get => Preferences.Get("clone_directory", DefaultRepositoriesDirectory);
        set => Preferences.Set("clone_directory", value);
    }

    /// <summary>The app's own folder, Documents\GitLab, used until the user picks another.</summary>
    public static string DefaultRepositoriesDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GitLab");

    /// <summary>Add every repository found in <see cref="RepositoriesDirectory"/> to the repository list at startup.</summary>
    public bool AutoAddRepositories
    {
        get => Preferences.Get("auto_add_repositories", true);
        set => Preferences.Set("auto_add_repositories", value);
    }

    /// <summary>Repositories the user removed from the list, so the folder scan does not add them back.</summary>
    public List<string> HiddenRepositories
    {
        get
        {
            try { return JsonSerializer.Deserialize<List<string>>(Preferences.Get("hidden_repositories", "[]")) ?? []; }
            catch (JsonException) { return []; }
        }
        set => Preferences.Set("hidden_repositories", JsonSerializer.Serialize(value));
    }

    public bool CloneWithSsh
    {
        get => Preferences.Get("clone_with_ssh", false);
        set => Preferences.Set("clone_with_ssh", value);
    }

    public string EditorCommand
    {
        get => Preferences.Get("editor_command", "code");
        set => Preferences.Set("editor_command", value);
    }

    public string GitExecutable
    {
        get => Preferences.Get("git_executable", "git");
        set => Preferences.Set("git_executable", string.IsNullOrWhiteSpace(value) ? "git" : value.Trim());
    }

    public string? LastRepository
    {
        get => Preferences.Get("last_repository", null as string);
        set => Preferences.Set("last_repository", value);
    }

    public List<string> Repositories
    {
        get
        {
            try { return JsonSerializer.Deserialize<List<string>>(Preferences.Get("repositories", "[]")) ?? []; }
            catch (JsonException) { return []; }
        }
        set => Preferences.Set("repositories", JsonSerializer.Serialize(value));
    }

    /// <summary>Repository folder → its project's name on the server, when that differs from the folder name.</summary>
    public Dictionary<string, string> RepositoryNames
    {
        get
        {
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(Preferences.Get("repository_names", "{}")) is { } names
                    ? new(names, StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase);
            }
            catch (JsonException) { return new(StringComparer.OrdinalIgnoreCase); }
        }
        set => Preferences.Set("repository_names", JsonSerializer.Serialize(value));
    }

    // ── Accounts ─────────────────────────────────────────────────────────────

    // Cached because SecureStorage is async and git needs the secrets synchronously for every command. Replaced, never
    // modified, so git commands running meanwhile always see a complete list.
    List<HostAccount> _accounts = [];
    bool _accountsLoaded;

    public IReadOnlyList<HostAccount> Accounts => _accounts;

    // Server accounts keep the key their API tokens have always had.
    static string SecretKey(string scope) => "token:" + scope;

    public async Task LoadAccountsAsync()
    {
        if (_accountsLoaded) return;
        List<HostAccount> accounts;
        try { accounts = JsonSerializer.Deserialize<List<HostAccount>>(Preferences.Get("accounts", "[]")) ?? []; }
        catch (JsonException) { accounts = []; }

        foreach (var a in accounts)
        {
            try { a.Secret = await SecureStorage.Default.GetAsync(SecretKey(a.Scope)); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AppSettings] SecureStorage read failed: {ex.Message}"); }
        }
        _accounts = accounts;

        await MigrateSingleGitLabAccountAsync();
        await MigrateGitLoginsAsync();
        _accountsLoaded = true;
    }

    /// <summary>Version 0.1 stored one GitLab URL and token; turn them into an account.</summary>
    async Task MigrateSingleGitLabAccountAsync()
    {
        var url = Preferences.Get("gitlab_url", "");
        if (string.IsNullOrEmpty(url)) return;
        string? token = null;
        try { token = await SecureStorage.Default.GetAsync("gitlab_token"); } catch { }
        if (!_accounts.Any(a => a.BaseUrl == url))
            await SaveAccountsAsync([.. _accounts, new HostAccount { Kind = HostingKind.GitLab, BaseUrl = url, Secret = token }]);
        Preferences.Remove("gitlab_url");
        Preferences.Remove("use_token_for_git");
        SecureStorage.Default.Remove("gitlab_token");
    }

    /// <summary>
    /// Up to version 0.1.2, logins from the git sign-in dialog were kept apart from accounts; turn them into accounts.
    /// Where a GitLab or GitHub account already had a different token, a login with an access token replaces it (git
    /// was using the login), while a password login is dropped in favour of the token, which the API needs.
    /// </summary>
    async Task MigrateGitLoginsAsync()
    {
        if (!Preferences.ContainsKey("git_login_scopes")) return;
        List<string> scopes;
        try { scopes = JsonSerializer.Deserialize<List<string>>(Preferences.Get("git_login_scopes", "[]")) ?? []; }
        catch (JsonException) { scopes = []; }

        var accounts = _accounts.Select(a => a.Clone()).ToList();
        foreach (var scope in scopes)
        {
            try
            {
                if (await SecureStorage.Default.GetAsync("gitlogin:" + scope) is { } json &&
                    JsonSerializer.Deserialize<OldGitLogin>(json) is { } login)
                {
                    var userName = login.UserName is "oauth2" or "x-access-token" ? null : login.UserName;
                    var existing = accounts.FirstOrDefault(a => login.Repository is null
                        ? a.Repository is null && HostAccount.SameHost(a.BaseUrl, login.Server)
                        : string.Equals(a.Repository, login.Repository, StringComparison.OrdinalIgnoreCase));
                    if (existing is null)
                    {
                        accounts.Add(new HostAccount
                        {
                            Kind = KindOf(login.Server),
                            BaseUrl = login.Server,
                            Repository = login.Repository,
                            UserName = userName,
                            Secret = login.Secret,
                        });
                    }
                    else if (string.IsNullOrEmpty(existing.Secret) || !existing.UsesApi || GitAuth.LooksLikeToken(login.Secret))
                    {
                        existing.UserName = userName;
                        existing.Secret = login.Secret;
                    }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AppSettings] git login migration failed: {ex.Message}"); }
            SecureStorage.Default.Remove("gitlogin:" + scope);
        }
        await SaveAccountsAsync(accounts);
        Preferences.Remove("git_login_scopes");
    }

    sealed record OldGitLogin(string Server, string UserName, string Secret, string? Repository = null);

    /// <summary>The kind of server at a URL, as far as accounts and earlier probes know.</summary>
    HostingKind KindOf(string url)
        => HostAccount.SameHost(url, GitHubUrl)
            ? HostingKind.GitHub
            : KnownHosts.FirstOrDefault(k => HostAccount.SameHost(k.BaseUrl, url))?.Kind ?? HostingKind.Unknown;

    /// <summary>Saves the accounts: remembered ones in Preferences and SecureStorage, the others only until the app exits.</summary>
    public async Task SaveAccountsAsync(IEnumerable<HostAccount> accounts)
    {
        var list = accounts.Select(a => a.Clone()).ToList();
        foreach (var gone in _accounts.Where(o => !list.Any(n => string.Equals(n.Scope, o.Scope, StringComparison.OrdinalIgnoreCase))))
            SecureStorage.Default.Remove(SecretKey(gone.Scope));
        foreach (var a in list)
        {
            a.Secret = string.IsNullOrWhiteSpace(a.Secret) ? null : a.Secret.Trim();
            if (a.Remember && a.Secret is not null) await SecureStorage.Default.SetAsync(SecretKey(a.Scope), a.Secret);
            else SecureStorage.Default.Remove(SecretKey(a.Scope));
        }
        Preferences.Set("accounts", JsonSerializer.Serialize(list.Where(a => a.Remember).ToList()));
        _accounts = list;
    }

    /// <summary>The account git uses for a remote: one for that repository (when given), else one for its whole server.</summary>
    public HostAccount? FindAccount(string server, string? repository = null)
        => (repository is null ? null : _accounts.FirstOrDefault(a => string.Equals(a.Repository, repository, StringComparison.OrdinalIgnoreCase)))
           ?? _accounts.FirstOrDefault(a => a.Repository is null && HostAccount.SameHost(a.BaseUrl, server));

    /// <summary>
    /// Git config that signs git in with the accounts, through http.&lt;url&gt;.extraHeader settings passed in the
    /// environment, so a secret never appears on a command line and is only sent to its own server or repository.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> GetGitConfig()
        => GitAuth.ExtraHeaderConfig(_accounts
            .Where(a => !string.IsNullOrEmpty(a.Secret))
            .Select(a => new GitCredential(a.Scope, a.Repository is not null, a.GitUserName, a.Secret!)));

    // ── Host detection cache ─────────────────────────────────────────────────

    /// <summary>What <see cref="HostProbe"/> found for hosts without an account, so each host is probed once.</summary>
    public Dictionary<string, HostingKind> DetectedHosts
    {
        get
        {
            try { return JsonSerializer.Deserialize<Dictionary<string, HostingKind>>(Preferences.Get("detected_hosts", "{}")) ?? []; }
            catch (JsonException) { return []; }
        }
        set => Preferences.Set("detected_hosts", JsonSerializer.Serialize(value));
    }

    /// <summary>Hosts whose kind is known: accounts first (they win), then probe results.</summary>
    public IReadOnlyList<KnownHost> KnownHosts =>
    [
        .. _accounts.Where(a => a.Kind != HostingKind.Unknown).Select(a => new KnownHost(a.Kind, a.BaseUrl)),
        .. DetectedHosts.Where(d => d.Value != HostingKind.Unknown).Select(d => new KnownHost(d.Value, "https://" + d.Key)),
    ];
}
