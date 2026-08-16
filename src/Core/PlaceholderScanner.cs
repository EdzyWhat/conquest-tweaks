using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ConquestTweaks;

/// <summary>
/// GROUP 4 (standalone core, folds into nobody). Diagnostic scanner behind <c>.ctc scan</c> and the
/// optional on-load report.
///
/// Walks the loaded block registry for blocks that resolve to a missing / placeholder texture
/// (Conquest leaving a variant un-wired, or a VOM/Juicy-Ores vein whose shape needs a texture code
/// the block doesn't provide). Groups the offenders, optionally logs a summary, and writes the full
/// list to ModConfig/ctc-missing-textures.txt. Parses each shape file at most once (cached).
/// </summary>
internal sealed class PlaceholderScanner
{
    private readonly ICoreClientAPI capi;

    // Cache of texture codes referenced by a shape (keyed by shape asset location) so the scanner
    // parses each shape file at most once.
    private readonly Dictionary<string, string[]> shapeCodeCache = new();

    // Texture codes the engine resolves on its own, so a block need not map them and they must
    // never be flagged: "null"/"none" are the "no face / cull" sentinels, and "0" is the implicit
    // default texture slot (this is why the game logs #cube/#ore1 gaps for broken veins but NEVER
    // #0). A code self-defined in the shape's own top-level "textures" dict is likewise fine.
    private static readonly HashSet<string> AutoResolvedShapeCodes = new() { "null", "none", "0" };

    public PlaceholderScanner(ICoreClientAPI capi) => this.capi = capi;

    /// <summary>Scan the block registry; returns (scanned, broken, reportPath).</summary>
    public (int scanned, int broken, string reportPath) Scan(bool logToConsole)
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
            // above misses, and why VOM veins scanned "clean" while showing pink in-world.
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
            capi.Logger.Warning("[conquesttweaks] Could not write scan report: {0}", e.Message);
            reportPath = "(could not write report)";
        }

        if (logToConsole)
            capi.Logger.Notification("[conquesttweaks] Placeholder scan: {0}/{1} blocks broken across {2} groups. Report: {3}",
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
}
