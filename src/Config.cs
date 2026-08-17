using System.Collections.Generic;

namespace ConquestTweaks;

/// <summary>
/// User-editable config, (de)serialized to
/// <c>VintagestoryData/ModConfig/conquesttweaks.json</c>.
///
/// Every block-family flag is an independent toggle. When ON, the mod overwrites Conquest's
/// texture bytes for that family with the bundled vanilla source art, collapsing Conquest's
/// extra tiled variants back onto the single vanilla texture - i.e. that family looks vanilla
/// again. When OFF, Conquest's textures are left untouched.
///
/// The mod NEVER introduces the pink/black "unknown" placeholder: it only overwrites a Conquest
/// texture path for which we actually bundled a real vanilla source, and only if that Conquest
/// path already exists in the loaded assets.
///
/// Texture and tint changes are baked into the block texture atlas at world/client load, so
/// edits here (or via the .ctc commands) take effect on the next relog / world reload.
/// </summary>
public class Config
{
    // ---- Ground / dirt families. ALL default OFF: out of the box the mod keeps Conquest's look
    //      untouched, and each family is an opt-in revert (turn on per family, then relog). ----

    /// <summary>Soil &amp; the grass-block dirt body (all fertility tiers).</summary>
    public bool RevertSoil = false;

    /// <summary>The green grass top-cover overlay on grass blocks (coverage stages).</summary>
    public bool RevertGrassCover = false;

    /// <summary>Forest floor.</summary>
    public bool RevertForestFloor = false;

    /// <summary>Peat &amp; peat piles.</summary>
    public bool RevertPeat = false;

    /// <summary>Clay (blue / fire / red).</summary>
    public bool RevertClay = false;

    /// <summary>Farmland (dry/moist tiers and their sides).</summary>
    public bool RevertFarmland = false;

    /// <summary>Cob.</summary>
    public bool RevertCob = false;

    /// <summary>Rammed earth.</summary>
    public bool RevertRammedEarth = false;

    /// <summary>Mud brick.</summary>
    public bool RevertMudBrick = false;

    /// <summary>Stone path (this covers the path block plus its slab &amp; stair variants,
    /// which reuse the same textures).</summary>
    public bool RevertStonePath = false;

    // ---- Foliage families (default OFF: Conquest reorganized these heavily; the grass-tint
    //      vibrancy dial below is usually the better lever for "too green" plants) ----

    /// <summary>Tall grass.</summary>
    public bool RevertTallGrass = false;

    /// <summary>Ferns, flowers, herbs, reeds, bamboo, waterlily, etc.
    /// NOTE: Conquest restructured many of these away from vanilla's layout, so coverage is
    /// partial - only textures with a clean vanilla equivalent revert. Prefer the grass-tint
    /// vibrancy dial for a uniform "less vibrant" look across all plants.</summary>
    public bool RevertOtherFoliage = false;

    // ---- Grass / plant tint vibrancy (green-selective desaturation of the tint colormaps) ----

    /// <summary>Master switch for the vibrancy pass. When on, we desaturate the GREEN of the two
    /// colormaps the game blends to tint plants: the <b>climate plant tint</b>
    /// (<c>environment/planttint.png</c>) and the <b>seasonal grass tint</b>
    /// (<c>environment/seasons/grasstint.png</c>).
    ///
    /// IMPORTANT: the climate plant tint is the DOMINANT term - the engine samples it as the base
    /// color and only overlays the seasonal tint on top (<c>ClientWorldMap.ApplyColorMapOnRgba</c>),
    /// so desaturating grasstint alone is nearly invisible. That tint is shared by everything green
    /// (grass, ferns, bushes, reeds, AND tree leaves via <c>climatePlantTint</c>), so this dial
    /// tones down all foliage green together - there is no colormap-only way to knock down grass
    /// green while sparing leaves.
    ///
    /// Default OFF: out of the box the mod leaves Conquest's foliage untouched. Turn it on (or run
    /// <c>.ctc vibrancy &lt;value&gt;</c>, which enables it) to tone green down.</summary>
    public bool GrassVibrancy = false;

    /// <summary>Saturation multiplier applied to GREEN tint pixels. 1.0 = untouched (vanilla
    /// Conquest look, the default); lower = less vibrant. ~0.8 is a gentle knock-down; ~0.6 is
    /// stronger.</summary>
    public float GrassGreenSaturation = 1.0f;

    /// <summary>Optional brightness (HSL lightness) multiplier on green tint pixels. 1.0 = off.
    /// Slightly below 1.0 (e.g. 0.95) tames Conquest's near-neon highlights.</summary>
    public float GrassGreenBrightness = 1.0f;

    /// <summary>Hue (degrees, 0-360) treated as the center of "green". VS grass tints sit
    /// around yellow-green to green; 100 is a good center.</summary>
    public float GreenHueCenter = 100f;

    /// <summary>Half-width (degrees) of the fully-affected green band around
    /// <see cref="GreenHueCenter"/>. Pixels within this are desaturated at full strength.</summary>
    public float GreenHueRange = 55f;

    /// <summary>Additional degrees over which the effect fades to zero past the band edge, so
    /// browns/yellows/autumn tones are left progressively untouched (no hard seam).</summary>
    public float GreenHueFalloff = 25f;

    /// <summary>Advanced: restrict the vibrancy pass to the seasonal grass tint ONLY, leaving the
    /// dominant climate plant tint untouched. Default false (i.e. we desaturate both, which is the
    /// only combination that produces a visible change). Setting this true reproduces the old
    /// near-invisible "season-only" behavior and is kept mainly for experimentation.</summary>
    public bool SeasonGrassTintOnly = false;

    // ---- Optional-mod compatibility fixes (C#/Harmony) ----
    //
    // Each C#/Harmony compat fix has a toggle here. The fix is applied only when its toggle is on
    // AND its target mod is detected at runtime (IModLoader.IsModEnabled), so leaving a toggle on
    // is inert for anyone without the target mod. JSON-patch fixes (VOM) have NO toggle - a JSON
    // patch can't read this config; its `dependsOn` is the gate. Applies on relog.

    /// <summary>Terrain Slabs connected-textures fix. When Terrain Slabs (<c>terrainslabs</c>) is
    /// installed alongside Conquest, this makes Conquest's connected (tiled) textures apply to slab
    /// blocks by selecting the position-correct tile on the JSON draw path (the engine otherwise
    /// picks a random tile variant for JSON-drawtype blocks). Default ON; inert without Terrain
    /// Slabs. See src/Compat/TerrainSlabs/SlabConnectedTexturesPatch.cs.</summary>
    public bool EnableSlabsFix = true;

    /// <summary>Connected-texture selector clamp (black-void guard). A ~one-line hardening: postfix
    /// <c>BakedCompositeTexture.GetTiledTexturesSelector</c> so its returned tile index is wrapped
    /// into range (<c>GameMath.Mod(index, tiles.Length)</c>). The engine's selector can return an
    /// out-of-range index whenever a block declares <c>tilesWidth</c> greater than the number of
    /// tiles it actually ships (then rows = tileCount / tilesWidth = 0 and the column term overshoots
    /// a too-short array) - which the non-clamping <c>TopSoil</c> render path turns into black/void
    /// blocks (this is what a naive clay grass-slab overlay did before we split its width byType). The
    /// clamp is a NO-OP for correctly-authored blocks (the index is already in range) and only ever
    /// converts a would-be void into a valid wrapped tile, so it is safe to leave on. Gated on
    /// <c>conquest</c> (our hard dep) so it is always active in the pack context; inert without it.
    /// This is arguably an engine-level (Anego) hardening rather than a pack fix - see the handoff
    /// docs. Default ON. See src/Compat/TiledSelectorClampPatch.cs.</summary>
    public bool EnableTiledSelectorClamp = true;

    // ---- Missing-texture / placeholder diagnostics ----
    //
    // The ore placeholder / Visible-Ores-&-Minerals repair is now done with static JSON patches
    // (assets/conquesttweaks/patches/compatibility/visibleoresandminerals/ore-*.json), not a runtime
    // toggle - a patch can't read this config, and it is a pure additive repair that only engages
    // when VOM is present.

    /// <summary>On world load, log a summary of any blocks that still resolve to a missing/
    /// placeholder texture (same data as <c>.ctc scan</c>). Off by default to keep logs quiet.</summary>
    public bool ReportMissingTexturesOnLoad = false;

    /// <summary>Enumerates every family flag by its config key, for the override loop and the
    /// <c>.ctc</c> command. Keys match the <c>&lt;family&gt;</c> subfolders produced by
    /// build/extract-vanilla.py.</summary>
    public IEnumerable<KeyValuePair<string, bool>> FamilyToggles()
    {
        yield return new("soil", RevertSoil);
        yield return new("grasscover", RevertGrassCover);
        yield return new("forestfloor", RevertForestFloor);
        yield return new("peat", RevertPeat);
        yield return new("clay", RevertClay);
        yield return new("farmland", RevertFarmland);
        yield return new("cob", RevertCob);
        yield return new("rammedearth", RevertRammedEarth);
        yield return new("mudbrick", RevertMudBrick);
        yield return new("stonepath", RevertStonePath);
        yield return new("tallgrass", RevertTallGrass);
        yield return new("otherfoliage", RevertOtherFoliage);
    }

    /// <summary>Sets a family flag by its config key. Returns false for an unknown key.</summary>
    public bool SetFamily(string key, bool value)
    {
        switch (key)
        {
            case "soil": RevertSoil = value; return true;
            case "grasscover": RevertGrassCover = value; return true;
            case "forestfloor": RevertForestFloor = value; return true;
            case "peat": RevertPeat = value; return true;
            case "clay": RevertClay = value; return true;
            case "farmland": RevertFarmland = value; return true;
            case "cob": RevertCob = value; return true;
            case "rammedearth": RevertRammedEarth = value; return true;
            case "mudbrick": RevertMudBrick = value; return true;
            case "stonepath": RevertStonePath = value; return true;
            case "tallgrass": RevertTallGrass = value; return true;
            case "otherfoliage": RevertOtherFoliage = value; return true;
            default: return false;
        }
    }
}
