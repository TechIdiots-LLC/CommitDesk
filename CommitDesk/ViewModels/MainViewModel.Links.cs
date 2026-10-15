using CommitDesk.Core.Git;
using CommitDesk.Core.Hosting;
using CommitDesk.Views;
using Microsoft.Extensions.DependencyInjection;

namespace CommitDesk.ViewModels;

// Launch arguments: a repository folder ("CommitDesk.exe <folder>"), or a link such as GitHub's "Open with GitHub
// Desktop" (x-github-client://openRepo/<url>?branch=…), at startup or handed over by a later launch.
public sealed partial class MainViewModel
{
    /// <summary>Opens what the arguments ask for. Returns false when they name nothing the app can open.</summary>
    public async Task<bool> HandleLaunchArgumentsAsync(IReadOnlyList<string> args)
    {
        foreach (var arg in args)
        {
            if (RepositoryLink.Parse(arg) is { } link)
            {
                await OpenRepositoryLinkAsync(link);
                return true;
            }
            if (Directory.Exists(arg) && await GitRepository.FindRootAsync(_git, Path.GetFullPath(arg)) is { } root)
            {
                if (!string.Equals(root, Repo?.Path, StringComparison.OrdinalIgnoreCase))
                    await RunAsync("Opening…", () => OpenRepositoryAsync(root), refresh: false);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The repository from a link: one already in the list (matched by server and project, so an SSH or ".git" remote
    /// matches too) opens, else the clone dialog opens with its URL; then the link's branch, if it names one.
    /// </summary>
    async Task OpenRepositoryLinkAsync(RepositoryLink link)
    {
        var wanted = HostedRemote.Parse(link.CloneUrl);
        string? local = null;
        if (wanted is not null)
        {
            foreach (var path in Repositories.Where(Directory.Exists).ToList())
            {
                var url = await new GitRepository(_git, path).GetRemoteUrlAsync(RemoteName);
                if (HostedRemote.Parse(url) is { } remote &&
                    string.Equals(remote.Host, wanted.Host, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(remote.ProjectPath, wanted.ProjectPath, StringComparison.OrdinalIgnoreCase))
                {
                    local = path;
                    break;
                }
            }
        }

        if (local is not null)
        {
            if (!string.Equals(local, Repo?.Path, StringComparison.OrdinalIgnoreCase))
                await RunAsync("Opening…", () => OpenRepositoryAsync(local), refresh: false);
        }
        else
        {
            var vm = _services.GetRequiredService<CloneViewModel>();
            vm.Url = link.CloneUrl;
            await _dialogs.PushModalAsync(new ClonePage(vm));
            if (await vm.Result is not { } cloned) return;
            await RunAsync("Opening…", () => OpenRepositoryAsync(cloned), refresh: false);
        }

        if (link.Branch is { } branch && Repo is not null && Status?.Branch != branch)
        {
            var branches = await Repo.GetBranchesAsync();
            var target = branches.FirstOrDefault(b => !b.IsRemote && b.Name == branch)
                         ?? branches.FirstOrDefault(b => b.IsRemote && b.NameWithoutRemote == branch);
            if (target is not null) await SwitchToBranchAsync(target);
            else StatusMessage = $"The link's branch {branch} isn't in this repository (try Fetch).";
        }
    }
}
