using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace ConquestTweaks;

/// <summary>
/// Thin orchestrator for the Conquest VS Tweaks &amp; Compatibility umbrella. Loads config, registers
/// the <c>.ctc</c> commands, and dispatches to the four independent feature groups. It holds no
/// feature logic itself - each group lives in its own folder so a source-mod author can read (and
/// fold in) exactly their slice (see docs/HANDOFF-*.md and CLAUDE.md):
///
///   GROUP 1  Conquest base copying    - none. We copy no Conquest art. The only bundled art is
///                                        base-game vanilla texture bytes (group 4's revert payload),
///                                        owned by Anego Studios (see CREDITS.md).
///   GROUP 2  ore-pack JSON compat      - Visible Ores &amp; Minerals (+ Juicy Ores). Pure JSON patches
///                                        under assets/.../patches/compatibility/&lt;modid&gt;/; no C#. They
///                                        self-gate via the patch's own `dependsOn` and are listed in
///                                        CompatFixes only so `.ctc list` can report them.
///   GROUP 3  Terrain Slabs Harmony fix - src/Compat/TerrainSlabs/. One transpiler, config-gated.
///   GROUP 4  standalone reverts/tweaks - src/Core/. The mod's own features; fold into nobody.
///
/// The C# runs client-only (<see cref="ShouldLoad"/>); the mod is packaged side=Universal only so the
/// group-2 ore JSON patches can apply server-side where blocktype JSON is resolved. This class does
/// no server work. Texture/tint edits bake into the atlas at load, so config / command changes take
/// effect on relog.
/// </summary>
public class ConquestTweaksModSystem : ModSystem
{
    private const string ConfigFile = "conquesttweaks.json";

    private ICoreAPI api = null!;
    private ICoreClientAPI capi = null!;
    private Config config = new();

    // Set in StartPre from Mod.Info.ModID; used as the Harmony instance id and the log prefix base.
    private string modId = "conquesttweaks";

    // Single Harmony instance, created lazily only when at least one C#/Harmony compat fix
    // activates (see StartClientSide). Null when no such fix is active; unpatched in Dispose.
    private Harmony? harmony;

    // GROUP 4 diagnostic scanner, created client-side.
    private PlaceholderScanner scanner = null!;

    // ---------------------------------------------------------------- optional-mod compat registry
    //
    // The always-on core (reverts + vibrancy, GROUP 4) is NOT here - only optional per-mod fixes that
    // activate when their target mod is detected. GROUP-2 JSON-patch fixes self-gate via the patch's
    // own `dependsOn` and are listed only so `.ctc list` can report them; GROUP-3 Harmony fixes carry
    // a config toggle + a [HarmonyPatchCategory] name and are applied in StartClientSide when their
    // target mod is present.
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
            DisplayName     = "Terrain Slabs connected textures (JSON-drawtype slabs)",
            TargetModId     = "terrainslabs",          // hard-deps placeonslabs, so this implies both
            Mechanism       = CompatMechanism.Harmony,
            HarmonyCategory = "terrainslabs-connected-textures",
            ConfigEnabled   = cfg => cfg.EnableSlabsFix,
        },
        new CompatFix
        {
            DisplayName = "Terrain Slabs grass-slab connected textures, TopSoil (base + overlay)",
            TargetModId = "terrainslabs",
            // JSON patch: repairs Conquest's own terrainslabs/{soil,clay}.json. Their grass-slab
            // base `all` and grass `specialSecondTexture` were degraded to bare `/*` wildcards (no
            // tiles/tilesWidth), so neither the dirt body nor the grass top could connect. Our patch
            // restores the full-block connected form on both layers (soil base per-fertility via a
            // nested allByType; clay overlay via tilesWidthByType to dodge the none-tile void).
            // Self-gates via the patch's dependsOn (conquest + terrainslabs); listed only for
            // `.ctc list`. See assets/.../patches/compatibility/terrainslabs/{soil,clay}.json.
            Mechanism   = CompatMechanism.JsonPatch,
        },
        new CompatFix
        {
            DisplayName     = "Connected-texture selector clamp (black-void guard)",
            TargetModId     = "conquest",          // hard dep => always active in the pack context
            Mechanism       = CompatMechanism.Harmony,
            HarmonyCategory = "tiled-selector-clamp",
            ConfigEnabled   = cfg => cfg.EnableTiledSelectorClamp,
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

    // IMPORTANT: hook AssetsLoaded, NOT AssetsFinalize. On a CLIENT (this is a client-only mod), the
    // engine runs AssetsFinalize from OnLevelFinalize - i.e. AFTER the block texture atlas has been
    // composed - so byte edits there are baked from the ORIGINAL bytes and do nothing. AssetsLoaded
    // runs after assets are loaded AND patched but BEFORE the atlas is composed, so our edits stick.
    // (See src/Core/TextureReverts.cs and src/Core/TintVibrancy.cs for the full rationale.)
    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Client) return;
        TextureReverts.Apply(api, config);   // GROUP 4
        TintVibrancy.Apply(api, config);     // GROUP 4
        // GROUP 2 ore repair (VOM / Juicy Ores) is delivered as JSON patches under
        // assets/conquesttweaks/patches/compatibility/<modid>/, not from C# (blocktype JSON is not
        // retrievable via api.Assets here). See src/Compat/README.md.
    }

    public override void StartClientSide(ICoreClientAPI capi)
    {
        this.capi = capi;
        scanner = new PlaceholderScanner(capi);   // GROUP 4
        if (config.ReportMissingTexturesOnLoad)
            capi.Event.LevelFinalize += () => scanner.Scan(logToConsole: false);

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
            .EndSubCommand()
            .BeginSubCommand("drawtype")
                .WithDescription("Diagnostic: group blocks whose code contains <pattern> by their render signature (drawtype/renderpass/shape/overlay-tiles)")
                .WithArgs(parsers.Word("pattern"))
                .HandleWith(OnDrawType)
            .EndSubCommand()
            .BeginSubCommand("slabfix")
                .WithDescription("Toggle the Terrain Slabs doMesh transpiler (GROUP 3): .ctc slabfix [on|off]; no arg reports current state. Relog to apply.")
                .WithArgs(parsers.OptionalWordRange("state", "on", "off"))
                .HandleWith(OnSlabFix)
            .EndSubCommand();

        ActivateHarmonyCompat(capi);   // GROUP 3
    }

    // ---------------------------------------------------------------- optional-mod compat activation

    /// <summary>Apply each Harmony-mechanism compat fix (GROUP 3) whose config toggle is on AND whose
    /// target mod is detected. The Harmony instance is created lazily on the first active fix, so no
    /// Harmony object exists when nothing activates. GROUP-2 JSON-patch fixes are skipped here - they
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
        var (scanned, broken, reportPath) = scanner.Scan(logToConsole: true);
        if (broken == 0)
            return TextCommandResult.Success($"Scanned {scanned} blocks - no placeholder/missing textures found.");
        return TextCommandResult.Success(
            $"Scanned {scanned} blocks - {broken} resolve to the placeholder. Full list written to:\n{reportPath}");
    }

    /// <summary>Read-only diagnostic. For every loaded block whose short code contains
    /// <c>&lt;pattern&gt;</c>, computes a "render signature" - the fields that decide which engine
    /// tesselator runs and whether connected/tiled selection can apply: resolved <c>DrawType</c> and
    /// <c>RenderPass</c>, the JSON shape base, the <c>HasTiles</c>/<c>HasAlternates</c> flags, and the
    /// number of baked tiles on the <c>specialSecondTexture</c> (grass) overlay. Blocks are grouped by
    /// identical signature so a family of variants collapses to one line. This is how we confirm, in a
    /// live Conquest + Terrain Slabs stack, whether grass-covered slabs actually reach TopsoilTesselator
    /// (DrawType TopSoil) and carry a tiled overlay (2ndTiles &gt; 1) - which dictates the fix's patch
    /// point. Writes nothing and changes nothing.</summary>
    private TextCommandResult OnDrawType(TextCommandCallingArgs args)
    {
        string pattern = ((string)args[0]).ToLowerInvariant();
        // signature -> (count, first matching code seen)
        var groups = new System.Collections.Generic.Dictionary<string, (int Count, string Example)>();
        int matched = 0;

        foreach (var block in capi.World.Blocks)
        {
            if (block?.Code == null) continue;
            string code = block.Code.ToShortString();
            if (code.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) < 0) continue;
            matched++;

            string shapeBase = block.Shape?.Base?.ToShortString() ?? "(none)";

            var secondBaked =
                block.Textures != null
                && block.Textures.TryGetValue("specialSecondTexture", out var ct)
                    ? ct?.Baked
                    : null;
            bool hasSecond = secondBaked != null;
            int secondTiles = secondBaked?.BakedTiles?.Length ?? 0;

            // Base (dirt-body) tiled-tile count on the top face. This is the layer the soil/clay base
            // fix targets: a connected base bakes N tiles here (e.g. low/verylow soil = 9, high = 16),
            // while a bare `/*` alternate base bakes 0. Comparing baseTiles on a full grass block vs.
            // its slab tells us whether the base actually connects on the slab, or only the overlay
            // does (the open "under-grass base shows one tile" question).
            var baseBaked =
                block.Textures != null
                && block.Textures.TryGetValue(BlockFacing.UP.Code, out var upct)
                    ? upct?.Baked
                    : null;
            int baseTiles = baseBaked?.BakedTiles?.Length ?? 0;
            // Alternate-texture (BakedVariants) count on the base. This is the layer that actually
            // produces VARIATION on the JSON/doMesh render path (grassless `-none` slabs): doMesh
            // swaps whole pre-baked meshes and those meshes only differ when the texture has
            // BakedVariants (a `/*` alternate base). A tiles-only base (baseTiles>0, baseVariants=0)
            // bakes identical meshes on JSON and renders UNIFORM - which is why a connected `-none`
            // soil slab shows a single texture while an alternate one varies. On the TopSoil path
            // (grass-covered variants) baseTiles connect fine; this column only matters for JSON.
            int baseVariants = baseBaked?.BakedVariants?.Length ?? 0;

            string sig = $"draw={block.DrawType} pass={block.RenderPass} shape={shapeBase} "
                       + $"hasTiles={block.HasTiles} hasAlt={block.HasAlternates} "
                       + $"baseTiles={baseTiles} baseVars={baseVariants} "
                       + $"2ndTex={(hasSecond ? "yes" : "no")} 2ndTiles={secondTiles}";

            if (groups.TryGetValue(sig, out var g))
                groups[sig] = (g.Count + 1, g.Example);
            else
                groups[sig] = (1, code);
        }

        if (matched == 0)
            return TextCommandResult.Success($"No loaded blocks match '{pattern}'.");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Conquest VS Tweaks - render signatures for '{pattern}'");
        sb.AppendLine($"{matched} blocks, {groups.Count} distinct signatures:");
        sb.AppendLine();
        foreach (var kv in groups)
            sb.AppendLine($"  [{kv.Value.Count,4}x] {kv.Key}\n           e.g. {kv.Value.Example}");

        // Chat output can't be copied out of the game, so write the full report to a file the user
        // can open, mirroring .ctc scan. Also log one line to the client log for good measure.
        string report = sb.ToString().TrimEnd();
        string reportPath = System.IO.Path.Combine(
            Vintagestory.API.Config.GamePaths.DataPath, "ModConfig", $"ctc-drawtype-{pattern}.txt");
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(reportPath)!);
            System.IO.File.WriteAllText(reportPath, report + "\n");
        }
        catch (Exception e)
        {
            capi.Logger.Warning("[{0}] could not write drawtype report: {1}", modId, e.Message);
            reportPath = "(could not write report; see below)";
        }
        capi.Logger.Notification("[{0}] drawtype '{1}': {2} blocks, {3} signatures. Report: {4}",
            modId, pattern, matched, groups.Count, reportPath);

        return TextCommandResult.Success($"{report}\n\nFull report written to:\n{reportPath}");
    }

    private TextCommandResult OnVibrancy(TextCommandCallingArgs args)
    {
        double v = (double)args[0];
        config.GrassGreenSaturation = (float)v;
        config.GrassVibrancy = true;
        api.StoreModConfig(config, ConfigFile);
        return TextCommandResult.Success($"Grass green saturation = {v:0.00} (vibrancy on). Relog to apply.");
    }

    /// <summary>Toggle <see cref="Config.EnableSlabsFix"/> (the GROUP-3 Terrain Slabs doMesh transpiler)
    /// from chat, so an on/off A-B comparison doesn't need a hand-edit of the config JSON. No arg reports
    /// the current state. Like every other <c>.ctc</c> setter this only writes config; the transpiler is
    /// applied once in <see cref="StartClientSide"/> (Harmony patches the compiled tesselator at load and
    /// chunk meshes are baked from it), so the change takes effect on relog - it is NOT live-patched.</summary>
    private TextCommandResult OnSlabFix(TextCommandCallingArgs args)
    {
        string? state = args[0] as string;
        if (string.IsNullOrEmpty(state))
            return TextCommandResult.Success(
                $"Terrain Slabs doMesh transpiler (EnableSlabsFix) is {(config.EnableSlabsFix ? "on" : "off")}. " +
                "Use .ctc slabfix on|off (relog to apply).");

        bool on = state == "on";
        config.EnableSlabsFix = on;
        api.StoreModConfig(config, ConfigFile);
        return TextCommandResult.Success($"Terrain Slabs doMesh transpiler → {(on ? "on" : "off")} (relog to apply).");
    }
}
