# CommitDesk privacy policy

*Effective 14 October 2026.*

CommitDesk is an app published by TechIdiots LLC ("we", "us") for Windows, macOS and Android. This policy explains what
information the app handles, where it goes, and the choices you have.

## In short

- We don't collect personal information through the app. There are no accounts with us, no analytics, no advertising
  and no tracking in the app.
- What the app stores stays on your device, and it only sends information to the servers you choose to use with it.


## What the app stores on your device

- **Accounts:** for each server you sign in to, its address, your username, and your password or access token. Passwords
  and tokens are kept in the operating system's secure storage (Windows Data Protection, the macOS keychain, the Android
  Keystore). You can remove an account in Options at any time.
- **Settings:** your repository list, folders, editor and other choices in Options.
- **Your repositories:** the files you clone, change and commit stay in the folders you choose.
- **A crash log:** if the app crashes, the error is written to a file in the app's own folder. It is never sent anywhere.

## What the app sends, and to whom

The app connects to a server only when you use it, over encrypted connections (HTTPS or SSH) unless you set up a server
without them:

- **Git servers you work with**, such as a GitLab™ server, GitHub or any other git host: the app clones, fetches, pulls
  and pushes your repositories, and sends your username and password or token to that server, and only to that server,
  to sign in.
- **The GitLab and GitHub APIs** of the servers you add: to list projects, branches, merge and pull requests and CI
  status. These requests carry your access token for that server.
- **Server detection:** when a repository's server is new to the app, the app asks that server which software it runs.
- **Update checks** (Windows and macOS, and the Android app from our releases page; not the Google Play version): the app
  reads the list of CommitDesk releases from gitlab.techidiots.net and GitHub. You can turn this off in Options.

## Location data

CommitDesk does not use your location.


## Third-party services

When the app talks to a server you chose (for example gitlab.com, github.com, or your organisation's own server), that
server's operator handles your information under its own privacy policy, not this one.

## What we receive

Nothing from the app itself. If you download the app or its updates from our servers or GitHub, those sites keep the
usual web server logs.

## Your choices and rights

Because the app's information stays on your device, you control it: remove accounts and settings in the app, or
uninstall it to remove them all. If you are in the EU/EEA or the UK, you have rights over personal data that others hold
about you; since we don't hold any from the app, there is nothing for us to access, correct or delete, but you are
welcome to ask.

## Children's privacy

The app is not directed at children under 13, and we don't knowingly collect information from them.

## Changes to this policy

When this policy changes, the new version is published here with a new effective date. Significant changes are also
mentioned in the app's release notes.

## Contact

Questions about this policy: contact us through [our forum](https://forum.techidiots.net/forum/) or open an issue on the
[CommitDesk project](https://gitlab.techidiots.net/techidiots-llc/commitdesk/-/issues).

*GITLAB is a trademark of GitLab Inc. in the United States and other countries and regions. CommitDesk is not affiliated
with GitLab Inc. or GitHub, Inc.*
