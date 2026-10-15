using System.Text.RegularExpressions;

namespace CommitDesk.Core.Git;

/// <summary>Parses the unified diff git prints for a single file.</summary>
public static partial class DiffParser
{
    [GeneratedRegex(@"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@")]
    private static partial Regex HunkHeaderRegex();

    public static FileDiff Parse(string path, string output, bool selectable, bool initiallySelected)
    {
        var diff = new FileDiff { Path = path };
        var lines = output.Split('\n');
        int count = lines.Length;
        if (count > 0 && lines[^1].Length == 0) count--;   // trailing newline

        DiffHunk? hunk = null;
        int oldNo = 0, newNo = 0;

        for (int i = 0; i < count; i++)
        {
            var raw = lines[i];

            if (hunk is null && !raw.StartsWith("@@"))
            {
                if (raw.StartsWith("diff --git") && diff.HeaderLines.Count > 0)
                    break;  // a second file; callers ask for one path at a time
                diff.HeaderLines.Add(raw);
                if (raw.StartsWith("new file mode")) diff.IsNewFile = true;
                else if (raw.StartsWith("deleted file mode")) diff.IsDeletedFile = true;
                else if (raw.StartsWith("Binary files") || raw.StartsWith("GIT binary patch")) diff.IsBinary = true;
                continue;
            }

            if (raw.StartsWith("@@"))
            {
                var m = HunkHeaderRegex().Match(raw);
                if (!m.Success) continue;
                int oldStart = int.Parse(m.Groups[1].Value);
                int oldCount = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 1;
                int newStart = int.Parse(m.Groups[3].Value);
                int newCount = m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 1;
                int index = diff.Hunks.Count;
                hunk = new DiffHunk
                {
                    Index = index,
                    OldStart = oldStart,
                    OldCount = oldCount,
                    NewStart = newStart,
                    NewCount = newCount,
                    Header = new DiffLine { Kind = DiffLineKind.Hunk, Raw = raw, HunkIndex = index, IsSelectable = selectable },
                };
                diff.Hunks.Add(hunk);
                oldNo = oldStart;
                newNo = newStart;
                continue;
            }

            if (hunk is null) continue;
            if (raw.StartsWith("diff --git")) break;

            DiffLine line;
            switch (raw.Length > 0 ? raw[0] : ' ')
            {
                case '+':
                    line = new DiffLine { Kind = DiffLineKind.Add, Raw = raw, NewLineNumber = newNo++, HunkIndex = hunk.Index, IsSelectable = selectable, IsSelected = initiallySelected };
                    break;
                case '-':
                    line = new DiffLine { Kind = DiffLineKind.Delete, Raw = raw, OldLineNumber = oldNo++, HunkIndex = hunk.Index, IsSelectable = selectable, IsSelected = initiallySelected };
                    break;
                case '\\':
                    line = new DiffLine { Kind = DiffLineKind.NoNewline, Raw = raw, HunkIndex = hunk.Index };
                    break;
                default:
                    line = new DiffLine { Kind = DiffLineKind.Context, Raw = raw.Length == 0 ? " " : raw, OldLineNumber = oldNo++, NewLineNumber = newNo++, HunkIndex = hunk.Index };
                    break;
            }
            hunk.Lines.Add(line);
        }

        return diff;
    }
}
