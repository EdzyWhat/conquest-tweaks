using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ConquestTweaks;

/// <summary>
/// GROUP 4 (standalone core, folds into nobody). The <c>.ctc reverts extract</c> enabler.
///
/// The per-family reverts (see <see cref="TextureReverts"/>) work by overwriting Conquest's texture
/// BYTES with base-game vanilla art at the same path. The PUBLIC release ships no such art (bundling
/// Anego Studios' textures would redistribute all-rights-reserved assets), so reverts are inert until
/// a player generates their OWN payload from files they already own. This does exactly that, in-game:
///
///   1. Reads which texture paths Conquest overrides, straight from the player's installed Conquest
///      pack (<c>ModLoader</c> tells us its source zip/folder - no path guessing).
///   2. For each, finds the matching VANILLA source in the player's own game install (discovered from
///      the loaded asset origins, so it is cross-platform), using the same resolver as the dev tool
///      build/extract-vanilla.py.
///   3. Writes those vanilla PNGs into a small side-car mod under the player's Mods folder:
///        Mods/conquesttweaks-vanilla/assets/conquesttweaks/textures/vanilla/&lt;family&gt;/&lt;rel&gt;
///      Assets register under the domain = the folder name below <c>assets/</c>, so this lands in the
///      <c>conquesttweaks</c> domain and <see cref="TextureReverts.PayloadPresent"/> finds it on the
///      next launch. Removing the folder cleanly disables reverts again.
///
/// Nothing is redistributed: the vanilla art comes from the player's own installed game, stays on
/// their own machine, and the side-car pack is generated locally. The stone/ore families (rock, sand,
/// gravel, cobblestone, drystone, ore) are intentionally NOT covered: Conquest wires those by
/// repointing the blocktype JSON to new connected-texture grid paths rather than overwriting bytes in
/// place, so the byte-swap revert cannot reach them (it would take a separate JSON-repatch mechanism).
/// </summary>
internal static class RevertExtractor
{
    // The side-car pack written under the user's Mods folder. Its assets live under the
    // "conquesttweaks" domain (the folder name below assets/), so the main mod's payload detection
    // - GetLocations("textures/vanilla/", "conquesttweaks") - finds them after a relaunch. The pack's
    // own modid differs from the main mod's so the two never collide in the mod loader.
    private const string PackDirName = "conquesttweaks-vanilla";
    private const string PackModId   = "conquesttweaksvanilla";

    // Family -> Conquest texture path prefixes (relative to textures/, i.e. "block/..."). MUST stay in
    // lockstep with Config.FamilyToggles()/SetFamily and build/extract-vanilla.py's FAMILIES. Only the
    // families Conquest overrides IN PLACE (same path, new bytes) are revertable this way; the
    // stone/ore families are deliberately absent (see the class summary).
    private static readonly (string Family, string[] Prefixes)[] Families =
    {
        ("soil",         new[] { "block/soil/fertility/" }),
        ("grasscover",   new[] { "block/plant/grasscoverage/" }),
        ("forestfloor",  new[] { "block/soil/forest/" }),
        ("peat",         new[] { "block/soil/peat/", "block/soil/peatpile/" }),
        ("clay",         new[] { "block/soil/clay/" }),
        ("farmland",     new[] { "block/soil/farmland/" }),
        ("cob",          new[] { "block/soil/cob/" }),
        ("rammedearth",  new[] { "block/soil/rammed/" }),
        ("mudbrick",     new[] { "block/soil/mudbrick/" }),
        ("stonepath",    new[] { "block/stone/path/" }),
        ("tallgrass",    new[] { "block/plant/tallgrass/" }),
        ("otherfoliage", new[] { "block/plant/fern/", "block/plant/ferntree/", "block/plant/flower/",
                                 "block/plant/herb/", "block/plant/reeds/", "block/plant/bamboo/",
                                 "block/plant/waterlily/" }),
    };

    // Vanilla domain subfolders under the game install's assets/ root.
    private static readonly string[] VanillaDomains = { "survival", "game", "creative" };

    // Structural remaps mirroring build/extract-vanilla.py's family_special().
    private static readonly Regex SoilRe      = new(@"^block/soil/fertility/([a-z]+)/\d+\.png$");
    private static readonly Regex GrassRe     = new(@"^block/plant/grasscoverage/([a-z]+)/\d+\.png$");
    private static readonly Regex StonePathRe = new(@"^block/stone/path/[a-z]+/(\d+)\.png$");
    private static readonly Regex PeatTopSide = new(@"^block/soil/peat/peat(top|side)\d*\.png$");
    private static readonly Regex ClayRe      = new(@"^block/soil/clay/(blue|fire|red)/\d+\.png$");
    private static readonly Regex ForestRe    = new(@"^block/soil/forest/forestsoil(\d)(\d)\.png$");
    private static readonly Regex FarmlandRe  = new(@"^block/soil/farmland/fert([a-z]+)-side\d*\.png$");
    private static readonly Regex TrailingDigits = new(@"\d+$");

    public sealed class Result
    {
        public bool Ok;
        public string Error = "";
        public int Applied;
        public int Unmapped;
        public int FamilyCount;
        public string OutputPath = "";
    }

    /// <summary>Generate the side-car payload. Returns a <see cref="Result"/>; never throws for the
    /// expected failure modes (missing Conquest source, no vanilla roots, a foreign folder already at
    /// the target) - those come back as <c>Ok = false</c> with a user-facing <c>Error</c>.</summary>
    public static Result Extract(ICoreAPI api)
    {
        var res = new Result();

        // 1) Where is the player's installed Conquest pack (zip or unpacked folder)?
        var conquest = api.ModLoader.Mods.FirstOrDefault(m => m.Info?.ModID == "conquest");
        string? conquestSource = conquest?.SourcePath;
        if (string.IsNullOrEmpty(conquestSource) || !PathExists(conquestSource))
        {
            res.Error = "Could not locate your installed Conquest pack. Is 'conquest' enabled?";
            return res;
        }

        // 2) Where are the player's own vanilla textures (base-game install assets root)?
        var vanillaRoots = FindVanillaRoots(api);
        if (vanillaRoots.Count == 0)
        {
            res.Error = "Could not find your game's base assets folder (survival/game/creative). "
                      + "This is unexpected - please report it.";
            return res;
        }

        // 3) Prepare the output folder. Refuse to touch a same-named folder we didn't create.
        string outDir = Path.Combine(GamePaths.DataPath, "Mods", PackDirName);
        string outBase = Path.Combine(outDir, "assets", "conquesttweaks", "textures", "vanilla");
        if (Directory.Exists(outDir))
        {
            if (!IsOurPack(outDir))
            {
                res.Error = $"A folder named '{PackDirName}' already exists in your Mods folder and "
                          + "was not created by this command. Remove it manually, then re-run.";
                return res;
            }
            try { Directory.Delete(outDir, recursive: true); }
            catch (Exception e)
            {
                res.Error = $"Could not clear the previous payload at {outDir}: {e.Message}";
                return res;
            }
        }

        // 4) Enumerate Conquest's game-domain texture overrides and copy each family's vanilla source.
        var perFamily = Families.ToDictionary(f => f.Family, _ => 0);
        try
        {
            foreach (string rel in EnumerateConquestGameTextures(conquestSource))
            {
                string? family = FamilyFor(rel);
                if (family == null) continue;

                string? src = ResolveVanilla(vanillaRoots, family, rel);
                if (src == null) { res.Unmapped++; continue; }

                string dst = Path.Combine(outBase, family,
                    rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, overwrite: true);
                perFamily[family]++;
                res.Applied++;
            }
        }
        catch (Exception e)
        {
            res.Error = $"Failed while reading Conquest or your game textures: {e.Message}";
            return res;
        }

        if (res.Applied == 0)
        {
            res.Error = "Found no revertable textures to extract - nothing was written. "
                      + "(Your Conquest version may differ from the one this was built against.)";
            // Leave no empty folder behind.
            try { if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true); } catch { }
            return res;
        }

        // 5) Write the side-car pack's modinfo so the game loads it as a content mod next launch.
        try
        {
            File.WriteAllText(Path.Combine(outDir, "modinfo.json"), PackModInfoJson());
        }
        catch (Exception e)
        {
            res.Error = $"Wrote the textures but could not write modinfo.json: {e.Message}";
            return res;
        }

        res.FamilyCount = perFamily.Count(kv => kv.Value > 0);
        res.OutputPath = outDir;
        res.Ok = true;
        api.Logger.Notification(
            "[conquesttweaks] Extracted {0} vanilla textures across {1} families to {2} ({3} Conquest paths had no vanilla source and were skipped).",
            res.Applied, res.FamilyCount, outDir, res.Unmapped);
        return res;
    }

    // ---------------------------------------------------------------- discovery

    private static bool PathExists(string p) => File.Exists(p) || Directory.Exists(p);

    /// <summary>The base-game assets roots (directories that contain survival/ + game/ + creative/).
    /// Discovered from the loaded asset origins so it works on any OS/install layout. An origin may be
    /// the assets/ root itself, or a per-domain folder (assets/game); both are normalized to the root.
    /// Mod origins (including Conquest's zip) don't hold that triad, so they're ignored here.</summary>
    private static List<string> FindVanillaRoots(ICoreAPI api)
    {
        var roots = new List<string>();
        void Add(string dir)
        {
            if (Directory.Exists(dir) && Directory.Exists(Path.Combine(dir, "game"))
                && Directory.Exists(Path.Combine(dir, "survival")) && !roots.Contains(dir))
                roots.Add(dir);
        }

        foreach (var origin in api.Assets.Origins)
        {
            string? op = origin?.OriginPath;
            if (string.IsNullOrEmpty(op)) continue;
            op = op.TrimEnd(Path.DirectorySeparatorChar, '/');
            Add(op);                                              // origin is the assets/ root
            string? name = Path.GetFileName(op);
            if (name != null && VanillaDomains.Contains(name))    // origin is assets/<domain>
                Add(Path.GetDirectoryName(op) ?? op);
        }
        return roots;
    }

    private static bool IsOurPack(string dir)
    {
        try
        {
            string info = Path.Combine(dir, "modinfo.json");
            return File.Exists(info) && File.ReadAllText(info).Contains("\"" + PackModId + "\"");
        }
        catch { return false; }
    }

    /// <summary>Yield the "block/..."-relative path of every PNG Conquest overrides in the game domain,
    /// reading from its zip or unpacked folder.</summary>
    private static IEnumerable<string> EnumerateConquestGameTextures(string source)
    {
        const string prefix = "assets/game/textures/";
        if (Directory.Exists(source))
        {
            string texRoot = Path.Combine(source, "assets", "game", "textures");
            if (!Directory.Exists(texRoot)) yield break;
            foreach (string full in Directory.EnumerateFiles(texRoot, "*.png", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(texRoot, full).Replace(Path.DirectorySeparatorChar, '/');
                yield return rel;
            }
        }
        else // a .zip (or any archive the mod loader accepted)
        {
            using var za = ZipFile.OpenRead(source);
            foreach (var entry in za.Entries)
            {
                if (entry.FullName.Length == 0 || entry.FullName.EndsWith("/")) continue;
                string name = entry.FullName.Replace('\\', '/');
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                yield return name.Substring(prefix.Length);
            }
        }
    }

    private static string? FamilyFor(string rel)
    {
        foreach (var (family, prefixes) in Families)
            foreach (var pre in prefixes)
                if (rel.StartsWith(pre, StringComparison.Ordinal))
                    return family;
        return null;
    }

    // ---------------------------------------------------------------- vanilla resolver (mirrors py)

    private static string? FindVanilla(List<string> roots, string rel)
    {
        string relOs = rel.Replace('/', Path.DirectorySeparatorChar);
        foreach (string root in roots)
            foreach (string domain in VanillaDomains)
            {
                string p = Path.Combine(root, domain, "textures", relOs);
                if (File.Exists(p)) return p;
            }
        return null;
    }

    private static string? FamilySpecial(List<string> roots, string family, string rel)
    {
        Match m;
        switch (family)
        {
            case "soil":
                m = SoilRe.Match(rel);
                if (m.Success) return FindVanilla(roots, $"block/soil/fert{m.Groups[1].Value}.png");
                break;
            case "grasscover":
                m = GrassRe.Match(rel);
                if (m.Success) return FindVanilla(roots, $"block/plant/grasscoverage/{m.Groups[1].Value}.png");
                break;
            case "stonepath":
                m = StonePathRe.Match(rel);
                if (m.Success)
                    return FindVanilla(roots, $"block/stone/path/normal{m.Groups[1].Value}.png")
                        ?? FindVanilla(roots, "block/stone/path/normal1.png");
                break;
            case "peat":
                if (PeatTopSide.IsMatch(rel)) return FindVanilla(roots, "block/soil/peat.png");
                if (rel.StartsWith("block/soil/peatpile/", StringComparison.Ordinal))
                    return FindVanilla(roots, "block/soil/peatpile/sides.png");
                break;
            case "clay":
                m = ClayRe.Match(rel);
                if (m.Success) return FindVanilla(roots, $"block/soil/{m.Groups[1].Value}clay.png");
                break;
            case "forestfloor":
                // Conquest added forestsoil6x-8x; vanilla only ships groups 1..5. Fold onto 1..5.
                m = ForestRe.Match(rel);
                if (m.Success)
                {
                    int grp = ((int.Parse(m.Groups[1].Value) - 1) % 5) + 1;
                    return FindVanilla(roots, $"block/soil/forest/forestsoil{grp}{m.Groups[2].Value}.png")
                        ?? FindVanilla(roots, "block/soil/forest/forestsoil11.png");
                }
                break;
            case "farmland":
                // Farmland sides are just the dirt side in vanilla -> the soil fertility texture.
                m = FarmlandRe.Match(rel);
                if (m.Success) return FindVanilla(roots, $"block/soil/fert{m.Groups[1].Value}.png");
                break;
        }
        return null;
    }

    /// <summary>Fallback vanilla rels in priority order (numeric-variant collapse, then ancestor
    /// walk) - mirrors build/extract-vanilla.py's generic_candidates().</summary>
    private static IEnumerable<string> GenericCandidates(string rel)
    {
        string stem = rel.EndsWith(".png") ? rel[..^4] : rel;
        var parts = stem.Split('/');

        string last = parts[^1];
        string baseLast = TrailingDigits.Replace(last, "");
        if (baseLast.Length > 0 && baseLast != last)
            foreach (string v in new[] { baseLast, baseLast + "1" })
                yield return string.Join('/', parts[..^1].Append(v)) + ".png";

        for (int i = parts.Length - 1; i >= 1; i--)
        {
            var anc = parts[..i];
            yield return string.Join('/', anc) + ".png";
            yield return string.Join('/', anc) + "1.png";
            string b = TrailingDigits.Replace(anc[^1], "");
            if (b.Length > 0 && b != anc[^1])
                yield return string.Join('/', anc[..^1].Append(b)) + ".png";
        }
    }

    private static string? ResolveVanilla(List<string> roots, string family, string rel)
    {
        return FindVanilla(roots, rel)                       // 1) exact same path
            ?? FamilySpecial(roots, family, rel)             // 2) family-specific structural remap
            ?? GenericCandidates(rel)                        // 3) generic reductions
                .Select(c => FindVanilla(roots, c))
                .FirstOrDefault(hit => hit != null);
    }

    // ---------------------------------------------------------------- side-car modinfo

    private static string PackModInfoJson() =>
        "{\n" +
        "\t\"type\": \"content\",\n" +
        "\t\"modid\": \"" + PackModId + "\",\n" +
        "\t\"name\": \"Conquest Tweaks - vanilla revert payload\",\n" +
        "\t\"description\": \"Auto-generated by the '.ctc reverts extract' command from your own game files. " +
        "Base-game Vintage Story textures (c) Anego Studios, used locally by Conquest Tweaks' per-family reverts. " +
        "Personal use only - do not redistribute. Delete this folder to disable reverts.\",\n" +
        "\t\"authors\": [\"generated by conquesttweaks\"],\n" +
        "\t\"version\": \"1.0.0\",\n" +
        "\t\"side\": \"client\",\n" +
        "\t\"dependencies\": { \"conquesttweaks\": \"\" }\n" +
        "}\n";
}
