## 1. Harmony infrastructure (build)

- [x] 1.1 Add a `<Reference Include="0Harmony">` (HintPath `$(VintageStoryPath)/Lib/0Harmony.dll`, `<Private>false</Private>`) to `src/Mod.csproj`, alongside the existing SkiaSharp/Newtonsoft refs
- [x] 1.2 Confirm the project still builds clean via `CONFIG=Release bash build/restage.sh` after adding the reference (no patches yet) — clean, 0 warnings/errors

## 2. Compat-module scaffolding (C#)

- [x] 2.1 Add `src/Compat/CompatFix.cs` — `CompatMechanism { JsonPatch, Harmony }` enum + `CompatFix` descriptor `{ DisplayName, TargetModId, Mechanism, ConfigEnabled?, HarmonyCategory?, IsEnabledInConfig(Config) }` (unified over both mechanisms so status reporting covers JSON + Harmony fixes; simpler than the D6 `IModCompat`/`Apply` shape since Harmony fixes apply via `PatchCategory`)
- [x] 2.2 In `ConquestVanillaVomModSystem`, cache `modId = Mod.Info.ModID` (StartPre) and add a `Harmony? harmony` field + a `static CompatFix[] CompatFixes` registry (VOM entry only; the slabs Harmony entry lands in the slab proposal)
- [x] 2.3 In `StartClientSide`, `ActivateHarmonyCompat`: for each Harmony fix where `IsEnabledInConfig(config) && capi.ModLoader.IsModEnabled(TargetModId)`, lazily `harmony ??= new Harmony(modId)`, `harmony.PatchCategory(HarmonyCategory)`, and log `compat active: …`. (Zero Harmony fixes registered yet, so the loop is inert until slabs.)
- [x] 2.4 Add `Dispose()` override → `harmony?.UnpatchAll(modId)` (null-guarded; instance only created when a Harmony fix activates)

## 3. Config toggles — DEFERRED to the slab proposal (now DONE there)

- [x] 3.1 Done in `add-terrain-slabs-compat` (its tasks 1.1/1.2): `Config.EnableSlabsFix = true` added; VOM keeps no field (JSON `dependsOn` is its gate). This change only provided the `CompatFix.ConfigEnabled` slot, which the slab fix plugs into.
- [x] 3.2 `EnableSlabsFix` round-trips via the standard `LoadModConfig`/`StoreModConfig` path (verified in the slab change).

## 4. VOM representation in the umbrella (no behavior change)

- [x] 4.1 Confirmed the three `vom-ore-*.json` patches are unchanged and still gate via `dependsOn: [{ modid: visibleoresandminerals }]`
- [x] 4.2 VOM represented in `CompatFixes` as a `JsonPatch`-mechanism fix (target `visibleoresandminerals`, no toggle) so `.cvv list` reports it

## 5. Compat status in `.cvv list`

- [x] 5.1 Added a "Compatibility fixes" section to `OnList`: for each fix shows target modid, detected (`IsModEnabled`), mechanism, and enabled state (`always` for no-toggle JSON fixes)
- [ ] 5.2 VOM shows `detected/not present, json, enabled always`. The slabs entry (detected + on/off) is verified when the slab fix is added.

## 6. Dependency posture (verify)

- [x] 6.1 Confirmed `modinfo.json` `dependencies` lists ONLY `game` + `conquest`; `visibleoresandminerals`/`terrainslabs` absent
- [x] 6.2 Confirmed `side: Universal`, `requiredOnClient: true`, `requiredOnServer: false`; `ShouldLoad(side) => side == Client` unchanged

## 7. Display re-scope — name confirmed: "Conquest Tweaks & Compatibility"

- [x] 7.1 Updated `modinfo.json` `name`/`description` to the umbrella name/framing
- [x] 7.2 Updated `README.md` H1 + intro and handbook `lang/en.json` `handbook-title`/`handbook-text` to the umbrella framing (reverts + vibrancy + VOM as features; slabs when added)
- [x] 7.3 Verified NO change to modid `conquestvanillavom`, `.cvv` command, asset domain, assembly/namespace `ConquestVanillaVom`, config filename, or `pageCode conquestvanillavom-guide`

## 8. Docs

- [x] 8.1 Reframed `CLAUDE.md` intro to the umbrella model; documented the two compat mechanisms (JSON `dependsOn` vs. runtime `IsModEnabled` + Harmony), the single-instance/PatchCategory pattern, and that Harmony is game-bundled (not shipped)
- [x] 8.2 Reframed `README.md` around included compatibility fixes and how each activates

## 9. Validate

- [x] 9.1 `openspec validate refactor-optional-mod-support --strict` clean
- [x] 9.2 Build clean and relogged; the Harmony compat path is confirmed active in-game (2026-08-16) via the Terrain Slabs fix rendering correctly on the Limestone Sand Slab — validates the `CompatFixes` registry → `ActivateHarmonyCompat` → `PatchCategory` flow end to end
