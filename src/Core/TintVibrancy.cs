using System;
using SkiaSharp;
using Vintagestory.API.Common;

namespace ConquestTweaks;

/// <summary>
/// GROUP 4 (standalone core, folds into nobody). Green-selective grass/plant tint vibrancy.
///
/// Desaturates the GREEN band of the two colormaps the engine blends to tint plants, leaving
/// browns/yellows/autumn tones untouched. Runs from the ModSystem's AssetsLoaded hook so the edited
/// bytes are baked into the block atlas.
///
/// THE DOMINANT TERM IS THE CLIMATE PLANT TINT, NOT THE SEASON GRASS TINT. The engine samples the
/// climate tint (environment/planttint.png, game domain) as the base and only overlays the seasonal
/// grass tint on top (ClientWorldMap.ApplyColorMapOnRgba), so desaturating grasstint alone is nearly
/// invisible. We desaturate the climate tint by default (config SeasonGrassTintOnly=false) and the
/// season grasstint. The climate tint is shared by all foliage (grass, ferns, bushes, reeds AND tree
/// leaves), so this dial tones down all foliage green together - there is no colormap-only way to
/// mute grass while sparing leaves.
///
/// DOMAIN GOTCHA: seasonalGrass is defined in survival/config/colormaps.json with an *unqualified*
/// base, so it resolves to survival:textures/environment/seasons/grasstint.png (Conquest also ships
/// a game: copy the colormap never reads). We overwrite grasstint in every domain it exists.
/// </summary>
internal static class TintVibrancy
{
    public static void Apply(ICoreAPI api, Config config)
    {
        if (!config.GrassVibrancy) return;

        // climatePlantTint is defined in game/config/colormaps.json => game-domain planttint.png.
        if (!config.SeasonGrassTintOnly)
            DesaturateGreen(api, config, "textures/environment/planttint.png",
                new[] { "game", "survival" }, "climate plant tint");

        // seasonalGrass resolves to survival:textures/environment/seasons/grasstint.png; overwrite
        // every domain the asset exists in so we hit whichever copy the colormap loader reads.
        DesaturateGreen(api, config, "textures/environment/seasons/grasstint.png",
            new[] { "survival", "game" }, "seasonal grass tint");
    }

    private static void DesaturateGreen(ICoreAPI api, Config config, string path, string[] domains, string label)
    {
        int edited = 0;
        foreach (var domain in domains)
        {
            if (DesaturateGreenOne(api, config, new AssetLocation(domain, path))) edited++;
        }
        if (edited == 0)
            api.Logger.Warning("[conquesttweaks] Tint colormap {0} not found in any of [{1}]; skipping {2}.",
                path, string.Join(", ", domains), label);
        else
            api.Logger.Notification(
                "[conquesttweaks] Desaturated green in {0} across {1} domain copy/copies (sat x{2:0.00}, bri x{3:0.00}).",
                label, edited, Clamp01Plus(config.GrassGreenSaturation), Clamp01Plus(config.GrassGreenBrightness));
    }

    /// <summary>Desaturate the green band of one colormap asset in place. Returns false (quietly)
    /// if the asset does not exist in that domain, so the caller can try several domains.</summary>
    private static bool DesaturateGreenOne(ICoreAPI api, Config config, AssetLocation loc)
    {
        var asset = api.Assets.TryGet(loc);
        if (asset == null) return false;

        SKBitmap? bmp = null;
        try
        {
            bmp = SKBitmap.Decode(asset.Data);
            if (bmp == null)
            {
                api.Logger.Warning("[conquesttweaks] Could not decode {0}; skipping vibrancy.", loc);
                return false;
            }

            float satMul = Clamp01Plus(config.GrassGreenSaturation);
            float briMul = Clamp01Plus(config.GrassGreenBrightness);

            var pixels = bmp.Pixels;                 // SKColor[]
            for (int i = 0; i < pixels.Length; i++)
            {
                SKColor c = pixels[i];
                c.ToHsl(out float h, out float s, out float l);
                float w = GreenWeight(config, h);    // 0 outside the green band, 1 at its core
                if (w <= 0f) continue;
                float ns = s * Lerp(1f, satMul, w);
                float nl = l * Lerp(1f, briMul, w);
                pixels[i] = SKColor.FromHsl(h, Clamp(ns, 0, 100), Clamp(nl, 0, 100), c.Alpha);
            }
            bmp.Pixels = pixels;

            using var img = SKImage.FromBitmap(bmp);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            asset.Data = data.ToArray();
            return true;
        }
        catch (Exception e)
        {
            api.Logger.Warning("[conquesttweaks] Vibrancy pass on {0} failed: {1}", loc, e.Message);
            return false;
        }
        finally
        {
            bmp?.Dispose();
        }
    }

    /// <summary>Weight of the green-desaturation effect for hue <paramref name="hue"/> (deg):
    /// 1 inside the core band, ramping to 0 across the falloff, so non-green tones are spared.</summary>
    private static float GreenWeight(Config config, float hue)
    {
        float d = Math.Abs(HueDelta(hue, config.GreenHueCenter));
        if (d <= config.GreenHueRange) return 1f;
        float edge = config.GreenHueRange + Math.Max(0.0001f, config.GreenHueFalloff);
        if (d >= edge) return 0f;
        return 1f - (d - config.GreenHueRange) / config.GreenHueFalloff;
    }

    /// <summary>Signed shortest angular distance (deg) between two hues, in [-180, 180].</summary>
    private static float HueDelta(float a, float b)
    {
        float d = (a - b + 540f) % 360f - 180f;
        return d;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    // Multipliers are meant for 0..1 but we tolerate slightly-over-1 "boost" values.
    private static float Clamp01Plus(float v) => v < 0f ? 0f : (v > 4f ? 4f : v);
}
