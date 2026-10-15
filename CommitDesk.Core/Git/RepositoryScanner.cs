namespace CommitDesk.Core.Git;

/// <summary>Finds existing checkouts under a folder such as Documents\GitHub.</summary>
public static class RepositoryScanner
{
    static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", "packages", "vendor",
    };

    /// <summary>
    /// Returns the work-tree roots found up to <paramref name="maxDepth"/> levels below <paramref name="root"/>
    /// (1 = direct children). A folder that is a repository is not searched further, so nested
    /// submodules and dependency checkouts are not listed.
    /// </summary>
    public static List<string> FindRepositories(string root, int maxDepth = 2)
    {
        var found = new List<string>();
        if (!Directory.Exists(root)) return found;
        Scan(Path.GetFullPath(root), 1, maxDepth, found);
        found.Sort(StringComparer.OrdinalIgnoreCase);
        return found;
    }

    /// <summary>True when the folder is a work-tree root (".git" is a folder, or a file for worktrees and submodules).</summary>
    public static bool IsRepository(string dir)
    {
        var git = Path.Combine(dir, ".git");
        return Directory.Exists(git) || File.Exists(git);
    }

    static void Scan(string dir, int depth, int maxDepth, List<string> found)
    {
        IEnumerable<string> children;
        try { children = Directory.EnumerateDirectories(dir); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return; }

        foreach (var child in children)
        {
            var name = Path.GetFileName(child);
            if (name.StartsWith('.') || SkippedFolders.Contains(name)) continue;
            try
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;   // junctions/symlinks
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { continue; }

            if (IsRepository(child)) found.Add(child);
            else if (depth < maxDepth) Scan(child, depth + 1, maxDepth, found);
        }
    }
}
