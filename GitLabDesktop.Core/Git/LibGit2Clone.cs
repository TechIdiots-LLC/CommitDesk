using LibGit2Sharp;

namespace GitLabDesktop.Core.Git;

/// <summary>
/// Clones with libgit2 (through LibGit2Sharp) instead of the git command line, for platforms without one (Android).
/// It signs in with the same extraHeader settings git gets (<see cref="GitAuth.ExtraHeaderConfig"/>), reports progress
/// as git-style lines so <see cref="GitProgress.Parse"/> and the clone dialog treat it like git's, and fails with a
/// <see cref="GitException"/> whose message the sign-in and certificate prompts recognise.
/// </summary>
public static class LibGit2Clone
{
    public static Task CloneAsync(string url, string targetDir, bool recurseSubmodules,
        IReadOnlyList<KeyValuePair<string, string>>? config, CancellationToken ct = default, Action<string>? progress = null)
        => Task.Run(() => Clone(url, targetDir, recurseSubmodules, config, ct, progress), ct);

    static void Clone(string url, string targetDir, bool recurseSubmodules,
        IReadOnlyList<KeyValuePair<string, string>>? config, CancellationToken ct, Action<string>? progress)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(targetDir))!);
        progress?.Invoke($"Cloning into '{Path.GetFileName(targetDir)}'...");

        var options = new CloneOptions { RecurseSubmodules = recurseSubmodules };
        options.FetchOptions.CustomHeaders = GitAuth.ExtraHeadersFor(url, config).ToArray();
        options.FetchOptions.OnTransferProgress = p =>
        {
            if (p.TotalObjects > 0 && p.ReceivedObjects < p.TotalObjects)
                progress?.Invoke($"Receiving objects: {Percent(p.ReceivedObjects, p.TotalObjects)}% ({p.ReceivedObjects}/{p.TotalObjects}), {Size(p.ReceivedBytes)}");
            else if (p.TotalObjects > 0)
                // LibGit2Sharp reports indexed objects rather than deltas; it is the same last phase of the download
                progress?.Invoke($"Resolving deltas: {Percent(p.IndexedObjects, p.TotalObjects)}% ({p.IndexedObjects}/{p.TotalObjects})");
            return !ct.IsCancellationRequested;   // false cancels the transfer
        };
        options.OnCheckoutProgress = (_, completed, total) =>
        {
            if (total > 0) progress?.Invoke($"Updating files: {Percent(completed, total)}% ({completed}/{total})");
        };

        try
        {
            Repository.Clone(url, targetDir, options);
        }
        catch (UserCancelledException)
        {
            throw new OperationCanceledException(ct);
        }
        catch (LibGit2SharpException ex)
        {
            throw new GitException(Describe(ex.Message), new GitResult(128, "", ex.Message));
        }
    }

    /// <summary>
    /// libgit2's errors in the words of git's, where the app looks for them: a refused sign-in (to ask for one) and an
    /// untrusted certificate (to offer the certificate options).
    /// </summary>
    public static string Describe(string message)
    {
        if (message.Contains("401", StringComparison.Ordinal) || message.Contains("authentication", StringComparison.OrdinalIgnoreCase))
            return $"fatal: Authentication failed ({message})";
        if (message.Contains("certificate", StringComparison.OrdinalIgnoreCase))
            return $"fatal: SSL certificate problem: {message}";
        return $"fatal: {message}";
    }

    static int Percent(long done, long total) => total == 0 ? 0 : (int)(done * 100 / total);

    static string Size(long bytes) => bytes >= 1 << 20 ? $"{bytes / 1048576.0:0.00} MiB" : $"{bytes / 1024.0:0.00} KiB";
}
