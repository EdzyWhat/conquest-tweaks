# Hey BeloMaximka

This is a drop-in fix for the "connected textures don't work on slabs with Conquest VS Edition"
issue you have listed as a Known Issue on Terrain Slabs. It's one Harmony transpiler — no asset
changes, no new blocks. I'd love to see it live in Terrain Slabs so it works for everyone without a
third mod, but co-maintaining it here is fine too. This doc covers both.

The whole fix is a single file: [`src/Compat/TerrainSlabs/SlabConnectedTexturesPatch.cs`](../src/Compat/TerrainSlabs/SlabConnectedTexturesPatch.cs).

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

The per-tile alternate **meshes already exist** for JSON blocks —
`ShapeTesselatorManager.Tesselate` builds `altblockModelDatas[blockId]`, one mesh per tile, for any
`HasTiles` block regardless of draw type. So the fix doesn't build anything; it just **redirects the
selection**: a transpiler pipes the first `MurmurHash3Mod` result through `CorrectTileIndex(...)`,
which returns `GameMath.Mod(GetTiledTexturesSelector(bakedTiles, UP, x, y, z), array.Length)` for
tiled blocks and the original hash otherwise.

Only the **first** `MurmurHash3Mod` in `doMesh` is redirected — there's a second one for random
rotations that must stay random.

Worth flagging: transpiler, not postfix. It matches an instruction sequence in your dependency's
compiled code, so it's version-sensitive by nature — see the fail-safe note below.

---

## Coverage (please read — it's not all slabs)

Tested against Conquest VS Edition v1.0.7 on 1.22.x:

| Slab family | Draw path | Fixed? |
|---|---|---|
| rock / gravel / sand (and grassless `*-none` soil variants) | `EnumDrawType.JSON` → `doMesh` | ✅ **yes** — confirmed on Limestone Sand Slab (`tilesWidth: 8`) |
| grass-covered soil / peat / clay slabs (the grassy top) | `TopSoil` renderpass, **not** `doMesh` | ❌ **no** — the grass top is a separate render path this patch never touches; those slab tops all draw the same tile |

And one geometry caveat even where it works: one tile index is chosen for the **whole slab mesh**, so
the join is correct on the dominant **top (UP) face** and may be imperfect on the thin vertical edge
faces. That was an acceptable trade for us (top face is where connected textures read); you may have a
better idea for the edges from inside Terrain Slabs.

If you want full coverage, the grass-top `TopSoil` path is the missing piece — I haven't traced how
`TopSoil` selects its texture, so I can't say yet whether the same positional-selector redirect
applies there. Happy to dig in with you.

---

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
- [ ] Confirm rock/gravel/sand slabs connect; note grass-top slabs still don't (TopSoil path)

---

*This fix (the C# here) is CC0 — no strings. Take it, rename it, relicense your copy, whatever helps.*

*I'll find you on the Terrain Slabs GitHub / Discord.*
