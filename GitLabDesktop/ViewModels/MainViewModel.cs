using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitLabDesktop.Converters;
using GitLabDesktop.Core.Git;
using GitLabDesktop.Core.Hosting;
using GitLabDesktop.Services;

namespace GitLabDesktop.ViewModels;

/// <summary>
/// State for the main window: the open repository, its working-tree changes and history, and the
/// merge/pull request and CI status of the current branch on GitLab or GitHub. Split across partial files by menu area.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    readonly AppSettings _settings;
    readonly GitRunner _git;
    readonly HostingRegistry _hosting;
    readonly GitSignInService _signIn;
    readonly CertificateTrustService _certificates;
    readonly DialogService _dialogs;
    readonly PlatformActions _platform;
    readonly IServiceProvider _services;

    bool _refreshing;
    bool _refreshPending;
    DateTimeOffset? _lastFetched;
    string? _defaultBranch;

    public MainViewModel(AppSettings settings, GitRunner git, HostingRegistry hosting, GitSignInService signIn, CertificateTrustService certificates,
        DialogService dialogs, PlatformActions platform, IServiceProvider services)
    {
        _settings = settings;
        _git = git;
        _hosting = hosting;
        _signIn = signIn;
        _certificates = certificates;
        _dialogs = dialogs;
        _platform = platform;
        _services = services;
        _git.ConfigProvider = _settings.GetGitConfig;
        _git.CommandCompleted += OnGitCommandCompleted;
        Repositories.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasRepositoryList));
            OnPropertyChanged(nameof(OpenRepositoryListText));
        };
    }

    public bool HasRepositoryList => Repositories.Count > 0;
    public string OpenRepositoryListText => $"Open one of your {Repositories.Count} repositories…";

    // ── State ────────────────────────────────────────────────────────────────

    public ObservableCollection<string> Repositories { get; } = [];

    /// <summary>Recent git commands and their output, newest last (Help › Git command log).</summary>
    public List<string> CommandLog { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRepository), nameof(RepoName), nameof(NoRepository))]
    private GitRepository? _repo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BranchName), nameof(SyncTitle), nameof(SyncSubtitle), nameof(CommitButtonText),
        nameof(CanCommit), nameof(UpdateFromDefaultText))]
    private RepositoryStatus? _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOperation), nameof(OperationBanner), nameof(ShowOperationBanner))]
    private RepositoryOperation _operation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRemote), nameof(SyncSubtitle))]
    private HostedRemote? _remote;

    /// <summary>The open merge/pull request from the current branch (the badge next to the branch).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChangeRequestText), nameof(HasChangeRequest), nameof(ViewChangeRequestText))]
    [NotifyCanExecuteChangedFor(nameof(ViewChangeRequestCommand))]
    private ChangeRequest? _changeRequest;

    /// <summary>
    /// When the branch has no open request: its latest merged or closed one, so Branch › View pull request still
    /// opens it after it is merged.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewChangeRequestText))]
    [NotifyCanExecuteChangedFor(nameof(ViewChangeRequestCommand))]
    private ChangeRequest? _pastChangeRequest;

    /// <summary>The request Branch › View opens: the open one, else the latest merged or closed one.</summary>
    ChangeRequest? ViewableChangeRequest => ChangeRequest ?? PastChangeRequest;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCi))]
    private CiStatus? _ci;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _busyText;
    [ObservableProperty] private string? _statusMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChangesTab))]
    private bool _isHistoryTab;

    public bool IsChangesTab => !IsHistoryTab;

    /// <summary>The diff takes the full width, hiding the file and commit lists.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiffExpandText), nameof(ShowLists))]
    private bool _isDiffExpanded;

    public bool ShowLists => !IsDiffExpanded;
    public string DiffExpandText => IsDiffExpanded ? "⤡ Show lists" : "⤢ Full width";

    [RelayCommand]
    void ToggleDiffExpanded() => IsDiffExpanded = !IsDiffExpanded;
    public bool HasRepository => Repo is not null;
    public bool NoRepository => Repo is null;
    public bool HasRemote => Remote is not null;
    public string RepoName => Repo?.Name ?? "No repository";

    public string BranchName => Status switch
    {
        null => "—",
        { Branch: { } b } => b,
        { HeadSha: { } sha } => $"HEAD detached at {sha[..7]}",
        _ => "(detached)",
    };

    public bool HasOperation => Operation != RepositoryOperation.None;

    /// <summary>Also for conflicts with nothing in progress, e.g. from a squash merge or restoring a stash.</summary>
    public bool ShowOperationBanner => HasOperation || HasConflicts;

    public string OperationBanner => Operation switch
    {
        RepositoryOperation.Merge => "A merge is in progress. Resolve any conflicts, then continue, or abort the merge.",
        RepositoryOperation.Rebase => "A rebase is in progress. Resolve any conflicts, then continue, or abort the rebase.",
        RepositoryOperation.CherryPick => "A cherry-pick is in progress. Resolve any conflicts, then continue, or abort.",
        RepositoryOperation.Revert => "A revert is in progress. Resolve any conflicts, then continue, or abort.",
        _ => HasConflicts ? "Some files have conflicts. Resolve them, then commit." : "",
    };

    public bool HasChangeRequest => ChangeRequest is not null;
    public string? ChangeRequestText => ChangeRequest?.Reference;
    public bool HasCi => Ci is not null;

    // ── Host-specific wording (GitLab: merge requests/pipelines, GitHub: pull requests/Actions) ──

    /// <summary>"GitLab", "GitHub", or the hostname when the server type is unknown.</summary>
    public string ProviderName => Remote?.ProviderName ?? "remote";
    string RequestName => Remote?.ChangeRequestName ?? "merge request";

    public string ViewOnHostText => $"View on {ProviderName}";
    public string CreateIssueText => $"Create issue on {ProviderName}";
    public string ViewIssuesText => $"View issues on {ProviderName}";
    public string ViewChangeRequestsText => $"View {RequestName}s on {ProviderName}";
    public string ViewCiListText => $"View {Remote?.CiListName ?? "pipelines"} on {ProviderName}";
    public string CompareToBranchText => $"Compare to branch on {ProviderName}…";
    public string CompareOnHostText => $"Compare on {ProviderName}";
    public string ViewBranchOnHostText => $"View branch on {ProviderName}";
    public string CreateChangeRequestText => $"Create {RequestName}…";
    public string ViewChangeRequestText => ViewableChangeRequest is { } cr
        ? $"View {RequestName} {cr.Reference}{cr.State switch { ChangeRequestState.Merged => " (merged)", ChangeRequestState.Closed => " (closed)", _ => "" }} on {ProviderName}"
        : $"View {RequestName} on {ProviderName}";
    public string CopyChangeRequestLinkText => $"Copy link to {RequestName}";
    public string ViewCiText => $"View {Remote?.CiName ?? "pipeline"} on {ProviderName}";
    public string OpenOnHostText => $"Open the repository on {ProviderName}";

    static readonly string[] HostTextProperties =
    [
        nameof(ProviderName), nameof(ViewOnHostText), nameof(CreateIssueText), nameof(ViewIssuesText),
        nameof(ViewChangeRequestsText), nameof(ViewCiListText), nameof(CompareToBranchText), nameof(CompareOnHostText),
        nameof(ViewBranchOnHostText), nameof(CreateChangeRequestText), nameof(ViewChangeRequestText), nameof(ViewCiText),
        nameof(OpenOnHostText), nameof(CopyChangeRequestLinkText),
    ];

    partial void OnRemoteChanged(HostedRemote? value)
    {
        foreach (var p in HostTextProperties) OnPropertyChanged(p);
    }

    string RemoteName => Status?.Upstream is { } u && u.Contains('/') ? u[..u.IndexOf('/')] : "origin";

    public string SyncTitle => Status switch
    {
        { Branch: not null, Upstream: null } => "Publish branch",
        { Behind: > 0 } => $"Pull {RemoteName}",
        { Ahead: > 0 } => $"Push {RemoteName}",
        _ => $"Fetch {RemoteName}",
    };

    public string SyncSubtitle => Status switch
    {
        null => "",
        { Branch: not null, Upstream: null } => $"Publish this branch to {ProviderName}",
        { Behind: > 0 } s => s.Ahead > 0 ? $"↓{s.Behind}  ↑{s.Ahead}" : $"↓{s.Behind}",
        { Ahead: > 0 } s => $"↑{s.Ahead}",
        _ => _lastFetched is { } t ? $"Last fetched {RelativeTimeConverter.Format(t)}" : "Never fetched",
    };

    // ── Startup ──────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        await _settings.LoadAccountsAsync();
        ApplySettings();
        foreach (var r in _settings.Repositories) Repositories.Add(r);
        if (_settings.AutoAddRepositories) await ScanRepositoriesFolderAsync();

        // "GitLabDesktop.exe <folder>" opens that repository, like "github ." for GitHub Desktop, and an
        // "Open with GitHub Desktop" link opens (or clones) its repository; otherwise the last one opens again.
        var handled = await HandleLaunchArgumentsAsync(Environment.GetCommandLineArgs().Skip(1).ToList());
        var last = _settings.LastRepository;
        if ((!handled || Repo is null) && last is not null && Directory.Exists(last)) await OpenRepositoryAsync(last);
    }

    public void ApplySettings()
    {
        _git.GitExecutable = _settings.GitExecutable;
        _hosting.Rebuild();
        if (Repo is not null) _ = LoadRemoteAsync();
    }

    /// <summary>
    /// Adds checkouts found in the repositories folder (e.g. Documents\GitHub) to the list, skipping any the
    /// user removed. Returns how many were added.
    /// </summary>
    public async Task<int> ScanRepositoriesFolderAsync()
    {
        var dir = _settings.RepositoriesDirectory;
        var found = await Task.Run(() => RepositoryScanner.FindRepositories(dir));
        var hidden = _settings.HiddenRepositories.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var known = Repositories.ToHashSet(StringComparer.OrdinalIgnoreCase);

        int added = 0;
        foreach (var path in found.Where(p => !hidden.Contains(p) && !known.Contains(p)))
        {
            Repositories.Add(path);
            added++;
        }
        if (added > 0) _settings.Repositories = [.. Repositories];
        return added;
    }

    void OnGitCommandCompleted(string command, GitResult result)
    {
        var entry = $"> {command}  (exit {result.ExitCode})\n{result.StdErr}".TrimEnd();
        lock (CommandLog)
        {
            CommandLog.Add(entry);
            if (CommandLog.Count > 300) CommandLog.RemoveRange(0, CommandLog.Count - 300);
        }
    }

    // ── Opening and refreshing ───────────────────────────────────────────────

    public async Task OpenRepositoryAsync(string path)
    {
        Repo = new GitRepository(_git, path);
        Status = null;
        Operation = RepositoryOperation.None;
        Remote = null;
        ChangeRequest = null;
        Ci = null;
        _defaultBranch = null;
        _lastFetched = null;
        ChangedFiles.Clear();
        SelectedChange = null;
        Commits.Clear();
        SelectedCommit = null;
        CommitSummary = "";
        CommitDescription = "";
        IsAmending = false;

        if (!Repositories.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            Repositories.Add(path);
            _settings.Repositories = [.. Repositories];
        }
        // Opening a repository explicitly undoes an earlier "Remove".
        var hidden = _settings.HiddenRepositories;
        if (hidden.RemoveAll(h => string.Equals(h, path, StringComparison.OrdinalIgnoreCase)) > 0)
            _settings.HiddenRepositories = hidden;
        _settings.LastRepository = path;

        await RefreshAsync(full: true);
        await LoadRemoteAsync();
    }

    async Task LoadRemoteAsync()
    {
        if (Repo is null) return;
        var repo = Repo;
        // May probe the server to learn whether it runs GitLab or GitHub.
        var remote = await _hosting.ResolveAsync(await repo.GetRemoteUrlAsync(RemoteName));
        var defaultBranch = (await repo.GetRemoteDefaultBranchAsync(RemoteName))?.Split('/', 2)[^1];
        if (repo != Repo) return;   // switched repositories while probing
        Remote = remote;
        _defaultBranch = defaultBranch;
        OnPropertyChanged(nameof(UpdateFromDefaultText));
        await LoadHostingInfoAsync();
    }

    /// <summary>The API client for this repository's server, when the user has an account there.</summary>
    IHostingService? HostingService => _hosting.For(Remote);

    /// <summary>Open merge/pull request and CI status for the current branch.</summary>
    async Task LoadHostingInfoAsync()
    {
        if (HostingService is not { } service || Status?.Branch is not { } branch)
        {
            ChangeRequest = null;
            PastChangeRequest = null;
            Ci = null;
            ViewChangeRequestCommand.NotifyCanExecuteChanged();   // depends on whether there is an account
            return;
        }
        var remote = Remote!;
        try
        {
            if (_defaultBranch is null)
            {
                _defaultBranch = await service.GetDefaultBranchAsync(remote.ProjectPath);
                OnPropertyChanged(nameof(UpdateFromDefaultText));
            }
            var crTask = service.FindOpenChangeRequestAsync(remote.ProjectPath, branch);
            var ciTask = service.GetCiStatusAsync(remote.ProjectPath, branch);
            // The lookup by branch name only sees requests from the project's own branches; else look through them all
            var cr = await crTask ?? await FindChangeRequestForBranchAsync(service, lookUpDirectly: false);
            // No open one: the branch's latest merged or closed request, if it had one
            var past = cr is null ? await FindPastChangeRequestAsync(service, remote.ProjectPath, branch) : null;
            var ci = await ciTask;
            if (Remote != remote || Status?.Branch != branch) return;   // moved on meanwhile
            ChangeRequest = cr;
            PastChangeRequest = past;
            Ci = ci;
            ViewChangeRequestCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainViewModel] {remote.ProviderName} info failed: {ex.Message}");
            StatusMessage = $"{remote.ProviderName}: {ex.Message}";
        }
    }

    public async Task RefreshAsync(bool full = false)
    {
        if (Repo is null) return;
        if (_refreshing)
        {
            _refreshPending = true;
            return;
        }
        _refreshing = true;
        try
        {
            do
            {
                _refreshPending = false;
                var repo = Repo;
                if (!Directory.Exists(repo.Path))
                {
                    StatusMessage = $"Repository folder not found: {repo.Path}";
                    return;
                }
                var status = await repo.GetStatusAsync();
                var op = await repo.GetOperationAsync();
                if (repo != Repo)
                {
                    // Switched repositories meanwhile: discard this result and refresh the new one.
                    _refreshPending = true;
                    full = true;
                    continue;
                }

                bool headChanged = Status?.HeadSha != status.HeadSha || Status?.Branch != status.Branch;
                bool branchChanged = Status?.Branch != status.Branch;
                // A push or fetch moves the upstream without moving HEAD, which changes which commits are unpushed.
                bool upstreamChanged = Status?.Upstream != status.Upstream || Status?.Ahead != status.Ahead;
                Status = status;
                Operation = op;
                await UpdateChangedFilesAsync(status.Files);

                if (op != RepositoryOperation.None && string.IsNullOrEmpty(CommitSummary))
                {
                    var prepared = await repo.GetPreparedCommitMessageAsync();
                    if (prepared is not null) SetCommitMessage(prepared);
                }

                if (full || headChanged || upstreamChanged) await LoadHistoryAsync();
                if (branchChanged && !full) _ = LoadHostingInfoAsync();
                StatusMessage = null;
            }
            while (_refreshPending);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            _refreshing = false;
        }
    }

    [RelayCommand]
    Task Refresh() => RefreshAsync(full: true);

    /// <summary>Runs a user action with a busy indicator, reports failures, then refreshes.</summary>
    async Task RunAsync(string busyText, Func<Task> action, bool refresh = true)
    {
        if (IsBusy) return;
        IsBusy = true;
        BusyText = busyText;
        bool signedIn = false, conflicted = false;
        try
        {
            // A login git doesn't have (or that was rejected) prompts for one and retries, until it works or is cancelled.
            while (true)
            {
                try
                {
                    await action();
                    break;
                }
                catch (GitException ex) when (GitSignInService.IsAuthenticationFailure(ex) && Repo is { } repo)
                {
                    BusyText = "Waiting for sign-in…";
                    var outcome = await _signIn.PromptAsync(await repo.GetRemoteUrlAsync(RemoteName), ex.Message);
                    if (outcome == SignInOutcome.Cancelled) break;
                    if (outcome == SignInOutcome.NotApplicable) throw;
                    signedIn = true;
                    BusyText = busyText;
                }
                catch (GitException ex) when (CertificateTrustService.IsCertificateProblem(ex) && Repo is { } repo)
                {
                    // An internal CA or a self-signed server: offer the Windows store, or skipping this server's check
                    BusyText = "Waiting for a certificate decision…";
                    if (!await _certificates.PromptAsync(await repo.GetRemoteUrlAsync(RemoteName), ex.Message)) break;
                    BusyText = busyText;
                }
            }
        }
        catch (GitException ex) when (GitErrors.StoppedOnConflicts(ex.Message))
        {
            // A merge, rebase, pull, cherry-pick or revert that stopped on conflicts: the Resolve conflicts dialog
            // (after the refresh below) says more than git's text.
            conflicted = true;
        }
        catch (GitException ex)
        {
            await _dialogs.AlertAsync("Git", ex.Message);
        }
        catch (Core.GitLab.GitLabApiException ex)
        {
            await _dialogs.AlertAsync("GitLab", ex.Message);
        }
        catch (Core.GitHub.GitHubApiException ex)
        {
            await _dialogs.AlertAsync("GitHub", ex.Message);
        }
        catch (Exception ex)
        {
            await _dialogs.AlertAsync("Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
        }
        if (refresh || conflicted) await RefreshAsync();
        // A token entered at sign-in may also have set up the API account (merge requests, CI status).
        if (signedIn) await LoadRemoteAsync();
        if (conflicted) await ResolveConflicts();
    }

    async Task<bool> RequireRemoteAsync()
    {
        if (Remote is not null) return true;
        await _dialogs.AlertAsync("No remote",
            "This repository has no remote that points at a hosted project. Set one in Repository › Repository settings.");
        return false;
    }

    /// <summary>Opens a web link, or explains why there is none (a server whose type could not be determined).</summary>
    async Task OpenLinkAsync(string? link)
    {
        if (link is not null)
        {
            await _platform.OpenUrlAsync(link);
            return;
        }
        await _dialogs.AlertAsync("Unknown server",
            $"GitLab Desktop could not tell whether {Remote?.Host} runs GitLab or GitHub, so it does not know its page layout. " +
            "Add an account for this server in File › Options to tell it.");
    }

    async Task<bool> RequireBranchAsync()
    {
        if (Status?.Branch is not null) return true;
        await _dialogs.AlertAsync("Detached HEAD", "Check out a branch first.");
        return false;
    }

    [RelayCommand]
    void ShowChanges() => IsHistoryTab = false;

    [RelayCommand]
    void ShowHistory() => IsHistoryTab = true;
}
