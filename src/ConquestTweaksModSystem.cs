using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace ConquestTweaks;

/// <summary>
/// Client-side companion to the Conquest VS Edition texture pack. (The C# runs client-only via
/// <see cref="ShouldLoad"/>; the mod is packaged side=Universal only so the separate VOM ore JSON
/// patches - see the ore-fix note below - can apply server-side. This class does no server work.)
///
/// Two independent jobs, both driven by <see cref="Config"/>:
///   1. Per-family texture reverts. For each enabled family we overwrite Conquest's texture
///      BYTES in-memory (in AssetsLoaded, after assets are loaded/patched and before the block
///      texture atlas is composed - see the note on that hook) with the bundled vanilla source
///      art. Conquest's extra
///      tiled variants collapse onto the single vanilla texture they map to => vanilla look.
///      We only ever overwrite a Conquest path for which a real vanilla source was bundled AND
///      that already exists in the loaded assets, so we never introduce the pink/black
///      "unknown" placeholder.
///   2. A green-selective vibrancy pass that desaturates the grass tint colormap (and optionally
///      the all-plant climate tint), leaving browns/yellows/autumn tones untouched.
///
/// A third concern - repairing Visible Ores &amp; Minerals ore veins that Conquest breaks - is NOT
/// done here: blocktype JSON isn't reachable from C#, so it lives in static JSON patches under
/// assets/conquesttweaks/patches/ (see the ore-fix note below).
///
/// Both C# jobs bake into the atlas at load, so config / command changes take effect on relog.
/// </summary>
public class ConquestTweaksModSystem : ModSystem
{
    private const string ConfigFile = "conquesttweaks.json";
    private const string SourcePrefix = "textures/vanilla/";   // under our own domain

    private ICoreAPI api = null!;
    private ICoreClientAPI capi = null!;
    private Config config = new();

    // Set in StartPre from Mod.Info.ModID; used as the Harmony instance id and the log prefix base.
    private string modId = "conquesttweaks";

    // Single Harmony instance, created lazily only when at least one C#/Harmony compat fix
    // activates (see StartClientSide). Null when no such fix is active; unpatched in Dispose.
    private Harmony? harmony;

    // Cache of texture codes referenced by a shape (keyed by shape asset location) so the scanner
    // parses each shape file at most once.
    private readonly Dictionary<string, string[]> shapeCodeCache = new();

    // ---------------------------------------------------------------- optional-mod compat registry
    //
    // The always-on core (reverts + vibrancy) is NOT here - only optional per-mod fixes that
    // activate when their target mod is detected. JSON-patch fixes (VOM) self-gate via the patch's
    // own `dependsOn` and are listed only so `.ctc list` can report them; Harmony fixes carry a
    // config toggle + a [HarmonyPatchCategory] name and are applied in StartClientSide when their
    // target mod is present. Add the Terrain Slabs connected-texture fix here (Mechanism = Harmony)
    // once its patch class exists.
    private static readonly CompatFix[] CompatFixes =
    {
        new CompatFix
        {
            DisplayName = "Visible Ores & Minerals ore-vein fix",
            TargetModId = "visibleoresandminerals",
            Mechanism   = CompatMechanism.JsonPatch,   // gated by dependsOn in the JSON patches; no C#
        },
        new CompatFix
        {
            DisplayName     = "Terrain Slabs connected textures",
            TargetModId     = "terrainslabs",          // hard-deps placeonslabs, so this implies both
            Mechanism       = CompatMechanism.Harmony,
            HarmonyCategory = "terrainslabs-connected-textures",
            ConfigEnabled   = cfg => cfg.EnableSlabsFix,
        },
    };

    // Client-only mod.
    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

    public override void StartPre(ICoreAPI api)
    {
        this.api = api;
        modId = Mod.Info.ModID;
        try
        {
            config = api.LoadModConfig<Config>(ConfigFile) ?? new Config();
        }
        catch (Exception e)
        {
            api.Logger.Warning("[conquesttweaks] Could not read config, using defaults: " + e.Message);
            config = new Config();
        }
        // Write back so a fresh install gets a fully-populated, editable file.
        api.StoreModConfig(config, ConfigFile);
    }

    // IMPORTANT: hook AssetsLoaded, NOT AssetsFinalize. On a CLIENT (this is a client-only mod),
    // the engine runs the AssetsFinalize mod phase from OnLevelFinalize - i.e. AFTER the block
    // texture atlas has already been composed (verified in VintagestoryLib: AfterAssetsLoaded ->
    // OnAssetsLoaded [AssetsLoaded phase] -> CreateNewAtlas/ComposeTextureAtlasses..., then much
    // later OnLevelFinalize -> AssetsFinalize phase). Editing texture/colormap bytes in
    // AssetsFinalize is too late: the atlas is baked from the ORIGINAL bytes, so nothing we do
    // shows up (this was the "reverts/vibrancy do nothing" bug). AssetsLoaded runs after assets are
    // loaded AND patched but BEFORE the atlas is composed, so our byte edits are picked up.
    // (On the SERVER, AssetsFinalize does run before block loading - but that's irrelevant here.)
    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Client) return;
        ApplyTextureReverts(api);
        ApplyTintVibrancy(api);
        // Ore placeholder / VOM repair is done with JSON patches under
        // assets/conquesttweaks/patches/ (blocktype JSON is not retrievable via
        // api.Assets here, so a byte-edit can't touch it - see the ore-fix note below).
    }

    public override void StartClientSide(ICoreClientAPI capi)
    {
        this.capi = capi;
        if (config.ReportMissingTexturesOnLoad)
            capi.Event.LevelFinalize += () => ScanMissingTextures(logToConsole: false);

        var parsers = capi.ChatCommands.Parsers;
        capi.ChatCommands.Create("ctc")
            .WithDescription("Conquest VS Tweaks & Compatibility controls (changes apply on relog)")
            .BeginSubCommand("list")
                .WithDescription("Show which texture each surface uses (vanilla/conquest) and vibrancy settings")
                .HandleWith(OnList)
            .EndSubCommand()
            .BeginSubCommand("set")
                .WithDescription("Choose the texture for a surface: .ctc set <name> vanilla|conquest")
                .WithArgs(parsers.Word("name"), parsers.WordRange("source", "vanilla", "conquest"))
                .HandleWith(OnSet)
            .EndSubCommand()
            .BeginSubCommand("vibrancy")
                .WithDescription("Green saturation multiplier 0..1 (lower = less vibrant): .ctc vibrancy <value>")
                .WithArgs(parsers.DoubleRange("value", 0, 1))
                .HandleWith(OnVibrancy)
            .EndSubCommand()
            .BeginSubCommand("scan")
                .WithDescription("List blocks whose textures resolve to the pink/black placeholder, and write a full report")
                .HandleWith(OnScan)
            .EndSubCommand();

        ActivateHarmonyCompat(capi);
    }

    // ---------------------------------------------------------------- optional-mod compat activation

    /// <summary>Apply each Harmony-mechanism compat fix whose config toggle is on AND whose target
    /// mod is detected. The Harmony instance is created lazily on the first active fix, so no
    /// Harmony object exists when nothing activates. JSON-patch fixes are skipped here - they
    /// self-gate via their patch's <c>dependsOn</c>.</summary>
    private void ActivateHarmonyCompat(ICoreClientAPI capi)
    {
        foreach (var fix in CompatFixes)
        {
            if (fix.Mechanism != CompatMechanism.Harmony) continue;
            if (!fix.IsEnabledInConfig(config)) continue;
            if (!capi.ModLoader.IsModEnabled(fix.TargetModId)) continue;

            // Fail-safe: a Harmony fix targets internal engine methods that can shift between game
            // versions. If PatchCategory can't resolve/patch its targets, log and keep the rest of
            // the mod (reverts, vibrancy, scanner, other fixes) fully working - never crash the client.
            try
            {
                harmony ??= new Harmony(modId);
                harmony.PatchCategory(fix.HarmonyCategory);
                capi.Logger.Notification("[{0}] compat active: {1} (target '{2}')",
                    modId, fix.DisplayName, fix.TargetModId);
            }
            catch (Exception e)
            {
                capi.Logger.Warning("[{0}] compat '{1}' unavailable on this game version (patch failed): {2}",
                    modId, fix.DisplayName, e.Message);
            }
        }
    }

    public override void Dispose()
    {
        // Only created if a Harmony compat fix activated; remove exactly our patches.
        harmony?.UnpatchAll(modId);
        harmony = null;
        base.Dispose();
    }

    // ---------------------------------------------------------------- texture reverts

    private void ApplyTextureReverts(ICoreAPI api)
    {
        var enabled = new HashSet<string>(
            config.FamilyToggles().Where(t => t.Value).Select(t => t.Key));
        if (enabled.Count == 0) return;

        int applied = 0, missing = 0;
        var sources = api.Assets.GetMany(SourcePrefix, "conquesttweaks");
        foreach (var src in sources)
        {
            string path = src.Location.Path;                 // textures/vanilla/<family>/<rel>
            if (!path.StartsWith(SourcePrefix)) continue;
            string rest = path.Substring(SourcePrefix.Length);
            int slash = rest.IndexOf('/');
            if (slash < 0) continue;
            string family = rest.Substring(0, slash);
            if (!enabled.Contains(family)) continue;
            string rel = rest.Substring(slash + 1);          // block/...

            // Conquest ships its overrides in the "game" domain; we mutate that same asset.
            var target = new AssetLocation("game", "textures/" + rel);
            var existing = api.Assets.TryGet(target);
            if (existing == null) { missing++; continue; }   // never Add => never a placeholder
            existing.Data = src.Data;
            applied++;
        }

        api.Logger.Notification(
            "[conquesttweaks] Reverted {0} Conquest textures across {1} families to vanilla ({2} paths had no live Conquest asset and were left alone).",
            applied, enabled.Count, missing);
    }

    // ---------------------------------------------------------------- green-selective vibrancy

    private void ApplyTintVibrancy(ICoreAPI api)
    {
        if (!config.GrassVibrancy) return;

        // The engine tints plants by sampling the CLIMATE plant tint as the base color and only
        // overlaying the seasonal tint on top (ClientWorldMap.ApplyColorMapOnRgba: num = climate;
        // num = ColorOverlay(num, season, weight); final = texture * num). So the climate plant
        // tint dominates - desaturating only the season grasstint is nearly invisible in-game
        // (exactly the "no difference" bug). We therefore desaturate the climate plant tint by
        // default, and only skip it in the advanced season-only mode.
        //
        // climatePlantTint is defined in game/config/colormaps.json => game-domain planttint.png.
        if (!config.SeasonGrassTintOnly)
            DesaturateGreen(api, "textures/environment/planttint.png",
                new[] { "game", "survival" }, "climate plant tint");

        // seasonalGrass is defined in survival/config/colormaps.json with an *unqualified* texture
        // base, so it resolves to survival:textures/environment/seasons/grasstint.png - NOT the
        // game-domain copy Conquest also ships. Overwrite every domain the asset exists in so we
        // hit whichever copy the colormap loader actually reads (survival is authoritative; editing
        // Conquest's game copy too is harmless).
        DesaturateGreen(api, "textures/environment/seasons/grasstint.png",
            new[] { "survival", "game" }, "seasonal grass tint");
    }

    private void DesaturateGreen(ICoreAPI api, string path, string[] domains, string label)
    {
        int edited = 0;
        foreach (var domain in domains)
        {
            if (DesaturateGreenOne(api, new AssetLocation(domain, path))) edited++;
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
    private bool DesaturateGreenOne(ICoreAPI api, AssetLocation loc)
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
                float w = GreenWeight(h);            // 0 outside the green band, 1 at its core
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
    private float GreenWeight(float hue)
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

    // ---------------------------------------------------------------- ore placeholder fix (JSON patches)
    //
    // Conquest does `op:remove /textures` on the ore blocktypes (ore-graded/ungraded/gem) and rebuilds
    // them via a `texturesByType`. When Visible Ores & Minerals is also present it turns those same
    // blocktypes into 3D `drawtype:json` veins whose shapes reference the texture codes `#cube`
    // (surrounding stone) and `#ore1` (the visible lump), wiring them with `op:add /textures/...` -
    // which SILENTLY FAILS whenever Conquest's `op:remove /textures` already ran (JSON-patch `add`
    // resolves the parent `/textures` with skipLast and no-ops if it's gone). The veins then have no
    // `cube`/`ore1` mapping and render the pink/black placeholder ("Missing mapping for texture code
    // #cube ...").
    //
    // This can NOT be repaired from C#: blocktype JSON assets are not retrievable via
    // api.Assets.TryGet at the asset-load phase (returns null in every domain - the categories the
    // asset manager exposes there don't include blocktypes), so a byte-edit like ApplyTextureReverts does
    // for textures is impossible here. The fix is instead three JSON patches under
    // assets/conquesttweaks/patches/vom-ore-*.json that `addmerge` onto the PARENT `/textures`
    // (addmerge resolves the parent to the block root, so it works even when `/textures` was removed),
    // re-adding a gap-free Conquest-rock `cube` for every ore/rock combo plus VOM's `ore1ByType` lump
    // map. Because blocktype JSON is resolved SERVER-side (the client just receives resolved blocks
    // over the network), the mod is packaged side=Universal so these patches actually apply where the
    // blocktype lives - a client-only mod's patch would have no target and silently miss. They are
    // gated `dependsOn visibleoresandminerals` and apply after Conquest's remove because this mod
    // hard-depends on `conquest` (and so loads later). No runtime config toggle - a static patch can't
    // read our config, and it's a pure additive repair when VOM is present.

    // ---------------------------------------------------------------- placeholder scanner (diagnostic)

    /// <summary>Walk the loaded block registry for blocks that resolve to a missing / placeholder
    /// texture (Conquest leaving a variant un-wired). Groups the offenders, prints a summary to
    /// chat/console, and writes the full list to ModConfig/ctc-missing-textures.txt.</summary>
    private (int scanned, int broken, string reportPath) ScanMissingTextures(bool logToConsole)
    {
        var groups = new Dictionary<string, List<string>>();
        int scanned = 0, broken = 0;

        foreach (var block in capi.World.Blocks)
        {
            if (block?.Code == null || block.BlockId == 0) continue; // skip air / unregistered
            // Multiblock helper blocks (monolithic placeholders + machine part-tops) legitimately
            // carry no visible texture wiring - they are invisible structural stand-ins, not the
            // pink/black "unknown" placeholder. Skip them so the scan surfaces only real gaps.
            if (block.Code.Path.Contains("multiblock")) continue;
            scanned++;

            var missing = new List<string>();
            bool noTextures = block.Textures == null || block.Textures.Count == 0;
            if (block.Textures != null)
                foreach (var kv in block.Textures)
                    CollectMissingRefs(kv.Value, missing);

            // Shape texture-code gaps: a json-drawtype block whose shape references a texture code
            // (e.g. #cube, #ore1) with no matching entry in block.Textures renders the placeholder
            // even when every wired texture asset resolves - the exact class the plain ref-check
            // above misses, and why VOM veins scanned "clean" while showing pink in-world. Flag any
            // shape code the block doesn't map.
            if (block.Shape?.Base != null)
                foreach (var code in GetShapeTextureCodes(block.Shape.Base))
                    if (block.Textures == null || !block.Textures.ContainsKey(code))
                        missing.Add("#" + code + " (shape code, no texture mapping)");

            if (!noTextures && missing.Count == 0) continue;

            broken++;
            string key = GroupKey(block.Code.Path);
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<string>();
            list.Add(noTextures
                ? block.Code.ToShortString() + "  (no texture wiring)"
                : block.Code.ToShortString() + "  -> missing: " + string.Join(", ", missing.Distinct()));
        }

        // Build report.
        var sb = new StringBuilder();
        sb.AppendLine($"Conquest VS Tweaks & Compatibility - missing/placeholder texture scan");
        sb.AppendLine($"scanned {scanned} blocks, {broken} resolve to the placeholder, {groups.Count} groups");
        sb.AppendLine();
        foreach (var g in groups.OrderByDescending(g => g.Value.Count))
        {
            sb.AppendLine($"== {g.Key} ({g.Value.Count}) ==");
            foreach (var line in g.Value) sb.AppendLine("  " + line);
            sb.AppendLine();
        }

        string reportPath = Path.Combine(GamePaths.DataPath, "ModConfig", "ctc-missing-textures.txt");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            File.WriteAllText(reportPath, sb.ToString());
        }
        catch (Exception e)
        {
            api.Logger.Warning("[conquesttweaks] Could not write scan report: {0}", e.Message);
            reportPath = "(could not write report)";
        }

        if (logToConsole)
            api.Logger.Notification("[conquesttweaks] Placeholder scan: {0}/{1} blocks broken across {2} groups. Report: {3}",
                broken, scanned, groups.Count, reportPath);

        return (scanned, broken, reportPath);
    }

    private void CollectMissingRefs(CompositeTexture ct, List<string> into)
    {
        if (ct == null) return;
        CheckRef(ct.Base, into);
        if (ct.Alternates != null)
            foreach (var alt in ct.Alternates) CheckRef(alt?.Base, into);
        if (ct.BlendedOverlays != null)
            foreach (var ov in ct.BlendedOverlays) CheckRef(ov?.Base, into);
    }

    private void CheckRef(AssetLocation? texPath, List<string> into)
    {
        if (texPath == null) return;
        // CompositeTexture paths omit the "textures/" category prefix and ".png" extension.
        var loc = new AssetLocation(texPath.Domain, "textures/" + texPath.Path + ".png");
        if (capi.Assets.TryGet(loc) == null) into.Add(texPath.ToShortString());
    }

    // Texture codes the engine resolves on its own, so a block need not map them and they must
    // never be flagged: "null"/"none" are the "no face / cull" sentinels, and "0" is the implicit
    // default texture slot (this is why the game logs #cube/#ore1 gaps for broken VOM veins but
    // NEVER #0). A code referenced by a shape but self-defined in that shape's own top-level
    // "textures" dict is likewise fine - only codes the shape references AND neither it nor the
    // block provides are real gaps.
    private static readonly HashSet<string> AutoResolvedShapeCodes = new() { "null", "none", "0" };

    /// <summary>The distinct texture codes (without the leading <c>#</c>) a shape's element faces
    /// reference but the shape itself does NOT define in its own top-level <c>textures</c> dict -
    /// i.e. the codes that must come from the block (e.g. <c>cube</c>, <c>ore1</c>). Engine
    /// auto-resolved codes (<see cref="AutoResolvedShapeCodes"/>) are excluded. Parsed once per
    /// shape and cached. On any read/parse failure returns an empty array (so the scanner never
    /// false-flags a block it couldn't inspect).</summary>
    private string[] GetShapeTextureCodes(AssetLocation shapeBase)
    {
        string key = shapeBase.ToShortString();
        if (shapeCodeCache.TryGetValue(key, out var cached)) return cached;

        var referenced = new HashSet<string>();
        var selfDefined = new HashSet<string>();
        try
        {
            var loc = new AssetLocation(shapeBase.Domain, "shapes/" + shapeBase.Path + ".json");
            var asset = capi.Assets.TryGet(loc);
            if (asset != null)
            {
                // VS shape files are JSON5-ish (may carry // comments); strip line comments so
                // Newtonsoft's strict reader doesn't throw, then walk every "texture" property.
                string text = StripLineComments(asset.ToText());
                var root = JToken.Parse(text);
                CollectShapeCodes(root, referenced);
                // Codes the shape provides itself (root "textures": { "<code>": "<path>", ... }).
                if (root is JObject ro && ro["textures"] is JObject texObj)
                    foreach (var p in texObj.Properties()) selfDefined.Add(p.Name);
            }
        }
        catch { /* leave empty - never a false positive from an unreadable shape */ }

        referenced.ExceptWith(selfDefined);
        referenced.ExceptWith(AutoResolvedShapeCodes);
        var arr = referenced.ToArray();
        shapeCodeCache[key] = arr;
        return arr;
    }

    /// <summary>Recursively collect the values of every <c>"texture"</c> property that names a code
    /// (starts with <c>#</c>), from anywhere in a shape's element/face tree.</summary>
    private static void CollectShapeCodes(JToken token, HashSet<string> into)
    {
        if (token is JObject obj)
        {
            foreach (var prop in obj.Properties())
            {
                if (prop.Name == "texture" && prop.Value.Type == JTokenType.String)
                {
                    string v = (string)prop.Value!;
                    if (v.StartsWith("#") && v.Length > 1) into.Add(v.Substring(1));
                }
                else CollectShapeCodes(prop.Value, into);
            }
        }
        else if (token is JArray arr)
        {
            foreach (var item in arr) CollectShapeCodes(item, into);
        }
    }

    private static string StripLineComments(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var line in s.Split('\n'))
        {
            int i = line.IndexOf("//", StringComparison.Ordinal);
            sb.Append(i >= 0 ? line.Substring(0, i) : line).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Group a variant code by its first two dash-segments (e.g. "ore-graded" style
    /// buckets) so the report is readable rather than one line per variant.</summary>
    private static string GroupKey(string codePath)
    {
        var parts = codePath.Split('-');
        return parts.Length >= 2 ? parts[0] + "-" + parts[1] : codePath;
    }

    // ---------------------------------------------------------------- commands

    private TextCommandResult OnList(TextCommandCallingArgs args)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Conquest VS Tweaks & Compatibility - texture per surface (relog to apply):");
        foreach (var t in config.FamilyToggles())
            sb.AppendLine($"  {t.Key,-13} {(t.Value ? "vanilla" : "conquest")}");
        sb.AppendLine($"Grass vibrancy: {(config.GrassVibrancy ? "on" : "off")}  " +
                      $"green sat x{config.GrassGreenSaturation:0.00}, bri x{config.GrassGreenBrightness:0.00}");
        sb.AppendLine($"  green band: center {config.GreenHueCenter:0}°, range ±{config.GreenHueRange:0}°, falloff {config.GreenHueFalloff:0}°");
        sb.AppendLine($"  targets: {(config.SeasonGrassTintOnly ? "seasonal grass tint only" : "climate plant tint + seasonal grass tint (affects all foliage green)")}");

        sb.AppendLine("Compatibility fixes (activate only when their target mod is present):");
        foreach (var fix in CompatFixes)
        {
            bool detected = capi.ModLoader.IsModEnabled(fix.TargetModId);
            string mechanism = fix.Mechanism == CompatMechanism.Harmony ? "harmony" : "json";
            string enabled = fix.ConfigEnabled == null ? "always" : (fix.IsEnabledInConfig(config) ? "on" : "off");
            sb.AppendLine($"  {fix.DisplayName} [{fix.TargetModId}]: {(detected ? "detected" : "not present")}, {mechanism}, enabled {enabled}");
        }
        return TextCommandResult.Success(sb.ToString().TrimEnd());
    }

    private TextCommandResult OnSet(TextCommandCallingArgs args)
    {
        string name = ((string)args[0]).ToLowerInvariant();
        bool useVanilla = (string)args[1] == "vanilla";
        if (!config.SetFamily(name, useVanilla))
            return TextCommandResult.Error($"Unknown surface '{name}'. Use .ctc list to see valid names.");
        api.StoreModConfig(config, ConfigFile);
        return TextCommandResult.Success($"{name} → {(useVanilla ? "vanilla" : "conquest")} (relog to apply).");
    }

    private TextCommandResult OnScan(TextCommandCallingArgs args)
    {
        var (scanned, broken, reportPath) = ScanMissingTextures(logToConsole: true);
        if (broken == 0)
            return TextCommandResult.Success($"Scanned {scanned} blocks - no placeholder/missing textures found.");
        return TextCommandResult.Success(
            $"Scanned {scanned} blocks - {broken} resolve to the placeholder. Full list written to:\n{reportPath}");
    }

    private TextCommandResult OnVibrancy(TextCommandCallingArgs args)
    {
        double v = (double)args[0];
        config.GrassGreenSaturation = (float)v;
        config.GrassVibrancy = true;
        api.StoreModConfig(config, ConfigFile);
        return TextCommandResult.Success($"Grass green saturation = {v:0.00} (vibrancy on). Relog to apply.");
    }
}
