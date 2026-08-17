# Conquest VS Tweaks & Compatibility

A companion mod for the [Conquest VS Edition](https://mods.vintagestory.at/conquest) texture pack —
it tunes one part of the Conquest look and smooths the pack over alongside other mods. It bundles:

- **A green-selective grass-tint vibrancy dial** — optionally tone down foliage green without touching
  dry, brown, or autumn tones. Off by default; opt in when you want it.
- **Optional per-mod compatibility fixes** — each activates automatically *only when its target mod
  is detected*, so you can run this alongside whatever's in your pack and it just fixes what's there
  to fix. Currently: the Visible Ores & Minerals ore-vein repair and the Terrain Slabs connected-
  textures fix (both below).

Out of the box it changes nothing about how Conquest looks — the vibrancy dial starts off, and the
compatibility fixes only repair rendering that's already broken. Requires the `conquest` mod (and
`game`); the compatibility fixes have **no** hard dependency on their targets — they're dormant until
the target mod shows up.

> **Mod authors:** each compatibility fix is built to fold back into the mod it targets — see the handoff docs in [`docs/`](./docs/) and [`CONTRIBUTING.md`](./CONTRIBUTING.md).

The vibrancy dial is **client-side** — install it on just your client and it works on any server. The
optional Visible Ores & Minerals fix (below) is the one part that patches server-side data, so in
**multiplayer** it only takes effect if the mod is installed on the server too; in single-player
everything works out of the box. The mod is not required on the server (`requiredOnServer: false`), so
a client-only install is always safe.

## Grass/plant vibrancy (green-selective)

Desaturates only the **green** part of the plant tint, leaving dry/brown/autumn tones untouched. It's
**off by default**; run `.ctc vibrancy 0.8` (or set `GrassVibrancy: true` in the config) to enable it.
`GrassGreenSaturation` is the main knob (1.0 = unchanged, **0.8 = a gentle knock-down**, ~0.6 =
stronger, 0.1 = almost grey-green).

The game tints plants by blending two colormaps — a **climate plant tint** (the dominant base) and
a **seasonal grass tint** (overlaid on top). Because the climate tint dominates, this dial
desaturates **both**; touching only the seasonal tint is nearly invisible. The climate
tint is shared by grass, ferns, bushes, reeds **and tree leaves**, so the effect tones down all
foliage green together — there's no colormap-only way to mute grass while leaving leaves vivid. (For
the curious: `SeasonGrassTintOnly: true` restricts the pass to the seasonal grass tint, which
reproduces the old near-invisible behavior.)

## Config

Three ways to configure it, all applying **on relog**:

**1. In-game commands** (chat — note the leading period, it's a client command):

- `.ctc list` — show the current vibrancy settings and which compatibility fixes are active
- `.ctc vibrancy <0..1>` — set the green-saturation multiplier (setting a value turns the dial on)
- `.ctc scan` — list blocks that resolve to the pink/black placeholder; writes a full report to
  `ModConfig/ctc-missing-textures.txt`
- `.ctc slabfix [on|off]` — toggle the Terrain Slabs connected-texture fix (`Config.EnableSlabsFix`);
  no arg reports the current state. Relog to apply.

**2. In-game handbook** — open the Survival Handbook (`H`) → **Guides** → **Conquest VS Tweaks & Compatibility**
for a page listing the commands, the vibrancy dial, and the compatibility fixes.

**3. Config file** — auto-created at `VintagestoryData/ModConfig/conquesttweaks.json`. Holds
everything the commands set, plus advanced knobs with no command: `GrassGreenBrightness`, the green
hue band (`GreenHueCenter`/`GreenHueRange`/`GreenHueFalloff`), `SeasonGrassTintOnly`, and
`ReportMissingTexturesOnLoad`.

> **Changes apply on relog.** The tint is baked into the texture atlas at world load, so there is no
> per-frame live preview — edit config / run a command, then relog.

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

## Build & package (macOS)

Develop / test in-place:

```sh
build/restage.sh                   # build + copy to VintagestoryData/Mods/conquesttweaks
```

Cut the release zip under `dist/`:

```sh
build/package.sh                   # → dist/conquesttweaks-<ver>.zip (portal-safe, redistributes nothing)
```

`VINTAGE_STORY` overrides the game path; `VS_DATA` overrides the data dir; `CONFIG` sets Debug/Release.
`dist/` is git-ignored. (Contributors: see [`CONTRIBUTING.md`](./CONTRIBUTING.md) for the complete
build & packaging notes.)
