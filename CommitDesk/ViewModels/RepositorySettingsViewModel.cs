using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommitDesk.Core.Git;

namespace CommitDesk.ViewModels;

/// <summary>
/// Repository › Repository settings, like GitHub Desktop's: the remote's URL, the .gitignore, and whether commits use
/// the global git name and email or ones set for this repository only. Completes with true when something was saved.
/// </summary>
public sealed partial class RepositorySettingsViewModel(GitRepository repo, string remoteName) : ModalViewModel<bool>
{
    protected override bool CancelledResult => false;

    public string RemoteName { get; } = remoteName;
    public string RemoteLabel => $"Primary remote repository ({RemoteName})";

    /// <summary>Whether Save changed the remote URL, so the caller re-reads which server the repository is on.</summary>
    public bool RemoteChanged { get; private set; }

    // ── Sections ─────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRemoteSection), nameof(IsIgnoreSection), nameof(IsConfigSection))]
    private int _section;

    public bool IsRemoteSection => Section == 0;
    public bool IsIgnoreSection => Section == 1;
    public bool IsConfigSection => Section == 2;

    [RelayCommand] void ShowSection(string index) => Section = int.Parse(index);

    // ── Values ───────────────────────────────────────────────────────────────

    [ObservableProperty] private string _remoteUrl = "";
    [ObservableProperty] private string _gitIgnore = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseGlobalConfig))]
    private bool _useLocalConfig;

    public bool UseGlobalConfig
    {
        get => !UseLocalConfig;
        set => UseLocalConfig = !value;
    }

    [ObservableProperty] private string _userName = "";
    [ObservableProperty] private string _email = "";

    string? _loadedRemote, _loadedIgnore, _localName, _localEmail, _globalName, _globalEmail;

    /// <summary>Switching to a local config starts from the global name and email; back to global shows those again.</summary>
    partial void OnUseLocalConfigChanged(bool value)
    {
        UserName = (value ? _localName ?? _globalName : _globalName) ?? "";
        Email = (value ? _localEmail ?? _globalEmail : _globalEmail) ?? "";
    }

    public async Task LoadAsync()
    {
        try
        {
            _loadedRemote = await repo.GetRemoteUrlAsync(RemoteName);
            RemoteUrl = _loadedRemote ?? "";
            _loadedIgnore = (await repo.ReadGitIgnoreAsync()).Replace("\r\n", "\n");
            GitIgnore = _loadedIgnore;
            _localName = await repo.GetLocalConfigAsync("user.name");
            _localEmail = await repo.GetLocalConfigAsync("user.email");
            _globalName = await repo.GetGlobalConfigAsync("user.name");
            _globalEmail = await repo.GetGlobalConfigAsync("user.email");
            UseLocalConfig = _localName is not null || _localEmail is not null;
            OnUseLocalConfigChanged(UseLocalConfig);   // fill the fields even when the value didn't change
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    [RelayCommand]
    async Task Save()
    {
        Error = null;
        if (UseLocalConfig && string.IsNullOrWhiteSpace(UserName) && string.IsNullOrWhiteSpace(Email))
        {
            Error = "Enter a name and email for this repository, or use your global Git config.";
            Section = 2;
            return;
        }
        IsBusy = true;
        try
        {
            var url = RemoteUrl.Trim();
            if (url.Length > 0 && url != (_loadedRemote ?? ""))
            {
                await repo.SetRemoteUrlAsync(RemoteName, url);
                RemoteChanged = true;
            }

            if (GitIgnore.Replace("\r\n", "\n").Replace('\r', '\n') != _loadedIgnore)
                await repo.WriteGitIgnoreAsync(GitIgnore);

            if (UseLocalConfig)
            {
                if (UserName.Trim() != (_localName ?? "")) await repo.SetLocalConfigAsync("user.name", UserName);
                if (Email.Trim() != (_localEmail ?? "")) await repo.SetLocalConfigAsync("user.email", Email);
            }
            else
            {
                // Back to the global config: drop this repository's own values
                if (_localName is not null) await repo.SetLocalConfigAsync("user.name", null);
                if (_localEmail is not null) await repo.SetLocalConfigAsync("user.email", null);
            }
            Complete(true);
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
    void Close() => Cancel();
}
