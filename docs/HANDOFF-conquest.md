# Hey CreativeRealms & Arkaik

First: this is a **companion** to Conquest VS Edition, not a fork or a reupload. It copies none of
your art and ships none of your files — it references your textures by path and resolves them from
the player's installed copy of your pack (which it hard-depends on). I wanted to be upfront about that
before anything else, because Conquest has no stated license and I've treated it as all-rights-reserved
throughout.

I'd love a quick sanity-check from you on a couple of things (a VOM compat you're currently missing,
and an optional "vanilla mode" idea), plus two heads-ups about your own compat patches — one on Juicy
Ores, and a small **fixable bug in your Terrain Slabs grass-slab textures**. **Discord
(discord.gg/ZagrZvn) is probably the best place** — happy to talk it through there before anything
gets adopted.

---

## 1. A VOM compat you don't currently ship (you might want this)

Your repo has `patches/compatibility/` folders for `terrainslabs`, `juicyores`, `conquestgeology`,
etc., but **none for Visible Ores & Minerals**. VOM + Conquest currently breaks: your
`op:remove /textures` on the ore blocktypes strips the parent that VOM's `op:add /textures/cube` then
can't target, so VOM's 3D veins render the pink/black placeholder (*"Missing mapping for texture code
#cube"*). Full mechanism in [`HANDOFF-vom.md`](./HANDOFF-vom.md).

We fix it with three JSON patches, and I deliberately laid them out to mirror **your** convention so
they'd drop straight into your tree:

```
src/assets/conquesttweaks/patches/compatibility/visibleoresandminerals/ore-{graded,ungraded,gem}.json
```

Each `addmerge`s onto the parent `/textures` (so it survives your remove), rebuilds `cube` from
**your** rock art (`block/stone/rock/conquest/{rock}/sides/1`, so the surrounding stone matches the
pack), and replicates VOM's lump mapping. If you'd rather own this compat upstream, the folder is
lift-and-drop — or gate your own `remove /textures` with `dependsOn: [{ modid: visibleoresandminerals,
invert: true }]`, exactly the pattern you already use for `juicyores`.

## 2. Heads-up on your Juicy Ores compat (no action needed, just fragile)

Your Juicy Ores compat (added 2026-01-15, in v1.0.7) works — it gates your `/textures` removal on
`juicyores` and meta-patches Juicy Ores' own patch files. One fragility worth knowing: the meta-patch
targets Juicy Ores' patch **array by index** (`/4` for graded, `/3` for ungraded/gem). If Juicy Ores
reorders its patch array in a future release, those indices silently mis-target and the placeholder
break returns. Because you already handle Juicy Ores, our mod does **not** add a Juicy Ores patch —
we didn't want to duplicate or conflict with yours.

## 3. A fixable bug in your Terrain Slabs grass-slab patches (soil + clay)

Grass-covered **soil** and **clay** *slabs* (Terrain Slabs) don't get connected grass textures with
Conquest, while the full grass blocks do. We traced it: it's a one-line omission in your own compat
patches, `patches/compatibility/terrainslabs/soil.json` and `clay.json`.

Your **full** grass soil block (`patches/survival/blocktypes/soil/soil.json`) correctly tiles the grass
overlay:

```json
"specialSecondTexture": {
    "base": "game:block/plant/grasscoverage/{grasscoverage}/1",
    "tiles": [ { "base": "game:block/plant/grasscoverage/{grasscoverage}/*" } ],
    "tilesWidth": 4
}
```

But the **slab** patches set it to a bare wildcard with no `tiles`/`tilesWidth`:

```json
"specialSecondTexture": { "base": "game:block/plant/grasscoverage/{grasscoverage}/*" }
```

Without a `tiles` block, that `/*` resolves as **random alternates** (a single tile picked at random)
rather than **connected tiles**, so the slab's grass top never joins the pattern. The engine's
`TopSoil` path connects the overlay fine when it's actually tiled — your `peat.json` slab patch already
includes the `tiles` block and works correctly, which is what pinned the diagnosis.

The fix is to give the soil slab `specialSecondTexture` the same tiled form your full block and your
peat slab already use (`tilesWidth: 4`, 16 tiles for `verysparse`). We ship exactly that as a stopgap
([`patches/compatibility/terrainslabs/soil.json`](../src/assets/conquesttweaks/patches/compatibility/terrainslabs/soil.json)
+ [`clay.json`](../src/assets/conquesttweaks/patches/compatibility/terrainslabs/clay.json)), but it
really belongs in your pack — mostly a two-key edit to files you already ship, and then no companion
mod is needed for it at all.

⚠️ **Clay has a sharp edge — don't just copy the soil form onto it.** Terrain Slabs' `clay.json`
blocktype uses a blanket `drawtype: TopSoil` (their `soil.json` uses `drawtypeByType`, so its `-none`
variant is Json/Opaque and never draws the overlay). Because the base-game `grasscoverage/none/` folder
has only **one** tile while `verysparse/` has 16, a blanket `tilesWidth: 4` on clay puts a 16-tile grid
on the 1-tile `none` overlay — and the TopSoil render path doesn't clamp the tile index, so no-grass
clay slabs (and their neighbours) render as **black voids**. Our clay patch sidesteps this with
`tilesWidthByType { "*-none": 1, "*": 4 }` — the same idiom your peat slab patch and your full clay
block already use. (Cleanest root fix: give your `clay.json` blocktype the same
`drawtypeByType`/`renderpassByType` split your `soil.json` has, so `-none` renders Json/Opaque.)

There's also a second, quieter layer on **both** soil and clay: your **slab** patches degrade the base
`all` to a `/*` wildcard (random alternates), whereas your full blocks connect it with `tiles` +
`tilesWidth`. So the base top doesn't connect on slabs the way it does on full blocks. We restore the
connected base in both our patches. For **clay** it's a flat `tilesWidth: 3` (the full clay block
connects every type uniformly, 9 tiles). For **soil** it's *per fertility*, matching your full soil
block exactly: `compost`/`medium` stay alternates (not a connectable grid — 10 and 9 tiles),
`low`/`verylow` connect at `tilesWidth: 3`, `high` at `tilesWidth: 4`. Because your slab compat
collapses all fertilities into one broad `texturesByType` key and the byType resolver is
first-match-wins (so we can't append more-specific keys), we express the soil split with an
`allByType` nested inside that broad key — the same object-valued nested-byType shape your
`forestfloor` compat already uses for `specialSecondTextureByType`. Cleanest root fix, as with the
overlay, is to give your slab patches the full-block base form directly.

**The one thing to change if you adopt this (single source of truth).** Our soil/clay slab patches
are, in effect, *transcribing your full-block `texturesByType` onto the slab* — the same per-fertility
widths (soil: compost/medium alternate, low/verylow width 3, high width 4; clay: width 3 for every
type) and the same tiled overlay. That transcription is exactly what's fragile: two copies of the
same authoring decision that can drift apart. The robust version on your side is to have your slab
compat **apply the same `texturesByType` block you already apply to the full block**, rather than the
degraded `/*` form — then there's one definition, slabs inherit it, and no companion patch (ours or a
future one) is needed at all. Everything else we ship around this (a render-path selector clamp that
makes an over-declared `tilesWidth` fail safe instead of rendering black, and a `.ctc drawtype`
diagnostic) is engine-level robustness and tooling, not pack content — useful to know about, but not
something you need to carry.

## 4. A positioning bug in your large wall-lantern shape

Your **large** lantern renders off its bracket when wall-mounted — the glass body doesn't seat on the
arm the way it should (your **small** wall lantern is fine). We traced it to
`shapes/block/metal/lantern/large/wall.json`: it has the glass **body** (`lantern` group) and the
**wallmount** bracket as two *independent top-level groups*, so the body doesn't share the bracket's
coordinate frame and ends up mispositioned relative to it.

The fix is structural, and it matches how your **small** wall lantern is already authored: wrap both
groups in a single parent group. Your `small/wall.json` nests everything under one `origin` group; the
large one skips that and keeps the body and bracket as siblings. Putting the large body and bracket in a
shared parent group seats the body correctly — no coordinate dialing-in needed; it was right the moment
the two groups were combined.

We ship exactly that as a stopgap
([`patches/compatibility/conquest/lantern-large-wall.json`](../src/assets/conquesttweaks/patches/compatibility/conquest/lantern-large-wall.json)):
it `add`s an `overall` wrapper group and `move`s your existing `lantern` and `wallmount` nodes into it
(relocating your nodes, not restating their geometry), plus the handful of transform scalars that come
with the wrapping. It references only your shape by path and ships none of your art, but it's
index-based against your 1.0.7 element tree, so it really belongs upstream — the clean root fix is to
author `large/wall.json` with the shared parent group the way `small/wall.json` already has one.


---

## On the bundled art (the licensing bit)

The **public** build (the one on the mod portal) bundles **no textures at all** — not yours, not the
base game's — so it redistributes nothing; my per-family "vanilla revert" feature is simply inert
there and re-enables itself only if a player generates their own base-game payload locally. A separate
**private** build (personal use only, never published or shared) bundles base-game Vintage Story
textures to restore the game's *own* original look over the pack; that art is owned by Anego Studios
(not you, and not relicensed by us — see [`CREDITS.md`](../CREDITS.md)). Either way it bundles **zero**
Conquest textures. If anything in here looks like it's leaning on your art beyond referencing it by
path, tell me and I'll fix it.

---

*Our original work (the C# and JSON patches) is CC0. Your pack, and the base-game art we reference,
are not ours to relicense.*
