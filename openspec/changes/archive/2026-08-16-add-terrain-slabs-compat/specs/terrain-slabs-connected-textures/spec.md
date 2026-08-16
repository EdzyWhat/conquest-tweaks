## ADDED Requirements

### Requirement: Connected textures apply to slabs when Terrain Slabs is present
When Terrain Slabs (`terrainslabs`) is installed alongside Conquest and the fix is enabled, the mod
SHALL make Conquest's connected (tiled) textures apply to slab blocks, by injecting position-correct
tile selection into the JSON draw path used to render slabs. Slab surfaces SHALL select their tile
from world position (via the engine's tiled-texture selector) rather than a random per-block variant.

#### Scenario: Terrain Slabs, Conquest, and fix all active
- **WHEN** the game loads with `conquest` and `terrainslabs` present and `EnableSlabsFix` enabled
- **THEN** the slab fix's Harmony patch category is applied during client startup
- **AND** slab blocks render with Conquest's connected textures selected by position, so a slab's top
  face joins the tiled pattern of the neighbouring full blocks

#### Scenario: Terrain Slabs absent
- **WHEN** `terrainslabs` is not detected
- **THEN** the slab fix's Harmony patches are not applied, regardless of the `EnableSlabsFix` toggle
- **AND** no error or missing-target warning is logged for the absent mod

#### Scenario: Fix disabled by config
- **WHEN** `terrainslabs` is present but `EnableSlabsFix` is false
- **THEN** the slab fix's Harmony patches are not applied and slab rendering is unchanged

### Requirement: Slab fix gates on terrainslabs alone
The slab fix SHALL gate on detection of `terrainslabs` only. Because Terrain Slabs hard-depends on
PlaceOnSlabs (`placeonslabs`), detecting `terrainslabs` is sufficient to imply both are present.

#### Scenario: Only terrainslabs checked
- **WHEN** the fix evaluates whether to activate
- **THEN** it checks `IsModEnabled("terrainslabs")` and does not require a separate `placeonslabs` check

### Requirement: Slab fix fails safe on incompatible game versions
The slab fix patches internal client render methods. If a target method cannot be resolved on the
installed game version, the mod SHALL log a warning and continue running with the slab fix inactive,
without crashing the client or affecting the reverts, vibrancy, scanner, or other compat fixes.

#### Scenario: Target method not found
- **WHEN** a target render method for the slab patch cannot be resolved at startup
- **THEN** the slab fix does not apply, a warning is logged, and the rest of the mod functions normally

### Requirement: Slab fix is torn down on dispose
The slab fix's Harmony patches SHALL be removed when the mod system is disposed, via the mod's single
owned Harmony instance and its `UnpatchAll(modid)` teardown.

#### Scenario: Mod disposed with slab fix active
- **WHEN** the slab fix was applied and the mod system is disposed
- **THEN** the slab fix's patches are removed along with all other patches under the mod's modid

### Requirement: Slab fix is reported in .cvv list
The `.cvv list` compatibility-fixes section SHALL include the Terrain Slabs fix, showing its target
mod (`terrainslabs`), whether that mod is detected, and whether it is enabled in config.

#### Scenario: User runs .cvv list with Terrain Slabs installed
- **WHEN** a user runs `.cvv list` with `terrainslabs` present
- **THEN** the output shows the Terrain Slabs fix as detected and reports its enabled state
