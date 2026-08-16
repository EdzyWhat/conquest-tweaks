## Why

Terrain Slabs (modid `terrainslabs`, which hard-depends on PlaceOnSlabs `placeonslabs`) adds
half-height slab blocks. Under the Conquest VS Edition texture pack, the pack's **connected
textures** (VS's tiled-texture system: neighbour-aware tile selection so adjacent blocks form a
continuous surface) do not apply to slabs — every slab picks a random tile variant instead of the
position-correct one, so slab surfaces look broken/mismatched next to the full blocks they should
blend with. The Terrain Slabs author documents this directly: *"Connected textures don't work for
slabs when using Conquest VS Edition. This applies to all slabs in game actually and requires a
harmony patch to the game engine (I will fix this someday)."*

Research into the engine (decompiled `VintagestoryLib`/client) confirms this is an **engine
limitation on the JSON draw path, not a Conquest bug**:

- Connected textures are driven by `BakedCompositeTexture.GetTiledTexturesSelector(tiles, tileSide,
  posX, posY, posZ)`, which returns the position-correct tile index for a tiled texture.
- **Only `CubeTesselator.Tesselate` calls that selector.** Slabs render as `EnumDrawType.JSON`
  (their shape is `game:block/basic/slab/slab-down`), so they route through `JsonTesselator.doMesh`,
  which contains **zero** tiled-texture code.
- Corroborating: `ShapeTesselatorManager.CreateFastTextureAlternates` early-returns for JSON blocks
  (`if (!block.HasTiles || block.DrawType == EnumDrawType.JSON) return;`), `TextureSource.UpdateVariant`
  ignores `BakedTiles`, and `JsonTesselator.doMesh` selects its texture variant by `MurmurHash3Mod`
  (a hash → **random**, not position-derived). Hence the mismatched tiles.

The umbrella infrastructure landed in the `refactor-optional-mod-support` change provides the exact
slot for this: a **C#/Harmony compat fix**, gated at runtime by `IsModEnabled("terrainslabs")` plus
a config toggle, applied via a Harmony category. This proposal fills that slot.

## What Changes

- **Add a Terrain Slabs connected-textures Harmony fix** that feeds the position-correct tiled-texture
  selector into the JSON draw path for slab blocks, so Conquest's connected textures apply to slabs.
  Deep-read of the decompiled client (`ShapeTesselatorManager.Tesselate`, `JsonTesselator.doMesh`,
  `BakedCompositeTexture.GetTiledTexturesSelector`) shows the fix is **a single Harmony transpiler on
  `Vintagestory.Client.NoObf.JsonTesselator.doMesh(TCTCache, MeshData, int)`** — NOT the two-patch
  "Option A" the initial research proposed. The tiled alternate meshes for JSON blocks **already
  exist** (`ShapeTesselatorManager.Tesselate` builds `altblockModelDatas[blockId]` with one mesh per
  tile for any block where `HasTiles`, JSON draw type included); `doMesh` simply picks among them with
  `GameMath.MurmurHash3Mod(posX,posY,posZ,len)` — **random per block** — at the line
  `int num = GameMath.MurmurHash3Mod(...); sourceMesh = array[num];`. The fix replaces just that index
  with `BakedCompositeTexture.GetTiledTexturesSelector(bakedTiles, tileSide, posX, posY, posZ)` (mod
  the array length), mirroring exactly what `CubeTesselator.Tesselate` already does for cube blocks.
  No patch to `TextureSource.UpdateVariant` or `CreateFastTextureAlternates` is needed. See design.md
  D3 for the corrected mechanism and D4 for the whole-mesh-single-tile limitation.
- **Add the slab fix to the `CompatFixes` registry** as a `Harmony`-mechanism `CompatFix`
  (`TargetModId = "terrainslabs"`, `HarmonyCategory = "terrainslabs-connected-textures"`,
  `ConfigEnabled = cfg => cfg.EnableSlabsFix`). The existing `ActivateHarmonyCompat` loop then picks
  it up automatically — no new activation code.
- **Add the patch class** tagged `[HarmonyPatchCategory("terrainslabs-connected-textures")]` under
  `src/Compat/` (e.g. `SlabConnectedTexturesPatch.cs`), containing the postfix + prefix/transpiler.
- **Add the `EnableSlabsFix` config toggle** to `Config.cs` (default **on**; still inert unless
  `terrainslabs` is detected). This completes task 3 that `refactor-optional-mod-support` deferred.
- **Gate on `terrainslabs` alone** (it hard-depends on `placeonslabs`, so its presence implies both).
- **Document + validate**: `.cvv list` shows the slab fix (detected + enabled); update handbook
  `lang/en.json` and README's supported-fixes list to mention Terrain Slabs; in-game validation of
  the render on the top face and edges.

Out of scope: reverting or restyling slab textures; fixing connected textures for **all** JSON-drawtype
blocks generally (the patch is scoped to slab blocks to keep blast radius small); any server-side
behavior (this is a pure client render fix).

## Capabilities

### New Capabilities
- `terrain-slabs-connected-textures`: When Terrain Slabs is installed and the fix is enabled,
  Conquest's connected (tiled) textures apply to slab blocks via a position-correct tile selection on
  the JSON draw path — the fix activates only on detection, is toggleable, and is torn down cleanly on
  dispose.

### Modified Capabilities
- `mod-compat-activation`: extended with the first concrete Harmony-mechanism fix and the
  `EnableSlabsFix` config toggle the umbrella change left as a slot (its tasks 3.1/3.2). No change to
  the activation mechanism itself.

## Impact

- **C#**: `ConquestVanillaVomModSystem.cs` (one new entry in the `CompatFixes` registry — no logic
  change); new `src/Compat/SlabConnectedTexturesPatch.cs` (the `[HarmonyPatchCategory]` patch class);
  `Config.cs` (`EnableSlabsFix`, default on, round-trips via `StoreModConfig`).
- **Runtime/Harmony**: patches `TextureSource.UpdateVariant` + `JsonTesselator.doMesh` in the client
  render path, applied only when `terrainslabs` is detected and the toggle is on; removed via the
  existing `Dispose` → `UnpatchAll`.
- **Assets/Docs**: handbook `lang/en.json` supported-fixes list + `README.md` gain a Terrain Slabs
  entry. No blocktype/texture asset changes.
- **Metadata**: `modinfo.json` unchanged — `terrainslabs` stays OUT of `dependencies` (soft target).
  Internal identifiers unchanged.
- **Risk**: **moderate** — this patches internal client render methods (`Vintagestory.Client.NoObf`),
  which can shift between game versions, and the whole-slab mesh receives one tile index (correct for
  the dominant top face, potentially imperfect on thin edge faces). Requires in-game validation and a
  clean fallback (fix simply doesn't apply / logs) if the target methods aren't found. See design.md.
