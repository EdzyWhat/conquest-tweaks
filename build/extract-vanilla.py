#!/usr/bin/env python3
"""
Dev tool. Regenerates the bundled vanilla-texture payload the mod ships.

For every texture Conquest overrides in a given block family, we find the matching VANILLA
source texture in the installed game and copy it into

    src/assets/conquestvanillavom/textures/vanilla/<family>/<conquest-relative-path>

At runtime the mod overwrites  game:textures/<conquest-relative-path>  with these bytes for
each enabled family - so filling a Conquest path with its vanilla source art reverts that
block's look, and each family toggles independently. Conquest's own extra variants (it tiles
far more than vanilla) collapse onto the single vanilla texture they correspond to, which is
exactly the vanilla appearance.

Run:  python3 build/extract-vanilla.py [--zip <ConquestZip>] [--game <VintageStory.app>]
"""
import argparse, os, re, sys, zipfile, shutil

# --- Family -> Conquest texture path prefixes (relative to .../textures/, i.e. "block/...") ---
# A prefix ending in "/" matches a directory; a bare prefix matches files that start with it.
FAMILIES = {
    "soil":         ["block/soil/fertility/"],
    "grasscover":   ["block/plant/grasscoverage/"],
    "forestfloor":  ["block/soil/forest/"],
    "peat":         ["block/soil/peat/", "block/soil/peatpile/"],
    "clay":         ["block/soil/clay/"],
    "farmland":     ["block/soil/farmland/"],
    "cob":          ["block/soil/cob/"],
    "rammedearth":  ["block/soil/rammed/"],
    "mudbrick":     ["block/soil/mudbrick/"],
    "stonepath":    ["block/stone/path/"],
    "tallgrass":    ["block/plant/tallgrass/"],
    "otherfoliage": ["block/plant/fern/", "block/plant/ferntree/", "block/plant/flower/",
                     "block/plant/herb/", "block/plant/reeds/", "block/plant/bamboo/",
                     "block/plant/waterlily/"],
}

DIGITS_TAIL = re.compile(r"^(.*?)(\d+)(\.png)$")


def vanilla_candidates(va, rel):
    """Yield possible on-disk vanilla source paths for a texture rel like 'block/soil/x.png'."""
    for domainfolder in ("survival", "game", "creative"):
        yield os.path.join(va, "assets", domainfolder, "textures", rel)


def find_vanilla(va, rel):
    for p in vanilla_candidates(va, rel):
        if os.path.isfile(p):
            return p
    return None


def family_special(va, family, rel):
    """Structural remaps for dirs Conquest reorganized away from the vanilla layout."""
    if family == "soil":
        m = re.match(r"block/soil/fertility/([a-z]+)/\d+\.png$", rel)
        if m:
            return find_vanilla(va, f"block/soil/fert{m.group(1)}.png")
    if family == "grasscover":
        m = re.match(r"block/plant/grasscoverage/([a-z]+)/\d+\.png$", rel)
        if m:
            return find_vanilla(va, f"block/plant/grasscoverage/{m.group(1)}.png")
    if family == "stonepath":
        m = re.match(r"block/stone/path/[a-z]+/(\d+)\.png$", rel)
        if m:
            return (find_vanilla(va, f"block/stone/path/normal{m.group(1)}.png")
                    or find_vanilla(va, "block/stone/path/normal1.png"))
    if family == "peat":
        # Conquest split peat into peat/peattop* & peat/peatside*; vanilla is one texture.
        if re.match(r"block/soil/peat/peat(top|side)\d*\.png$", rel):
            return find_vanilla(va, "block/soil/peat.png")
        if rel.startswith("block/soil/peatpile/"):
            return find_vanilla(va, "block/soil/peatpile/sides.png")
    if family == "clay":
        m = re.match(r"block/soil/clay/(blue|fire|red)/\d+\.png$", rel)
        if m:
            return find_vanilla(va, f"block/soil/{m.group(1)}clay.png")
    if family == "forestfloor":
        # Conquest added forestsoil6x-8x; vanilla only ships groups 1..5. Fold onto 1..5.
        m = re.match(r"block/soil/forest/forestsoil(\d)(\d)\.png$", rel)
        if m:
            grp = ((int(m.group(1)) - 1) % 5) + 1
            return (find_vanilla(va, f"block/soil/forest/forestsoil{grp}{m.group(2)}.png")
                    or find_vanilla(va, "block/soil/forest/forestsoil11.png"))
    if family == "farmland":
        # Farmland sides are just the dirt side in vanilla -> the soil fertility texture.
        m = re.match(r"block/soil/farmland/fert([a-z]+)-side\d*\.png$", rel)
        if m:
            return find_vanilla(va, f"block/soil/fert{m.group(1)}.png")
    return None


def generic_candidates(rel):
    """Yield fallback vanilla rels in priority order for a Conquest texture rel."""
    stem = rel[:-4] if rel.endswith(".png") else rel
    parts = stem.split("/")
    # same directory, alternate last-segment names (strip trailing digits, or ->1)
    last = parts[-1]
    base_last = re.sub(r"\d+$", "", last)
    for v in [base_last, base_last + "1"] if base_last and base_last != last else []:
        yield "/".join(parts[:-1] + [v]) + ".png"
    # walk up the directory tree: an ancestor directory used as a single texture
    for i in range(len(parts) - 1, 0, -1):
        anc = parts[:i]
        yield "/".join(anc) + ".png"
        yield "/".join(anc) + "1.png"
        b = re.sub(r"\d+$", "", anc[-1])
        if b and b != anc[-1]:
            yield "/".join(anc[:-1] + [b]) + ".png"


def resolve_source(va, family, rel):
    """Return an on-disk vanilla texture path that best matches Conquest texture `rel`."""
    # 1) exact same path
    hit = find_vanilla(va, rel)
    if hit:
        return hit
    # 2) family-specific structural remaps
    hit = family_special(va, family, rel)
    if hit:
        return hit
    # 3) generic reductions (numeric-variant collapse, then ancestor walk)
    for cand in generic_candidates(rel):
        hit = find_vanilla(va, cand)
        if hit:
            return hit
    return None


def family_for(rel):
    for fam, prefixes in FAMILIES.items():
        for pre in prefixes:
            if pre.endswith("/"):
                if rel.startswith(pre):
                    return fam
            else:
                base = pre.rsplit("/", 1)[0] + "/"
                if rel.startswith(pre) and rel.startswith(base):
                    return fam
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--zip", default=os.path.expanduser(
        "~/Downloads/Conquest VS Edition v1.0.7.zip"))
    ap.add_argument("--game", default="/Applications/Vintage Story.app")
    args = ap.parse_args()

    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out_base = os.path.join(root, "src", "assets", "conquestvanillavom",
                            "textures", "vanilla")
    if os.path.isdir(out_base):
        shutil.rmtree(out_base)

    tex_prefix = "assets/game/textures/"
    counts = {f: 0 for f in FAMILIES}
    unmapped = {f: [] for f in FAMILIES}

    with zipfile.ZipFile(args.zip) as z:
        names = [n for n in z.namelist()
                 if n.startswith(tex_prefix) and n.lower().endswith(".png")]
        for name in names:
            rel = name[len(tex_prefix):]           # "block/..."
            fam = family_for(rel)
            if fam is None:
                continue
            src = resolve_source(args.game, fam, rel)
            if src is None:
                unmapped[fam].append(rel)
                continue
            dst = os.path.join(out_base, fam, rel)
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            shutil.copyfile(src, dst)
            counts[fam] += 1

    print("=== Bundled vanilla textures per family ===")
    total = 0
    for fam in FAMILIES:
        print(f"  {fam:14s} {counts[fam]:4d}  (unmapped: {len(unmapped[fam])})")
        total += counts[fam]
    print(f"  {'TOTAL':14s} {total:4d}")

    any_unmapped = False
    for fam, lst in unmapped.items():
        if lst:
            any_unmapped = True
            print(f"\n--- UNMAPPED in {fam} ({len(lst)}) ---")
            for r in lst[:40]:
                print("   ", r)
            if len(lst) > 40:
                print(f"    ... and {len(lst) - 40} more")
    sys.exit(2 if any_unmapped else 0)


if __name__ == "__main__":
    main()
