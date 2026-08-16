## 1. Config toggle (completes refactor tasks 3.1/3.2)

- [x] 1.1 Added `EnableSlabsFix` (bool, default `true`) to `src/Config.cs` in a new compat-settings group, doc comment notes it is detection-gated (inert without Terrain Slabs) and applies on relog
- [x] 1.2 `EnableSlabsFix` is a plain public field → round-trips through `LoadModConfig`/`StoreModConfig` like every other field (no custom serialization)

## 2. Registry entry (plugs into existing infrastructure)

- [x] 2.1 Added the `Harmony`-mechanism `CompatFix` to `CompatFixes` (`DisplayName = "Terrain Slabs connected textures"`, `TargetModId = "terrainslabs"`, `Mechanism = Harmony`, `HarmonyCategory = "terrainslabs-connected-textures"`, `ConfigEnabled = cfg => cfg.EnableSlabsFix`)
- [x] 2.2 `ActivateHarmonyCompat` picks it up with no logic change (applies category only when `IsEnabledInConfig` && `IsModEnabled("terrainslabs")`)

## 3. Harmony patch class (single transpiler — corrected mechanism, design.md D3)

- [x] 3.1 Added `src/Compat/SlabConnectedTexturesPatch.cs`, tagged `[HarmonyPatchCategory("terrainslabs-connected-textures")]`, `[HarmonyPatch(typeof(JsonTesselator), nameof(doMesh))]`; `MurmurHash3Mod`/helper resolved via `AccessTools`
- [x] 3.2 Transpiler uses `CodeMatcher.MatchStartForward` on the FIRST `GameMath.MurmurHash3Mod` call and inserts `ldarg.1 (vars); ldarg.3 (lodLevel); call CorrectTileIndex` right after it (leaving the corrected index on the stack); throws if the pattern isn't found
- [x] 3.3 Helper `CorrectTileIndex(int randomIndex, TCTCache vars, int lodLevel)`: re-derives the alt-mesh array from `vars`+`lodLevel` (mirrors doMesh:555), and if `block.HasTiles` and tiled `BakedTiles` resolve, returns `GameMath.Mod(GetTiledTexturesSelector(bakedTiles, UP, x,y,z), array.Length)`; else `randomIndex`. Wrapped in try/catch → never throws
- [x] 3.4 `tileSide = BlockFacing.UP.Index` (top-face-correct, design.md D4); `TCTCache` fields (`block`, `blockId`, `posX/Y/Z`, `shapes`) + `ShapeTesselatorManager.altblockModelDatasLod*` all verified accessible by a clean compile against VintagestoryLib
- [x] 3.5 The `HasTiles` guard + `BakedTiles` resolution scope the change to tiled blocks; non-tiled JSON alternates keep the original random index (helper returns `randomIndex`); cube path untouched

## 4. Fail-safe

- [x] 4.1 `ActivateHarmonyCompat` wraps `PatchCategory` in try/catch → a resolution/patch failure logs `compat '…' unavailable on this game version (patch failed)` and leaves the rest of the mod working (design.md D5); the transpiler's `CorrectTileIndex` is itself try/catch-guarded

## 5. Docs

- [x] 5.1 Added Terrain Slabs to the supported-fixes list in handbook `lang/en.json` and a dedicated section in `README.md`'s compatibility area (VTML: no HTML-escaping)
- [x] 5.2 Documented the mechanism + edge-face limitation (D4) for maintainers in `CLAUDE.md` (Terrain Slabs fix subsection) and design.md

## 6. Validate

- [x] 6.1 `openspec validate add-terrain-slabs-compat --strict` clean
- [x] 6.2 Build clean (`CONFIG=Release bash build/restage.sh`); new patch type confirmed present in the staged assembly
- [ ] 6.3 **In-game validation (mandatory, design.md D7)**: with Conquest + Terrain Slabs + PlaceOnSlabs installed — (a) slab top faces join Conquest's connected textures with neighbours, (b) no NRE/tesselation errors in the client log, (c) `.cvv list` shows the fix detected + enabled; then with Terrain Slabs absent, (d) confirm the fix stays inert and nothing regresses
