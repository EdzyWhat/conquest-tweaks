# Hey Roteroktober / 12Port / Craluminum2413

This is a fix for the "archways look vanilla next to Conquest full blocks" mismatch — Medieval
Architecture's rock/brick/cobblestone/plank textures resolve to plain vanilla art even when Conquest
VS Edition is installed, so an archway built next to a Conquest-textured wall visibly clashes. It's a
JSON-patch-only fix, no C#. You're welcome to fold it into MA; it can also just live in our umbrella
mod as-is — this doc explains it either way.

The patch is one generated file:
[`src/assets/conquesttweaks/patches/compatibility/medievalarchitecture/texture-remap.json`](../src/assets/conquesttweaks/patches/compatibility/medievalarchitecture/texture-remap.json)
(37 ops across 32 block files), produced by
[`build/generate-ma-compat-patches.py`](../build/generate-ma-compat-patches.py) from a local extraction
of the mod — re-run it against a fresh extraction if you restructure texture paths or the
`behaviors[]` array in any block file (see **Fragility** below).

---

## Why it happens (the mechanism)

Not a bug in either mod on its own — Conquest simply never reskins the paths MA hardcodes:

1. Your archway/window/gate/roof/trapdoor blocks resolve `material`/`rim`/`plaster`/`sill`/
   `materialplanks`/`trussmaterial` textures via
   `AttributeRenderingLibrary.BlockShapeTexturesFromAttributes`, templated on the player-chosen
   `{rock}`/`{wood}` attribute, e.g. `"game:block/stone/rock/{rock}1"`.
2. Conquest never overrides that *exact* vanilla path. It reskins rock/brick(stonebrick)/cobblestone/
   planks by shipping its own art under a **different** folder convention instead (new blocks/tiled
   variant sets), and leaves the plain vanilla `{rock}1`-style file it inherited untouched — because
   vanilla itself still actively uses that file for loose rocks, ore host rock, cobblestone paths,
   plank floors, etc. (Conquest reskinning it in place would reskin all of *that* too, not just your
   archways.)
3. So MA's texture ref resolves fine (no placeholder) — it just resolves to genuine, un-reskinned
   vanilla art, which reads as a clash next to anything Conquest-textured.

Four families are affected; three others MA references are **already fine** and untouched by this fix:
`stone/agedbrick`, `wood/debarked`, and `clay/daub/*` are families Conquest *does* override in place,
so MA already shows Conquest's look for those. `wood/planks/generic` is likewise already
Conquest-overridden. `overlay/damagedstone`/`overlay/crackedcobblestone` have no Conquest equivalent
at all, so those stay vanilla by necessity (nothing to swap in).

---

## The fix

One `addmerge` op per affected block file, each self-gated `dependsOn: [{ "modid":
"medievalarchitecture" }]`, targeting whichever container the hit lives in:

- Nested under an ARL behavior: `path: "/behaviors/<N>/properties/textures"` (or `texturesByType` for
  `roof.json`'s truss material) — `addmerge` deep-merges, so only the specific style-key/texture-key
  we touch changes; every sibling key (glass, wood trim, unrelated originblock styles) is untouched.
- Top-level `textures` dict (the four non-ARL files — `gate.json`, `portcullis.json`,
  `drawbridge.json`, `trapdoor.json`, `trapdoor_small*.json`): `path: "/textures"`.
- `file: "confession:blocktypes/<relpath>"` — **your mod's own domain**, `confession` (your
  `modinfo.json` has no `domain` override, so the on-disk `assets/confession/` folder registers as
  the `confession:` asset domain at runtime — not `game:`, which is only where the *textures* Live,
  not your blocktypes).

Mapping (verified against Conquest VS Edition 1.0.7's actual shipped texture folders, all 14 vanilla
rock types and all 12 real wood types — `wood` and `generic` are non-species fallbacks, left alone):

| MA references (vanilla) | Redirected to (Conquest) |
|---|---|
| `game:block/stone/rock/{rock}<N>` | `game:block/stone/rock/conquest/{rock}/sides/<N>` |
| `game:block/stone/rock/granite1` (your hardcoded rim/plaster granite) | `game:block/stone/rock/conquest/granite/sides/1` |
| `game:block/stone/cobblestone/{rock}1` | `game:block/stone/cobblestone/{rock}/sides/1` |
| `game:block/stone/brick/{rock}1` | `game:block/stone/brick/{rock}/1` |
| `game:block/wood/planks/{wood}1` (incl. `{trussmaterial}`) | `game:block/wood/planks/{wood}/top/1` |

The wood mapping uses `top/1`, not `sides/1`: only Conquest's `acacia` planks ship a `sides/` tile —
every other wood type ships only `top/`+`bottom/`, so `top/1` is the one convention with full
coverage across all 12 species.

Deliberately a **single fixed tile per rock/wood**, not randomized — same reasoning as our VOM ore-vein
fix (see `HANDOFF-vom.md`): these are static per-file texture refs, not `texturesByType` wildcards, so
there's nothing for the tesselator to vary across.

---

## Fragility (please read before you change block file structure)

Unlike our VOM/terrainslabs patches, which target `/textures` (an object key, always resolvable), most
of these ops target a numeric `behaviors[N]` **array index** — the position of your
`BlockShapeTexturesFromAttributes` behavior within each file's `behaviors` list, which we had to
resolve concretely per file (it's `2` or `3` in most archway files, `1` in `windowsill_a.json` and
`roof.json`). If you reorder, add, or remove a behavior entry in one of these files in a future
release, the index will drift and that file's op will either silently miss (fails safe — no crash, no
corruption, just reverts to showing vanilla art again) or, if the index happens to still be in range
but now points at a different behavior, merge an inert unused `textures` key into that behavior's
properties (harmless, but pointless). Either way: re-run `build/generate-ma-compat-patches.py` against
a fresh extraction whenever you bump MA and we'll regenerate.

---

## Your options

- **Fold in:** if you'd rather MA shipped Conquest-aware texture refs directly, the mapping table above
  is a ready reference — you could either hardcode Conquest's paths behind a `dependsOn: conquest`
  patch of your own, or (cleaner long-term) add a small indirection layer so reskin packs can register
  their own family mapping instead of us patching your literals from outside.
- **Co-maintain:** leave it here. It's dormant unless MA is installed, and it only ever changes texture
  refs Conquest doesn't already control, so it can't harm an MA-only or Conquest-only setup.

*This fix (the JSON here, and the generator script) is CC0. The Conquest textures it references are not
ours and resolve from the player's installed pack; nothing from either mod is redistributed.*
