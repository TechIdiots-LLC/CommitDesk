using System.Text;

namespace CommitDesk.Core.Git;

/// <summary>
/// Builds a patch containing only the selected lines of a diff, for <c>git apply --cached</c>.
/// Unselected removals become context; unselected additions are dropped.
/// </summary>
public static class PartialPatchBuilder
{
    /// <returns>The patch text, or null when no line is selected.</returns>
    public static string? Build(FileDiff diff)
    {
        var sb = new StringBuilder();
        bool allDeletionsSelected = diff.ChangeLines.All(l => l.Kind != DiffLineKind.Delete || l.IsSelected);

        string? oldPath = diff.HeaderLines.FirstOrDefault(h => h.StartsWith("--- a/"))?[6..];

        foreach (var h in diff.HeaderLines)
        {
            if (h.StartsWith("index ")) continue;
            // A partially staged deletion leaves the file in place, so it becomes a modification.
            if (diff.IsDeletedFile && !allDeletionsSelected)
            {
                if (h.StartsWith("deleted file mode")) continue;
                if (h.StartsWith("+++ ") && oldPath is not null)
                {
                    sb.Append("+++ b/").Append(oldPath).Append('\n');
                    continue;
                }
            }
            sb.Append(h).Append('\n');
        }

        int delta = 0;
        bool any = false;
        foreach (var hunk in diff.Hunks)
        {
            if (!hunk.Lines.Any(l => l.IsChange && l.IsSelected))
                continue;

            var body = new StringBuilder();
            int oldCount = 0, newCount = 0;
            bool lastKept = false;
            foreach (var l in hunk.Lines)
            {
                switch (l.Kind)
                {
                    case DiffLineKind.Context:
                        body.Append(l.Raw).Append('\n'); oldCount++; newCount++; lastKept = true;
                        break;
                    case DiffLineKind.Delete when l.IsSelected:
                        body.Append(l.Raw).Append('\n'); oldCount++; lastKept = true;
                        break;
                    case DiffLineKind.Delete:
                        body.Append(' ').Append(l.Raw, 1, l.Raw.Length - 1).Append('\n'); oldCount++; newCount++; lastKept = true;
                        break;
                    case DiffLineKind.Add when l.IsSelected:
                        body.Append(l.Raw).Append('\n'); newCount++; lastKept = true;
                        break;
                    case DiffLineKind.Add:
                        lastKept = false;
                        break;
                    case DiffLineKind.NoNewline when lastKept:
                        body.Append(l.Raw).Append('\n');
                        break;
                }
            }

            int newStart = hunk.OldStart + delta;
            if (oldCount == 0) newStart++;
            if (newCount == 0) newStart--;
            sb.Append($"@@ -{hunk.OldStart},{oldCount} +{newStart},{newCount} @@\n");
            sb.Append(body);
            delta += newCount - oldCount;
            any = true;
        }

        return any ? sb.ToString() : null;
    }
}
