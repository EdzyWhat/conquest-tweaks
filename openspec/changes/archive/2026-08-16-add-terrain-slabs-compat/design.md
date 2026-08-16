## Context

The `refactor-optional-mod-support` change turned the mod into a Conquest compatibility umbrella and
built the C#/Harmony activation slot: a `CompatFix` registry, a lazily-created single `Harmony(modId)`
instance, an `ActivateHarmonyCompat` loop that applies `harmony.PatchCategory(fix.HarmonyCategory)`
for each Harmony fix whose config toggle is on **and** whose `TargetModId` is detected via
`capi.ModLoader.IsModEnabled`, and `Dispose → UnpatchAll(modId)`. It intentionally deferred the first
real Harmony fix (and the `EnableSlabsFix` config toggle, its tasks 3.1/3.2) to this proposal.

The problem being fixed (from engine research on the decompiled client/lib):

- **Connected textures** = VS's tiled-texture system. A `CompositeTexture` with `tiles`/`tilesWidth`
  bakes into `BakedCompositeTexture.TilesInfo`, and `BakedCompositeTexture.GetTiledTexturesSelector(
  tiles, tileSide, posX, posY, posZ)` returns the position-correct tile index so neighbouring blocks
  form a continuous surface. Conquest ships tiled textures for its terrain blocks.
- **`CubeTesselator.Tesselate` consults that selector per face** (`GetTiledTexturesSelector(array2, i,
  posX, posY, posZ)` → `array2[Mod(sel, len)].TextureSubId`). Slabs are `EnumDrawType.JSON` (shape
  `game:block/basic/slab/slab-down`), so they render through `JsonTesselator.doMesh(TCTCache, MeshData,
  int)`.

**Corrected mechanism (verified against the decompile — supersedes the initial "Option A" research).**
The tiled alternate meshes for JSON blocks **already exist**. `ShapeTesselatorManager.TesselateBlock`
sets `block.HasTiles = TileTexturesCount(block) > 0` for *any* draw type, and
`ShapeTesselatorManager.Tesselate(...)` then builds `altblockModelDatas[block.BlockId]` with one mesh
per tile — `for (j in tilesCount) { texSource.UpdateVariant(block, j % tilesCount); Tesselate... }`
(lines ~1060-1071). So a tiled JSON slab has its per-tile meshes baked. The **only** defect is the
selection: `JsonTesselator.doMesh` picks the alternate with
`int num = GameMath.MurmurHash3Mod(vars.posX, ..., vars.posZ, array.Length); sourceMesh = array[num];`
— a **random** hash, not the neighbour-aware tile index. `CreateFastTextureAlternates`'s
`|| block.DrawType == EnumDrawType.JSON` early-return only skips building `block.FastTextureVariants`,
which the JSON path never reads — so it is **irrelevant** to this fix, and `TextureSource.UpdateVariant`
does **not** need patching (it already ran, per-tile, at bake time).

So the fix is a **single** change: replace that one `MurmurHash3Mod` selection with
`BakedCompositeTexture.GetTiledTexturesSelector(bakedTiles, tileSide, posX, posY, posZ)` (mod array
length) when `block.HasTiles`, exactly mirroring `CubeTesselator`. This requires Harmony (no JSON/asset
edit can alter the tesselator), but it is one transpiler on one method, not the two-patch pair.

## Goals / Non-Goals

**Goals:**
- Make Conquest's connected textures apply to Terrain Slabs slab blocks, activated only when
  `terrainslabs` is present and the fix is enabled.
- Plug into the existing umbrella infrastructure with no changes to the activation mechanism — just a
  registry entry, a patch class, and the config toggle.
- Fail safe: if the target internal methods can't be resolved on the installed game version, the fix
  logs and does nothing rather than crashing the client.

**Non-Goals:**
- Connected textures for *all* JSON-drawtype blocks (scope to slabs to bound risk).
- Server-side or gameplay changes (pure client render).
- Reverting/restyling slab textures, or touching PlaceOnSlabs placement behavior.
- Perfect per-face tiling on the thin edge faces if the engine only affords one tile index per mesh
  (see D4).

## Decisions

**D1 — Deliver as a C#/Harmony `CompatFix`, gated on `terrainslabs` + `EnableSlabsFix`.**
The fix is a `Harmony`-mechanism `CompatFix { TargetModId = "terrainslabs", HarmonyCategory =
"terrainslabs-connected-textures", ConfigEnabled = cfg => cfg.EnableSlabsFix }` added to `CompatFixes`.
`ActivateHarmonyCompat` already applies exactly the enabled+detected Harmony fixes, so no activation
code changes. *Alternative — a standalone `Harmony.PatchAll` in StartClientSide: rejected; it bypasses
the umbrella's detection/toggle gating and status reporting.*

**D2 — Gate on `terrainslabs` alone (not both mods).**
Terrain Slabs hard-depends on PlaceOnSlabs (`placeonslabs`), so `IsModEnabled("terrainslabs")` implies
both are present. Detecting the single mod that owns the slab blocks is sufficient and simplest.
*Alternative — require both: redundant.*

**D3 — Patch shape = a single transpiler on `JsonTesselator.doMesh` (corrected from Option A).**
A **transpiler on `Vintagestory.Client.NoObf.JsonTesselator.doMesh(TCTCache, MeshData, int)`** that
locates the alternate-mesh selection `int num = GameMath.MurmurHash3Mod(posX, ..., posZ, array.Length)`
(the FIRST `MurmurHash3Mod` call — there is a second, at line ~568, for random rotations, which must
be left untouched) and replaces the computed index with a call to a helper that returns
`GameMath.Mod(BakedCompositeTexture.GetTiledTexturesSelector(bakedTiles, tileSide, posX, posY, posZ),
array.Length)` when the block `HasTiles` and its tiled `BakedTiles` can be resolved; otherwise it
returns the original `MurmurHash3Mod` value unchanged (so non-tiled JSON alternates keep their random
pick). A prefix/postfix cannot do this — the selection is a method-internal local feeding a private
call — so a transpiler is required. **No** patch to `TextureSource.UpdateVariant` or
`CreateFastTextureAlternates` (per the corrected mechanism in Context). *Alternative — reroute slabs
through `CubeTesselator`: rejected; slabs legitimately draw via JSON, and rerouting risks shape/AO
regressions.*

**D3a — Transpiler is fragile; wrap it in a defensive matcher + fail-safe.** Match on the
`GameMath.MurmurHash3Mod` call operand and the surrounding `ldloc array` / `stloc num` pattern via
`CodeMatcher`; if the expected pattern isn't found (game version drift), throw so `PatchCategory`
fails and the D5 fail-safe deactivates the fix with a warning rather than silently mis-patching. The
helper reads the current block, position, and the tiled `BakedCompositeTexture[]` from `TCTCache vars`
(the first arg) — the exact `TCTCache` field names for the alternate array / position are pinned
against the installed assembly during implementation.

**D4 — Accept one tile index per slab mesh initially.**
`doMesh` tesselates the whole slab shape; the cleanest injection point yields a single tile selection
for the mesh. This is correct for the dominant **top** face (where connected textures matter most) and
may be imperfect on the thin vertical edge faces. Ship the top-face-correct version, note the edge
limitation, and only pursue per-face selection if in-game validation shows it's objectionable.
*Rationale — matches the research finding; per-face tiling would need much deeper tesselator surgery
for marginal visual gain on 1-2px edges.*

**D5 — Fail safe on method-resolution failure.**
Harmony `PatchCategory` throws if a target method isn't found. Wrap the slab fix's activation so a
resolution failure is caught, logged as a warning (`[modid] slab fix unavailable on this game version`),
and leaves the rest of the mod fully working. Prefer resolving target methods defensively (via
`AccessTools`) inside the patch class. *This keeps a game update from bricking the client.*

**D6 — Config: `EnableSlabsFix = true` by default.**
Default on so users who have Terrain Slabs + Conquest get the fix automatically; still detection-gated,
so it's completely inert for anyone without Terrain Slabs. Round-trips through `StoreModConfig` like
every other field. Completes `refactor-optional-mod-support` tasks 3.1/3.2. *Alternative — default off:
rejected; the fix's whole point is zero-setup repair, and detection gating already prevents side
effects.*

**D7 — Validation is in-game and mandatory before release.**
Because this patches internal `Vintagestory.Client.NoObf` render methods, unit reasoning isn't enough:
build, install alongside Terrain Slabs + PlaceOnSlabs + Conquest, and visually confirm (a) slab top
faces join Conquest's connected textures with neighbours, (b) no NRE/tesselation errors in the client
log, (c) `.cvv list` shows the fix detected + enabled, and (d) with Terrain Slabs absent the fix stays
inert and nothing regresses.

## Open questions for the user
1. **Edge-face imperfection (D4)** — acceptable to ship top-face-correct first and iterate only if the
   thin edge faces look wrong in-game? (Recommended: yes.)
2. **Default on (D6)** — confirm `EnableSlabsFix` defaults to on (detection-gated). (Recommended: yes.)
