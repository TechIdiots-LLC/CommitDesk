namespace CommitDesk.Core.Git;

public sealed partial class GitRepository
{
    /// <summary>
    /// Runs a command that moves HEAD or the working tree (switch, merge, pull…) with submodule recursion off, then
    /// brings the submodules in line with <see cref="SyncSubmodulesAsync"/>. With submodule.recurse on, git's own
    /// recursive checkout fails on a branch that adds a submodule ("not a git repository: .git/modules/…") because it
    /// can't clone one, and leaves the working tree half switched.
    /// </summary>
    async Task<GitResult> RunMovingHeadAsync(IEnumerable<string> args, CancellationToken ct = default)
    {
        await RemoveBrokenSubmoduleStubsAsync();
        var result = await Run(["-c", "submodule.recurse=false", .. args], ct: ct);
        await SyncSubmodulesAsync(ct);
        return result;
    }

    Task<GitResult> RunMovingHeadAsync(params string[] args) => RunMovingHeadAsync((IEnumerable<string>)args);

    /// <summary>
    /// When the repository has submodules and git is set to recurse into them (submodule.recurse, Options › Include
    /// submodules), clones any that are new and checks each out at the commit the current branch records.
    /// </summary>
    public async Task SyncSubmodulesAsync(CancellationToken ct = default)
    {
        if (!File.Exists(System.IO.Path.Combine(Path, ".gitmodules"))) return;
        var recurse = await Run(["config", "--type=bool", "--get", "submodule.recurse"], throwOnError: false);
        if (recurse.Success && recurse.StdOut.Trim() == "true")
        {
            await RemoveBrokenSubmoduleStubsAsync();
            await Run(["submodule", "update", "--init", "--recursive"], ct: ct);
        }
    }

    /// <summary>
    /// A recursive checkout that failed on a new submodule leaves stubs that break git in that repository ("fatal: not a
    /// git repository: vendor/x/../../.git/modules/vendor/x", even for <c>git status</c>): .git/modules/&lt;name&gt;
    /// holding only a config file (sometimes hooks too) but no repository, and a working folder &lt;name&gt; holding
    /// only a .git file pointing at it. Neither has any data, so both are removed; the submodule is cloned afresh when a
    /// branch needs it.
    /// </summary>
    /// <remarks>
    /// The walk never looks inside a real submodule repository (a folder with HEAD or objects) except for its own
    /// "modules" folder of nested submodules: folders such as objects/pack, refs/heads or hooks inside one have files
    /// and no HEAD of their own, and must never be mistaken for stubs.
    /// </remarks>
    /// <returns>Whether anything was removed.</returns>
    public async Task<bool> RemoveBrokenSubmoduleStubsAsync()
    {
        var gitDir = (await Run("rev-parse", "--absolute-git-dir")).StdOut.Trim();
        var modules = System.IO.Path.Combine(gitDir, "modules");
        if (!Directory.Exists(modules)) return false;

        static bool IsRepository(string dir)
            => File.Exists(System.IO.Path.Combine(dir, "HEAD")) || Directory.Exists(System.IO.Path.Combine(dir, "objects"));

        // A group folder ("vendor" in .git/modules/vendor/x) has repositories somewhere below it
        static bool HasRepositoryBelow(string dir)
            => Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories).Any(IsRepository);

        var removed = false;
        void Visit(string dir)
        {
            foreach (var child in Directory.EnumerateDirectories(dir).ToList())
            {
                if (IsRepository(child))
                {
                    var nested = System.IO.Path.Combine(child, "modules");
                    if (Directory.Exists(nested)) Visit(nested);
                    continue;
                }
                // git gives every submodule folder its own config; a group folder ("vendor") has none. A folder with a
                // config but no repository is a stub, unless (oddly) a repository sits below it.
                if (!File.Exists(System.IO.Path.Combine(child, "config")) || HasRepositoryBelow(child))
                {
                    Visit(child);
                    continue;
                }

                // A stub. Its working folder is the module name, with nested "modules/" levels dropped
                var name = System.IO.Path.GetRelativePath(modules, child).Replace('\\', '/');
                var workDir = System.IO.Path.Combine(Path, name.Replace("/modules/", "/"));
                var gitFile = System.IO.Path.Combine(workDir, ".git");
                if (File.Exists(gitFile) &&
                    Directory.EnumerateFileSystemEntries(workDir).Count() == 1 &&
                    File.ReadAllText(gitFile).Replace('\\', '/').TrimEnd().EndsWith("modules/" + name, StringComparison.Ordinal))
                    Directory.Delete(workDir, recursive: true);
                Directory.Delete(child, recursive: true);
                removed = true;
            }
        }
        Visit(modules);
        return removed;
    }
}
