namespace CommitDesk.Core.Git;

/// <summary>Parses <c>git status --porcelain=v2 --branch -z</c>.</summary>
public static class StatusParser
{
    public static RepositoryStatus Parse(string output)
    {
        string? branch = null, oid = null, upstream = null;
        int ahead = 0, behind = 0;
        var files = new List<FileChange>();

        var entries = output.Split('\0');
        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            if (e.Length == 0) continue;

            switch (e[0])
            {
                case '#':
                    var parts = e.Split(' ');
                    if (parts.Length < 3) break;
                    switch (parts[1])
                    {
                        case "branch.oid": oid = parts[2] == "(initial)" ? null : parts[2]; break;
                        case "branch.head": branch = parts[2] == "(detached)" ? null : parts[2]; break;
                        case "branch.upstream": upstream = parts[2]; break;
                        case "branch.ab" when parts.Length >= 4:
                            ahead = int.Parse(parts[2].TrimStart('+'));
                            behind = int.Parse(parts[3].TrimStart('-'));
                            break;
                    }
                    break;

                case '1':
                {
                    // 1 XY sub mH mI mW hH hI path
                    var p = e.Split(' ', 9);
                    files.Add(new FileChange(p[8], null, KindFromXY(p[1])));
                    break;
                }
                case '2':
                {
                    // 2 XY sub mH mI mW hH hI Xscore path \0 origPath
                    var p = e.Split(' ', 10);
                    var orig = i + 1 < entries.Length ? entries[++i] : null;
                    var kind = p[8].StartsWith('C') ? FileChangeKind.Copied : FileChangeKind.Renamed;
                    files.Add(new FileChange(p[9], orig, kind));
                    break;
                }
                case 'u':
                {
                    // u XY sub m1 m2 m3 mW h1 h2 h3 path
                    var p = e.Split(' ', 11);
                    files.Add(new FileChange(p[10], null, FileChangeKind.Conflicted));
                    break;
                }
                case '?':
                    files.Add(new FileChange(e[2..], null, FileChangeKind.Untracked));
                    break;
            }
        }

        files.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
        return new RepositoryStatus
        {
            Branch = branch,
            HeadSha = oid,
            Upstream = upstream,
            Ahead = ahead,
            Behind = behind,
            Files = files,
        };
    }

    static FileChangeKind KindFromXY(string xy)
    {
        char x = xy[0], y = xy[1];
        if (x == 'A') return FileChangeKind.Added;
        if (x == 'D' || y == 'D') return FileChangeKind.Deleted;
        if (x == 'T' || y == 'T') return FileChangeKind.TypeChanged;
        return FileChangeKind.Modified;
    }
}
