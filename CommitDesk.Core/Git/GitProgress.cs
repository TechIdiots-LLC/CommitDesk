using System.Text.RegularExpressions;

namespace CommitDesk.Core.Git;

/// <summary>
/// One progress update from a clone: a readable status ("Receiving objects: 45% (450/1000), 1.20 MiB | 2.00 MiB/s")
/// and an overall fraction across git's phases, so the bar fills once instead of restarting at every phase.
/// </summary>
public sealed partial record GitProgress(string Status, double? Fraction)
{
    // "remote: Counting objects:  45% (450/1000)" / "Receiving objects: 100% (12/12), 1.2 MiB | 3 MiB/s, done."
    [GeneratedRegex(@"^(?:remote:\s*)?(?<phase>[A-Z][a-z]+(?: [a-z]+)*):\s+(?<pct>\d{1,3})%")]
    private static partial Regex PercentLine();

    // Where each phase sits in the overall bar, roughly by how long it takes (like GitHub Desktop's weighting)
    static readonly (string Phase, double Start, double End)[] Phases =
    [
        ("Enumerating objects", 0.00, 0.02),
        ("Counting objects", 0.02, 0.06),
        ("Compressing objects", 0.06, 0.10),
        ("Receiving objects", 0.10, 0.70),
        ("Resolving deltas", 0.70, 0.90),
        ("Updating files", 0.90, 1.00),
    ];

    /// <summary>Parses a line of <c>git clone --progress</c> output; null for lines that aren't progress.</summary>
    public static GitProgress? Parse(string line)
    {
        line = line.Trim();
        if (line.Length == 0) return null;

        if (line.StartsWith("Cloning into ", StringComparison.Ordinal))
            return new GitProgress(line.TrimEnd('.') + "…", 0);

        if (PercentLine().Match(line) is { Success: true } m)
        {
            var phase = m.Groups["phase"].Value;
            var pct = Math.Clamp(int.Parse(m.Groups["pct"].Value), 0, 100) / 100.0;
            var status = line.StartsWith("remote:", StringComparison.Ordinal) ? line["remote:".Length..].Trim() : line;
            if (status.EndsWith(", done.", StringComparison.Ordinal)) status = status[..^", done.".Length];
            var range = Phases.FirstOrDefault(p => p.Phase == phase);
            return new GitProgress(status, range.Phase is null ? null : range.Start + (range.End - range.Start) * pct);
        }

        // "remote: Enumerating objects: 1234, done." has no percentage
        if (line.StartsWith("remote: Enumerating objects", StringComparison.Ordinal))
            return new GitProgress(line["remote:".Length..].Trim().TrimEnd('.').Replace(", done", ""), 0.01);

        if (line.StartsWith("Submodule ", StringComparison.Ordinal) || line.StartsWith("Submodule path ", StringComparison.Ordinal))
            return new GitProgress(line, null);

        return null;
    }
}
