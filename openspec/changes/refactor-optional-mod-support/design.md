## Context

The mod (modid `conquestvanillavom`, `side: Universal`, C# client-only via `ShouldLoad(side) => side == Client`
at `ConquestVanillaVomModSystem.cs:51`) hard-depends on `conquest` and does three things today: (a)
per-family vanilla texture reverts and (b) a grass-tint vibrancy dial — both in-memory byte edits at
`AssetsLoaded`, both meaningful only with Conquest present — and (c) a Visible Ores & Minerals (VOM)
ore-vein placeholder fix delivered as three JSON patches that self-gate via
`dependsOn: [{ modid: visibleoresandminerals }]` (`assets/conquestvanillavom/patches/vom-ore-*.json`).
All C# lives in one file.

We are about to add a second optional-mod fix — Terrain Slabs connected-textures under Conquest —
which the engine research shows requires a **Harmony** patch on the JSON tesselator path (slabs are
`EnumDrawType.JSON`; that path ignores tiled textures entirely). Harmony patches can't self-gate the
way JSON patches do, so we need a runtime activation mechanism. Rather than bolt it on, this change
establishes the umbrella model so this and future optional-mod fixes have a consistent home.

Reference pattern (user-provided): the **Toolsmith** mod (`Mario90900/Toolsmith`) does exactly this at
scale — `api.ModLoader.IsModEnabled("smithingplus")` / `"xskills"` / `"canjewelry"` gate compat
branches throughout, a single static `Harmony HarmonyInstance = new Harmony(ModId)` is guarded against
double-init and applied via granular `HarmonyInstance.PatchCategory("<category>")` calls, and
`Dispose` → `UnpatchAll(ModId)`. We adopt the same shape.

## Goals / Non-Goals

**Goals:**
- Re-scope the mod's *presentation* to a Conquest compatibility umbrella (always-on core + optional
  per-mod fixes) while keeping every internal identifier stable.
- Define the two compat mechanisms (JSON `dependsOn` vs. runtime `IsModEnabled` + Harmony) and where
  each belongs, so contributors know which to reach for.
- Add the C#/Harmony activation infrastructure (single Harmony instance, per-fix category, runtime
  gating, config toggle) as the slot the slab fix plugs into.
- Keep the dependency posture honest: only `conquest`/`game` are hard deps; optional targets are soft.
- Report compat status to the user (`.cvv list`).

**Non-Goals:**
- The Terrain Slabs connected-textures patch itself (target method, fix logic, difficulty) — separate
  proposal; this change only builds the extension point.
- Any change to reverts/vibrancy/scanner behavior, the VOM patch content, or the `AssetsLoaded`
  byte-edit timing crux.
- Renaming the modid, `.cvv` command, asset domain, assembly/namespace, or handbook `pageCode`.
- Reaching *into* another mod's API (`GetModSystem<T>`) — our fixes patch the engine, not the target
  mod's code, so mere presence detection (`IsModEnabled`) suffices.

## Decisions

**D1 — Re-scope the display name only; freeze all internal identifiers. [RESOLVED — name confirmed by user]**
The name "Conquest Vanilla Reverts + Visible Ores & Minerals Fix" appears in `modinfo.json` `name`/
`description`, `README.md` H1, and the handbook `lang/en.json` `handbook-title`/`handbook-text`. We
reword these to the confirmed umbrella name **"Conquest Tweaks & Compatibility"** (chosen by the user
via AskUserQuestion) describing reverts + vibrancy + VOM + slabs as included features. We do **NOT** change: modid
`conquestvanillavom` (also the asset domain `assets/conquestvanillavom/`, the vanilla-texture source
lookups, and lang-key domain), the `.cvv` command, the `ConquestVanillaVom` assembly/namespace, or the
handbook `pageCode: conquestvanillavom-guide` — changing any of these would silently wipe every user's
`ModConfig/conquestvanillavom.json` and break the asset domain. *Alternative — full rename incl. modid:
rejected (config-loss + churn for cosmetics; the modid isn't user-facing).* **User must confirm the final
display name before the wording edits land.**

**D2 — Two compat mechanisms, chosen by capability, not preference.**
- *JSON-patch compat* (VOM): a patch that only adds/merges block JSON and can self-gate via
  `dependsOn`. Needs no C# and no config toggle (a JSON patch can't read config). Stays exactly as is.
- *C#/Harmony compat* (slabs, future): anything requiring engine/render behavior. Gated at runtime by
  `IModLoader.IsModEnabled(targetModId)` **and** a `Config` toggle; applied via a Harmony category.
*Alternative — force everything through C#: rejected; VOM's JSON `dependsOn` is simpler, server-side-
correct, and already working.*

**D3 — Single Harmony instance, one category per fix, lazy init (Toolsmith pattern).**
The ModSystem owns `static Harmony harmony`. On `StartClientSide`, iterate the C#/Harmony compat
modules; for each whose config toggle is on AND target mod is detected, lazily `harmony ??= new
Harmony(Mod.Info.ModID)` and `harmony.PatchCategory("<fix-category>")`. `Dispose` → `harmony?.UnpatchAll(
Mod.Info.ModID)`. Using `PatchCategory` (patches tagged `[HarmonyPatchCategory("<fix>")]`) instead of a
blanket `PatchAll()` means only detected+enabled fixes are applied, and each fix's patches live in one
tagged class. Guard double-init like Toolsmith (`if (harmony != null) return;` equivalent via the null-
coalescing lazy init). *Alternative — `PatchAll(typeof(X))` per module: fine too, but categories read
better for an umbrella and match the reference mod.*

**D4 — Harmony is referenced, never shipped.** `0Harmony.dll` is bundled with the game at
`$(VintageStoryPath)/Lib/0Harmony.dll`; add a `<Reference Include="0Harmony">` with `<Private>false</Private>`
to `src/Mod.csproj` (same as the existing SkiaSharp/Newtonsoft refs). Shipping our own copy risks
version clashes with the game's.

**D5 — Config: per-fix toggle for C#/Harmony fixes only.** Add e.g. `EnableSlabsFix = true` (default on,
still gated by detection so it's inert without Terrain Slabs). VOM gets no field — `dependsOn` is its
gate and a JSON patch can't read config. Effective activation for a C# fix = `toggle && IsModEnabled(target)`.

**D6 — Compat-module shape: a tiny interface, not a framework.** One interface (`TargetModId`,
`EnabledInConfig(Config)`, `Apply(ICoreClientAPI, Harmony)`) and a small array in the ModSystem. The
reverts/vibrancy/scanner stay where they are (they're core, not optional-mod compat). Keep the diff
minimal — extracting the core into modules is optional polish, explicitly out of scope here. *Alternative
— a general plugin/registry system: over-engineered for 1 JSON + 1 Harmony fix.*

**D7 — Dependency posture.** `conquest` + `game` stay HARD deps (load-ordering after Conquest is relied
on by the reverts and the VOM patch; the mod is meaningless without Conquest). `visibleoresandminerals`
and `terrainslabs` stay OUT of `modinfo.json` dependencies (adding them makes them required). `side:
Universal` + client-only `ShouldLoad` unchanged (VOM's JSON needs the server side; all C# is client-render).

**D8 — Report status in `.cvv list`.** Add a "Compatibility fixes" section: for each optional fix, show
target modid, detected (`IsModEnabled`) yes/no, and enabled (config) yes/no. Cheap, and it makes the
umbrella legible to users. VOM shows as detected-only (no toggle); slabs shows detected + enabled.

## Open questions for the user
1. **Final display name** (D1) — ✅ RESOLVED: "Conquest Tweaks & Compatibility". (Internal ids stay put.)
2. **Slab fix config default** — `EnableSlabsFix` default on (recommended, still detection-gated) or off?
3. **Gate the slab fix on `terrainslabs` alone, or require both `terrainslabs` + `placeonslabs`?**
   (Terrain Slabs hard-depends on PlaceOnSlabs, so `terrainslabs` present implies both — `terrainslabs`
   alone is likely sufficient. Confirmed in the slab proposal.)
