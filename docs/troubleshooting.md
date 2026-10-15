# Troubleshooting

## The git command log

**Help › Git command log** shows the last 100 git commands the app ran, with their exit codes and error output. It's
the first place to look when something fails, and safe to share: passwords and tokens are never part of a command.

## Sign-in problems

| Symptom | What to check |
| --- | --- |
| The sign-in prompt keeps coming back | The password or token is wrong or has expired. GitHub only accepts a token, not your password. Create a new token with **Create token…**. |
| *Sign-in to … failed* for a token that used to work | The token expired or was revoked. Enter a new one; it replaces the saved account. |
| GitHub: *Permission denied* or *403* for an organization's repository | The organization may restrict personal access tokens. An owner may need to approve the token, or its resource owner must be the organization. |
| GitLab: *You are not allowed to push* | Your role in the project (for example Reporter) or a protected branch doesn't allow pushing. Check with a project maintainer. |
| One repository needs a different user | Sign in again for that repository and choose **Only this repository**. See [When git asks you to sign in](accounts.md#when-git-asks-you-to-sign-in). |
| SSH remotes fail | The app uses your normal SSH setup. Check that `ssh -T git@server` works in a terminal and that your key is loaded in the agent. |
| *SSL certificate problem: self-signed certificate in certificate chain* or *unable to get local issuer certificate* | The server's certificate comes from an authority git doesn't know, usually your organisation's own. On Windows, turn on **Use the Windows certificate store** in [Options › Certificates](options.md#certificates) (the app offers it when this happens). For a server with a truly self-signed certificate, add it to the servers whose certificates aren't checked. |
| Merge/pull requests or CI don't show | They need a GitLab or GitHub account with an access token for that server (Options › Accounts › **Test connection**). Other git servers don't have them. |

## Update problems

| Symptom | What to check |
| --- | --- |
| *The downloaded installer isn't validly signed* | The download was damaged or isn't an official release. Try again, or download the installer from the Releases page and run it. |
| No update is offered | Check **Help › Check for updates…**. Updates you skipped aren't offered at startup, and pre-releases are offered only when turned on in [Options](options.md#updates). |
| The update check fails | The app needs to reach the release pages over HTTPS. A proxy or firewall may block them. |

## Git not found

*git was not found* or similar errors mean git isn't installed or isn't on your PATH. Install it (see
[Git is required](getting-started.md#git-is-required)), or set its full path in
[Options › Tools](options.md#tools) and click **Test git**.

## Crashes

If the app closes unexpectedly, details are saved in the crash log:

- **Windows**: `%LOCALAPPDATA%\CommitDesk\CommitDesk\Data\crash.log`

Please include it, along with what you were doing, when reporting a problem. You can report problems as an issue in
this project.
