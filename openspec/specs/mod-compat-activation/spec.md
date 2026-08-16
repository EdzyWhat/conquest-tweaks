# mod-compat-activation Specification

## Purpose
TBD - created by archiving change refactor-optional-mod-support. Update Purpose after archive.
## Requirements
### Requirement: Always-on Conquest core vs. optional per-mod fixes
The mod SHALL present as a Conquest compatibility umbrella composed of an always-on core (per-family
vanilla texture reverts and the grass-tint vibrancy dial) that requires only Conquest, plus optional
compatibility fixes that each target a specific third-party mod and activate only when that mod is
present. The core SHALL function with no third-party mod beyond `conquest` installed.

#### Scenario: Only Conquest installed
- **WHEN** the game loads with `conquest` present but neither Visible Ores & Minerals nor Terrain Slabs installed
- **THEN** the reverts and vibrancy dial apply normally
- **AND** no optional compatibility fix activates, and no error or missing-target warning is logged for the absent optional mods

#### Scenario: An optional target mod is present
- **WHEN** the game loads with `conquest` and a supported optional mod (e.g. Visible Ores & Minerals) present
- **THEN** that mod's compatibility fix activates in addition to the always-on core

### Requirement: JSON-patch compatibility self-gates via dependsOn
A compatibility fix that can be expressed purely as block/asset JSON SHALL be delivered as a JSON patch
that self-gates with `dependsOn` on its target mod, and SHALL NOT require any C# code or config toggle.
The Visible Ores & Minerals ore-vein fix SHALL remain implemented this way.

#### Scenario: Target mod for a JSON fix is absent
- **WHEN** the JSON fix's target mod is not loaded
- **THEN** the patch is skipped by the engine's `dependsOn` gate and has no effect

### Requirement: C#/Harmony compatibility gates on runtime detection and config
A compatibility fix that requires engine or render behavior SHALL be implemented as a Harmony patch that
activates only when BOTH its config toggle is enabled AND its target mod is detected at runtime via
`IModLoader.IsModEnabled(targetModId)`. When either condition is false, the fix's Harmony patches SHALL
NOT be applied.

#### Scenario: Target mod present and toggle on
- **WHEN** the target mod is detected and the fix's config toggle is enabled
- **THEN** the fix's Harmony patch category is applied during client startup

#### Scenario: Target mod absent
- **WHEN** the target mod is not detected
- **THEN** the fix's Harmony patches are not applied, regardless of the config toggle

#### Scenario: Toggle disabled
- **WHEN** the fix's config toggle is disabled
- **THEN** the fix's Harmony patches are not applied, even if the target mod is detected

### Requirement: Single owned Harmony instance with clean teardown
The mod SHALL own a single Harmony instance keyed on its modid, create it lazily only when at least one
C#/Harmony fix activates, apply each fix as its own Harmony patch category, and unpatch all of its
patches on `Dispose`.

#### Scenario: No C#/Harmony fix active
- **WHEN** no C#/Harmony compatibility fix activates during startup
- **THEN** no Harmony instance is created

#### Scenario: Mod disposed
- **WHEN** the mod system is disposed
- **THEN** all Harmony patches applied under the mod's modid are removed

### Requirement: Dependency posture reflects true requirements
`modinfo.json` SHALL declare only `game` and `conquest` as dependencies. Optional target mods
(`visibleoresandminerals`, `terrainslabs`, and any future targets) SHALL NOT appear in `modinfo.json`
dependencies. The mod SHALL remain `side: Universal` with a client-only C# runtime.

#### Scenario: Optional mod not installed
- **WHEN** a user installs the mod alongside only Conquest
- **THEN** the mod loads successfully with no unmet-dependency error for any optional target mod

### Requirement: Stable internal identity is preserved across the re-scope
The re-scope to an umbrella SHALL change only user-facing display text (mod `name`/`description`, README
title, handbook title/body). It SHALL NOT change the modid, the `.ctc` command, the `conquesttweaks`
asset domain, the assembly/namespace, the config filename, or the handbook `pageCode`.

#### Scenario: Existing user updates the mod
- **WHEN** a user who already has `ModConfig/conquesttweaks.json` updates to the re-scoped version
- **THEN** their existing config is loaded unchanged and their `.ctc` commands continue to work

### Requirement: Compatibility status is reported to the user
The `.ctc list` output SHALL include a compatibility-fixes section listing each optional fix with its
target mod, whether that mod is detected, and (for C#/Harmony fixes) whether it is enabled in config.

#### Scenario: User runs .ctc list
- **WHEN** a user runs `.ctc list`
- **THEN** the output shows, for each optional fix, its target modid, detected yes/no, and enabled state

### Requirement: Per-fix config toggle for C#/Harmony fixes persists
Each C#/Harmony compatibility fix SHALL be backed by a boolean field in `Config` (e.g.
`EnableSlabsFix`) that defaults to enabled and round-trips through the mod's config load/store
(`StoreModConfig`) like every other setting. The toggle SHALL gate activation in addition to runtime
detection (effective activation = toggle enabled AND target mod detected). JSON-patch fixes SHALL NOT
have such a toggle (a JSON patch cannot read config; its `dependsOn` is the gate).

#### Scenario: New Harmony fix ships with its toggle
- **WHEN** a C#/Harmony compatibility fix is added
- **THEN** a corresponding `Config` boolean exists, defaults to on, and persists across sessions via
  the config file

#### Scenario: User disables a fix in config
- **WHEN** a user sets the fix's toggle to false in `ModConfig/conquesttweaks.json` and relogs
- **THEN** the fix's Harmony patches are not applied even when its target mod is detected

