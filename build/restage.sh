#!/usr/bin/env bash
# Build the mod and (re)stage it as an unpacked mod folder in VintagestoryData/Mods.
#
# Layout produced:
#   Mods/conquestvanillavom/
#     modinfo.json
#     ConquestVanillaVom.dll
#     assets/conquestvanillavom/textures/vanilla/<family>/...
#
# Env:
#   VINTAGE_STORY   game install (default /Applications/Vintage Story.app)
#   VS_DATA         data dir     (default ~/Library/Application Support/VintagestoryData)
#   CONFIG          Debug|Release (default Debug)
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DOTNET="${DOTNET:-/opt/homebrew/bin/dotnet}"
CONFIG="${CONFIG:-Debug}"
TFM="net10.0"
MODID="conquestvanillavom"
VS_DATA="${VS_DATA:-$HOME/Library/Application Support/VintagestoryData}"
DEST="$VS_DATA/Mods/$MODID"

echo ">> building ($CONFIG)"
"$DOTNET" build "$ROOT/src/Mod.csproj" -c "$CONFIG" -v q -nologo

DLL="$ROOT/src/bin/$CONFIG/$TFM/ConquestVanillaVom.dll"
[ -f "$DLL" ] || { echo "!! built DLL not found at $DLL" >&2; exit 1; }

echo ">> staging to $DEST"
rm -rf "$DEST"
mkdir -p "$DEST"
cp "$DLL" "$DEST/"
cp "$ROOT/src/modinfo.json" "$DEST/"
[ -f "$ROOT/src/modicon.png" ] && cp "$ROOT/src/modicon.png" "$DEST/"
cp -R "$ROOT/src/assets" "$DEST/assets"

echo ">> done. $(find "$DEST/assets" -type f | wc -l | tr -d ' ') bundled assets staged."
echo "   Restart Vintage Story (or relog) to apply."
