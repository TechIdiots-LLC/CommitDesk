using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommitDesk.Core.Git;
using CommitDesk.Core.Hosting;
using CommitDesk.Services;

namespace CommitDesk.ViewModels;

/// <summary>A configured account to browse in the Clone dialog.</summary>
public sealed record CloneSource(string Title, IHostingService Service)
{
    public override string ToString() => Title;
}

/// <summary>Clone a project from one of the accounts (or any URL). Completes with the local path, or null.</summary>
public sealed partial class CloneViewModel : ModalViewModel<string?>
{
    readonly AppSettings _settings;
    readonly GitRunner _git;
    readonly GitSignInService _signIn;
    readonly CertificateTrustService _certificates;
    CancellationTokenSource? _searchCts;
    CancellationTokenSource? _cloneCts;

    public CloneViewModel(AppSettings settings, HostingRegistry hosting, GitRunner git, GitSignInService signIn, CertificateTrustService certificates)
    {
        _settings = settings;
        _git = git;
        _signIn = signIn;
        _certificates = certificates;
        _baseDirectory = settings.RepositoriesDirectory;
        _useSsh = settings.CloneWithSsh;
        foreach (var (account, service) in hosting.Services.Where(s => s.Service.IsConfigured))
            Sources.Add(new CloneSource(account.Title, service));
        _selectedSource = Sources.FirstOrDefault();
    }

    protected override string? CancelledResult => null;

    public ObservableCollection<CloneSource> Sources { get; } = [];
    public ObservableCollection<HostedProject> Projects { get; } = [];

    public bool CanBrowse => Sources.Count > 0;
    public bool HasSeveralSources => Sources.Count > 1;
    public string InstanceText => CanBrowse
        ? "Choose one of your projects, or paste any repository URL below."
        : "Add a GitLab or GitHub account in Options to browse your projects, or paste any repository URL below.";

    [ObservableProperty] private CloneSource? _selectedSource;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private HostedProject? _selectedProject;
    [ObservableProperty] private string _url = "";
    [ObservableProperty] private string _baseDirectory;
    [ObservableProperty] private string _localPath = "";
    [ObservableProperty] private bool _useSsh;

    [ObservableProperty] private string _cloneStatus = "";
    [ObservableProperty] private double _cloneProgress;

    long _lastProgressTicks;

    /// <summary>
    /// git's progress lines, from the process reader's thread. Updates at most ten times a second (git redraws far more
    /// often), except for a new phase, so each phase's first and last lines aren't lost.
    /// </summary>
    void OnCloneProgress(string line)
    {
        if (GitProgress.Parse(line) is not { } p) return;
        var now = Environment.TickCount64;
        var newPhase = !p.Status.StartsWith(CloneStatus.Split(':')[0], StringComparison.Ordinal);
        if (!newPhase && !p.Status.Contains("100%") && now - Interlocked.Read(ref _lastProgressTicks) < 100) return;
        Interlocked.Exchange(ref _lastProgressTicks, now);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            CloneStatus = p.Status;
            // A submodule starts its own clone: let the bar run again rather than sit at 100%
            if (p.Fraction is { } f) CloneProgress = f;
        });
    }

    /// <summary>Starts as git's submodule.recurse setting (Options › Repositories); can be changed for this clone.</summary>
    [ObservableProperty] private bool _recurseSubmodules;

    public async Task InitializeAsync()
    {
        RecurseSubmodules = await GitRepository.GetDefaultBoolAsync(_git, "submodule.recurse") ?? false;
        await LoadProjectsAsync();
    }

    partial void OnSearchChanged(string value) => _ = LoadProjectsAsync();

    partial void OnSelectedSourceChanged(CloneSource? value) => _ = LoadProjectsAsync();

    async Task LoadProjectsAsync()
    {
        if (SelectedSource is not { } source) return;
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(250, cts.Token);   // debounce typing
            var list = await source.Service.ListProjectsAsync(Search, cts.Token);
            if (cts.IsCancellationRequested) return;
            Projects.Clear();
            foreach (var p in list) Projects.Add(p);
            Error = null;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    partial void OnSelectedProjectChanged(HostedProject? value)
    {
        if (value is not null) Url = UseSsh ? value.SshUrl : value.HttpUrl;
    }

    partial void OnUseSshChanged(bool value) => OnSelectedProjectChanged(SelectedProject);

    partial void OnUrlChanged(string value) => UpdateLocalPath();

    partial void OnBaseDirectoryChanged(string value) => UpdateLocalPath();

    void UpdateLocalPath()
    {
        var name = HostedRemote.Parse(Url, _settings.KnownHosts)?.ProjectPath.Split('/')[^1];
        LocalPath = name is null ? "" : Path.Combine(BaseDirectory, name);
    }

    [RelayCommand]
    async Task Browse()
    {
        try
        {
            var result = await CommunityToolkit.Maui.Storage.FolderPicker.Default.PickAsync(BaseDirectory, CancellationToken.None);
            if (result.IsSuccessful) BaseDirectory = result.Folder.Path;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    [RelayCommand]
    async Task Clone()
    {
        if (string.IsNullOrWhiteSpace(Url) || string.IsNullOrWhiteSpace(LocalPath)) return;
        if (Directory.Exists(LocalPath) && Directory.EnumerateFileSystemEntries(LocalPath).Any())
        {
            Error = $"{LocalPath} already exists and is not empty.";
            return;
        }
        IsBusy = true;
        Error = null;
        CloneStatus = "Connecting…";
        CloneProgress = 0;
        _cloneCts = new CancellationTokenSource();
        try
        {
            while (true)
            {
                try
                {
                    // Through libgit2 where there is no git (GitRunner.UseLibGit2)
                    await GitRepository.CloneAsync(_git, Url.Trim(), LocalPath, RecurseSubmodules, _cloneCts.Token, OnCloneProgress);
                    break;
                }
                catch (GitException ex) when (GitSignInService.IsAuthenticationFailure(ex))
                {
                    // A private repository: ask for a login and try again, or stop if the user cancels.
                    var outcome = await _signIn.PromptAsync(Url.Trim(), ex.Message);
                    if (outcome == SignInOutcome.Cancelled) return;
                    if (outcome == SignInOutcome.NotApplicable) throw;
                }
                catch (GitException ex) when (CertificateTrustService.IsCertificateProblem(ex))
                {
                    // An internal CA or a self-signed server: offer the Windows store, or skipping this server's check
                    if (!await _certificates.PromptAsync(Url.Trim(), ex.Message)) throw;
                }
            }
            _settings.CloneWithSsh = UseSsh;
            Complete(LocalPath);
        }
        catch (OperationCanceledException)
        {
            Error = "Clone cancelled.";
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    void Close()
    {
        _cloneCts?.Cancel();
        Cancel();
    }
}
