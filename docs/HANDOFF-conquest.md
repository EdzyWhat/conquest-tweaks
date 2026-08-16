# Hey CreativeRealms & Arkaik

First: this is a **companion** to Conquest VS Edition, not a fork or a reupload. It copies none of
your art and ships none of your files — it references your textures by path and resolves them from
the player's installed copy of your pack (which it hard-depends on). I wanted to be upfront about that
before anything else, because Conquest has no stated license and I've treated it as all-rights-reserved
throughout.

I'd love a quick sanity-check from you on two things (a VOM compat you're currently missing, and an
optional "vanilla mode" idea), plus one heads-up about your Juicy Ores compat. **Discord
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

## 3. Optional: an upstream "vanilla per family" toggle

Our mod also does something you might find interesting for the pack itself: a per-family **revert to
vanilla** (soil, grass cover, forest floor, clay, farmland, stone path, …) plus a green-selective
grass-tint **vibrancy** dial. It's how players who love most of Conquest but want, say, vanilla soil
get there without leaving the pack. It works by overwriting texture bytes in-memory at load
(`AssetsLoaded`, before the atlas composes) — it edits no blocktype JSON.

This is **not** something to fold in as-is (it ships base-game art — see below — and it's a client
preference layer, not pack content). But if you ever wanted an official "toned-down" pack variant, the
approach and the family groupings might be a useful reference. Purely an offer; ignore freely.

---

## On the bundled art (the licensing bit)

The one thing this mod bundles is **base-game Vintage Story textures** — used only to restore the
game's *own* original look over the pack, redistributed byte-for-byte, and owned by Anego Studios (not
you, and not relicensed by us — see [`CREDITS.md`](../CREDITS.md)). It bundles **zero** Conquest
textures. If anything in here looks like it's leaning on your art beyond referencing it by path, tell
me and I'll fix it.

---

*Our original work (the C# and JSON patches) is CC0. Your pack, and the base-game art we reference,
are not ours to relicense.*
