<!--
Thanks for contributing. This mod is an umbrella of four independent groups — keep a PR inside one
group where you can, and preserve the invariants below.
-->

## What group does this touch?

- [ ] Group 4 — `src/Core/` (reverts / vibrancy / scanner; the mod's own features)
- [ ] Group 3 — `src/Compat/TerrainSlabs/` (connected-textures Harmony fix)
- [ ] Group 2 — `src/assets/.../patches/compatibility/` (ore-pack JSON compat)
- [ ] Docs / build / other

## Summary

<!-- What changes, and why. Link the issue / ModDB thread if there is one. -->

## Invariants I didn't break

- [ ] **No base-game or third-party art committed.** `src/assets/conquesttweaks/textures/vanilla/`
      stays `.gitignore`d; no Conquest / VOM / Terrain Slabs / Juicy Ores textures or DLLs added.
      (`git status` shows no texture files staged.)
- [ ] **The modid `conquesttweaks` is unchanged** (asset domain, config file, `.ctc` command, assembly
      name, handbook pageCode all depend on it).
- [ ] **Compat stays dormant without its target.** New fixes gate on `IsModEnabled(target)` (Harmony)
      or `dependsOn` (JSON), and don't touch a target-absent setup.
- [ ] **Harmony fixes keep the try/catch fail-safe** so a version-drift patch failure deactivates just
      that fix, never crashes the client.
- [ ] Built clean (`build/restage.sh`) and, for render/patch changes, verified in-game after a relog.

## Testing

<!-- Game version, which mods installed, what you saw. `.ctc scan` output if relevant. -->
