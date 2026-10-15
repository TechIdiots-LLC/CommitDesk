using System.Text.RegularExpressions;

namespace CommitDesk.Core.Git;

/// <summary>A conflicted file: how many conflict markers are left, and which sides still have the file.</summary>
/// <param name="Markers">"&lt;&lt;&lt;&lt;&lt;&lt;&lt; " lines in the working file.</param>
/// <param name="HasOurs">False when the current side deleted the file (modify/delete conflict).</param>
/// <param name="HasTheirs">False when the incoming side deleted it.</param>
/// <param name="Unmerged">False once the file has been resolved (staged): git no longer lists it as conflicted.</param>
public sealed record ConflictInfo(int Markers, bool HasOurs, bool HasTheirs, bool ExistsOnDisk, bool Unmerged = true);

/// <summary>What each side of a conflict is called: "ours" is the branch being changed, "theirs" what comes in.</summary>
public sealed record ConflictSides(string Ours, string Theirs);

public enum ConflictChoice
{
    /// <summary>Keep the current side's version ("ours").</summary>
    Ours,
    /// <summary>Take the incoming side's version ("theirs").</summary>
    Theirs,
    /// <summary>Keep the working file as edited (or its deletion) and mark it resolved.</summary>
    AsIs,
}

public sealed partial class GitRepository
{
    [GeneratedRegex(@"^Merge (?:remote-tracking )?branch(?:es)? '([^']+)'")]
    private static partial Regex MergeMessageBranch();

    public async Task<ConflictInfo> GetConflictAsync(string path)
    {
        if (UseLibGit2) return await Lg2Async(r => Lg2Conflict(r, path));
        // Unmerged index entries: stage 1 = common ancestor, 2 = ours, 3 = theirs
        var stages = (await Run(["ls-files", "-u", "-z", "--", path], throwOnError: false)).StdOut
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Split(' ', 3) is [_, _, var rest] && rest.Length > 0 ? rest[0] : '0')
            .ToHashSet();

        var full = System.IO.Path.Combine(Path, path);
        var markers = 0;
        if (File.Exists(full))
        {
            foreach (var line in File.ReadLines(full))
                if (line.StartsWith("<<<<<<< ", StringComparison.Ordinal) || line == "<<<<<<<") markers++;
        }
        return new ConflictInfo(markers, stages.Contains('2'), stages.Contains('3'), File.Exists(full), Unmerged: stages.Count > 0);
    }

    /// <summary>
    /// Names for the two sides. During a rebase they swap: "ours" is the branch being rebased onto and "theirs" is
    /// your own commit being replayed. After a stash is restored with conflicts, "theirs" is the stashed changes.
    /// </summary>
    public async Task<ConflictSides> GetConflictSidesAsync(RepositoryOperation op)
    {
        string gitDir, current;
        if (UseLibGit2)
            (gitDir, current) = await Lg2Async(r => (r.Info.Path, r.Info.IsHeadDetached ? "" : r.Head.FriendlyName));
        else
        {
            gitDir = (await Run("rev-parse", "--absolute-git-dir")).StdOut.Trim();
            current = (await Run(["rev-parse", "--abbrev-ref", "HEAD"], throwOnError: false)).StdOut.Trim();
        }
        if (current is "" or "HEAD") current = "the current commit";

        string Read(string relative)
        {
            var f = System.IO.Path.Combine(gitDir, relative);
            return File.Exists(f) ? File.ReadAllText(f).Trim() : "";
        }

        async Task<string> NameOf(string rev, string fallback)
        {
            if (rev.Length == 0) return fallback;
            if (UseLibGit2)
            {
                var revision = rev == "MERGE_HEAD" ? Read("MERGE_HEAD") : rev;
                return await Lg2Async(repo => NameOfCommit(repo, revision, fallback));
            }
            var r = await Run(["name-rev", "--name-only", "--no-undefined", "--exclude=tags/*", rev], throwOnError: false);
            var name = r.StdOut.Trim();
            if (!r.Success || name.Length == 0) return fallback;
            // "main~2" is a commit on main; "remotes/origin/main" reads better as "origin/main"
            name = name.Split('~', '^')[0];
            return name.StartsWith("remotes/", StringComparison.Ordinal) ? name["remotes/".Length..] : name;
        }

        switch (op)
        {
            case RepositoryOperation.Merge:
            {
                var match = MergeMessageBranch().Match(Read("MERGE_MSG"));
                var theirs = match.Success ? match.Groups[1].Value : await NameOf("MERGE_HEAD", "the merged branch");
                return new ConflictSides(current, theirs);
            }
            case RepositoryOperation.Rebase:
            {
                var dir = Directory.Exists(System.IO.Path.Combine(gitDir, "rebase-merge")) ? "rebase-merge" : "rebase-apply";
                var head = Read($"{dir}/head-name");
                var mine = head.StartsWith("refs/heads/", StringComparison.Ordinal) ? head["refs/heads/".Length..] : "your commits";
                var onto = await NameOf(Read($"{dir}/onto"), "the upstream branch");
                return new ConflictSides(onto, mine);
            }
            case RepositoryOperation.CherryPick:
            {
                var sha = Read("CHERRY_PICK_HEAD");
                return new ConflictSides(current, sha.Length >= 7 ? $"commit {sha[..7]}" : "the cherry-picked commit");
            }
            case RepositoryOperation.Revert:
                return new ConflictSides(current, "the revert");
            default:
                return new ConflictSides(current, "your stashed changes");
        }
    }

    /// <summary>Resolves one conflicted file and marks it resolved (staged).</summary>
    public async Task ResolveConflictAsync(string path, ConflictChoice choice)
    {
        if (UseLibGit2)
        {
            await Lg2Async(r => Lg2ResolveConflict(r, path, choice));
            return;
        }
        var info = await GetConflictAsync(path);
        var keep = choice switch
        {
            ConflictChoice.Ours => info.HasOurs,
            ConflictChoice.Theirs => info.HasTheirs,
            _ => info.ExistsOnDisk,
        };
        if (!keep)
        {
            // The chosen side deleted the file
            await Run("rm", "--quiet", "--force", "--ignore-unmatch", "--", path);
            return;
        }
        if (choice != ConflictChoice.AsIs)
            await Run("checkout", choice == ConflictChoice.Ours ? "--ours" : "--theirs", "--", path);
        await Run("add", "--", path);
    }
}
