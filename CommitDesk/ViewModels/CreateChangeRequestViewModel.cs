using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommitDesk.Core.Git;
using CommitDesk.Core.Hosting;
using CommitDesk.Services;

namespace CommitDesk.ViewModels;

/// <summary>
/// Create a merge request (GitLab) or pull request (GitHub) for the current branch.
/// Completes with the created request, or null.
/// </summary>
public sealed partial class CreateChangeRequestViewModel(
    IHostingService service,
    PlatformActions platform,
    GitRepository repo,
    HostedRemote remote,
    string remoteName,
    string sourceBranch,
    string defaultTarget) : ModalViewModel<ChangeRequest?>
{
    protected override ChangeRequest? CancelledResult => null;

    public string SourceBranch => sourceBranch;
    public string ProjectPath => $"{remote.ProviderName} · {remote.ProjectPath}";
    public string Heading => $"Create {remote.ChangeRequestName}";
    public string CreateButtonText => Heading;

    /// <summary>Delete-source-branch and squash are per-request options on GitLab; on GitHub they are repository settings.</summary>
    public bool IsGitLab => remote.Kind == HostingKind.GitLab;

    public ObservableCollection<string> TargetBranches { get; } = [];
    public ObservableCollection<CommitInfo> Commits { get; } = [];

    [ObservableProperty] private string _targetBranch = defaultTarget;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private bool _isDraft;
    [ObservableProperty] private bool _removeSourceBranch = true;
    [ObservableProperty] private bool _squash;
    [ObservableProperty] private bool _assignToMe = true;
    [ObservableProperty] private bool _openAfterCreate = true;

    public string CommitsHeader => Commits.Count == 1 ? "1 commit" : $"{Commits.Count} commits";

    public async Task InitializeAsync()
    {
        try
        {
            foreach (var b in (await service.ListBranchesAsync(remote.ProjectPath)).Where(b => b != sourceBranch))
                TargetBranches.Add(b);
            if (!TargetBranches.Contains(TargetBranch) && TargetBranches.Count > 0)
                TargetBranch = TargetBranches[0];
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            if (!TargetBranches.Contains(TargetBranch)) TargetBranches.Add(TargetBranch);
        }
        await LoadCommitsAsync();
    }

    partial void OnTargetBranchChanged(string value) => _ = LoadCommitsAsync();

    /// <summary>Commits on the source branch that are not on the target; also seeds the title/description.</summary>
    async Task LoadCommitsAsync()
    {
        try
        {
            var commits = await repo.GetLogAsync(0, 200, $"{remoteName}/{TargetBranch}..{sourceBranch}");
            Commits.Clear();
            foreach (var c in commits) Commits.Add(c);
            OnPropertyChanged(nameof(CommitsHeader));

            if (string.IsNullOrEmpty(Title))
            {
                if (commits.Count == 1)
                {
                    Title = commits[0].Summary;
                    Description = commits[0].Body;
                }
                else
                {
                    Title = Humanize(sourceBranch);
                    Description = string.Join('\n', commits.Reverse().Select(c => $"- {c.Summary}"));
                }
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    /// <summary>"fix/security-cve_123" → "Security cve 123".</summary>
    static string Humanize(string branch)
    {
        var name = branch.Contains('/') ? branch[(branch.LastIndexOf('/') + 1)..] : branch;
        name = name.Replace('-', ' ').Replace('_', ' ').Trim();
        return name.Length == 0 ? branch : char.ToUpperInvariant(name[0]) + name[1..];
    }

    [RelayCommand]
    async Task Create()
    {
        if (string.IsNullOrWhiteSpace(Title) || string.IsNullOrWhiteSpace(TargetBranch)) return;
        IsBusy = true;
        Error = null;
        try
        {
            var created = await service.CreateChangeRequestAsync(remote.ProjectPath, new NewChangeRequest
            {
                SourceBranch = sourceBranch,
                TargetBranch = TargetBranch,
                Title = Title.Trim(),
                Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
                Draft = IsDraft,
                AssignToMe = AssignToMe,
                RemoveSourceBranch = IsGitLab && RemoveSourceBranch,
                Squash = IsGitLab && Squash,
            });
            if (OpenAfterCreate) await platform.OpenUrlAsync(created.WebUrl);
            Complete(created);
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
    async Task OpenInBrowser()
    {
        if (remote.NewChangeRequestLink(sourceBranch, TargetBranch) is { } link) await platform.OpenUrlAsync(link);
        Cancel();
    }

    [RelayCommand]
    void Close() => Cancel();
}
