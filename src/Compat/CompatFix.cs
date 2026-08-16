using System;
using Vintagestory.API.Client;

namespace ConquestTweaks;

/// <summary>How an optional-mod compatibility fix is delivered.</summary>
public enum CompatMechanism
{
    /// <summary>Delivered as a JSON patch under assets/.../patches/ that self-gates via
    /// <c>dependsOn</c> on its target mod. Needs no C# and no config toggle (a JSON patch can't
    /// read config), and applies server-side where blocktype JSON is resolved. Example: the
    /// Visible Ores &amp; Minerals ore-vein fix.</summary>
    JsonPatch,

    /// <summary>Delivered as a Harmony patch applied at client startup, gated at runtime by
    /// <see cref="ICoreAPI"/> mod detection AND a config toggle. Used for fixes that need engine
    /// or render behavior a JSON patch can't express. Example: the Terrain Slabs connected-texture
    /// fix.</summary>
    Harmony,
}

/// <summary>
/// Describes one optional-mod compatibility fix in the umbrella. The always-on core (texture
/// reverts + vibrancy) is NOT a compat fix - only per-mod fixes that activate when their target
/// mod is present are registered here.
///
/// A <see cref="JsonPatch"/> fix is informational only (the patch itself self-gates via
/// <c>dependsOn</c>); it appears here so <c>.ctc list</c> can report it. A <see cref="Harmony"/>
/// fix carries the config gate (<see cref="ConfigEnabled"/>) and the Harmony patch category the
/// ModSystem applies when the fix activates.
/// </summary>
public sealed class CompatFix
{
    /// <summary>Human-readable name shown in <c>.ctc list</c> and startup logs.</summary>
    public required string DisplayName { get; init; }

    /// <summary>The modid this fix targets, checked with <c>api.ModLoader.IsModEnabled</c>.</summary>
    public required string TargetModId { get; init; }

    /// <summary>How the fix is delivered.</summary>
    public required CompatMechanism Mechanism { get; init; }

    /// <summary>Config gate for <see cref="CompatMechanism.Harmony"/> fixes. <c>null</c> means the
    /// fix has no toggle (all <see cref="CompatMechanism.JsonPatch"/> fixes).</summary>
    public Func<Config, bool>? ConfigEnabled { get; init; }

    /// <summary>For <see cref="CompatMechanism.Harmony"/> fixes: the <c>[HarmonyPatchCategory]</c>
    /// name applied via <c>harmony.PatchCategory(...)</c> when the fix activates.</summary>
    public string? HarmonyCategory { get; init; }

    /// <summary>Whether the fix is enabled in config. Fixes with no toggle are always enabled.</summary>
    public bool IsEnabledInConfig(Config cfg) => ConfigEnabled == null || ConfigEnabled(cfg);
}
