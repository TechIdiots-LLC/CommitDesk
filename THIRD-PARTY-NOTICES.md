# Third-party notices

CommitDesk is released under the BSD 3-Clause License (see LICENSE). It includes the following software, under their own
licenses.

| Component | Used for | License |
| --- | --- | --- |
| .NET runtime and libraries, .NET MAUI, Microsoft.Extensions.* | The app framework | MIT ([dotnet/runtime](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT), [dotnet/maui](https://github.com/dotnet/maui/blob/main/LICENSE.txt)) |
| Windows App SDK (Windows only) | Windows user interface | MIT ([microsoft/WindowsAppSDK](https://github.com/microsoft/WindowsAppSDK/blob/main/LICENSE)) |
| CommunityToolkit.Maui, CommunityToolkit.Mvvm | Controls and view models | MIT ([CommunityToolkit](https://github.com/CommunityToolkit/Maui/blob/main/LICENSE)) |
| LibGit2Sharp 0.32.0 | .NET access to libgit2 | MIT ([libgit2/libgit2sharp](https://github.com/libgit2/libgit2sharp/blob/master/LICENSE.md)) |
| libgit2 1.8.6 | Git on platforms without the git command line (Android) | GPL v2 with a linking exception; full text, including the licenses of the code it bundles (zlib, PCRE2, llhttp, xdiff, SHA-1 collision detection), in [licenses/libgit2-COPYING.txt](licenses/libgit2-COPYING.txt) |
| Mbed TLS 3.6.7 (Android only) | HTTPS for libgit2 | Apache License 2.0; full text in [licenses/mbedtls-LICENSE.txt](licenses/mbedtls-LICENSE.txt) |

## libgit2

libgit2 is distributed under the GNU General Public License version 2 with a linking exception, which allows it to be
linked into programs under other licenses. The Android build uses libgit2 at commit
`5853918c4c6a7b12f8becf4bd11ff4362ebb9020` ([source](https://github.com/libgit2/libgit2/tree/5853918c4c6a7b12f8becf4bd11ff4362ebb9020)),
built by `build/build-libgit2-android.sh` in this repository with two build-only changes: the default certificate
location (Android's system certificates) and GNU C90 mode for the Android headers.

## Mbed TLS

Copyright The Mbed TLS Contributors. Licensed under the Apache License, Version 2.0. The Android build uses the
unmodified [mbedtls-3.6.7](https://github.com/Mbed-TLS/mbedtls/releases/tag/mbedtls-3.6.7) release.

## Trademarks

GITLAB is a trademark of GitLab Inc. in the United States and other countries and regions. GitHub is a trademark of
GitHub, Inc. Git is a trademark of the Software Freedom Conservancy. CommitDesk is not affiliated with any of them.
