# Syncing

The third toolbar button sends and receives changes. Its label shows what it will do next:

| Button | When | What it does |
| --- | --- | --- |
| **Publish branch** | The branch isn't on the server yet | Pushes it and sets it to track the server's copy. |
| **Pull origin** ↓2 | The server has commits you don't (↓ how many) | Pulls them in. If you also have unpushed commits (↑), they're merged. |
| **Push origin** ↑1 | You have commits the server doesn't (↑ how many) | Pushes them. Tags on those commits go too. |
| **Fetch origin** | You're up to date | Checks the server for new commits. The subtitle says when it last checked. |

The same actions are in the **Repository** menu: **Push** (Ctrl+P), **Pull** (Ctrl+Shift+P) and **Fetch**
(Ctrl+Shift+T).

## Publishing to your fork

When `origin` is the project itself (for example a clone of maplibre/maplibre-native) but you want your branches in
your own fork, choose where they're published:

- **Branch › Publish branch to…** (also on the branch button's right-click menu) lists the repository's remotes. If you
  have a GitLab or GitHub account for the server and no remote points at your fork yet, **Your fork: *you/project***
  is listed first. Picking it adds the fork as a remote named after your login. If the fork doesn't exist yet, you're
  offered the server's fork page. The branch is pushed there and tracks it from then on.
- When the fork is added you're asked whether to **publish new branches to it by default**. You can change this later in
  [Repository settings](repositories.md#repository-settings) › **Publish new branches to**.

With your fork as the default, a branch you make, including one checked out from the project's own branch (such as
`origin/feature/x`), shows **Publish branch** with *To you* underneath, and publishing sends it to your fork. These keep
going where they came from:

- the default branch (`main`), which still pulls from and pushes to the project;
- branches checked out from someone else's remote, such as a merge/pull request from another fork;
- any branch you sent somewhere else with **Publish branch to…**, which is remembered for that branch.

Update from *default branch*, the GitLab/GitHub links and new merge/pull requests still use the project, not your fork.
The settings are git's own (`remote.pushDefault` and `branch.<name>.pushRemote`), so `git push` in a terminal goes to
the same place.

## Newer commits on the server

If someone else pushed to the branch since you last fetched, the server refuses your push. The app says so and offers
**Fetch**. After fetching, the toolbar shows **Pull origin** with both counts (for example ↓2 ↑1): pull to combine
their commits with yours, then push.

## Diverged branches

If both you and the server have new commits and you push, you're asked to choose:

- **Pull (merge remote changes)**: the safe choice. Pull, then push again.
- **Force push (overwrite remote)**: replaces the server's branch with yours. Use this only after rewriting your own
  history, for example after amending a pushed commit or rebasing. It's done with `--force-with-lease`, which refuses
  if the server has commits you haven't fetched.

## Pull behaviour

Pulling uses your git `pull.rebase` setting if you have one. Without it, the app merges, because newer versions of git
otherwise refuse to pull a branch that has diverged. If the merge has conflicts, see
[Conflicts](branches-and-requests.md#conflicts).

## Uncommitted changes in the way

If you have uncommitted changes to files the pull would change, git won't pull. The app offers **Stash and pull**: it
sets your changes aside, pulls, and puts them back on top. If some of them clash with the pulled commits, those files
are marked as [conflicted](branches-and-requests.md#conflicts) for you to resolve, and a copy of your changes is kept
under **Branch › Restore stashed changes…** so nothing is lost. If the pull itself fails, your changes stay in that
stash.

## Signing in

If the server needs a sign-in the app doesn't have, or rejects the saved one, you're
[asked to sign in](accounts.md#when-git-asks-you-to-sign-in) and the action is retried.
