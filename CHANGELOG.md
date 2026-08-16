# Changelog

All notable changes to this project. Dates are ISO (YYYY-MM-DD).

## [Unreleased]

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
- **Terrain Slabs coverage** confirmed in-game (Conquest v1.0.7): rock/gravel/sand slabs get correct
  connected textures; grass-covered soil/peat/clay slabs do **not** (their grassy top renders on the
  `TopSoil` renderpass, which the `doMesh` transpiler doesn't touch).
- **Juicy Ores**: intentionally not patched. Conquest VS Edition has shipped working Juicy Ores compat
  since 2026-01-15 (v1.0.7); a patch here would be redundant and risk conflicting with Conquest's
  index-based meta-patch.

## Earlier history

See git log:
- Rebrand to "Conquest VS Tweaks & Compatibility" (modid frozen as `conquesttweaks`); OpenSpec changes
  archived.
- Rescope to a compatibility umbrella + add the Terrain Slabs connected-textures fix.
- Initial: Conquest Vanilla Reverts + Visible Ores & Minerals fix.
