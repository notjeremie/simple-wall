#!/bin/bash
# Builds dist/simple-wall.zip -- the deploy package for the Win7 wall PC.
# Run from anywhere; requires the wallvm SSH alias to build Release in the VM
# (nothing builds on the Mac). Modeled on build-spike-package.sh.
set -euo pipefail

REPO="/Users/notjeremie/Documents/Coding/simple-wall"
DIST="$REPO/dist"
STAGE="$DIST/stage"
REL="$REPO/src/SimpleWall/bin/Release/net48"

cd "$REPO"

# Wipe the Release output before building. Incremental builds over the SMB share
# accumulate: stale files from older builds are never removed, and macOS collision
# renaming leaves "SimpleWall.exe 2.config"-style duplicates behind. Left alone this
# silently doubled the package (1168 files / 97MB against the expected ~45MB) and
# shipped junk to the wall PC. The build is ~10s from cold; correctness is worth it.
echo "==> Cleaning Release output"
rm -rf "$REPO/src/SimpleWall/bin/Release" "$REPO/src/SimpleWall/obj/Release"

echo "==> Building Release in the VM"
ssh wallvm 'cmd /c "pushd \\Mac\Home\Documents\Coding\simple-wall && dotnet build -c Release src\SimpleWall\SimpleWall.csproj -clp:ErrorsOnly"' 2>&1 | tail -4

if [ ! -f "$REL/SimpleWall.exe" ]; then
    echo "Release build not found at $REL -- did the VM build fail?" >&2
    exit 1
fi

echo "==> Staging"
rm -rf "$STAGE" "$DIST/simple-wall.zip"
mkdir -p "$STAGE/SimpleWall/app"

cp -R "$REL"/. "$STAGE/SimpleWall/app/"

# The app is x64. The x86 natives are dead weight and roughly half the payload.
rm -rf "$STAGE/SimpleWall/app/libvlc/win-x86"

# Runtime artifacts must never ship -- a stale log or config would be read as
# this deployment's own, and a shipped config.json would overwrite the wall's.
rm -f "$STAGE/SimpleWall/app/simple-wall.log" "$STAGE/SimpleWall/app/simple-wall.1.log" \
      "$STAGE/SimpleWall/app/config.json"
# The .pdb is debug symbols -- no value on the wall, and it just inflates the zip.
rm -f "$STAGE/SimpleWall/app/SimpleWall.pdb"

cp "$REPO/packaging/install.bat"                 "$STAGE/SimpleWall/install.bat"
cp "$REPO/docs/RUNBOOK.md"                        "$STAGE/SimpleWall/RUNBOOK.md"
cp "$REPO/docs/plans/2026-07-16-acceptance.md"    "$STAGE/SimpleWall/acceptance.md"

echo "==> Sanity checks"
fail=0
check() { if [ -e "$STAGE/SimpleWall/$1" ]; then echo "  ok   $1"; else echo "  MISS $1"; fail=1; fi; }
check "app/SimpleWall.exe"
check "app/libvlc/win-x64/libvlc.dll"
check "app/libvlc/win-x64/libvlccore.dll"
check "app/libvlc/win-x64/plugins"
check "install.bat"
check "RUNBOOK.md"
check "acceptance.md"

if [ -d "$STAGE/SimpleWall/app/libvlc/win-x86" ]; then echo "  WARN win-x86 still present"; fail=1; fi
if [ -e "$STAGE/SimpleWall/app/config.json" ];    then echo "  WARN config.json shipped"; fail=1; fi

[ "$fail" -eq 0 ] || { echo "PACKAGE INCOMPLETE -- not zipping"; exit 1; }

echo "==> Zipping"
cd "$STAGE"
zip -qr "$DIST/simple-wall.zip" SimpleWall
cd "$REPO"
rm -rf "$STAGE"

echo
echo "Package ready: $DIST/simple-wall.zip"
du -h "$DIST/simple-wall.zip" | cut -f1
