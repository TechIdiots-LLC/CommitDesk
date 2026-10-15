using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommitDesk.Core.Git;
using CommitDesk.Core.Hosting;
using CommitDesk.Services;

namespace CommitDesk.ViewModels;

/// <summary>
/// What the user entered in the git sign-in dialog. A null username means an access token without one. Kind is the
/// server type: detected, or chosen in the dialog when the app couldn't tell.
/// </summary>
public sealed record GitSignInInput(string? UserName, string Secret, bool Remember, bool ThisRepositoryOnly, HostingKind Kind);

/// <summary>
/// Asks for a username and password or access token after git could not authenticate to an HTTPS remote.
/// Completes with the input, or null when cancelled.
/// </summary>
public sealed partial class GitSignInViewModel : ModalViewModel<GitSignInInput?>
{
    readonly HostedRemote? _remote;
    readonly bool? _serverAccountUsesApi;

    /// <param name="rejected">The title of the account git just used and the server rejected, if any.</param>
    /// <param name="serverAccountUsesApi">
    /// Whether the server's existing account is also the app's API sign-in, or null when the server has no account yet
    /// (then it depends on the server type).
    /// </param>
    public GitSignInViewModel(string server, HostedRemote? remote, string? repositoryName, string? currentUser,
        string? rejected, bool? serverAccountUsesApi, string gitError)
    {
        _remote = remote;
        _serverAccountUsesApi = serverAccountUsesApi;
        Host = new Uri(server).Host;
        RepositoryName = repositoryName;
        GitError = gitError.Trim();
        _userName = currentUser ?? "";

        // Only an unrecognised server's type is asked for; a detected one is just stated.
        CanChooseServerType = remote is not null && remote.Kind == HostingKind.Unknown;
        _serverTypeIndex = ServerTypes.IndexOf(remote?.Kind ?? HostingKind.Unknown);

        Heading = rejected is not null ? $"Sign-in to {Host} failed" : $"Sign in to {Host}";
        Message = rejected is not null
            ? $"{Host} rejected the sign-in from your {rejected} account{(currentUser is null ? "" : $" ({currentUser})")}. " +
              "Enter a new password or access token, or cancel."
            : $"Git needs a username and a password or access token to reach {Host}.";
    }

    protected override GitSignInInput? CancelledResult => null;

    public string Host { get; }
    public string? RepositoryName { get; }
    public bool HasRepositoryName => RepositoryName is not null;
    public string Heading { get; }
    public string Message { get; }
    public string GitError { get; }

    // ── Server type ──────────────────────────────────────────────────────────

    public bool CanChooseServerType { get; }
    public bool ShowsServerType => !CanChooseServerType && ServerTypeText.Length > 0;
    public IReadOnlyList<string> ServerTypeNames => ServerTypes.Names;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Kind), nameof(ServerTypeText), nameof(SecretHelp), nameof(CanCreateToken),
        nameof(ServerUsesApi), nameof(ShowApiNote), nameof(ApiNote))]
    private int _serverTypeIndex;

    public HostingKind Kind => ServerTypes.KindAt(ServerTypeIndex);

    bool IsGitHubDotCom => Kind == HostingKind.GitHub && _remote?.IsOn(AppSettings.GitHubUrl) == true;

    public string ServerTypeText => Kind switch
    {
        HostingKind.GitLab => $"{Host} is a GitLab server.",
        HostingKind.GitHub => IsGitHubDotCom ? "" : $"{Host} is a GitHub Enterprise server.",
        _ => "",
    };

    public string ServerTypeHelp =>
        $"The app couldn't tell what {Host} runs. Choose GitLab or GitHub Enterprise to get merge/pull requests and links " +
        "that match it, or Other for git only.";

    /// <summary>Whether a whole-server sign-in here is also the API sign-in (GitLab servers and github.com).</summary>
    public bool ServerUsesApi => _serverAccountUsesApi ?? (Kind == HostingKind.GitLab || IsGitHubDotCom);

    /// <summary>Shown while the sign-in is for the whole server and that account is also the API sign-in.</summary>
    public bool ShowApiNote => ServerUsesApi && ForAllRepositories;

    public string ApiNote => Kind == HostingKind.GitHub
        ? "This account also shows pull requests and checks status, which need an access token rather than a password."
        : "This account also shows merge requests and pipeline status, which need an access token rather than a password.";

    public string SecretHelp => Kind switch
    {
        HostingKind.GitHub => "GitHub needs an access token here, not your account password (scopes: repo, workflow).",
        HostingKind.GitLab => "With an access token (scope write_repository) the username can be left blank.",
        _ => "A password, or an access token if the server requires one.",
    };

    public bool CanCreateToken => Kind == HostingKind.GitLab || IsGitHubDotCom;

    // ── Sign-in ──────────────────────────────────────────────────────────────

    [ObservableProperty] private string _userName;
    [ObservableProperty] private string _secret = "";
    [ObservableProperty] private bool _remember = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowApiNote))]
    private bool _forAllRepositories = true;

    [ObservableProperty] private bool _forThisRepository;

    partial void OnForAllRepositoriesChanged(bool value) { if (value) ForThisRepository = false; }
    partial void OnForThisRepositoryChanged(bool value) { if (value) ForAllRepositories = false; }

    [RelayCommand] void ChooseAllRepositories() => ForAllRepositories = true;
    [RelayCommand] void ChooseThisRepository() => ForThisRepository = true;

    [RelayCommand]
    async Task CreateToken()
    {
        if (_remote is null || !CanCreateToken) return;
        var url = Kind == HostingKind.GitHub
            ? "https://github.com/settings/tokens/new?description=GitLab+Desktop&scopes=repo,workflow,read:user"
            : $"{_remote.WebUrl[..^(_remote.ProjectPath.Length + 1)]}/-/user_settings/personal_access_tokens" +
              "?name=GitLab+Desktop&scopes=api,read_user,write_repository";
        await Launcher.Default.OpenAsync(new Uri(url));
    }

    [RelayCommand]
    void SignIn()
    {
        var secret = Secret.Trim();
        if (secret.Length == 0)
        {
            Error = "Enter a password or access token.";
            return;
        }
        var user = UserName.Trim();
        if (user.Length == 0 && !GitAuth.LooksLikeToken(secret) && Kind != HostingKind.GitHub)
        {
            Error = "Enter your username to sign in with a password.";
            return;
        }
        // Without a username, git sends the token with the host's conventional one (see HostAccount.GitUserName).
        Complete(new GitSignInInput(user.Length == 0 ? null : user, secret, Remember, ForThisRepository && HasRepositoryName, Kind));
    }

    [RelayCommand]
    void Close() => Cancel();
}
