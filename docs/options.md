# Options

**File › Options…** (Ctrl+,). Changes apply when you click **Save**.

## Accounts

GitLab, GitHub and other git server sign-ins. See [Accounts and signing in](accounts.md).

## Repositories

| Setting | |
| --- | --- |
| **Repositories folder** | Where clones and new repositories go by default, and where existing repositories are found. **Browse…** to choose a folder. |
| **Use default** | `Documents\GitLab`. |
| **Use GitHub Desktop folder** | `Documents\GitHub`, so repositories you cloned with GitHub Desktop show up too. |
| **Automatically add repositories found in this folder** | Adds repositories in the folder, and one level of subfolders, to the repository list. The line above it says how many were found. |
| **Clone with SSH instead of HTTPS** | The default for the **SSH** box in the clone dialog. |
| **Include submodules** | The default for **Include submodules** in the clone dialog. It is git's own `submodule.recurse` setting (in your global git config), so switching branches and pulling also update submodules, in the app and in git on the command line. Off unless you've turned it on. |
| **Support long file paths** (Windows) | Lets git clone and check out repositories with paths longer than Windows' usual 260 characters. It is git's `core.longpaths` setting. Git for Windows often turns it on for everyone in its own (system) config; the box shows what git actually uses, and turning it off writes `false` to your global config when that's needed to override the system one. |

## Certificates

| Setting | |
| --- | --- |
| **Use the Windows certificate store** (Windows) | Git checks HTTPS servers against the certificate authorities Windows trusts, like your browser, including an organisation's own authority that its domain or IT department added. Without it, Git for Windows uses OpenSSL with its own list of public authorities, so an internal server fails with *SSL certificate problem: self-signed certificate in certificate chain* even when the browser accepts it. It is git's `http.sslBackend` setting (`schannel`); the box shows what git actually uses, including the choice Git for Windows' installer made in its system config. |
| **Servers whose certificates aren't checked** | For a server you trust that has a truly self-signed certificate. Type its name and choose **Don't check this server…**; **Check again** removes it. Each is git's `http.<url>.sslVerify false` for that server only, so other servers, including GitHub, are still checked. Without the check, someone between you and the server could pretend to be it and see your sign-in and code, so prefer the Windows certificate store when it works. |

When git refuses a certificate while cloning, fetching, pulling or pushing, the app offers the same two choices and
tries again.

## Links from GitHub (Windows)

GitHub's **Code › Open with GitHub Desktop** button opens a repository in your desktop git app. GitLab Desktop can open
these links instead: it opens the repository if it's in your list (matched by server and project, whatever its remote
URL looks like), switching to the branch the link names, or opens the clone dialog with the URL filled in. If the app is
already running, the link goes to its window.

Windows decides which app opens these links, and only you can change that, so nothing is taken over silently:

- The section shows which app opens them now, for example GitHub Desktop.
- **Make GitLab Desktop the default…** registers GitLab Desktop as able to open them and opens Windows Settings at its
  default apps page: choose GitLab Desktop for **X-GITHUB-CLIENT** there. If no other app opens these links, GitLab
  Desktop becomes their handler straight away.
- **Stop opening these links** removes GitLab Desktop's registration (and your choice of it in Settings), so the other
  app opens them again. Uninstalling the app does the same. Another app's registration is never changed.

## Updates

| Setting | |
| --- | --- |
| **Check for a new version when the app starts** | On by default. **Help › Check for updates…** always works. |
| **Include pre-releases** | Also offer test versions such as `0.3.0-rc.1`. |

The installed version is shown below. See [Updates](getting-started.md#updates).

## Tools

| Setting | |
| --- | --- |
| **External editor command** | The command **Open in editor** runs, given the folder or file path. The default is `code` (Visual Studio Code). Examples: `code`, `notepad++`, `subl`, or a full path to the program. |
| **Git executable** | `git` from your PATH by default, or a full path to `git.exe`. **Test git** shows the version found. |

## Where settings are kept

Settings live in the app's local data folder:

- **Windows (installer)**: `%LOCALAPPDATA%\GitLabDesktop\GitLabDesktop\Settings`
- **macOS**: the app's container in `~/Library`

Passwords and tokens are kept in the system's secure storage, never in a plain settings file. The window's size and
position, the divider widths and the last open repository are remembered too.
