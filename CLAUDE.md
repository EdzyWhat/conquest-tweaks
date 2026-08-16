# conquest-tweaks

C#/.NET Vintage Story mod (modid `conquesttweaks`, display name **"Conquest VS Tweaks &
Compatibility"**) that layers over the **Conquest VS Edition** texture pack (modid `conquest`, a hard
dep). It's a **Conquest compatibility umbrella**: an always-on core (per-family vanilla texture
reverts + a green-selective grass-tint vibrancy dial) plus **optional per-mod compatibility fixes
that activate only when their target mod is detected**. Packaged `side: Universal` so its VOM ore
JSON patches apply server-side, but the **C# runtime is client-only** (`ShouldLoad` gates to client)
— all visual work is client-side. Independent of the sibling VS mods in `~/claude` — treat as its
own project.

**NB — internal identity is frozen, display name is not.** The modid `conquesttweaks` is
load-bearing (asset domain `assets/conquesttweaks/`, config file `conquesttweaks.json`, the
`.ctc` command, the `ConquestTweaks` assembly/namespace, handbook `pageCode`). Only the
user-facing *display* name/description was re-scoped to the umbrella framing; never rename the modid
(it would wipe every user's config and break the asset domain).

## Compatibility model (two mechanisms)
Only `conquest` (+`game`) are hard deps; optional target mods are **soft** and never in `modinfo.json`
`dependencies`. Each optional fix activates only when its target mod is present, via one of:
- **JSON-patch compat** (e.g. Visible Ores & Minerals): a patch under `assets/.../patches/` that
  self-gates with `dependsOn: [{ modid: … }]`. No C#, no config toggle (a JSON patch can't read
  config), and it applies server-side where blocktype JSON is resolved.
- **C#/Harmony compat** (e.g. the Terrain Slabs connected-texture fix): a Harmony patch applied at
  client startup, gated at runtime by `capi.ModLoader.IsModEnabled(targetModId)` **and** a `Config`
  toggle. The ModSystem owns one `Harmony(modId)` instance created **lazily** only when a fix
  activates, applies each fix as its own `[HarmonyPatchCategory]` via `harmony.PatchCategory(…)`, and
  `UnpatchAll(modId)` in `Dispose`. Harmony is **game-bundled** (`Lib/0Harmony.dll`) — referenced,
  never shipped.

Optional fixes are declared in the `CompatFixes` registry (`ConquestTweaksModSystem.cs`) as
`CompatFix` descriptors (`src/Compat/CompatFix.cs`); `.ctc list` reports each fix's target, whether
it's detected, its mechanism, and (Harmony fixes) its enabled state. `ActivateHarmonyCompat` wraps
`PatchCategory` in try/catch so a version-drift patch failure deactivates just that fix (warning
logged) instead of crashing the client.

### Terrain Slabs connected-textures fix (`src/Compat/TerrainSlabs/SlabConnectedTexturesPatch.cs`)
Category `terrainslabs-connected-textures`, target `terrainslabs`, toggle `Config.EnableSlabsFix`
(default on). Connected/tiled textures only work on cube-drawtype blocks: `CubeTesselator.Tesselate`
picks each face's tile via `BakedCompositeTexture.GetTiledTexturesSelector(tiles, side, x, y, z)`.
Slabs draw as `EnumDrawType.JSON` → `JsonTesselator.doMesh`. **The per-tile alternate meshes already
exist for JSON blocks** (`ShapeTesselatorManager.Tesselate` builds `altblockModelDatas[blockId]`,
one mesh per tile, for any `HasTiles` block regardless of draw type) — `doMesh` just selects among
them with `GameMath.MurmurHash3Mod(x,y,z,len)` (**random**) instead of the position selector.
`CreateFastTextureAlternates`'s `|| DrawType==JSON` early-return only skips `FastTextureVariants`
(unused by the JSON path), so it's irrelevant; `UpdateVariant` needs no patch (it ran per-tile at
bake time). **The fix is ONE transpiler on `doMesh`**: redirect the FIRST `MurmurHash3Mod` result
(NOT the second — that one's for random rotations) through `CorrectTileIndex`, which returns
`GameMath.Mod(GetTiledTexturesSelector(bakedTiles, UP, x,y,z), array.Length)` for tiled blocks and
the original index otherwise. One tile index per whole mesh → correct on the top face, imperfect on
thin edge faces (accepted). Needs in-game validation (patches internal `Vintagestory.Client.NoObf`).

## Layout — four feature groups (folder boundary = handoff boundary)
The source is foldered so a source-mod author can read/adopt exactly their slice (mirrors the
`libgui-toolsmith-sharpness` handoff model; see `docs/HANDOFF-*.md`, `CONTRIBUTING.md`, and the
per-group table in `README.md`). `ConquestTweaksModSystem.cs` is a **thin orchestrator** — config
load, `.ctc` commands, compat dispatch — holding no feature logic.
- **Group 1 (Conquest base copying): none.** We copy no Conquest art. The only bundled art is
  base-game vanilla (group 4's payload), owned by Anego Studios (`CREDITS.md`). All four target mods
  (Conquest, VOM, Terrain Slabs, Juicy Ores) state no license → treated as all-rights-reserved; we
  reproduce/derive none of their assets.
- **Group 4 (standalone core) — `src/Core/`:** `TextureReverts.cs`, `TintVibrancy.cs`,
  `PlaceholderScanner.cs`. The mod's own features; fold into nobody.
- **Group 3 (Terrain Slabs Harmony fix) — `src/Compat/TerrainSlabs/`:** ports to Terrain Slabs
  unchanged (`docs/HANDOFF-terrainslabs.md`). **Coverage (in-game, Conquest 1.0.7):** rock/gravel/sand
  slabs (JSON draw) get correct connected textures; grass-covered soil/peat/clay slabs do NOT — their
  grassy top renders on the `TopSoil` renderpass, which the `doMesh` transpiler doesn't hook.
- **Group 2 (ore-pack JSON compat) — `src/assets/conquesttweaks/patches/compatibility/<modid>/`:**
  mirrors Conquest's own `patches/compatibility/<modid>/` convention (`docs/HANDOFF-vom.md`,
  `docs/HANDOFF-conquest.md`). Only `visibleoresandminerals/` ships. **Juicy Ores is intentionally
  NOT patched:** Conquest has shipped working Juicy Ores compat since 2026-01-15 (v1.0.7) — a patch
  here would be redundant and risk conflicting with Conquest's index-based meta-patch.
- `src/Compat/CompatFix.cs` — shared registry descriptor; `src/Compat/README.md` maps the two compat
  mechanisms.
- `src/` also holds `Mod.csproj`, `modinfo.json`, `Config.cs`, and `assets/conquesttweaks/`:
  - `textures/vanilla/<family>/…` — bundled vanilla revert art (generated, git-ignored).
  - `patches/compatibility/visibleoresandminerals/ore-*.json` — the optional VOM ore-vein fix (see below).
  - `config/handbook/00-conquesttweaks.json` + `lang/en.json` — an in-game Survival Handbook
    **Guides** page (`pageCode: conquesttweaks-guide`) documenting the `.ctc` commands, the
    revertable families, and the vibrancy dial. Guide pages are discovered from any domain's
    `config/handbook/*.json`; `title`/`text` are domain-qualified lang keys resolved to VTML rich
    text (`<strong>`/`<br>`/`<a href="handbook://…">`). VTML passes literal `&` and non-tag text
    through, so we do NOT HTML-escape (no `&amp;`/`&lt;`); command placeholders are written `[name]`
    / `[0..1]` rather than `<name>` so the tokenizer doesn't treat them as tags.
- `build/extract-vanilla.py` — dev tool; regenerates the bundled vanilla texture payload from the
  local game install by resolving each Conquest texture in a family to its vanilla source.
- `build/restage.sh` — build + stage as an unpacked mod folder in `VintagestoryData/Mods/`.
- `Directory.Build.props` — net10.0, `VintageStoryPath` from `$VINTAGE_STORY` else the default app.

## Mechanism (why it's the way it is)
- **Revert = in-memory byte override**, done in **`AssetsLoaded`** (client only) — NOT
  `AssetsFinalize`. This is the crux: this is a **client-only** mod, and on the client the engine
  runs the `AssetsFinalize` mod phase from `OnLevelFinalize`, i.e. **AFTER the block texture atlas
  is already composed** (verified in `VintagestoryLib`: `AfterAssetsLoaded` → `OnAssetsLoaded`
  [`AssetsLoaded` phase] → `CreateNewAtlas`/`ComposeTextureAtlasses_StageA/B/C`; then much later
  `OnLevelFinalize` → `AssetsFinalize` phase). Editing texture/colormap **bytes** in
  `AssetsFinalize` is a silent no-op on the client — the atlas bakes the ORIGINAL bytes. (This was
  the long-standing "reverts & vibrancy do nothing" bug: the log showed the edits running ~3s
  *after* `Composed 4 … blocks texture atlases`.) `AssetsLoaded` runs after assets are loaded AND
  patched but BEFORE the atlas is composed, so our byte edits are picked up. On the *server*
  `AssetsFinalize` genuinely runs before block loading — but that side never applies here.
  For each enabled family, overwrite `game:textures/<rel>` `.Data` with our bundled vanilla bytes.
  No blocktype JSON editing → load-order-independent, and families toggle independently. Conquest's
  extra tiled variants collapse onto the one vanilla source = vanilla look.
- **Base content lives in the `game` domain** (despite on-disk `survival/`/`creative/` folders), and
  Conquest ships its overrides under `assets/game/textures/…` — so both source discovery
  (extract-vanilla.py) and the runtime target use the `game` domain.
- **Anti-placeholder guarantee:** we only overwrite a path when (a) a real vanilla source was bundled
  for it and (b) `api.Assets.TryGet(target) != null`. We never `Add` a missing asset, so we can't
  introduce the pink/black `unknown` texture.
- **Vibrancy** = green-selective HSL saturation cut on the plant-tint colormap texture(s) via
  SkiaSharp decode → per-pixel `ToHsl`/`FromHsl` weighted by a hue band (center/range/falloff) →
  re-encode PNG → write back to `asset.Data`.
  - **THE DOMINANT TERM IS THE CLIMATE PLANT TINT, NOT THE SEASON GRASS TINT.** The engine tints
    plants in `ClientWorldMap.ApplyColorMapOnRgba`: `num = climatePlantTint pixel;
    num = ColorOverlay(num, seasonalGrass pixel, weight); final = textureColor * num`. The climate
    tint (`environment/planttint.png`) is the base; the season tint is only overlaid on top. So
    desaturating **only** grasstint is nearly invisible — this was the "vibrancy does nothing" bug.
    We now desaturate `planttint.png` by default (config `SeasonGrassTintOnly=false`) *and* the
    season grasstint. `climatePlantTint` is shared by grass, ferns, bushes, reeds AND tree leaves
    (17 blocktypes), so this dial tones down all foliage green together — there is no colormap-only
    way to knock down grass while sparing leaves.
  - **DOMAIN GOTCHA:** `seasonalGrass` is defined in `survival/config/colormaps.json` with an
    *unqualified* `base: "environment/seasons/grasstint"` → resolves to
    **`survival:`**`textures/…/grasstint.png` (Conquest also ships a `game:` copy the colormap never
    reads). We overwrite grasstint in **every** domain it exists (`survival`, then `game`).
    `climatePlantTint` is defined in `game/config/colormaps.json` → genuinely
    `game:textures/environment/planttint.png`. So "base content = game domain" holds for block
    textures but NOT for the survival-domain colormap configs. Colormap load path verified in
    `VintagestoryLib` (`ClientWorldMap.LoadColorMaps` reads via `AssetManager.Get` and bakes each
    colormap into the block atlas via `GetOrAddTextureLocation` — so our **`AssetsLoaded`** byte-edit
    is picked up because it precedes atlas composition; seasonal tints render on the GPU by sampling
    that atlas region at the `RectIndex` the colormap was assigned).
  - **Empirical color note (measured 2026-08):** Conquest's tint colormaps are *less* saturated
    than vanilla's (planttint greenest-pixel sat ≈0.44 vs vanilla 0.96; grasstint ≈0.62 vs 1.00),
    and the grass-blade textures (`plant/tallgrass/free/*`) are pure grayscale in both — so on-screen
    grass green comes ENTIRELY from the tint colormap, and Conquest is *not* the source of "too
    green" (it already tones the base-game tint down). Reverting plant textures to vanilla would make
    foliage *more* saturated, not less. The vibrancy dial (tint desaturation) is the correct lever;
    it only ever appeared broken because of the `AssetsFinalize`-too-late bug above.
- **Not live:** textures + tint are baked into the atlas at load. Config/command changes apply on
  relog. `.ctc list|set|vibrancy` edit + persist config via `StoreModConfig`.

## Coverage status (per extract-vanilla.py)
Full (0 unmapped): soil, grasscover, forestfloor, peat, clay, farmland, cob, rammedearth.
Near-full: mudbrick (1), stonepath (1), tallgrass (2 = Conquest's own typo'd filenames).
**gravels was intentionally dropped** (2026-08, user's call — they don't want gravel reverted); it
was the largest family (325 files) and is no longer in FAMILIES, Config, or the bundle.
**Config defaults (2026-08, = the release config):** ground families `soil`, `grasscover`,
`forestfloor`, `clay`, `farmland`, `stonepath` default ON/vanilla; the earthy building materials
`peat`, `cob`, `rammedearth`, `mudbrick` default OFF/conquest. Foliage (`tallgrass`, `otherfoliage`)
defaults OFF/conquest. Vibrancy on, green sat 0.8.
Partial: **otherfoliage (216 unmapped)** — Conquest renamed vanilla's named fern/flower variants
(`tall`,`short`,`center1`…) to numeric ones; no clean mapping. Defaults OFF; steer users to the tint
dial instead. Don't invest in per-species foliage mapping unless asked.

## Ore/rock placeholder fix — Visible Ores & Minerals compat (JSON patches)
Conquest's `patches/survival/blocktypes/stone/ore-{graded,ungraded,gem}.json` do
`op:remove /textures` then rebuild via a `texturesByType`. On its own this is fine — a fresh
`.ctc scan` of Conquest 1.0.7 (no VOM) is clean, no ore×rock gaps. The real breakage is with
**Visible Ores & Minerals (VOM)** installed.

**Why VOM breaks (the mechanism):** VOM patches the same three ore blocktypes into 3D veins
(`replace /drawtype json` + `shapeByType`) whose shapes reference the texture codes **`#cube`**
(surrounding stone), **`#ore1`** (visible lump), and `#0`. It wires `cube`/`ore1` via
`op:add /textures/cube…` / `op:add /textures/ore1ByType`. JSON-patch `add` resolves the **parent**
path (`/textures`) with `skipLast` and **no-ops if that parent is missing** (verified in
`Tavis.JsonPatch` `JsonNetTargetAdapter.AddInsertPrepend`, bundled at
`Vintage Story.app/Lib/Tavis.JsonPatch.dll`; the op enum `EnumJsonPatchOp{Add,AddEach,Remove,
Replace,Copy,Move,AddMerge}` is in `Mods/VSEssentials.dll`). Because Conquest already did
`op:remove /textures`, VOM's adds silently fail → the veins have no `cube`/`ore1` mapping → the
client logs `Missing mapping for texture code #cube during shape tesselation of block
game:ore-medium-emerald-slate using shape visibleoresandminerals:block/gem_medium` and renders the
pink/black placeholder.

**Why this can NOT be fixed from C#:** blocktype JSON assets are **not retrievable via
`api.Assets.TryGet`** on the client — it returns null in *every* domain (`game:`, `survival:`)
for `blocktypes/stone/ore-*.json`. (Textures are retrievable and byte-editable — see the reverts —
but blocktype/itemtype categories are not exposed there.) So the old `ApplyOreFix` byte-edit was a
permanent silent no-op; it never ran. **Removed.**

**Why the mod is `side: Universal` (not `Client`):** blocktype JSON is resolved **server-side** —
the client receives already-resolved block definitions over the network, so a *client-only* mod's
JSON patch has no target and silently misses (log: "missing files on N patches"). Making the mod
Universal (`modinfo.json`; `requiredOnClient:true`, `requiredOnServer:false`) lets the patch apply
server-side where the blocktype exists; the server resolves the veins and ships them to the client.
The **C# ModSystem stays client-only** (`ShouldLoad(side) => side==Client`) — only the JSON asset
patches ride the server side. (Pending in-game verification of the ore render at time of writing.)

**The fix = three JSON patches** at `assets/conquesttweaks/patches/compatibility/
visibleoresandminerals/ore-{graded,ungraded,gem}.json`, each a single op:
- `op: "addmerge"`, `path: "/textures"` (the **parent**, not `/textures/cube`). `addmerge` →
  `AddMergeOperation` → `AddInsertPrepend`, which resolves the parent of `/textures` to the **block
  root** (always exists), so it works even when Conquest removed `/textures`: if absent it *sets*
  `/textures` to our value; if present it deep-merges. This is the crux — targeting `/textures/cube`
  would fail exactly like VOM's does.
- `file: "game:blocktypes/stone/ore-{…}.json"` — the **game** domain (this is the target VOM uses,
  even though the file sits on disk under `assets/survival/…`; block code domain wins).
- `dependsOn: [{ "modid": "visibleoresandminerals" }]` — the patch is skipped entirely unless VOM is
  loaded.
- `value`: a `cube` = `block/stone/rock/conquest/{rock}/sides/1` (Conquest rock art, so the stone
  around the vein matches the pack — VOM's own cube uses vanilla `{rock}1`) + overlays
  `block/stone/ore/{type}1|2|3`; and an `ore1ByType` replicating VOM's lump mapping. **All refs are
  UNQUALIFIED = game domain** — base survival/ content registers under `game:` at runtime (a
  `survival:` domain does NOT resolve here), which is exactly how VOM references them; an earlier
  `survival:`-qualified draft was wrong and missed. Per file: graded → nugget/{type}
  (+ nativegold/nativesilver overrides); ungraded → ungraded/{type} (+ flint, crushed/alum); gem →
  gem/{diamond,emerald,olivine} else ungraded/{type}.

**Ordering is guaranteed** without depending on VOM: this mod **hard-depends on `conquest`**, so it
loads after Conquest and its patch applies after Conquest's `op:remove /textures`. Whether VOM's
patch runs before ours (it failed, we set `/textures`) or after (we set it, VOM's adds then succeed
and coexist), the result has a valid `cube` → no placeholder.

**Verified paths (all game domain, unqualified):** `block/stone/rock/conquest/{rock}/sides/1` exists
for all 24 rocks (Conquest pack); `block/stone/ore/{type}1|2|3` overlays use no `{grade}` prefix
(`nativecopper1` exists, `poornativecopper1` does not); all VOM lump textures resolve under `game:`.

## Diagnostic scanner (`.ctc scan`, client command — NOT `/ctc`)
`.ctc scan` (and `Config.ReportMissingTexturesOnLoad` → runs on `LevelFinalize`) walks
`capi.World.Blocks`. A block is flagged if:
1. `block.Textures` is empty (no wiring), or
2. any `CompositeTexture` ref (`Base` / `Alternates[].Base` / `BlendedOverlays[].Base`) points at a
   `textures/<path>.png` asset `Assets.TryGet` can't find, or
3. **(new) its shape references a texture code with no mapping in `block.Textures`** — e.g. a VOM
   vein whose shape wants `#cube`/`#ore1` but Conquest stripped `/textures`. `GetShapeTextureCodes`
   loads `block.Shape.Base` (`shapes/<path>.json`), strips `//` comments, and recursively collects
   every `"texture"` face value starting with `#`; any code absent from `block.Textures` is a gap.
   **It subtracts (a) codes the shape self-defines in its own top-level `textures` dict and
   (b) engine auto-resolved sentinels `#null`/`#none`/`#0`** (`AutoResolvedShapeCodes`) — without
   this, check #3 false-flagged ~2200 blocks (`#null` ×1573, `#0` ×305, plus shape-local codes),
   because those never need a block-side mapping (the game logs `#cube`/`#ore1` gaps for broken
   veins but NEVER `#0`, confirming `#0` auto-resolves). The client tesselation log is still the
   ground truth; the scanner just surfaces the same gaps without a report.

Groups by the first two dash-segments of the code, prints a summary, writes the full list to
`ModConfig/ctc-missing-textures.txt`. **Skips any block whose code contains `multiblock`** (invisible
structural stand-ins, no wiring by design).

**Why check #3 exists:** the old scanner only validated that referenced texture *assets* exist, so
it **false-cleaned VOM veins** — the block's wired refs (nugget/gem lumps) resolved fine, but the
shape needed a `#cube` code that wasn't in the texture dict at all. Scan said "3 placeholders"
(benign machine part-tops) while the screenshot showed dozens of pink ore blocks. Check #3 closes
that blind spot. NOTE: the game *already* logs `Missing mapping for texture code #…` for these at
tesselation time — the client log is the ground truth; the scanner just surfaces it without a report.

**Scan result (2026-08, Conquest 1.0.7, no VOM):** clean. The 154 hits before the multiblock filter
were benign internal blocks (≈150 `multiblock-monolithic` + 4 machine part-tops) — no ores, no real
`unknown.png` gaps. So a general surgical auto-fill (was "approach B") is unnecessary for this pack;
the VOM patches plus the filter are sufficient.

## Approach B (general surgical auto-fill) — NOT built yet
Detecting broken variants must be runtime (needs the resolved block registry), but fixing textures
must happen pre-atlas (in `AssetsLoaded` — same hook the reverts use; NOT `AssetsFinalize`, which is
too late on the client, see the Mechanism section) — a timing split. The tractable design: in
`AssetsLoaded`, for each Conquest-patched blocktype, enumerate its variant combos, resolve each
texturesByType/wildcard ref, and inject a bundled vanilla fallback ONLY for variants whose refs are
missing. Substantial + needs per-variant resolution logic. Decision: run `.ctc scan` first to see
whether anything beyond ores actually breaks before investing in B.

## Build / test loop
```sh
python3 build/extract-vanilla.py   # after changing FAMILIES or resolver
build/restage.sh                   # build + stage; then relog in-game
```
dotnet: `/opt/homebrew/bin/dotnet`. Game DLLs + SkiaSharp/Newtonsoft referenced from the install via
`$(VintageStoryPath)`, all `Private=false`.
