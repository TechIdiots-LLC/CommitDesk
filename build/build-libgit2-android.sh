#!/usr/bin/env bash
# Builds libgit2 for Android (arm64-v8a, x86_64) as the shared library LibGit2Sharp loads (libgit2-<hash>.so), with
# HTTPS through a static mbedTLS. LibGit2Sharp.NativeBinaries ships no Android build, so the app carries this one.
#
#   build/build-libgit2-android.sh <output-dir>
#
# Needs the Android NDK (ANDROID_NDK_HOME or ANDROID_NDK_ROOT), CMake and Ninja on PATH, curl and tar. Runs in Git Bash on
# Windows as well as on Linux and macOS.
#
# The libgit2 commit must be the one the LibGit2Sharp version in CommitDesk.csproj was built against (its
# LibGit2Sharp.NativeBinaries package names it in libgit2/libgit2_hash.txt), or the managed calls won't match.
set -euo pipefail

OUT_DIR="$(mkdir -p "${1:?usage: build-libgit2-android.sh <output-dir>}" && cd "$1" && pwd)"

LIBGIT2_COMMIT=5853918c4c6a7b12f8becf4bd11ff4362ebb9020   # libgit2 1.8.6, for LibGit2Sharp 0.32.0
LIBGIT2_NAME=git2-${LIBGIT2_COMMIT:0:7}                   # LibGit2Sharp imports "git2-5853918"
MBEDTLS_VERSION=3.6.7                                     # long-term support line
MBEDTLS_SHA256=a7e8bcbec0e6f761b4af24f25677626b35f762f68eef79c08677a363212d11f6
ANDROID_PLATFORM=android-26                               # the app's SupportedOSPlatformVersion
ABIS="${ABIS:-arm64-v8a x86_64}"

NDK="${ANDROID_NDK_HOME:-${ANDROID_NDK_ROOT:-}}"
[ -n "$NDK" ] && [ -f "$NDK/build/cmake/android.toolchain.cmake" ] || { echo "Set ANDROID_NDK_HOME to the Android NDK"; exit 1; }
command -v cmake > /dev/null && command -v ninja > /dev/null || { echo "CMake and Ninja must be on PATH"; exit 1; }

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
cd "$WORK"

echo "Downloading libgit2 $LIBGIT2_COMMIT and mbedTLS $MBEDTLS_VERSION"
curl -fsSL -o libgit2.tar.gz "https://github.com/libgit2/libgit2/archive/$LIBGIT2_COMMIT.tar.gz"
# The test data has symlinks Windows can't create, and isn't needed
tar xzf libgit2.tar.gz --exclude='*/tests'
mv "libgit2-$LIBGIT2_COMMIT" libgit2
curl -fsSL -o mbedtls.tar.bz2 "https://github.com/Mbed-TLS/mbedtls/releases/download/mbedtls-$MBEDTLS_VERSION/mbedtls-$MBEDTLS_VERSION.tar.bz2"
echo "$MBEDTLS_SHA256  mbedtls.tar.bz2" | sha256sum -c -
tar xjf mbedtls.tar.bz2 --exclude='*/tests/data_files'
mv "mbedtls-$MBEDTLS_VERSION" mbedtls

# Android keeps its trusted CAs as one file per certificate in this folder; libgit2 loads a folder given as the default
# location with mbedtls_x509_crt_parse_path. Its CMake only accepts a default that exists on the build machine, so set
# it in the source, and give CMake an existing folder so it doesn't go looking for an openssl executable instead.
sed -i.orig 's|char \*crtpath = GIT_DEFAULT_CERT_LOCATION;|char *crtpath = "/system/etc/security/cacerts";|' \
    libgit2/src/libgit2/streams/mbedtls.c
grep -q '"/system/etc/security/cacerts"' libgit2/src/libgit2/streams/mbedtls.c || { echo "Couldn't set the certificate location"; exit 1; }

# libgit2 compiles as strict C90, but the NDK's Linux headers (linux/swab.h) use `inline`, which only GNU C90 allows
sed -i.orig 's/C_EXTENSIONS OFF/C_EXTENSIONS ON/' libgit2/src/libgit2/CMakeLists.txt libgit2/src/util/CMakeLists.txt
[ "$(grep -c 'C_EXTENSIONS ON' libgit2/src/libgit2/CMakeLists.txt libgit2/src/util/CMakeLists.txt | grep -c ':1')" = 2 ] ||
    { echo "Couldn't switch libgit2 to GNU C90"; exit 1; }

# libgit2 only names the library LIBGIT2_FILENAME together with a versioned SONAME (libgit2-5853918.so.1.8), but an
# APK carries plain lib*.so files: name it here and leave SONAME off, so the file and its SONAME are libgit2-5853918.so
sed -i.orig 's/^ide_split_sources(libgit2package)$/&\nset_target_properties(libgit2package PROPERTIES OUTPUT_NAME ${LIBGIT2_FILENAME})/' \
    libgit2/src/libgit2/CMakeLists.txt
grep -q 'PROPERTIES OUTPUT_NAME ${LIBGIT2_FILENAME})$' libgit2/src/libgit2/CMakeLists.txt || { echo "Couldn't set the library name"; exit 1; }

TOOLCHAIN="$NDK/build/cmake/android.toolchain.cmake"
# Play requires native libraries aligned for 16 KB memory pages (apps targeting Android 15+)
PAGE_FLAGS="-Wl,-z,max-page-size=16384"
for ABI in $ABIS; do
    echo "=== $ABI ==="
    PREFIX="$WORK/mbedtls-$ABI"
    cmake -S mbedtls -B "build-mbedtls-$ABI" -G Ninja -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_TOOLCHAIN_FILE="$TOOLCHAIN" -DANDROID_ABI="$ABI" -DANDROID_PLATFORM="$ANDROID_PLATFORM" \
        -DCMAKE_POSITION_INDEPENDENT_CODE=ON -DENABLE_TESTING=OFF -DENABLE_PROGRAMS=OFF \
        -DUSE_SHARED_MBEDTLS_LIBRARY=OFF -DUSE_STATIC_MBEDTLS_LIBRARY=ON -DCMAKE_INSTALL_PREFIX="$PREFIX"
    cmake --build "build-mbedtls-$ABI" --target install

    # The mbedTLS paths are given directly: the NDK toolchain only lets find_library search its own sysroot
    cmake -S libgit2 -B "build-libgit2-$ABI" -G Ninja -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_TOOLCHAIN_FILE="$TOOLCHAIN" -DANDROID_ABI="$ABI" -DANDROID_PLATFORM="$ANDROID_PLATFORM" \
        -DCMAKE_SHARED_LINKER_FLAGS="$PAGE_FLAGS" \
        -DBUILD_SHARED_LIBS=ON -DBUILD_TESTS=OFF -DBUILD_CLI=OFF -DBUILD_EXAMPLES=OFF \
        -DLIBGIT2_FILENAME="$LIBGIT2_NAME" -DSONAME=OFF \
        -DUSE_SSH=OFF -DUSE_HTTPS=mbedTLS -DCERT_LOCATION="$WORK" \
        -DMBEDTLS_INCLUDE_DIR="$PREFIX/include" -DMBEDTLS_LIBRARY="$PREFIX/lib/libmbedtls.a" \
        -DMBEDX509_LIBRARY="$PREFIX/lib/libmbedx509.a" -DMBEDCRYPTO_LIBRARY="$PREFIX/lib/libmbedcrypto.a" \
        -DUSE_SHA1=CollisionDetection -DUSE_SHA256=Builtin -DUSE_BUNDLED_ZLIB=ON -DREGEX_BACKEND=builtin \
        -DUSE_NTLMCLIENT=OFF -DUSE_GSSAPI=OFF -DUSE_ICONV=OFF
    cmake --build "build-libgit2-$ABI"

    LIB="$(find "build-libgit2-$ABI" -name "lib$LIBGIT2_NAME.so" | head -1)"
    [ -n "$LIB" ] || { echo "lib$LIBGIT2_NAME.so wasn't built for $ABI"; exit 1; }
    mkdir -p "$OUT_DIR/$ABI"
    "$NDK"/toolchains/llvm/prebuilt/*/bin/llvm-strip --strip-unneeded -o "$OUT_DIR/$ABI/lib$LIBGIT2_NAME.so" "$LIB"
    ls -l "$OUT_DIR/$ABI/lib$LIBGIT2_NAME.so"
done
echo "Built lib$LIBGIT2_NAME.so for: $ABIS (in $OUT_DIR)"
