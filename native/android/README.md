# libgit2 for Android

`<abi>/libgit2-5853918.so` is libgit2 1.8.6 (commit `5853918`, the one LibGit2Sharp 0.32.0 is built against) for the
Android app, which has no git command line. LibGit2Sharp.NativeBinaries ships no Android build, so these are built by
[`build/build-libgit2-android.sh`](../../build/build-libgit2-android.sh):

- HTTPS through a static mbedTLS 3.6.7, trusting Android's system certificates (`/system/etc/security/cacerts`);
- no SSH (the app signs in over HTTPS);
- linked for 16 KB memory pages, which Google Play requires for apps targeting Android 15 and later.

To rebuild, with the Android NDK, CMake and Ninja installed:

```sh
ANDROID_NDK_HOME=/path/to/android-ndk build/build-libgit2-android.sh native/android
```

When LibGit2Sharp is updated, its LibGit2Sharp.NativeBinaries package names the libgit2 commit it needs
(`libgit2/libgit2_hash.txt`): change `LIBGIT2_COMMIT` in the script to match, rebuild, and update the file name in
`CommitDesk/CommitDesk.csproj`.
