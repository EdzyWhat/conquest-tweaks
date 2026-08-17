#!/usr/bin/env bash
# Package the mod into a release .zip under dist/.
#
# TWO builds, ONE codebase (the DLL auto-detects the payload at runtime; see
# TextureReverts.PayloadPresent):
#
#   PUBLIC  (default)  dist/conquesttweaks-<version>.zip
#       Ships NO base-game art (assets/conquesttweaks/textures/vanilla/ is excluded), so it
#       redistributes nothing. The per-family vanilla reverts are inert in this build; vibrancy
#       and the compatibility fixes work fully. This is the portal-safe upload.
#
#   FULL   (--full)    dist/conquesttweaks-<version>-full.zip
#       Includes the bundled vanilla-texture payload, so reverts work out of the box. Contains
#       base-game textures (c) Anego Studios => PERSONAL USE, do not publish. Requires the payload
#       to be present first: run  python3 build/extract-vanilla.py  against your own install.
#
# Env (shared with restage.sh):
#   CONFIG   Debug|Release (default Release for packaging)
#   DOTNET   dotnet path   (default /opt/homebrew/bin/dotnet)
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DOTNET="${DOTNET:-/opt/homebrew/bin/dotnet}"
CONFIG="${CONFIG:-Release}"
TFM="net10.0"
MODID="conquesttweaks"

MODE="public"
if [ "${1:-}" = "--full" ]; then MODE="full"; fi
if [ "${1:-}" = "--public" ]; then MODE="public"; fi

VANILLA_DIR="$ROOT/src/assets/$MODID/textures/vanilla"
VERSION="$(grep -o '"version"[[:space:]]*:[[:space:]]*"[^"]*"' "$ROOT/src/modinfo.json" | grep -o '[0-9][^"]*' | head -1)"
[ -n "$VERSION" ] || { echo "!! could not read version from modinfo.json" >&2; exit 1; }

echo ">> building ($CONFIG)"
"$DOTNET" build "$ROOT/src/Mod.csproj" -c "$CONFIG" -v q -nologo
DLL="$ROOT/src/bin/$CONFIG/$TFM/ConquestTweaks.dll"
[ -f "$DLL" ] || { echo "!! built DLL not found at $DLL" >&2; exit 1; }

# --- assemble a clean staging tree (zip root = mod root) --------------------------------------
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
cp "$DLL" "$STAGE/"
cp "$ROOT/src/modinfo.json" "$STAGE/"
[ -f "$ROOT/src/modicon.png" ] && cp "$ROOT/src/modicon.png" "$STAGE/"
cp -R "$ROOT/src/assets" "$STAGE/assets"

if [ "$MODE" = "full" ]; then
    if [ ! -d "$VANILLA_DIR" ] || [ -z "$(find "$VANILLA_DIR" -type f -name '*.png' 2>/dev/null | head -1)" ]; then
        echo "!! --full needs the vanilla payload, but $VANILLA_DIR is empty." >&2
        echo "   Generate it first:  python3 build/extract-vanilla.py" >&2
        exit 1
    fi
    SUFFIX="-full"
    echo ">> FULL build: bundling vanilla payload (PERSONAL USE - contains base-game art, do not publish)"
else
    # PUBLIC: strip the base-game texture payload so nothing is redistributed.
    rm -rf "$STAGE/assets/$MODID/textures/vanilla"
    SUFFIX=""
    echo ">> PUBLIC build: vanilla payload excluded (portal-safe, redistributes nothing)"
fi

# --- zip --------------------------------------------------------------------------------------
DIST="$ROOT/dist"
mkdir -p "$DIST"
OUT="$DIST/$MODID-$VERSION$SUFFIX.zip"
rm -f "$OUT"
( cd "$STAGE" && zip -rq "$OUT" . -x '*.DS_Store' )

echo ">> wrote $OUT"
echo "   $(find "$STAGE/assets" -type f | wc -l | tr -d ' ') assets, $(du -h "$OUT" | cut -f1) total."
