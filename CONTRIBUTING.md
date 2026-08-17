# Contributing

This mod is an umbrella of four independent feature groups (see [Group boundaries](#group-boundaries-dont-cross-them-casually) below).
Most contributions touch exactly one group; the folder you're in tells you which.

## Build & test loop (macOS)

```sh
python3 build/extract-vanilla.py   # only after changing FAMILIES or the resolver (group 4)
build/restage.sh                   # build + stage to VintagestoryData/Mods/conquesttweaks; then relog
```

- `dotnet`: `/opt/homebrew/bin/dotnet`. Game DLLs + SkiaSharp/Newtonsoft/0Harmony are referenced from
  the install via `$(VintageStoryPath)`, all `Private=false` — we never ship them.
- `VINTAGE_STORY` overrides the game path; `VS_DATA` overrides the data dir; `CONFIG=Release` for a
  release build.
- Texture/tint/patch changes bake into the atlas or resolve server-side at load — there is no live
  preview. Edit, restage, **relog**.

### Packaging a release

`build/package.sh` cuts a zip to `dist/` (git-ignored). One codebase, one DLL, two zips — the DLL
auto-detects the payload at load (`TextureReverts.PayloadPresent`):

```sh
build/package.sh              # PUBLIC  → dist/conquesttweaks-<ver>.zip  (no base-game art; the only build to share)
build/package.sh --private    # PRIVATE → dist/conquesttweaks-<ver>-private.zip  (bundles the vanilla payload)
```

- **Public** strips `textures/vanilla/`, so it redistributes nothing. The per-family reverts start
  inert in it, but a player can enable them with `.ctc reverts extract` (see `RevertExtractor.cs`),
  which regenerates the payload locally from *their own* game files into a `conquesttweaks-vanilla`
  side-car mod — the handbook's advanced section documents the flow. This is the portal upload.
- **Private** bundles the base-game payload so reverts work out of the box. It contains Anego Studios
  art — **personal use only, never publish or share** — and errors unless you've run
  `build/extract-vanilla.py` first.

## Re-verifying the patch surface after a game or mod update

Two things here patch code we don't own, so they need re-checking when their target moves:

- **Terrain Slabs Harmony fix** (`src/Compat/TerrainSlabs/SlabConnectedTexturesPatch.cs`) is a
  transpiler matching an instruction sequence in `Vintagestory.Client.NoObf.JsonTesselator.doMesh`. A
  game update can reshape that method. If the IL pattern no longer matches, the transpiler throws and
  the fix self-deactivates with a warning (the client keeps working) — but the fix is then *off*.
  Re-verify against the decompiled client after a VS update; the `openspec/` archive for this change
  documents the exact `doMesh` shape it was written against.
- **The ore JSON patches** (`src/assets/conquesttweaks/patches/compatibility/<modid>/`) mirror the
  target mod's texture-code names (`#cube`/`#ore1`) and `ore1ByType` mapping, and reference Conquest
  rock paths. If VOM renames its codes, or Conquest reorganizes its rock art, re-check the `value`
  blocks. Run `.ctc scan` in-game to surface any block that still resolves to the placeholder (it
  writes a full report to `ModConfig/ctc-missing-textures.txt`).

## Group boundaries (don't cross them casually)

- **Group 4 — `src/Core/`** (reverts, vibrancy, scanner): the mod's own features. Fold into nobody.
- **Group 3 — `src/Compat/TerrainSlabs/`**: ports to Terrain Slabs unchanged (`docs/HANDOFF-terrainslabs.md`).
- **Group 2 — `src/assets/.../patches/compatibility/`**: the ore-pack JSON compat (`docs/HANDOFF-vom.md`,
  `docs/HANDOFF-conquest.md`).
- **Group 1 — Conquest base copying**: intentionally empty. We copy no Conquest art. Do not add any.

## Assets: what may and may not be committed

- **Never commit** `src/assets/conquesttweaks/textures/vanilla/` — that's base-game Vintage Story art,
  regenerated per-machine by `build/extract-vanilla.py`, and it's `.gitignore`d for a reason (it's
  Anego Studios' art, not ours; see [CREDITS.md](./CREDITS.md)). A `git add -A` after a rename can
  silently un-ignore it — check `git status` before committing.
- Never add Conquest, VOM, Terrain Slabs, or Juicy Ores textures/DLLs. We reference them by path and
  resolve from the player's own installs.
