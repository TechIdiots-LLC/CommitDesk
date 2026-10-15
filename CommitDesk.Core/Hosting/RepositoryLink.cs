namespace CommitDesk.Core.Hosting;

/// <summary>
/// A link that asks a desktop git client to open a repository: GitHub's "Open with GitHub Desktop"
/// (x-github-client://openRepo/https://github.com/owner/repo?branch=main&amp;filepath=README.md, and the older
/// github-windows: / github-mac: forms), or the same shape with this app's own commitdesk: scheme.
/// </summary>
/// <param name="CloneUrl">The repository's URL, e.g. https://github.com/owner/repo.</param>
/// <param name="Branch">The branch to switch to, if the link names one.</param>
/// <param name="FilePath">A file in the repository the link points at, if any.</param>
public sealed record RepositoryLink(string CloneUrl, string? Branch, string? FilePath)
{
    /// <summary>
    /// The schemes the app understands. Only commitdesk: is the app's own (gitlab-desktop: from when it was called GitLab
    /// Desktop still works, for links saved then).
    /// </summary>
    public static readonly string[] Schemes = ["x-github-client", "github-windows", "github-mac", "commitdesk", "gitlab-desktop"];

    public static RepositoryLink? Parse(string? link)
    {
        if (string.IsNullOrWhiteSpace(link)) return null;
        link = link.Trim().Trim('"');
        var colon = link.IndexOf(':');
        if (colon <= 0 || !Schemes.Contains(link[..colon], StringComparer.OrdinalIgnoreCase)) return null;

        // "scheme://openRepo/<url>[?branch=…&filepath=…]"; the URL itself may be percent-encoded
        var rest = link[(colon + 1)..].TrimStart('/');
        const string open = "openRepo/";
        if (!rest.StartsWith(open, StringComparison.OrdinalIgnoreCase)) return null;
        rest = rest[open.Length..];

        string? query = null;
        var q = rest.IndexOf('?');
        if (q >= 0)
        {
            query = rest[(q + 1)..];
            rest = rest[..q];
        }
        var url = Uri.UnescapeDataString(rest);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) return null;

        string? Param(string name)
        {
            foreach (var pair in (query ?? "").Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                var key = eq < 0 ? pair : pair[..eq];
                if (key.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    var value = eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
                    return value.Length == 0 ? null : value;
                }
            }
            return null;
        }
        return new RepositoryLink(url.TrimEnd('/'), Param("branch"), Param("filepath"));
    }

    /// <summary>
    /// Splits a Windows command line (what a second launch passes on to the running app) into arguments, honouring
    /// double quotes like the C runtime does for the simple cases git clients produce.
    /// </summary>
    public static IReadOnlyList<string> SplitCommandLine(string? commandLine)
    {
        var args = new List<string>();
        if (string.IsNullOrEmpty(commandLine)) return args;
        var current = new System.Text.StringBuilder();
        bool inQuotes = false, any = false;
        foreach (var c in commandLine)
        {
            if (c == '"') { inQuotes = !inQuotes; any = true; continue; }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (any) args.Add(current.ToString());
                current.Clear();
                any = false;
                continue;
            }
            current.Append(c);
            any = true;
        }
        if (any) args.Add(current.ToString());
        return args;
    }
}
