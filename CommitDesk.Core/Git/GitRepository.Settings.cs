namespace CommitDesk.Core.Git;

public sealed partial class GitRepository
{
    string GitIgnorePath => System.IO.Path.Combine(Path, ".gitignore");

    /// <summary>The repository's top-level .gitignore, or "" when there is none.</summary>
    public async Task<string> ReadGitIgnoreAsync()
        => File.Exists(GitIgnorePath) ? await File.ReadAllTextAsync(GitIgnorePath) : "";

    /// <summary>
    /// Writes the top-level .gitignore, keeping the file's existing line endings (text boxes hand back \r or \r\n on
    /// Windows) and ending it with a newline. Clearing the text leaves an empty file rather than deleting a file git
    /// may track; with no file yet, empty text creates nothing.
    /// </summary>
    public async Task WriteGitIgnoreAsync(string text)
    {
        var existing = File.Exists(GitIgnorePath) ? await File.ReadAllTextAsync(GitIgnorePath) : null;
        var newline = existing?.Contains("\r\n") == true ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
        var content = string.IsNullOrWhiteSpace(lines) ? "" : lines.Replace("\n", newline) + newline;
        if (content == existing) return;
        if (existing is null && content.Length == 0) return;   // nothing to create
        await File.WriteAllTextAsync(GitIgnorePath, content);
    }

    /// <summary>A value from this repository's own config (not inherited from the global one), or null.</summary>
    public async Task<string?> GetLocalConfigAsync(string key)
    {
        var r = await Run(["config", "--local", "--get", key], throwOnError: false);
        return r.Success ? r.StdOut.TrimEnd('\r', '\n') : null;
    }

    /// <summary>A value from the user's global git config, or null.</summary>
    public async Task<string?> GetGlobalConfigAsync(string key)
    {
        var r = await Run(["config", "--global", "--get", key], throwOnError: false);
        return r.Success ? r.StdOut.TrimEnd('\r', '\n') : null;
    }

    /// <summary>Sets a value in this repository's config; null or empty removes it.</summary>
    public async Task SetLocalConfigAsync(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            await Run(["config", "--local", "--unset", key], throwOnError: false);   // exit 5 when it wasn't set
        else
            await Run("config", "--local", key, value.Trim());
    }
}
