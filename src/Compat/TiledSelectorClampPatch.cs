using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace ConquestTweaks;

// ---------------------------------------------------------------------------------------------
// Connected-texture selector clamp (black-void guard).  Category: "tiled-selector-clamp".
//
// WHY (verified against the decompiled client):
//   BakedCompositeTexture.GetTiledTexturesSelector(tiles, side, x, y, z) picks WHICH tile of a
//   connected/tiled texture to draw for a given world position. Its arithmetic is:
//
//       tilesWidth = tiles[0].TilesWidth;          // declared grid COLUMN count
//       n          = tiles.Length / tilesWidth;    // grid ROW count = tileCount / width
//       index      = Mod(pos + rot, tilesWidth) + tilesWidth * Mod(pos', n);
//
//   The index is in range [0, tileCount) ONLY while tilesWidth <= tiles.Length. If a block declares
//   a tilesWidth GREATER than the number of tiles it actually ships, n = tileCount / tilesWidth
//   truncates to 0, the row term vanishes, and the column term (0..tilesWidth-1) indexes PAST the
//   too-short array. The cube path masks this (its callers clamp downstream), but the TopSoil path
//   (grass-covered soil/clay slabs and full blocks) reads tiles[index] directly with NO clamp - so
//   an over-declared width renders BLACK/VOID blocks, and neighbours flip to black on re-tesselation.
//   We hit exactly this when a naive grass-slab clay overlay put tilesWidth 4 on the single-tile
//   `grasscoverage/none/` texture (see terrainslabs/clay.json's tilesWidthByType note).
//
// FIX: a one-line postfix that wraps the returned index into range:
//       __result = GameMath.Mod(__result, tiles.Length);
//   For any correctly-authored block (tilesWidth <= tileCount) the index is ALREADY in range, so the
//   Mod is a no-op and behaviour is identical to vanilla. The clamp only ever engages on a
//   mis-authored width, turning a would-be void into a valid (wrapped) tile. It therefore eliminates
//   the whole black-void bug CLASS regardless of any future width/tile-count drift in Conquest's (or
//   anyone's) data - so our tilesWidth values (and Conquest's) no longer have to be perfectly
//   re-verified against every art change to stay void-safe.
//
// LAYERING (for adoption - see docs/HANDOFF-*.md): this is not a Conquest pack fix, it is an
//   ENGINE-level render hardening. The right long-term home is the game itself (Anego): the selector
//   should not be able to return an out-of-range index for the TopSoil path any more than it can for
//   the cube path. We ship it here as a safety net for the Conquest stack; a source team folding in
//   our slab work does NOT need this clamp to make connected textures correct - it only makes the
//   render path robust against mis-declared widths.
//
// FAIL-SAFE: applied via harmony.PatchCategory in a try/catch (ConquestTweaksModSystem); if the
//   target method can't be resolved on some game version, this fix deactivates alone with a warning
//   and the rest of the mod keeps working.
// ---------------------------------------------------------------------------------------------
[HarmonyPatchCategory("tiled-selector-clamp")]
internal static class TiledSelectorClampPatch
{
    [HarmonyPatch(typeof(BakedCompositeTexture), nameof(BakedCompositeTexture.GetTiledTexturesSelector))]
    [HarmonyPostfix]
    private static void ClampToRange(ref int __result, BakedCompositeTexture[] tiles)
    {
        // No-op whenever the index is already valid (every correctly-authored block); only wraps an
        // out-of-range index produced by a declared tilesWidth > tiles.Length.
        if (tiles != null && tiles.Length > 0)
            __result = GameMath.Mod(__result, tiles.Length);
    }
}
