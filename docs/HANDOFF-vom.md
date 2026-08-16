# Hey Skyforger007

This is a fix for the Conquest VS Edition incompatibility reported on the Visible Ores & Minerals
ModDB page — the one where ore veins render the pink/black placeholder and the log fills with
*"Missing mapping for texture code #cube"*. It's a small set of JSON patches, no C#. You're welcome
to fold it into VOM; it can also just live in our umbrella mod. This doc explains the fix either way.

Since VOM has no public repo, the easiest channel is a ModDB comment reply or DM — point me at
wherever's convenient.

The patches are three files:
[`src/assets/conquesttweaks/patches/compatibility/visibleoresandminerals/ore-{graded,ungraded,gem}.json`](../src/assets/conquesttweaks/patches/compatibility/visibleoresandminerals/).

---

## Why it breaks (the mechanism)

It's a JSON-patch load-order clash, not a bug in either mod on its own:

1. Conquest does `op:remove /textures` on `game:blocktypes/stone/ore-{graded,ungraded,gem}.json`, then
   rebuilds them via a `texturesByType`.
2. VOM turns those same three blocktypes into 3D veins (`replace /drawtype json` + `shapeByType`)
   whose shapes reference the texture codes `#cube` (surrounding stone) and `#ore1` (the lump), wired
   with `op:add /textures/cube…` and `op:add /textures/ore1ByType`.
3. A JSON-patch `add` resolves the **parent** path (`/textures`) and **no-ops silently if that parent
   is missing** (verified in Tavis.JsonPatch `AddInsertPrepend`). Because Conquest already removed
   `/textures`, VOM's adds silently fail → the veins have no `#cube`/`#ore1` mapping → placeholder.

Conquest ships compat folders for several mods (`terrainslabs`, `juicyores`, …) but **none for VOM**,
so nothing currently repairs this — which is why the fix lives here.

---

## The fix

Each of the three patches is a single op:

- `op: "addmerge"`, `path: "/textures"` — the **parent**, not `/textures/cube`. `addmerge` resolves
  the parent of `/textures` to the block root (which always exists), so it works even when Conquest
  removed `/textures`: absent → it sets `/textures`; present → it deep-merges. Targeting
  `/textures/cube` directly would fail for exactly the same reason VOM's `add` does.
- `file: "game:blocktypes/stone/ore-{…}.json"` — the game domain (block code domain wins even though
  the file sits on disk under `survival/`).
- `dependsOn: [{ "modid": "visibleoresandminerals" }]` — skipped entirely unless VOM is loaded.
- `value`: a `cube` whose base is **Conquest's own rock art** (`block/stone/rock/conquest/{rock}/sides/1`,
  present for all 24 rocks) so the surrounding stone matches the pack, plus the ore overlays; and an
  `ore1ByType` replicating VOM's lump mapping (all refs unqualified = game domain).

Ordering is guaranteed without depending on VOM's load position: our mod hard-depends on `conquest`,
so it loads after Conquest's remove. Whether VOM's patch ran before ours (it failed; we set
`/textures`) or after (we set it; VOM's adds then succeed and coexist), the result has a valid `cube`
→ no placeholder.

---

## Your options

- **Fold in:** if you'd rather VOM shipped a Conquest-aware variant, the cleanest form is a
  `dependsOn: [{ modid: conquest }]` `addmerge` on `/textures` in your own patches, so your veins
  self-heal when Conquest strips the parent. The `value` here is a ready template — swap the cube base
  to vanilla `{rock}1` if you don't want to hard-reference Conquest art.
- **Co-maintain:** leave it here. It's dormant unless VOM is installed, and it only ever *adds* a
  mapping, so it can't harm a VOM-only or Conquest-only setup.

Either way, a heads-up if VOM changes its `#cube`/`#ore1` texture-code names or its `ore1ByType`
mapping — those are what the patch mirrors.

*This fix (the JSON here) is CC0. The Conquest rock textures it references are not ours and resolve
from the player's installed pack.*
