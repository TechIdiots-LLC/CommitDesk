using LibGit2Sharp;

namespace CommitDesk.Core.Git;

/// <summary>
/// libgit2's errors in the words of git's, where the app looks for them (<see cref="GitErrors"/>, <see cref="GitAuth"/>,
/// <see cref="GitCertificates"/>): a refused sign-in asks for one, an untrusted certificate offers the certificate
/// options, a rejected push offers to fetch, and local changes in the way offer to stash or bring them.
/// </summary>
public static class LibGit2Errors
{
    /// <param name="action">What was being done, for local changes in the way: "checkout" or "merge".</param>
    public static GitException ToGitException(LibGit2SharpException ex, string action = "checkout")
    {
        var message = ex switch
        {
            NonFastForwardException =>
                $" ! [rejected]        (fetch first)\nerror: failed to push some refs\nhint: Updates were rejected because the remote contains work that you do not have locally. ({ex.Message})",
            CheckoutConflictException =>
                $"error: Your local changes to the following files would be overwritten by {action}:\n{ex.Message}\nPlease commit your changes or stash them before you {(action == "merge" ? "merge" : "switch branches")}.",
            EmptyCommitException => "nothing to commit (no changes are included)",
            _ => Describe(ex.Message),
        };
        return new GitException(message, new GitResult(128, "", ex.Message));
    }

    /// <summary>A libgit2 error message, reworded where the app looks for git's wording.</summary>
    public static string Describe(string message)
    {
        if (message.Contains("401", StringComparison.Ordinal) || message.Contains("authentication", StringComparison.OrdinalIgnoreCase))
            return $"fatal: Authentication failed ({message})";
        if (message.Contains("certificate", StringComparison.OrdinalIgnoreCase))
            return $"fatal: SSL certificate problem: {message}";
        return $"fatal: {message}";
    }
}
