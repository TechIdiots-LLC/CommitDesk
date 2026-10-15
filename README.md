# CommitDesk

A desktop git client in the style of GitHub Desktop that works with GitLab™ (self-hosted servers and gitlab.com),
GitHub, and any other git server, built with .NET MAUI.

CommitDesk was called *GitLab Desktop* up to version 0.6. Installing or updating to CommitDesk replaces it and keeps
your settings.

![CommitDesk showing a change to commit](docs/images/changes.png)

- **Commit exactly what you mean**: tick whole files, or click the gutter to commit single lines.
- **See image changes**: before and after side by side, instead of *Binary file changed*.
- **Branches made easy**: switch, create, merge, squash, rebase, stash, and leave changes on the branch they belong to.
- **Merge requests and pull requests**: create them in the app, and see the open request and its CI status in the
  toolbar.
- **Any server**: GitLab (any instance, recognised automatically), GitHub, or a plain git server over HTTPS or SSH.
- **History**: browse commits and amend, revert, reset, cherry-pick or tag them.
- **Updates itself** on Windows, from this project's releases.

## Get it

Download the installer for Windows, the `.dmg` for macOS, or the zip from this project's **Releases** page. Git must
be installed. See [Getting started](docs/getting-started.md).

## Documentation

- [Getting started](docs/getting-started.md): install, first run and updates
- [Accounts and signing in](docs/accounts.md)
- [Repositories](docs/repositories.md) · [Making changes](docs/making-changes.md) · [History](docs/history.md)
- [Branches, merge requests and pull requests](docs/branches-and-requests.md) · [Syncing](docs/syncing.md)
- [GitLab, GitHub and other servers](docs/gitlab-and-github.md) · [Options](docs/options.md) ·
  [Keyboard shortcuts](docs/shortcuts.md)
- [Troubleshooting](docs/troubleshooting.md)
- [Privacy policy](docs/privacy.md) · [Third-party notices](THIRD-PARTY-NOTICES.md)

In the app, **Help › Documentation** (F1) opens these pages.

## Building

```sh
dotnet build CommitDesk/CommitDesk.csproj -f net10.0-windows10.0.19041.0
dotnet test CommitDesk.Core.Tests
```

[Building and releasing](docs/maintainers.md) covers the project layout, CI, releases, signing and version bumps.
Changes are listed in [CHANGELOG.md](CHANGELOG.md).

## License

CommitDesk is released under the [BSD 3-Clause License](LICENSE).

## Trademarks

CommitDesk is an independent project. It is not affiliated with, endorsed, sponsored or approved by GitLab Inc. or
GitHub, Inc. GITLAB is a trademark of GitLab Inc. in the United States and other countries and regions. GitHub and
GitHub Desktop are trademarks of GitHub, Inc. Git is a trademark of the Software Freedom Conservancy.
