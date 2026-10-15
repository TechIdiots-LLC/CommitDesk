namespace CommitDesk.Core.Git;

/// <summary>Plain-text patches for copying (e.g. into an AI assistant or an issue): what git diff and git show print.</summary>
public sealed partial class GitRepository
{
    // Plain unified diffs: no colour or external diff tool, whatever the user's config says
    static readonly string[] PlainDiff = ["--no-color", "--no-ext-diff"];

    /// <summary>
    /// The uncommitted changes to these files (or to every changed file) against the last commit, staged or not, as one
    /// unified diff. New untracked files are included in full, as git would show them once added.
    /// </summary>
    public async Task<string> GetWorkingPatchAsync(IEnumerable<FileChange>? files = null, bool isUnborn = false)
    {
        if (UseLibGit2) return await Lg2Async(r => Lg2WorkingPatch(r, files?.ToList()));
        var all = files is null;
        var changes = (files ?? (await GetStatusAsync()).Files).ToList();
        var parts = new List<string>();

        var tracked = changes.Where(f => f.Kind != FileChangeKind.Untracked).Select(f => f.Path).ToList();
        if (tracked.Count > 0 || all)
        {
            // Against HEAD covers staged and unstaged edits together; a repository without commits has only the index
            List<string> args = ["diff", .. PlainDiff, isUnborn ? "--cached" : "HEAD"];
            if (!all) args.AddRange(["--", .. tracked]);
            var r = await Run(args, throwOnError: false);
            if (r.StdOut.Length > 0) parts.Add(r.StdOut);
        }
        foreach (var untracked in changes.Where(f => f.Kind == FileChangeKind.Untracked))
        {
            // Exit code 1 just means "there are differences"
            var r = await Run(["diff", .. PlainDiff, "--no-index", "--", "/dev/null", untracked.Path], throwOnError: false);
            if (r.StdOut.Length > 0) parts.Add(r.StdOut);
        }
        return string.Concat(parts.Select(p => p.EndsWith('\n') ? p : p + "\n"));
    }

    /// <summary>One file's change in a commit (against its first parent), as a unified diff.</summary>
    public async Task<string> GetCommitFilePatchAsync(string sha, FileChange file)
    {
        if (UseLibGit2) return await Lg2Async(r => Lg2CommitDiffText(r, sha, file));
        List<string> args = ["show", .. PlainDiff, "--first-parent", "--format=", sha, "--", file.Path];
        if (file.OldPath is { } old) args.Add(old);   // both sides of a rename
        return (await Run(args)).StdOut;
    }

    /// <summary>A commit as git show prints it: author, date, message and its diff (against its first parent).</summary>
    public async Task<string> GetCommitPatchAsync(string sha)
        => UseLibGit2 ? await Lg2Async(r => Lg2CommitPatch(r, sha)) : (await Run(["show", .. PlainDiff, "--first-parent", "--format=medium", sha])).StdOut;
}
