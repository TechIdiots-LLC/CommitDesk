# Building and releasing

## Building from source

You need the .NET 10 SDK with the MAUI workload (`dotnet workload install maui`) and git.

```sh
dotnet build CommitDesk/CommitDesk.csproj -f net10.0-windows10.0.19041.0
dotnet test CommitDesk.Core.Tests
```

`CommitDesk.sln` opens the projects in Visual Studio. To run against a repository directly:

```sh
CommitDesk.exe C:\path\to\repository
```

## Project layout

| Project | Contents |
| --- | --- |
| `CommitDesk.Core` | Plain .NET 10 library: the git CLI wrapper (`Git/`), status, diff and log parsing, the partial-commit patch builder, image headers, host detection and links (`Hosting/`), the GitLab and GitHub REST clients (`GitLab/`, `GitHub/`), and the update feed (`Updates/`). |
| `CommitDesk.Core.Tests` | xUnit tests, mostly against real temporary git repositories. |
| `CommitDesk` | The MAUI app. `ViewModels/MainViewModel.*.cs` is split by menu area, and `Views/MainPage.xaml` holds the main window. |
| `build/` | Scripts shared by the GitLab and GitHub pipelines: installer, signing, macOS packaging, version bump. |
| `docs/` | This documentation. |

### How line-level commits work

Line-level commits work the same way as in GitHub Desktop. On commit, the index is reset to HEAD and fully included
files are added with `git add`. For each partially included file, a patch with only the selected lines is applied
with `git apply --cached`.

### How git signs in

An account's secret reaches git as an `http.<url>.extraHeader` setting, passed through `GIT_CONFIG_COUNT` environment
variables, so it never appears on a command line. Repository-scoped sign-ins are listed before server-wide ones so they
win. `GIT_TERMINAL_PROMPT=0` and `GCM_INTERACTIVE=never` stop git and Git Credential Manager from prompting. When git
reports an authentication failure, the app shows its own sign-in prompt and retries.

## Platforms

| Platform | Status |
| --- | --- |
| Windows (x64, ARM64) | Primary target. Released as zips and NSIS installers. |
| macOS (Mac Catalyst, universal) | Released as a `.dmg`. Without a Developer ID certificate it is ad-hoc signed. |
| Android | Preview. Installs and runs, but phones have no git command line. It would need a different git back end (such as libgit2) or an API-only mode for reviewing merge/pull requests and CI. |
| iOS | Compiles only; not released. |

## CI and releases

The GitLab pipeline (`.gitlab-ci.yml`) and the GitHub workflows (`.github/workflows/`) do the same jobs and share the
scripts in `build/`. The project lives on GitLab and is mirrored to GitHub, so both run.

- **CI** runs on merge/pull requests and pushes to `main`. It runs the Core tests, then builds the Windows app (x64,
  ARM64), a debug-signed Android APK and, on GitHub (or GitLab with a macOS runner), an ad-hoc signed macOS `.dmg`.
  The builds are kept as 7-day artifacts.
- **Release** runs on pushes to `main`. When no release exists yet for `<ApplicationDisplayVersion>` in
  `CommitDesk/CommitDesk.csproj`, it runs the tests, then builds and publishes:
  - the Windows zips and installers;
  - the macOS `.dmg`;
  - the Android APK, only when a release keystore is configured.

  The release notes are that version's section of `CHANGELOG.md`. Versions with a `-` (for example `0.3.0-rc.1`) are
  published as pre-releases.
- **Bump version** is started by hand: on GitLab, *Build › Pipelines › New pipeline* with `BUMP_VERSION` set; on
  GitHub, *Actions › Create bump version PR*. It bumps the version and moves the changelog's `## master` notes under
  the new version, adding the merge/pull requests merged since the last tag. Then it opens a merge/pull request, and
  merging that triggers the release.

### Changelog

Add a line under `## master` in `CHANGELOG.md`, in *Features and improvements* or *Bug fixes*, with every
user-visible change. The bump job turns that section into the next version's release notes.

### Runners

The GitLab pipeline runs on a Windows shell runner tagged `windows` with:

- the .NET 10 SDK and MAUI workloads;
- git, Python 3 and NSIS;
- for signing, the Windows SDK.

The jobs that build the app (Windows, Android, Google Play) pin the .NET SDK 10.0.4xx band and workload set 10.0.401
in a .NET install of their own from the runner's `dotnet-workload-env`, so jobs running in parallel don't restore
workloads into the shared install at the same time. On a runner without it they run `dotnet workload restore`.

The macOS jobs (`ci-macos`, `release-macos`) run on a shell runner tagged `macos`, set up with the
[gitlab-runner-install](https://gitlab.techidiots.net/techidiots-llc/gitlab-runner-install) project's
`install-macos-runner.sh`. They are on by default; set the project variable `MACOS_RUNNER` to `false` to skip them
while no macOS runner is online, or they wait for one. They pin the .NET SDK 10.0.4xx band and workload set
10.0.401, the same pins as the GitHub workflows, in a .NET install of their own from the runner's `dotnet-workload-env`,
so a CI build and a release (or other projects) can run on the Mac at once. On a runner without it they use its
shared `~/.dotnet`, with a `global.json` written by the job.
Workload set 10.0.401 builds for the Mac Catalyst 26.0 SDK, so the runner needs Xcode 26.0 or a later 26.x, which needs a recent macOS
(Xcode 26.6 needs macOS 26.2). An Intel Mac works and still builds the universal app.

### Secrets

| GitLab CI/CD variable / GitHub secret | Needed for |
| --- | --- |
| `CI_PUSH_TOKEN` (GitLab only) | Bump version: a project access token, role Maintainer, scopes `api` and `write_repository`. |
| `SIG_PFX_B64` (GitHub: `SIG_PFX`), `SIG_PFX_PASS` | Optional: Windows code signing. Without them the builds are unsigned, with a warning. |
| `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD` | Optional: the Android APK in releases. `build/Test-AndroidKeystore.ps1` checks a keystore and its passwords. |
| `MACOS_SIGNING_IDENTITY`, plus on GitHub `MACOS_CERT_P12_BASE64`, `MACOS_CERT_PASSWORD` | Optional: Developer ID signing of the macOS app. |
| `APPLE_ID`, `APPLE_TEAM_ID`, `APPLE_APP_PASSWORD` | Optional: notarizing the signed macOS app. |
| `PLAY_SERVICE_ACCOUNT_JSON_BASE64` (GitHub: `PLAY_SERVICE_ACCOUNT_JSON`) | Optional: publishing to Google Play. See [Google Play](#google-play). |

On GitLab, make the secrets Protected and Masked with environment scope `signing/main`. On GitHub, put them in the
`release` environment.

### Signing and updates

Installed Windows copies update themselves by running the new installer with `/UPDATE /D=<install folder>`. In
update mode the installer shows only its progress page ("Updating CommitDesk"), waits there for the app to exit,
closes by itself when done and restarts the app. Versions up to 0.4.0 also pass `/S`, which runs the same update
without a window. Before running the installer, the app
checks the installer's Authenticode signature, but only when the running app is itself signed. A self-signed
certificate is accepted only if the installer is signed with exactly the same certificate (matching thumbprint) as the
running app. If releases are self-signed, changing the certificate means users must install the next version by hand
once.

The app finds releases on this project's GitLab releases first, and GitHub's as a fallback. The project locations are
constants in `CommitDesk/Services/AppUpdater.cs`, along with the documentation link used by Help › Documentation.

### Google Play

The app is published on Google Play by the developer account **techidiots-llc**, as package `net.techidiots.commitdesk`.
Publishing is never automatic. On GitLab, start the **publish-play** job (▶) on the release pipeline of the version
you want to publish; on GitHub, run **Actions › Publish to Google Play** (it publishes the version on the branch you
run it from). Either one builds an Android App Bundle (`.aab`) signed with the release keystore, uploads it, and
releases it on the `internal` testing track, with that version's changelog section as release notes (cut to Play's
500 characters). It is built with `-p:PlayStore=true`, which turns the in-app updater off: Play
 doesn't allow apps it installs to update themselves (the APK on the releases keeps the updater). The bundle is also kept as an artifact for 30 days.

The job's variables choose where it goes: `PLAY_TRACK` (`internal`, `alpha` for closed testing, `beta` for open
testing, or `production`) and `PLAY_RELEASE_STATUS` (`completed`, or `draft` to review and roll out in Play Console).
Set them when starting the job. Every upload needs a higher version code than the last; that is
`<ApplicationVersion>` in `CommitDesk.csproj`, which the version bump raises.

The Android app is still a preview (there is no git on phones), and Play's review can reject apps whose main
features don't work. Keep it on the internal testing track, which isn't reviewed, until it does.

**Setting up Play (once):**

1. In Play Console, create the app, with the package name `net.techidiots.commitdesk`.
2. Upload the first bundle by hand (Google only accepts API uploads for an app that already has one): run the
   publish-play job before setting up the Play key below. Without the key it builds and signs the bundle, skips the
   upload with a warning, and still succeeds. Download the `.aab` from the job's artifacts (GitHub: the
   `play-bundle` artifact) and upload it in Play Console › Testing › Internal testing › Create new release. This also sets up Play App Signing, with the
   release keystore as the upload key. Keep that keystore safe: every later upload must be signed with it.
3. In Google Cloud, create a service account (no roles needed in Cloud), create a JSON key for it, and enable the
   Google Play Android Developer API for the project.
4. In Play Console › Users and permissions, invite the service account's email, and give it access to the app with
   "Release to testing tracks" (and "Release to production" if you will publish there).
5. Store the key: on GitLab as `PLAY_SERVICE_ACCOUNT_JSON_BASE64` (the JSON file base64-encoded, so it can be masked),
   Protected and Masked with environment scope `signing/main`; on GitHub as `PLAY_SERVICE_ACCOUNT_JSON` (the JSON as
   is) in the `release` environment.

On GitHub, the track and status are chosen when running the workflow instead of through variables.

## Documentation

These pages are plain Markdown with relative links and images, so they read the same on GitLab and GitHub. Screenshots
in `docs/images/` are full window captures at 1728×918, taken from a demo repository with a clean profile so no real
accounts or paths appear. Keep new screenshots the same size, and don't show real server names or tokens.
