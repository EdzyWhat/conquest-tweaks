# Conquest VS Tweaks & Compatibility

A companion mod for the [Conquest VS Edition](https://mods.vintagestory.at/conquest) texture pack —
an umbrella for tuning the Conquest look and smoothing it over alongside other mods. It bundles:

- **Per-family vanilla reverts** — selectively restore the game's **original** appearance for the
  block families you find too vibrant/cartoonish, each an independent toggle.
- **A green-selective grass-tint vibrancy dial** — tone down foliage green without touching dry,
  brown, or autumn tones.
- **Optional per-mod compatibility fixes** — each activates automatically *only when its target mod
  is detected*, so you can run this alongside whatever's in your pack and it just fixes what's there
  to fix. Currently: the Visible Ores & Minerals ore-vein repair and the Terrain Slabs connected-
  textures fix (both below).

Requires the `conquest` mod (and `game`); the compatibility fixes have **no** hard dependency on
their targets — they're dormant until the target mod shows up.

> **Source-mod authors:** each compatibility fix is written to fold back into the mod it targets. See
> [`docs/HANDOFF-terrainslabs.md`](./docs/HANDOFF-terrainslabs.md),
> [`docs/HANDOFF-vom.md`](./docs/HANDOFF-vom.md), and
> [`docs/HANDOFF-conquest.md`](./docs/HANDOFF-conquest.md), plus [`CONTRIBUTING.md`](./CONTRIBUTING.md).

The visual tweaks (texture reverts + vibrancy) are **client-side** — install it on just your client
and they work on any server. The optional Visible Ores & Minerals fix (below) is the one part that
patches server-side data, so in **multiplayer** it only takes effect if the mod is installed on the
server too; in single-player everything works out of the box. The mod is not required on the server
(`requiredOnServer: false`), so a client-only install is always safe.

## What it can revert (each an independent toggle)

Ground/dirt reverted to **vanilla** by default: `soil`, `grasscover` (the grass-block top-cover),
`forestfloor`, `clay`, `farmland`, `stonepath` (path + its slab/stair variants).

Kept on **Conquest** by default (earthy building materials — switch with `.ctc set <name> vanilla`):
`peat`, `cob`, `rammedearth`, `mudbrick`.

Foliage (default **conquest**): `tallgrass`, `otherfoliage` (ferns/flowers/herbs/reeds/bamboo/…).
Conquest heavily restructured foliage, so `otherfoliage` coverage is **partial** — the grass-tint
vibrancy dial (below) is usually the better lever for "plants are too green."

## Grass/plant vibrancy (green-selective)

Desaturates only the **green** part of the plant tint, leaving dry/brown/autumn tones untouched.
`GrassGreenSaturation` is the main knob (1.0 = unchanged, **0.8 = the default gentle knock-down**,
~0.6 = stronger, 0.1 = almost grey-green).

The game tints plants by blending two colormaps — a **climate plant tint** (the dominant base) and
a **seasonal grass tint** (overlaid on top). Because the climate tint dominates, this dial
desaturates **both** by default; touching only the seasonal tint is nearly invisible. The climate
tint is shared by grass, ferns, bushes, reeds **and tree leaves**, so the effect tones down all
foliage green together — there's no colormap-only way to mute grass while leaving leaves vivid. (For
the curious: `SeasonGrassTintOnly: true` restricts the pass to the seasonal grass tint, which
reproduces the old near-invisible behavior.)

## Config

Three ways to configure it, all applying **on relog**:

**1. In-game commands** (chat — note the leading period, it's a client command):

- `.ctc list` — show which texture each surface uses (vanilla/conquest) and the vibrancy settings
- `.ctc set <name> vanilla|conquest` — pick the texture for a surface, e.g. `.ctc set stonepath conquest`
- `.ctc vibrancy <0..1>` — set the green-saturation multiplier
- `.ctc scan` — list blocks that resolve to the pink/black placeholder; writes a full report to
  `ModConfig/ctc-missing-textures.txt`

**2. In-game handbook** — open the Survival Handbook (`H`) → **Guides** → **Conquest VS Tweaks & Compatibility**
for a page listing the commands, the revertable families, and the vibrancy dial.

**3. Config file** — auto-created at `VintagestoryData/ModConfig/conquesttweaks.json`. Holds
everything the commands set, plus advanced knobs with no command: `GrassGreenBrightness`, the green
hue band (`GreenHueCenter`/`GreenHueRange`/`GreenHueFalloff`), `SeasonGrassTintOnly`, and
`ReportMissingTexturesOnLoad`.

> **Changes apply on relog.** Block textures and tint are baked into the texture atlas at world
> load, so there is no per-frame live preview — edit config / run a command, then relog.

## How it works

In `AssetsLoaded` (after the game's assets are loaded and patched, but before the block texture
atlas is composed) the mod overwrites Conquest's texture **bytes** in-memory with bundled vanilla
source art, keyed at the same path. Conquest's extra tiled variants collapse onto the single vanilla
texture → vanilla look. The revert pass edits no blocktype JSON, so it's load-order-independent.
(The only JSON patches the mod ships are the optional VOM ore fix below.)

**It never introduces the pink/black `unknown` placeholder:** a Conquest texture is overwritten only
when a real vanilla source was bundled for it *and* that Conquest asset actually exists in the loaded
set.

## Visible Ores & Minerals compatibility (ore placeholders)

[Visible Ores & Minerals (VOM)](https://mods.vintagestory.at/visibleoresandminerals) turns the ore
blocktypes into 3D ore veins. Its texture wiring silently fails when Conquest strips those blocks'
textures first (a load-order clash in how JSON patches add into a removed object) — so the veins show
the pink/black placeholder and the log fills with *"Missing mapping for texture code #cube"*.

When VOM is installed, this mod repairs the veins with a small set of JSON patches: it re-adds the
surrounding-stone texture using **Conquest rock art** across *every* ore/rock combo, plus VOM's ore/
gem lump textures. The patches only activate when VOM is present, and apply after Conquest, so
nothing changes if you don't run VOM. No configuration needed.

Run **`.ctc scan`** to list any blocks that still resolve to the placeholder (it also detects veins
whose shape needs a texture code the block doesn't provide); a full report is written to
`ModConfig/ctc-missing-textures.txt`. Without VOM, Conquest 1.0.7's own ores scan clean.

## Terrain Slabs compatibility (connected textures)

[Terrain Slabs](https://mods.vintagestory.at/terrainslabs) (which requires PlaceOnSlabs) adds
half-height slab blocks. Under Conquest, the pack's **connected textures** don't line up on slabs —
every slab picks a *random* tile instead of the position-correct one, so slab surfaces look
mismatched next to the full blocks they should blend with. This is an engine limitation on the JSON
draw path slabs use (only cube-shaped blocks get position-aware tile selection), not a Conquest bug.

When Terrain Slabs is installed, this mod applies a small Harmony patch that feeds slabs the same
position-correct tile selection cube blocks already get, so Conquest's connected textures line up.
It activates only when `terrainslabs` is detected and is on by default; disable it with
`EnableSlabsFix: false` in the config. The connected-texture join is correct on the slab's top face
(where it matters most); the thin edge faces may be imperfect.

> This patches internal client render code, so it can only take effect on a matching game version.
> If a game update moves the code it targets, the fix quietly deactivates (a warning is logged) and
> the rest of the mod keeps working.

## Project structure

The mod is an umbrella of **four independent feature groups**, foldered so a source-mod author can
read (and adopt) exactly their slice — the folder boundary *is* the fold-in boundary:

| Group | Lives in | Folds into | Handoff |
|---|---|---|---|
| **1. Conquest base copying** | *(nothing)* | — | We copy no Conquest art. The only bundled art is base-game vanilla (group 4's payload), owned by Anego Studios — see [CREDITS.md](./CREDITS.md). |
| **2. Ore-pack JSON compat** | `src/assets/conquesttweaks/patches/compatibility/<modid>/` | VOM / Conquest | [HANDOFF-vom](./docs/HANDOFF-vom.md), [HANDOFF-conquest](./docs/HANDOFF-conquest.md) |
| **3. Terrain Slabs Harmony fix** | `src/Compat/TerrainSlabs/` | Terrain Slabs | [HANDOFF-terrainslabs](./docs/HANDOFF-terrainslabs.md) |
| **4. Standalone reverts / tweaks** | `src/Core/` | nobody (the mod itself) | — |

`src/ConquestTweaksModSystem.cs` is a thin orchestrator that loads config, registers the `.ctc`
commands, and dispatches to the groups; `src/Compat/README.md` maps the two compat mechanisms.

## Build & install (macOS)

```sh
python3 build/extract-vanilla.py   # regenerate bundled vanilla art from your local install
build/restage.sh                   # build + copy to VintagestoryData/Mods/conquesttweaks
```

`VINTAGE_STORY` overrides the game path; `VS_DATA` overrides the data dir.
