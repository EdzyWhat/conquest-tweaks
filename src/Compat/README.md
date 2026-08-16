# `src/Compat/` — optional-mod compatibility fixes

Everything here is **dormant unless its target mod is installed**. The folders are drawn so a
source-mod author can read (and lift) exactly their slice. See the matching `docs/HANDOFF-*.md` for
each fold-in.

Two delivery mechanisms, one per how the fix has to reach the game:

## JSON-patch compat (no C#)

A patch under `../assets/conquesttweaks/patches/compatibility/<targetmodid>/` that self-gates with
`dependsOn: [{ modid: … }]`. No C# and no config toggle (a JSON patch can't read config), and it
applies **server-side** where blocktype JSON is resolved — which is why the whole mod is packaged
`side: Universal` even though the C# is client-only. Layout mirrors Conquest's own
`patches/compatibility/<modid>/` convention so the folder drops straight into their tree.

- **Visible Ores & Minerals** — `../assets/conquesttweaks/patches/compatibility/visibleoresandminerals/`.
  Repairs VOM ore veins Conquest breaks. Handoff: [`docs/HANDOFF-vom.md`](../../docs/HANDOFF-vom.md)
  (and [`docs/HANDOFF-conquest.md`](../../docs/HANDOFF-conquest.md) — Conquest ships no VOM compat and
  could adopt this).
- **Juicy Ores** — *not shipped.* Conquest already ships working Juicy Ores compat (since 2026-01-15,
  v1.0.7), so a patch here would be redundant and risks conflicting with Conquest's meta-patch. See
  the note in [`docs/HANDOFF-conquest.md`](../../docs/HANDOFF-conquest.md).

## Harmony compat (C#)

A Harmony patch applied at client startup, gated at runtime by `api.ModLoader.IsModEnabled(target)`
**and** a `Config` toggle. Used when the fix needs engine/render behaviour a JSON patch can't express.
The ModSystem owns one lazy `Harmony(modId)` instance, applies each fix as its own
`[HarmonyPatchCategory]` via `harmony.PatchCategory(...)` inside a try/catch fail-safe, and
`UnpatchAll(modId)` in `Dispose`. `0Harmony` is game-bundled — referenced `Private=false`, never
shipped.

- **`TerrainSlabs/`** — `SlabConnectedTexturesPatch.cs`, category `terrainslabs-connected-textures`,
  toggle `Config.EnableSlabsFix`. Makes Conquest's connected textures line up on slabs (rock/gravel/
  sand; grass-top soil slabs are a separate render path and not covered). Handoff:
  [`docs/HANDOFF-terrainslabs.md`](../../docs/HANDOFF-terrainslabs.md).

## Registry

`CompatFix.cs` is the shared descriptor. Fixes are declared in the `CompatFixes` array in
`../ConquestTweaksModSystem.cs`; `.ctc list` reports each fix's target, whether it's detected, its
mechanism, and (Harmony fixes) its enabled state.
