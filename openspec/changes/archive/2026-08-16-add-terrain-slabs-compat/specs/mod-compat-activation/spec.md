## ADDED Requirements

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
- **WHEN** a user sets the fix's toggle to false in `ModConfig/conquestvanillavom.json` and relogs
- **THEN** the fix's Harmony patches are not applied even when its target mod is detected
