#!/usr/bin/env bash
# Builds the macOS (Mac Catalyst) app as a universal (Apple silicon + Intel) .app and packages it in a .dmg.
# Used by the GitHub workflows and, when a macOS runner is available, the GitLab pipeline.
#
#   build/build-macos.sh <version-label> <output-dir>
#
# Signing is optional and driven by environment variables:
#   MACOS_SIGNING_IDENTITY   A "Developer ID Application: …" identity already in the keychain. Without it the app is
#                            ad-hoc signed: it runs, but Gatekeeper asks users to right-click > Open the first time.
#   APPLE_ID, APPLE_TEAM_ID, APPLE_APP_PASSWORD
#                            With a signing identity, also notarize and staple the .dmg (app-specific password).
set -euo pipefail

LABEL="$1"
OUT_DIR="$2"
PROJECT=CommitDesk/CommitDesk.csproj
VOLUME_NAME="CommitDesk"

# Report the command that failed as a "::error::" line. GitHub Actions turns these into annotations, which can be read
# without signing in (job logs can't); elsewhere it is just a log line.
trap 'echo "::error title=build-macos.sh::line $LINENO failed (exit $?): $BASH_COMMAND"' ERR

# Never left empty: macOS's bash 3.2 treats "${empty_array[@]}" as an unbound variable under set -u.
if [ -n "${MACOS_SIGNING_IDENTITY:-}" ]; then
  echo "Signing with: $MACOS_SIGNING_IDENTITY"
  # Hardened runtime is required for notarization; the entitlements allow the .NET runtime's JIT.
  sign_args=(-p:EnableCodeSigning=true "-p:CodesignKey=$MACOS_SIGNING_IDENTITY" -p:UseHardenedRuntime=true)
else
  echo "WARNING: MACOS_SIGNING_IDENTITY is not set; the app will be ad-hoc signed and not notarized."
  # Don't let the build look for a signing certificate; the bundle is ad-hoc signed below instead.
  sign_args=(-p:EnableCodeSigning=false)
fi

# A build, not a publish: publish would wrap the app in an installer .pkg, and a drag-to-Applications .dmg is the
# usual way to ship a Mac app outside the App Store. EnableWindowsTargeting: the csproj also targets Windows, and
# restore evaluates every target framework even when -f picks one (NETSDK1100 otherwise).
BUILD_LOG=$(mktemp)
if ! dotnet build "$PROJECT" -f net10.0-maccatalyst -c Release -p:EnableWindowsTargeting=true "${sign_args[@]}" 2>&1 | tee "$BUILD_LOG"; then
  # Surface the compiler/MSBuild errors as annotations too
  grep -E '(: error |error [A-Z]+[0-9]+:)' "$BUILD_LOG" | sed -E 's/ \[[^]]*\]$//' | sort -u | head -20 |
    while IFS= read -r line; do echo "::error title=dotnet build::$line"; done
  exit 1
fi

BASE=CommitDesk/bin/Release/net10.0-maccatalyst
# Prefer the bundle directly in the output folder (the universal app); only search deeper if it isn't there.
APP=$(find "$BASE" -maxdepth 1 -name '*.app' -type d | head -1)
[ -z "$APP" ] && APP=$(find "$BASE" -maxdepth 2 -name '*.app' -type d | head -1)
if [ -z "$APP" ]; then
  echo "No .app bundle found under $BASE"; find "$BASE" -maxdepth 3 || true
  exit 1
fi
echo "App bundle: $APP"
lipo -info "$APP/Contents/MacOS/"* 2>/dev/null || true

if [ -z "${MACOS_SIGNING_IDENTITY:-}" ]; then
  # Apple silicon refuses to run unsigned code at all; an ad-hoc signature is enough to launch.
  codesign --force --deep --sign - "$APP"
fi

mkdir -p "$OUT_DIR"
DMG="$OUT_DIR/CommitDesk-$LABEL-macos.dmg"
STAGING=$(mktemp -d)
trap 'rm -rf "$STAGING"' EXIT
ditto "$APP" "$STAGING/$(basename "$APP")"
ln -s /Applications "$STAGING/Applications"
# hdiutil on hosted Mac runners intermittently fails with "Resource busy"; retry a few times before giving up.
for attempt in 1 2 3 4; do
  if hdiutil create -volname "$VOLUME_NAME" -srcfolder "$STAGING" -ov -format UDZO "$DMG"; then break; fi
  if [ "$attempt" = 4 ]; then echo "::error title=hdiutil::could not create $DMG"; exit 1; fi
  echo "hdiutil failed (attempt $attempt); retrying in 10 seconds"
  sleep 10
done

if [ -n "${MACOS_SIGNING_IDENTITY:-}" ]; then
  codesign --force --sign "$MACOS_SIGNING_IDENTITY" "$DMG"
  if [ -n "${APPLE_ID:-}" ] && [ -n "${APPLE_TEAM_ID:-}" ] && [ -n "${APPLE_APP_PASSWORD:-}" ]; then
    echo "Notarizing $DMG"
    xcrun notarytool submit "$DMG" --apple-id "$APPLE_ID" --team-id "$APPLE_TEAM_ID" --password "$APPLE_APP_PASSWORD" --wait
    xcrun stapler staple "$DMG"
  else
    echo "APPLE_ID / APPLE_TEAM_ID / APPLE_APP_PASSWORD not set; skipping notarization."
  fi
fi

echo "Disk image: $DMG"
