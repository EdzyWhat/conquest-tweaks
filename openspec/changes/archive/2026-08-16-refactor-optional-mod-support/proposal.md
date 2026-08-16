## Why

The mod started as "Conquest Vanilla Reverts" and grew a Visible Ores & Minerals (VOM) ore-vein
fix, so its identity now reads as "Conquest Vanilla Reverts + Visible Ores & Minerals Fix" — a name
that advertises one specific optional-mod fix in the title. We want to add a second optional-mod
fix (Terrain Slabs connected-textures under Conquest, tracked in a separate proposal) and expect
more over time. Baking each supported mod into the name and treating them as near-requirements
doesn't scale and misrepresents the mod: only `conquest` is truly required.

This change **re-scopes the mod into a Conquest compatibility umbrella**: an always-on core (per-family
vanilla texture reverts + the grass-tint vibrancy dial, both meaningful only with Conquest) plus a
set of **optional compatibility fixes that each activate only when their target mod is detected**.
It establishes the architecture and conventions the slab fix (and future fixes) plug into, without
changing the mod's stable identity (modid, `.cvv` command, asset domain, config filename).

Importantly, the VOM fix is *already* in the desired shape — its JSON patches self-gate via
`dependsOn: [{ modid: visibleoresandminerals }]` (`assets/conquestvanillavom/patches/vom-ore-*.json`),
so it needs no code and no hard dependency. This change generalizes that "supports-if-present"
principle and adds the missing piece: a **C#/Harmony** activation path for fixes that JSON patches
can't express (like the slab render fix), gated at runtime by `api.ModLoader.IsModEnabled(...)`.

## What Changes

- **Re-scope the display identity** (name/description/handbook wording) to a compatibility-umbrella
  framing (e.g. "Conquest Compatibility Fixes"), presenting reverts, vibrancy, VOM, and the coming
  slabs fix as included features. **Keep all internal identifiers stable**: modid `conquestvanillavom`,
  the `.cvv` command, the `conquestvanillavom` asset domain, the `ConquestVanillaVom`
  assembly/namespace, and the handbook `pageCode` — renaming any of these would wipe every user's
  config and break the asset domain. (Exact name is an open decision — see design.md D1.)
- **Formalize two compat mechanisms** and where each belongs:
  - *JSON-patch compat* (e.g. VOM): self-gates via `dependsOn`; no C#, no config toggle. Unchanged.
  - *C#/Harmony compat* (e.g. slabs): gated at runtime by `IModLoader.IsModEnabled(targetModId)` **and**
    a config toggle; applied through a single Harmony instance owned by the ModSystem.
- **Add Harmony infrastructure** to the ModSystem: reference the game-bundled `0Harmony.dll` (do not
  ship it), construct one `Harmony(Mod.Info.ModID)` instance lazily when at least one C# compat module
  activates, and `UnpatchAll(Mod.Info.ModID)` in `Dispose`.
- **Add a minimal compat-module registry**: a small interface describing each C#/Harmony fix
  (target modid, config-enabled check, apply method), iterated in `StartClientSide`; each module is
  applied only when its config toggle is on and its target mod is detected. This is the extension
  point the slab fix will implement.
- **Add per-compat config toggles** for the C#/Harmony fixes (default on, still gated by detection).
  VOM keeps no toggle (a JSON patch can't read config; `dependsOn` is its gate).
- **Surface compatibility status** in `.cvv list`: a section showing each optional fix, whether its
  target mod is detected, and whether it is enabled.
- **Keep dependency posture correct**: `conquest` (and `game`) stay HARD deps in `modinfo.json`;
  `visibleoresandminerals` and `terrainslabs` stay OUT of modinfo `dependencies` (listing them would
  make them required — the opposite of the goal). `side: Universal` and the client-only `ShouldLoad`
  gate are unchanged.
- **Reframe docs** (`CLAUDE.md`, `README.md`, handbook `lang/en.json`) around the umbrella model and
  document the two compat mechanisms + the detection/Harmony pattern.

Out of scope (separate proposal): the actual Terrain Slabs connected-textures Harmony patch — its
target method, fix logic, and difficulty. This change only builds the slot it plugs into.

## Capabilities

### New Capabilities
- `mod-compat-activation`: How the mod activates optional compatibility fixes — the umbrella model
  (always-on Conquest core vs. optional per-mod fixes), the two activation mechanisms (JSON
  `dependsOn` vs. runtime `IsModEnabled` + Harmony), the dependency posture (hard `conquest`, soft
  optional targets), config toggles, and how compat status is reported to the user.

### Modified Capabilities
<!-- None — no existing specs under openspec/specs/ yet; this is the first captured capability. -->

## Impact

- **C#**: `ConquestVanillaVomModSystem.cs` (add Harmony instance + compat-module iteration in
  `StartClientSide`, unpatch in `Dispose`, compat section in `.cvv list`); new `Compat/` folder with
  a compat-module interface and the (empty until the slab proposal) module registry; `Config.cs`
  (add per-compat toggles). `Mod.csproj` (add `0Harmony` reference, `Private=false`).
- **Assets**: VOM patches under `assets/conquestvanillavom/patches/` unchanged. Handbook
  `lang/en.json` title/text reworded.
- **Metadata**: `modinfo.json` display `name`/`description` reworded; `dependencies`, `side`,
  `requiredOnClient/Server` unchanged. Internal identifiers (modid, domain, `.cvv`, namespaces,
  `pageCode`) unchanged.
- **Docs**: `README.md`, `CLAUDE.md` reframed.
- **Non-goal / no change**: the reverts, vibrancy, and scanner logic; the VOM patch content; the
  stable modid/command/domain identity; the `AssetsLoaded` byte-edit timing crux.
