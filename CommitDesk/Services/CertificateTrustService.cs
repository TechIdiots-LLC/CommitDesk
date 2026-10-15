using CommitDesk.Core.Git;

namespace CommitDesk.Services;

/// <summary>
/// Handles git refusing a server's certificate. On Windows it first offers the Windows certificate store, which trusts a
/// company's own certificate authority when Windows does (Git for Windows' OpenSSL has its own list without it). Failing
/// that, it offers to stop checking that one server's certificates, with a warning. Both are in Options › Git too.
/// </summary>
public sealed class CertificateTrustService(GitRunner git, DialogService dialogs)
{
    public static bool IsCertificateProblem(Exception ex) => ex is GitException && GitCertificates.IsCertificateProblem(ex.Message);

    /// <summary>True when a setting changed and the git command should run again; false to report git's error.</summary>
    public async Task<bool> PromptAsync(string? remoteUrl, string gitError)
    {
        if (GitCertificates.ServerKey(remoteUrl) is not { } server) return false;
        var host = new Uri(server).Authority;

        if (OperatingSystem.IsWindows() && !await GitCertificates.UsesWindowsCertificateStoreAsync(git))
        {
            if (await dialogs.ConfirmAsync("Certificate not trusted",
                    $"git doesn't trust the certificate of {host}:\n\n{gitError}\n\n" +
                    "Git for Windows checks certificates against its own list of certificate authorities, not the ones Windows " +
                    "trusts, so a server whose certificate comes from your organisation's own authority fails even when your " +
                    "browser accepts it. Use the Windows certificate store instead (for all of git), and try again?",
                    "Use the Windows certificate store", "Not now"))
            {
                await GitCertificates.SetUseWindowsCertificateStoreAsync(git, true);
                return true;
            }
        }

        if (!await dialogs.ConfirmAsync("Certificate not trusted",
                $"The certificate of {host} isn't trusted:\n\n{gitError}\n\n" +
                $"If {host} is a server you know, with a self-signed certificate, git can stop checking its certificate. " +
                "Only do this for a server you trust: without the check, someone between you and the server could pretend to be " +
                "it and see your sign-in and code. It applies to this server only; Options › Git lists the servers and turns " +
                "checking back on.",
                $"Don't check {host}", "Cancel"))
            return false;
        await GitCertificates.AddUntrustedServerAsync(git, server);
        return true;
    }
}
