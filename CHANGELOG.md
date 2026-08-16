# Changelog

All notable changes to this project. Dates are ISO (YYYY-MM-DD).

## [Unreleased]

### Added
- **Grass-slab connected textures** for Terrain Slabs. Grass-covered **soil** and **clay** slabs now
  connect their grassy top like the full blocks do. Root cause was a data bug in Conquest's own
  Terrain Slabs compat patches (`terrainslabs/soil.json`, `clay.json`): their grass-slab
  `specialSecondTexture` lacked the `tiles`/`tilesWidth` block, so it baked as random alternates
  instead of connected tiles. Fixed with two JSON patches that re-add the tiled definition onto
  Conquest's entries (`src/assets/conquesttweaks/patches/compatibility/terrainslabs/{soil,clay}.json`),
  referencing only base-game texture paths. **Clay needed a different patch shape than soil**: Terrain
  Slabs' `clay.json` blocktype declares a blanket `drawtype: TopSoil` (soil uses
  `drawtypeByType` so its `-none` variant is Json/Opaque), so a naive blanket `tilesWidth: 4` put a
  16-tile grid on the 1-tile `grasscoverage/none/` overlay and — because the TopSoil path doesn't clamp
  the tile index — rendered no-grass clay slabs (and their neighbours) as black voids. The clay patch
  therefore tiles the overlay via `tilesWidthByType {*-none: 1, *: 4}` (the same idiom Conquest ships
  for its peat slab and full clay block). It also restores the **base** clay texture to the full
  block's connected form (`all` with `tiles` + `tilesWidth: 3`), which Conquest's slab patch had
  degraded to a bare `/*` alternate wildcard — so grass-clay slab tops now connect on both layers.
  The **soil** base was restored too, but *per fertility* to match the full soil block (unlike clay's
  flat width): `compost`/`medium` stay random alternates (not authored as a connectable grid),
  `low`/`verylow` connect at `tilesWidth: 3` (9 tiles), `high` at `tilesWidth: 4` (16 tiles). Because
  Conquest's slab patch collapses every fertility into one broad `texturesByType` key and the byType
  resolver is first-match-wins by insertion order (so appended sibling keys can't win), the soil base
  split is expressed with an `allByType` nested inside that broad key — the same object-valued
  nested-byType shape Conquest's own forest-floor compat uses for `specialSecondTextureByType`; it
  falls back to Conquest's alternates if the mechanism ever fails to resolve, so there's no regression
  risk. Peat slabs were already correct in Conquest;
  forest-floor
  slabs were verified as a non-bug — their `forestoverlay{grass}` is a per-stage single texture with no
  tiled set to connect, and Conquest's full forest-floor block is un-tiled the same way. This is a
  `TopSoil`-renderpass fix and is separate from the Harmony `doMesh` transpiler — which, on the current
  pack, is a **confirmed no-op** (see Notes): rock/gravel/sand slabs are `/*` alternates
  (`HasTiles=false`), so the transpiler's gate skips them and they render varied via Conquest's own
  alternates. **So the grass-slab JSON patches are the only slab change that alters what you see this
  release.**
- `.ctc slabfix [on|off]` command to toggle the Terrain Slabs `doMesh` transpiler (`EnableSlabsFix`);
  no argument reports the current state. Writes config only (the patch is applied once at load), so
  relog to apply. Documented in the in-game handbook Guides page and the README.
- **Connected-texture selector clamp (black-void guard).** A one-line Harmony postfix on
  `BakedCompositeTexture.GetTiledTexturesSelector` that wraps its returned tile index into range
  (`GameMath.Mod(index, tiles.Length)`). The engine's selector can return an out-of-range index
  whenever a block declares `tilesWidth` greater than the tiles it actually ships (rows =
  `tileCount / tilesWidth` = 0, so the column term overshoots a too-short array); the non-clamping
  `TopSoil` render path turns that into black/void blocks (the failure mode a naive clay grass-slab
  overlay hit). The clamp is a **no-op for correctly-authored blocks** and only ever converts a
  would-be void into a valid wrapped tile, so it eliminates the whole black-void bug class regardless
  of future width/tile-count drift — our (and Conquest's) declared widths no longer have to be
  re-verified against tile counts to stay void-safe. Gated on `conquest` (hard dep ⇒ always active in
  the pack context) with config toggle `EnableTiledSelectorClamp` (default on);
  `src/Compat/TiledSelectorClampPatch.cs`. Documented as an engine-level (Anego) hardening in the
  handoff adoption maps, not a pack fix.
- `.ctc drawtype <pattern>` diagnostic: groups matching blocks by render signature (drawtype /
  renderpass / shape / **base tile count** / overlay tile count) and writes a report to
  `ModConfig/ctc-drawtype-<pattern>.txt`. Used to confirm the grass-slab fix (overlay tile count goes
  0 → 16/20 to match the full block) and to compare a full grass block's base `baseTiles` against its
  slab's (diagnosing whether the dirt body actually connects on the slab or only the grass top does).

### Changed
- Reorganized the source into four legible feature groups so a source-mod author can read and fold in
  exactly their slice (mirrors the handoff model of the sibling `libgui-toolsmith-sharpness` project):
  - `src/Core/` — the standalone reverts / vibrancy / scanner (group 4), extracted from the monolithic
    ModSystem, which is now a thin orchestrator.
  - `src/Compat/TerrainSlabs/` — the connected-textures Harmony fix (group 3).
  - `src/assets/conquesttweaks/patches/compatibility/<modid>/` — the ore-pack JSON compat (group 2),
    re-nested to mirror Conquest VS Edition's own `patches/compatibility/<modid>/` convention (was
    `patches/vom-ore-*.json`).
- Added a handoff doc set: `docs/HANDOFF-terrainslabs.md` (BeloMaximka), `docs/HANDOFF-vom.md`
  (Skyforger007), `docs/HANDOFF-conquest.md` (CreativeRealms & Arkaik), plus `CONTRIBUTING.md`, this
  changelog, and a PR template.

### Notes
- **Terrain Slabs coverage** confirmed in-game (Conquest v1.0.7, VS 1.22.6):
  - **Grass-covered soil/clay slabs now connect** via the JSON patches added this release — the grassy
    top renders on the `TopSoil` renderpass (a data fix, not the transpiler). This is the real,
    visible win of the release.
  - **The `doMesh` Harmony transpiler is a no-op on this pack** — verified by an on/off/original A-B
    screenshot test (2026-08-16, identical across all three). Conquest authors rock/gravel/sand slabs
    (and grassless `*-none` soil) as `/*` alternates (`block.HasTiles==false`), and `CorrectTileIndex`
    early-returns for non-`HasTiles` blocks, so it never fires on them; their varied appearance is
    Conquest's own random alternates. It's kept (default on, `.ctc slabfix`) as a staged,
    mechanism-correct hook for a future `HasTiles` tiled-JSON slab — with an untested tiled-JSON caveat
    (`UpdateVariant` is a no-op for tiles, so baked meshes may be identical and the redirect may still
    not connect; see `docs/HANDOFF-terrainslabs.md`).
  - Peat slabs were already correct in Conquest.
- **Juicy Ores**: intentionally not patched. Conquest VS Edition has shipped working Juicy Ores compat
  since 2026-01-15 (v1.0.7); a patch here would be redundant and risk conflicting with Conquest's
  index-based meta-patch.

## Earlier history

See git log:
- Rebrand to "Conquest VS Tweaks & Compatibility" (modid frozen as `conquesttweaks`); OpenSpec changes
  archived.
- Rescope to a compatibility umbrella + add the Terrain Slabs connected-textures fix.
- Initial: Conquest Vanilla Reverts + Visible Ores & Minerals fix.
