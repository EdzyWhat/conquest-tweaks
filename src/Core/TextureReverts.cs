using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace ConquestTweaks;

/// <summary>
/// GROUP 4 (standalone core, folds into nobody). Per-family vanilla texture reverts.
///
/// For each enabled family we overwrite Conquest's texture BYTES in-memory (in the ModSystem's
/// AssetsLoaded hook - after assets are loaded/patched but BEFORE the block texture atlas is
/// composed) with the bundled vanilla source art we ship under our own domain. Conquest's extra
/// tiled variants then collapse onto the single vanilla texture they map to =&gt; vanilla look.
///
/// Anti-placeholder guarantee: we only overwrite a Conquest path for which a real vanilla source
/// was bundled AND that already exists in the loaded assets, so we never introduce the pink/black
/// "unknown" placeholder. Editing no blocktype JSON, this pass is load-order-independent.
///
/// This is the mod's own original feature; it depends on none of the optional target mods and is
/// not part of any handoff.
/// </summary>
internal static class TextureReverts
{
    private const string SourcePrefix = "textures/vanilla/";   // under our own (conquesttweaks) domain

    public static void Apply(ICoreAPI api, Config config)
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
}
