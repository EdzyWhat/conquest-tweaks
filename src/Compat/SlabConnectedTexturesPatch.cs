using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace ConquestVanillaVom;

// ---------------------------------------------------------------------------------------------
// Terrain Slabs connected-textures fix (Harmony).  Category: "terrainslabs-connected-textures".
//
// WHY (verified against the decompiled client - see openspec/changes/add-terrain-slabs-compat):
//   Connected/tiled textures work on CUBE-drawtype blocks because CubeTesselator.Tesselate picks
//   each face's tile with BakedCompositeTexture.GetTiledTexturesSelector(tiles, side, x, y, z) -
//   a neighbour-aware, position-derived index. Slabs draw as EnumDrawType.JSON and render through
//   JsonTesselator.doMesh instead. The per-tile alternate MESHES already exist for JSON blocks
//   (ShapeTesselatorManager.Tesselate builds altblockModelDatas[blockId] with one mesh per tile
//   for ANY block where HasTiles), but doMesh selects among them with
//       int num = GameMath.MurmurHash3Mod(vars.posX, .., vars.posZ, array.Length);
//       sourceMesh = array[num];
//   i.e. a RANDOM hash, not the position-correct tile - so slab surfaces don't join the pattern.
//
// FIX: a transpiler that leaves that MurmurHash3Mod result on the stack and pipes it through
//   CorrectTileIndex(...), which - when the block HasTiles and a tiled BakedCompositeTexture[]
//   resolves - returns GameMath.Mod(GetTiledTexturesSelector(bakedTiles, UP, x, y, z), len) and
//   otherwise returns the original hash unchanged. Only the FIRST MurmurHash3Mod in doMesh is
//   redirected (there is a SECOND, for random rotations, which must stay random).
//
// LIMITATION (design.md D4): one tile index is chosen for the whole slab mesh - correct for the
//   dominant top (UP) face; the thin vertical edge faces may be imperfect. Ship top-face-correct.
//
// FAIL-SAFE: if the IL pattern isn't found (game-version drift) the transpiler throws, and the
//   ModSystem's ActivateHarmonyCompat catch turns the fix off with a warning (design.md D5).
// ---------------------------------------------------------------------------------------------
[HarmonyPatchCategory("terrainslabs-connected-textures")]
internal static class SlabConnectedTexturesPatch
{
    // Whole-mesh single-tile selection uses the top face - where connected textures matter most.
    private static readonly int TopTileSide = BlockFacing.UP.Index;

    private static readonly MethodInfo MurmurHash3Mod =
        AccessTools.Method(typeof(GameMath), nameof(GameMath.MurmurHash3Mod),
            new[] { typeof(int), typeof(int), typeof(int), typeof(int) })
        ?? throw new InvalidOperationException("GameMath.MurmurHash3Mod(int,int,int,int) not found");

    private static readonly MethodInfo Corrector =
        AccessTools.Method(typeof(SlabConnectedTexturesPatch), nameof(CorrectTileIndex))
        ?? throw new InvalidOperationException("CorrectTileIndex helper not found");

    [HarmonyPatch(typeof(JsonTesselator), nameof(JsonTesselator.doMesh))]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions)
            .MatchStartForward(new CodeMatch(ci => ci.Calls(MurmurHash3Mod)));

        if (matcher.IsInvalid)
            throw new InvalidOperationException(
                "doMesh: could not find the first GameMath.MurmurHash3Mod call to redirect - " +
                "the game's JsonTesselator has likely changed.");

        // After the (first) MurmurHash3Mod call the random index is on the stack. Append:
        //   ldarg.1 (TCTCache vars)  ; ldarg.3 (int lodLevel)  ; call CorrectTileIndex(int,vars,int)
        // leaving the corrected index on the stack in its place.
        matcher.Advance(1).Insert(
            new CodeInstruction(OpCodes.Ldarg_1),
            new CodeInstruction(OpCodes.Ldarg_3),
            new CodeInstruction(OpCodes.Call, Corrector));

        return matcher.InstructionEnumeration();
    }

    /// <summary>Given the engine's random alternate-mesh index, return the position-correct tile
    /// index for a tiled block, else the original index unchanged. Re-derives the alternate-mesh
    /// array from <paramref name="vars"/> + <paramref name="lodLevel"/> exactly as doMesh does, so
    /// the transpiler doesn't need to reach the method-internal local. Never throws.</summary>
    public static int CorrectTileIndex(int randomIndex, TCTCache vars, int lodLevel)
    {
        try
        {
            Block block = vars.block;
            if (block == null || !block.HasTiles) return randomIndex;

            MeshData[]? array = SelectAltArray(vars, lodLevel);
            if (array == null || array.Length == 0) return randomIndex;

            BakedCompositeTexture[]? tiles = FindBakedTiles(block);
            if (tiles == null || tiles.Length == 0) return randomIndex;

            int sel = BakedCompositeTexture.GetTiledTexturesSelector(
                tiles, TopTileSide, vars.posX, vars.posY, vars.posZ);
            return GameMath.Mod(sel, array.Length);
        }
        catch
        {
            // Any unexpected shape of data -> fall back to the engine's original pick.
            return randomIndex;
        }
    }

    // Mirror of doMesh's array selection (line ~555 in the decompile):
    //   (lodLevel + 1) / 2 == 1 -> Lod1 ; lodLevel == 0 -> Lod0 ; else -> Lod2
    private static MeshData[]? SelectAltArray(TCTCache vars, int lodLevel)
    {
        var shapes = vars.shapes;
        int blockId = vars.blockId;
        if ((lodLevel + 1) / 2 == 1) return shapes.altblockModelDatasLod1[blockId];
        if (lodLevel == 0)           return shapes.altblockModelDatasLod0[blockId];
        return shapes.altblockModelDatasLod2[blockId];
    }

    // The block's tiled texture set. Prefer the top face; else the first texture that carries tiles.
    private static BakedCompositeTexture[]? FindBakedTiles(Block block)
    {
        if (block.Textures == null) return null;

        if (block.Textures.TryGetValue(BlockFacing.UP.Code, out var top))
        {
            var t = top?.Baked?.BakedTiles;
            if (t != null && t.Length != 0) return t;
        }
        foreach (var kv in block.Textures)
        {
            var t = kv.Value?.Baked?.BakedTiles;
            if (t != null && t.Length != 0) return t;
        }
        return null;
    }
}
