using System.Collections.Generic;

namespace ConquestVanillaVom;

/// <summary>
/// User-editable config, (de)serialized to
/// <c>VintagestoryData/ModConfig/conquestvanillavom.json</c>.
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
/// edits here (or via the .cvv commands) take effect on the next relog / world reload.
/// </summary>
public class Config
{
    // ---- Ground / dirt families (default ON: these are the "too cartoonish" surfaces) ----

    /// <summary>Soil &amp; the grass-block dirt body (all fertility tiers).</summary>
    public bool RevertSoil = true;

    /// <summary>The green grass top-cover overlay on grass blocks (coverage stages).</summary>
    public bool RevertGrassCover = true;

    /// <summary>Forest floor.</summary>
    public bool RevertForestFloor = true;

    /// <summary>Peat &amp; peat piles.</summary>
    public bool RevertPeat = true;

    /// <summary>Clay (blue / fire / red).</summary>
    public bool RevertClay = true;

    /// <summary>Farmland (dry/moist tiers and their sides).</summary>
    public bool RevertFarmland = true;

    /// <summary>Cob.</summary>
    public bool RevertCob = true;

    /// <summary>Rammed earth.</summary>
    public bool RevertRammedEarth = true;

    /// <summary>Mud brick.</summary>
    public bool RevertMudBrick = true;

    /// <summary>Stone path (this covers the path block plus its slab &amp; stair variants,
    /// which reuse the same textures).</summary>
    public bool RevertStonePath = true;

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
    /// green while sparing leaves.</summary>
    public bool GrassVibrancy = true;

    /// <summary>Saturation multiplier applied to GREEN tint pixels. 1.0 = untouched (vanilla
    /// Conquest look); lower = less vibrant. ~0.6 is a noticeable, natural knock-down.</summary>
    public float GrassGreenSaturation = 0.6f;

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

    // ---- Missing-texture / placeholder diagnostics ----
    //
    // The ore placeholder / Visible-Ores-&-Minerals repair is now done with static JSON patches
    // (assets/conquestvanillavom/patches/vom-ore-*.json), not a runtime toggle - a patch can't
    // read this config, and it is a pure additive repair that only engages when VOM is present.

    /// <summary>On world load, log a summary of any blocks that still resolve to a missing/
    /// placeholder texture (same data as <c>.cvv scan</c>). Off by default to keep logs quiet.</summary>
    public bool ReportMissingTexturesOnLoad = false;

    /// <summary>Enumerates every family flag by its config key, for the override loop and the
    /// <c>.cvv</c> command. Keys match the <c>&lt;family&gt;</c> subfolders produced by
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
