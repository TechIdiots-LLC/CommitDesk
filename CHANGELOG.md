# Changelog

## master
### ✨ Features and improvements
- _...Add new stuff here..._

### 🐞 Bug fixes
- _...Add new stuff here..._

## 0.6.2
### ✨ Features and improvements

### 🐞 Bug fixes

## 0.6.1
### ✨ Features and improvements

### 🐞 Bug fixes

## 0.6.0
### ✨ Features and improvements
- add macos ci

## 0.5.3
### ✨ Features and improvements

### 🐞 Bug fixes

## 0.5.2
### ✨ Features and improvements
- **Open GitHub's "Open with GitHub Desktop" links** (Windows): Options › Links from GitHub can make GitLab Desktop the app for them, through Windows Settings so it never takes them over without your say. A link opens its repository if you have it (switching to its branch) or the clone dialog with the URL filled in.
- Only one copy of the app runs: launching it again, from a link or with `GitLabDesktop.exe <folder>`, brings the open window forward and opens it there.
- **Servers with internal or self-signed certificates**: Options › Certificates can make git use the Windows certificate store (so a server whose certificate comes from your organisation's own authority works, as it does in the browser), and lists servers whose certificates git doesn't check. When git refuses a certificate while cloning, fetching, pulling or pushing, the app offers these and tries again.

### 🐞 Bug fixes
- Clicking on the word "Changes" (or its count) in the Changes tab did nothing; only the space around it switched tabs.
- In History, the first file of a commit sometimes showed no diff (until another file was picked and then it again), when the previous commit's selected file had the same name and kind of change.

## 0.5.1
### ✨ Features and improvements

### 🐞 Bug fixes
- View pull request / merge request opened the list of all requests when there was no GitLab or GitHub account for the server. For a public github.com project it now finds the branch's request without an account; a request from a fork is found too (by the branch's latest commit), also for the toolbar's #12 badge; and when none can be found it opens the requests from that branch rather than all of them. The Pull/Merge requests tab now lists up to 300 open requests instead of 100.

## 0.5.0
### ✨ Features and improvements
- Right-click a file in a commit in History, like GitHub Desktop: show or open it, copy its diff in that commit or its path, or view the file as it was in that commit on GitLab/GitHub.

### 🐞 Bug fixes

## 0.4.7
### ✨ Features and improvements
- Right-click the repository or branch in the toolbar, like GitHub Desktop: copy the repository name, path or remote URL, or the branch name, its link or its merge/pull request link, plus quick access to rename, delete, view on GitLab/GitHub and open elsewhere.
- **Copy diffs for AI assistants and issues**: Copy diff on a changed file, Copy all changes as patch on the changes header, and Copy commit as patch in History copy plain unified diffs (new files included; commits with their message). The status bar confirms what was copied.

### 🐞 Bug fixes
- 0.4.6's cleanup of broken submodule leftovers could mistake folders inside real submodule repositories (such as `objects/pack` or `hooks`) for leftovers and try to delete them, failing with "Access to the path 'pack-….idx' is denied". It now only ever removes a submodule folder that has no repository in it, and never looks inside a real one.

## 0.4.6
### ✨ Features and improvements

### 🐞 Bug fixes
- A repository where a submodule checkout had failed could stay broken, with every refresh failing with "fatal: not a git repository: …/.git/modules/…". The app now finds and removes the empty leftovers such a failure leaves (any submodule, not just ones the current branch lists) when reading the status and before switching, pulling or merging.

## 0.4.5
### ✨ Features and improvements
- **Cherry-pick commit…** asks which branch to apply the commit to, like GitHub Desktop, and switches there first, instead of always using the current branch.
- Cloning shows git's progress, like GitHub Desktop: a progress bar across the whole clone and what git is doing ("Receiving objects: 45% (450/1000), 1.20 MiB | 2.00 MiB/s", "Resolving deltas", "Updating files"), instead of just "Cloning…".

### 🐞 Bug fixes
- Switching to a branch that adds a submodule failed with "not a git repository: …/.git/modules/…" when Include submodules (git's `submodule.recurse`) is on, leaving the working folder half switched. Switching, pulling, merging and rebasing now move the branch first and then update submodules (`git submodule update --init --recursive`), cloning new ones and clearing the empty leftovers such a failure leaves behind.

## 0.4.4
### ✨ Features and improvements
- Cloning shows git's progress, like GitHub Desktop: a progress bar across the whole clone and what git is doing ("Receiving objects: 45% (450/1000), 1.20 MiB | 2.00 MiB/s", "Resolving deltas", "Updating files"), instead of just "Cloning…".

### 🐞 Bug fixes

## 0.4.3
### ✨ Features and improvements
- **Support long file paths** in Options › Repositories (Windows): git's `core.longpaths`, for repositories with paths over 260 characters. The option shows what git uses, including Git for Windows' system setting. The Include submodules option now also follows the system config the same way.
- **Repository settings** is now a dialog like GitHub Desktop's, with **Remote** (the remote URL), **Ignored files** (edit the `.gitignore`) and **Git config** (use your global name and email, or set ones for this repository only).
- **Submodules**: the clone dialog has an **Include submodules** checkbox, and **Options › Repositories › Include submodules** sets its default. That option is git's own `submodule.recurse` setting, so switching branches and pulling also update submodules, in the app and on the command line. Clones used to always include submodules; they now follow this setting, which is off unless you've turned it on.

### 🐞 Bug fixes

## 0.4.2
### ✨ Features and improvements
- **Pull requests / Merge requests in the branch list**, like GitHub Desktop: a second tab lists the project's open requests with their number, author, age, draft state and CI status. Choosing one checks out its branch; one from a fork is added as a remote and tracked as `pr/12` (`mr/12` on GitLab), so pulling gets the author's new commits. Public github.com repositories are listed without an account (CI status then isn't shown); otherwise it needs a GitLab or GitHub account for the server.
- Long file paths keep the file name whole and shorten the folder from the front ("…Core/GitHub/GitHubClient.cs"), and hovering shows the full path.

### 🐞 Bug fixes

## 0.4.1
### ✨ Features and improvements
- Installing an update on Windows now shows an "Updating GitLab Desktop" progress window instead of running silently, then starts the app again. The installed version starts the installer, so this shows from the update after this one.

### 🐞 Bug fixes

## 0.4.0
### ✨ Features and improvements
- **Resolve conflicts in the app**, like GitHub Desktop: when a merge, rebase, pull, cherry-pick or revert stops on conflicts, a dialog lists every conflicted file with how many conflicts are left, with **Open in editor** and **Use a version** (either branch's file, or keeping a deletion). It watches the files, so a file saved without conflict markers shows as resolved straight away, with nothing to mark; **Continue** is enabled once all are. A banner and the Changes tab offer the same choices afterwards.
- A push refused because the server has newer commits now explains that and offers **Fetch**, after which the toolbar offers Pull with both counts, like GitHub Desktop.
- A pull blocked by uncommitted changes offers **Stash and pull**: the changes are set aside, the pull runs, and they are put back on top (any that clash are marked as conflicted, and a copy stays in the stash).

### 🐞 Bug fixes

## 0.3.1
### ✨ Features and improvements

### 🐞 Bug fixes
- After an update, the taskbar and Start menu could keep showing the old icon, because Windows had cached it. The installer now tells Explorer to refresh icons.

## 0.3.0
### ✨ Features and improvements
- **Image previews**: changed images (PNG, JPEG, GIF, BMP, ICO, WebP, TIFF) are shown as pictures instead of "Binary file changed", in Changes and History: the old one framed in red and the new one in green side by side, or a single "Added"/"Deleted" image, on a checkerboard so transparency shows, with format, pixel size and file size. Icons show their largest image.
- The repository and branch lists drop down under their toolbar buttons, like GitHub Desktop, instead of filling the window. Add/Clone and New branch are buttons beside the filter, branches are grouped into the default branch, other branches and remote branches, and Enter picks the first match while Esc or a click outside closes the list.
- **Git server accounts**: Options › Accounts › + Add Git server account signs git in to any other server over HTTPS (Gitea, Bitbucket Server, a plain git host and so on) with a username and password or token. These accounts are used for git only; merge/pull requests and CI need a GitLab or GitHub account.
- Help › Documentation (F1) opens the user guide.
- **Server type**: when the app can't tell whether a server runs GitLab or GitHub Enterprise, the sign-in prompt asks. A GitLab server behind a proxy can then still get merge requests, pipelines and **Create token…**, and GitHub Enterprise gets GitHub's wording and links. Every account in Options except github.com can have its type changed, and GitHub Enterprise accounts now have an editable server URL.
- New GitLab Desktop logo: the app, installer, taskbar and Start menu icons (sharp from 16 to 256 px on Windows), the macOS icon, the Android/iOS launcher icon and splash screen, and the welcome screen.

### 🐞 Bug fixes
- The "Use this sign-in for" choices in the sign-in prompt showed no text on Windows.
- The sign-in prompt now checks what the server runs before asking, instead of treating a server the app hadn't checked yet as a plain git server. A server that gives no answer isn't asked again for 10 minutes.
- Installing an update no longer fails with "The downloaded installer isn't validly signed" when releases are signed with a self-signed certificate. The installer must still be untampered and signed with exactly the certificate the running app is signed with.

## 0.2.0
### ✨ Features and improvements
- Accounts and saved git sign-ins are now one list of accounts. An account signs git in to its server (or one repository), and on GitLab and GitHub it is also used for merge/pull requests and CI status; signing in when git asks adds or updates the account. Existing sign-ins are converted automatically. An account's token is now always used for git on its server.
- The Changes tab shows how many files have changed.
- Right-click the changed files header to discard or stash all changes.

### 🐞 Bug fixes
- Syncing a repository without a saved sign-in no longer shows two sign-in prompts: Git Credential Manager no longer opens its own window, and the app asks once.

## 0.1.2
### ✨ Features and improvements

### 🐞 Bug fixes
- Dragging the dividers between the lists and the diff now resizes them on Windows (the cursor changed, but the layout didn't update), and a divider's width is only saved after an actual drag.
- Commits in History no longer show as "Not yet pushed" after pushing them.

## 0.1.1
### ✨ Features and improvements
- The window remembers its size, position and maximized state between runs. The first run, or a saved position that is no longer on any screen, opens centred and sized to fit the display.
- Drag the dividers between the file/commit lists and the diff to resize them; the widths are remembered.
- **Full width** button on the diff (also View › Full-width diff, Ctrl+3) hides the lists so the diff uses the whole window. It always starts off after a restart.
- **Updates**: the app checks its GitLab and GitHub releases at startup (and from Help › Check for updates). Installed Windows copies download the new installer and update in place, restarting afterwards; macOS and Android open the download. A release can be skipped so the startup check stops offering it; automatic checks and pre-releases can be turned on or off in Options.
- **Git sign-in dialog**: when a push, pull, fetch or clone over HTTPS has no login or the login is rejected, the app asks for a username and password or access token and retries. Sign-ins can be remembered (secure storage), apply to a whole server or just one repository, and a token can also become the server's API token. Saved sign-ins can be forgotten in Options.

### 🐞 Bug fixes
- The window no longer opens taller than the screen on scaled displays.
- macOS build: fixed the packaging script under macOS's bash 3.2, and unsigned builds no longer look for a signing certificate.

## 0.1.0
### ✨ Features and improvements
- First release: a GitHub Desktop-style client for GitLab and GitHub. Changes and History tabs, line-level commits, branch management, merge/pull request creation, CI status, cloning, and a repositories folder that picks up existing checkouts.
- Recognises whether each repository is hosted on GitLab or GitHub (asking the server when needed) and shows that host's wording and links.
