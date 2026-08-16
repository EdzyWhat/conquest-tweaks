# Hey BeloMaximka

This is a drop-in fix for the "connected textures don't work on slabs with Conquest VS Edition"
issue you have listed as a Known Issue on Terrain Slabs. It's one Harmony transpiler — no asset
changes, no new blocks. I'd love to see it live in Terrain Slabs so it works for everyone without a
third mod, but co-maintaining it here is fine too. This doc covers both.

The whole fix is a single file: [`src/Compat/TerrainSlabs/SlabConnectedTexturesPatch.cs`](../src/Compat/TerrainSlabs/SlabConnectedTexturesPatch.cs).

> **Status (Conquest v1.0.7, VS 1.22.6 — please read before adopting).** On the *current* Conquest
> pack this transpiler is **dormant**: toggling it makes **no visible difference** to rock / gravel /
> sand slabs. Conquest authors those slabs (and the grassless `*-none` soil variants) as `/*` wildcard
> **alternates**, so `block.HasTiles == false`, and `CorrectTileIndex` returns the original index early
> for any non-`HasTiles` block — it never fires on them. They already render varied-but-random, and
> that variation is **Conquest's own alternate selection, not this patch**. An on/off/original A-B
> screenshot test (2026-08-16, andesite full block + slab) came back pixel-identical across all three.
> The transpiler is **kept** because it is the *correct* redirection the moment a slab is authored as a
> **tiled JSON** block (`HasTiles == true`, i.e. real `tiles`/`tilesWidth` art) — none of which ship on
> slabs today. One honest open question remains even for that case (see *the tiled-JSON caveat* below):
> we have not verified that the pre-baked alt-meshes actually differ per tile on a `HasTiles` JSON
> block, and `TextureSource.UpdateVariant` is a no-op for tiles — so redirecting the index may still
> resolve to identical meshes unless the block *also* carries texture alternates. Treat the transpiler
> as a staged, mechanism-correct patch awaiting a block that exercises it, not a shipping fix.

---

## What it does

Connected/tiled textures (Conquest's `tiles`/`tilesWidth` art) only line up on **cube**-drawtype
blocks, because `CubeTesselator.Tesselate` picks each face's tile with
`BakedCompositeTexture.GetTiledTexturesSelector(tiles, side, x, y, z)` — a neighbour-aware,
position-derived index. Slabs draw as `EnumDrawType.JSON` and render through `JsonTesselator.doMesh`,
which selects among the per-tile alternate meshes with

```csharp
int num = GameMath.MurmurHash3Mod(vars.posX, .., vars.posZ, array.Length);  // RANDOM, not positional
```

so each slab picks a *random* tile and the surfaces never join the pattern.

An array of alternate **meshes already exists** for `HasTiles` JSON blocks —
`ShapeTesselatorManager.Tesselate` builds `altblockModelDatas[blockId]`, one entry per tile, regardless
of draw type. So the fix doesn't build anything; it just **redirects the selection**: a transpiler
pipes the first `MurmurHash3Mod` result through `CorrectTileIndex(...)`,
which returns `GameMath.Mod(GetTiledTexturesSelector(bakedTiles, UP, x, y, z), array.Length)` for
tiled blocks and the original hash otherwise.

Only the **first** `MurmurHash3Mod` in `doMesh` is redirected — there's a second one for random
rotations that must stay random.

Worth flagging: transpiler, not postfix. It matches an instruction sequence in your dependency's
compiled code, so it's version-sensitive by nature — see the fail-safe note below.

---

## Coverage (please read — two different fixes)

Tested against Conquest VS Edition v1.0.7 on 1.22.6:

| Slab family | Draw path | Fixed? | By what |
|---|---|---|---|
| rock / gravel / sand (and grassless `*-none` soil variants) | `EnumDrawType.JSON` → `doMesh` | ⚠️ **no-op today** — render fine anyway | These are `/*` alternates (`HasTiles=false`); the transpiler's `HasTiles` gate skips them, so it contributes nothing. They render varied via **Conquest's own random alternates**. The transpiler *would* fire on a tiled-JSON (`HasTiles=true`) slab — none ship today. (Retracts an earlier "confirmed on Limestone Sand Slab" note that predated the A-B test.) |
| grass-covered **soil** & **clay** slabs (the grassy top) | `TopSoil` renderpass, **not** `doMesh` | ✅ **yes** | a **JSON patch**, not Harmony — see below |
| grass-covered **peat** slabs | `TopSoil` renderpass | ✅ already correct | Conquest's own peat patch got it right |
| forest-floor slabs | `TopSoil` renderpass | ✅ n/a — **not a bug** | uses `forestoverlay{grass}` (only `forestoverlay1..7`, one per grass *stage* — no tiled set exists to connect); Conquest's own **full** forest-floor block is un-tiled the same way, so there's no full-vs-slab discrepancy to fix. Verified against v1.0.7. |

So on the current pack, the **only slab fix that changes what you see is the grass-top JSON patch**
(row 2). The transpiler (row 1) is the staged, dormant piece described in the Status note above.

### The tiled-JSON caveat (why "dormant" isn't quite "works, just unused")

Even on a `HasTiles=true` JSON slab, redirecting the tile index only helps if the pre-baked alt-meshes
actually differ per tile. `ShapeTesselatorManager.Tesselate` builds each alt-mesh via
`TextureSource.UpdateVariant`, which **only swaps textures that carry `BakedVariants`** (alternates) —
it is a **no-op for `BakedTiles`**. So a *tiles-only* JSON block bakes N **identical** meshes, and
selecting a different index resolves to the same geometry → still no connection. The transpiler would
visibly connect a slab only if that slab were authored as tiled JSON **and** the engine baked
per-tile-distinct meshes for it (e.g. because it also carries alternates). We haven't found a live
block that exercises this, so it's an untested path — flagged honestly rather than claimed. If Terrain
Slabs wants true connected textures on grassless slabs, the robust route is engine-side (bake
per-tile-distinct meshes for tiled JSON blocks, or route slabs through a tiles-aware tesselator like
`TopSoil`/`Cube`), not this index redirect.

And one geometry caveat that applies *if* the transpiler ever does fire: one tile index is chosen for
the **whole slab mesh**, so a join would be correct on the dominant **top (UP) face** and imperfect on
the thin vertical edge faces.

### The grass-top fix turned out to be a data bug, not a tesselator gap

Once we instrumented it in-game (drawtype + baked-tile count per block), the grass-top case was **not**
a missing engine feature: the vanilla `TopSoil` path connects the grass overlay fine *when the overlay
texture is actually tiled*. The full grass soil block connects because Conquest gives its
`specialSecondTexture` a `tiles`/`tilesWidth` block; grass **slabs** didn't connect because Conquest's
own Terrain Slabs compat patches (`conquest:patches/compatibility/terrainslabs/soil.json` and
`clay.json`) set the slab's `specialSecondTexture` to a bare `grasscoverage/{grasscoverage}/*` wildcard
**without** `tiles`/`tilesWidth` — so it baked as random alternates (one tile) instead of connected
tiles. (Conquest's `peat.json` includes the `tiles` block and is already correct — which is what
confirmed the diagnosis.)

Our fix is therefore a tiny JSON patch that re-adds the tiled definition onto Conquest's own slab
entries: [`src/assets/conquesttweaks/patches/compatibility/terrainslabs/soil.json`](../src/assets/conquesttweaks/patches/compatibility/terrainslabs/soil.json)
and [`clay.json`](../src/assets/conquesttweaks/patches/compatibility/terrainslabs/clay.json). It
references only base-game texture paths (Conquest overrides them in place), so nothing is
redistributed. **This one really belongs upstream in Conquest, not Terrain Slabs** — it's correcting
Conquest's compat file — so I've also flagged it in the Conquest handoff. Noted here only so the
coverage table is complete.

#### Clay needs a different patch shape than soil — because of a drawtype difference in *your* blocktypes

This one's a genuine heads-up for Terrain Slabs, not just Conquest. Your `blocktypes/soil.json` sets
`drawtypeByType {"*-none": "Json", "*": "TopSoil"}` and `renderpassByType {"*-none": "Opaque", "*":
"TopSoil"}` — so a no-grass (`-none`) soil slab draws as plain JSON/Opaque and never renders the grass
overlay at all. But `blocktypes/clay.json` uses a **blanket** `drawtype: "TopSoil"` /
`renderpass: "TopSoil"` for *every* variant, including `-none`.

That difference bit us. Conquest ships `grasscoverage/none/` with a **single** tile but
`grasscoverage/verysparse/` with **16**. The naive fix (merge one tiled `specialSecondTexture` with
`tilesWidth: 4` onto every matched clay variant, exactly what worked for soil) applies a 16-tile grid
to the 1-tile `none` overlay. On soil that's harmless — `none` is on the JSON `doMesh` path, whose
tile index is clamped by the baked-tile count (and it isn't even drawn, being Opaque). On clay,
`none` renders through **TopSoil**, which does *not* clamp: the connected-texture selector computes an
index up to 15 into a 1-element array → out-of-range → **black/void slabs**, and neighbours flip to
black on the next re-tesselation. (Found it the hard way in a playtest.)

So the clay overlay uses **`tilesWidthByType`** to tile only the grass-bearing coverage and give the
`-none` variant a width of 1 (matching its single tile) — the exact idiom Conquest already ships for
its peat slab and its full clay block:

```json
"specialSecondTexture": {
    "base": "game:block/plant/grasscoverage/{grasscoverage}/1",
    "tiles": [ { "base": "game:block/plant/grasscoverage/{grasscoverage}/*" } ],
    "tilesWidthByType": { "*-none": 1, "*": 4 }
}
```

If you'd rather fix it at the root inside Terrain Slabs, giving `clay.json` the same
`drawtypeByType`/`renderpassByType` split soil already has (`-none` → Json/Opaque) would make clay
behave like soil and sidestep the whole fragility — then a plain tiled `specialSecondTexture` would be
safe on clay too.

#### The same omission also degraded the *base* clay texture on slabs

Once the overlay was connecting, a playtest turned up a second layer of the same bug: the base clay
top on grass slabs showed no connected pattern. Cause is identical — Conquest's slab patch sets the
base `all` to a bare `game:block/soil/clay/{type}/*` (random alternates), whereas its **full** clay
block gives `all` the connected `tiles` + `tilesWidth: 3` (9 tiles). On the `TopSoil` path a `tiles`
base routes through the connected selection (`fastBlockTextureSubidsByFace`), while a `/*` base only
ever gets `HasAlternates` random picks — so the slab base can't connect until it's given the tiled
form. Our clay patch now also restores `all` to the full-block form (`tilesWidth: 3`), which is safe
because the base is coverage-independent (9 tiles for every type — no `none` mismatch).

**The soil slab base needed the same fix — but per-fertility, not flat.** Soil's slab patch has the
same base omission (`all: soil/fertility/{fertility}/*` vs. the full block's connected
`tiles`/`tilesWidth`), so soil grass-slab bases didn't connect either. But unlike clay (uniform
`tilesWidth: 3` for every type), your **full** soil block treats the base *per fertility*:
`compost`/`medium` stay **alternates** (they aren't authored as a connectable grid — compost is 10
tiles, medium 9), `low`/`verylow` connect at `tilesWidth: 3` (9 tiles = 3×3), and `high` connects at
`tilesWidth: 4` (16 tiles = 4×4). We couldn't express that split by merging a flat base onto
Conquest's single broad slab key (a plain `tiles` base would force-connect compost/medium too), and we
couldn't add more-specific sibling keys because the byType resolver is **first-match-wins by insertion
order** and `addmerge` *appends* keys after Conquest's broad key (which would always match first). So
the soil base uses an **`allByType`** nested inside Conquest's broad key — structurally identical to
Conquest's own `specialSecondTextureByType` (object-valued nested byType); `all` isn't special to the
byType resolver, only to the tesselator that runs later, so `allByType`→`all` is the same transform:

```json
"allByType": {
    "*-compost-*": { "base": "game:block/soil/fertility/{fertility}/*" },
    "*-medium-*":  { "base": "game:block/soil/fertility/{fertility}/*" },
    "*-high-*":    { "base": "game:block/soil/fertility/{fertility}/1",
                     "tiles": [ { "base": "game:block/soil/fertility/{fertility}/*" } ], "tilesWidth": 4 },
    "*":           { "base": "game:block/soil/fertility/{fertility}/1",
                     "tiles": [ { "base": "game:block/soil/fertility/{fertility}/*" } ], "tilesWidth": 3 }
}
```

Specific fertilities are listed first (`compost`/`medium` → alternates, `high` → width 4) with `*`
last catching `low`/`verylow` at width 3. `tilesWidth` matches each fertility's tile count exactly
(9 = 3×3, 16 = 4×4), and the base is coverage-independent (no `-none` mismatch), so there's no
overshoot and no black-void risk. If `allByType` ever failed to resolve, Conquest's existing `all`
alternates simply remain — no regression. As with clay, the cleanest root fix lives in Conquest, not
Terrain Slabs; this is only a stopgap.

---

## Adoption map — what's a lift, what you'd author fresh, what's really the engine's job

We built this as a *pathway to upstreaming*, not a permanent third mod. Here's the honest layering so
you can take exactly the right slice and leave the rest:

**Easy to pick up from us (near-verbatim):**
- **The `doMesh` transpiler** (`SlabConnectedTexturesPatch.cs`) — ~130 self-contained lines, no
  dependency on the rest of our mod. Gate on `conquest` and apply via `PatchCategory`. It's
  mechanism-correct and genuinely engine-shaped, so it belongs in Terrain Slabs — **but note it's
  dormant on the current pack** (see the Status note and the tiled-JSON caveat): it fires only on
  `HasTiles` JSON slabs, of which Conquest ships none today. Take it as the staged hook for when you
  *do* author tiled slabs, not as a fix that changes today's rock/gravel/sand rendering.
- **The `.ctc drawtype <pattern>` diagnostic** — groups blocks by render signature (drawtype /
  renderpass / shape / `baseTiles` / overlay `2ndTiles`). It's how we tell whether a slab actually
  connects vs. only looks like it. Reusable to validate any connected-texture change.

**What we'd change if we owned it (better than our stopgap):**
- **Give `clay.json` the same `drawtypeByType`/`renderpassByType` split `soil.json` has**, so `-none`
  renders Json/Opaque. That single change makes clay behave like soil, removes the black-void
  fragility at the root, and lets a plain tiled overlay be safe on clay — no `tilesWidthByType`
  gymnastics needed. This is the highest-leverage fix on your side.

**Better authored net-new (not something a patch/transpiler can reach well):**
- If the *base dirt body* under grass on a slab shows a single tile while the grass top varies (an
  open question in our soil testing — the base tiles *are* baked, so it's not a data gap), that's a
  slab base-composition detail inside Terrain Slabs, not something Conquest's JSON or our transpiler
  can drive. Worth a look from inside the slab tesselation.

**Engine-level (Anego, not you and not Conquest):**
- We also ship a **connected-texture selector clamp** (`TiledSelectorClampPatch.cs`): a one-line
  postfix wrapping `BakedCompositeTexture.GetTiledTexturesSelector`'s return into range. The selector
  can return an out-of-range index whenever `tilesWidth > tileCount` (rows = `count / width` = 0), and
  the TopSoil path reads `tiles[index]` unclamped → black voids. This is a *render-robustness* net,
  not a slab fix — you don't need it to make connected textures correct — but it's why we no longer
  have to perfectly re-verify every declared width against tile counts. The proper home for it is the
  game engine.

## Your options

### Option A: fold it in

You already keep per-mod compat under `assets/terrainslabs/compatibility/<modid>/patches/` (JSON).
This one is C# because it has to patch a compiled tesselator method, but the spirit's the same — a
Conquest-gated compat that's dormant otherwise. To bring it over:

1. Copy `CorrectTileIndex` / `SelectAltArray` / `FindBakedTiles` + the transpiler into Terrain Slabs
   (it's ~130 lines, all in the one file, no dependencies on the rest of this mod).
2. Gate activation on `api.ModLoader.IsModEnabled("conquest")` so it's inert without the pack.
3. It's already tagged `[HarmonyPatchCategory("terrainslabs-connected-textures")]` and applied via
   `harmony.PatchCategory(...)`, so it won't get swept into a blanket `PatchAll`. Rename the category
   to your own convention if you prefer.
4. `0Harmony` is game-bundled — reference it `Private=false`, never ship a copy.

That's the whole move. Nothing else here needs to come with it.

### Option B: co-maintain the standalone

Also fine — it ships in our umbrella mod today, gated so it only activates when both `terrainslabs`
and `conquest` are present. Main ask: a heads-up if Terrain Slabs ever changes how slabs draw (e.g.
away from `EnumDrawType.JSON`), since that's the assumption the patch rests on.

---

## Fail-safe (how it behaves when the game moves under it)

If the IL pattern isn't found (a game update reshapes `JsonTesselator.doMesh`), the transpiler throws
and the caller catches it, logs a warning, and **deactivates just this fix** — the rest of the mod
keeps working, the client never crashes. If you fold it in, keep that try/catch around
`PatchCategory` so a version-drift never takes Terrain Slabs down with it.

---

## Checklist (fold-in)

- [ ] Copy `SlabConnectedTexturesPatch.cs` into Terrain Slabs
- [ ] Gate on `api.ModLoader.IsModEnabled("conquest")`
- [ ] Apply via `harmony.PatchCategory(...)` inside a try/catch fail-safe
- [ ] Reference `0Harmony` `Private=false` (game-bundled, don't ship it)
- [ ] Re-verify the transpiler still matches `doMesh` on your target game version (see CONTRIBUTING.md)
- [ ] Understand it's **dormant on Conquest v1.0.7** — it fires only on `HasTiles` JSON slabs (none
      ship today); rock/gravel/sand slabs already look right via Conquest's own alternates
- [ ] If you author a **tiled-JSON** slab, first confirm the engine bakes per-tile-distinct meshes for
      it (see the tiled-JSON caveat) before relying on the transpiler to connect it
- [ ] Grass-top soil/clay slabs are fixed separately by a JSON patch that corrects Conquest's own
      compat file (see the coverage note) — that one is better folded into Conquest, not here

---

*This fix (the C# here) is CC0 — no strings. Take it, rename it, relicense your copy, whatever helps.*

*I'll find you on the Terrain Slabs GitHub / Discord.*
