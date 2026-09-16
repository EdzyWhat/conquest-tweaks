#!/usr/bin/env python3
"""Dev tool - NOT part of the build. Regenerates
src/assets/conquesttweaks/patches/compatibility/medievalarchitecture/texture-remap.json
by scanning a local extraction of the Medieval Architecture mod (reference/medievalarchitecture-<ver>/)
for vanilla stone/wood texture refs in families Conquest reskins under a different path, and emitting
one addmerge JSON-patch op per source file that redirects them to Conquest's equivalent tile.

Re-run after bumping MEDIEVALARCHITECTURE_REF to a new extracted version. The mapping rules and
array-index assumptions are verified against Conquest VS Edition v1.0.7 and MA v1.1.1 - re-verify
Conquest coverage (see conversation history / HANDOFF doc) if either pack majorly restructures its
texture folders.
"""
import json, re, os, glob

MEDIEVALARCHITECTURE_REF = "reference/medievalarchitecture-1.1.1/assets/confession/blocktypes"
OUT_PATH = "src/assets/conquesttweaks/patches/compatibility/medievalarchitecture/texture-remap.json"

# (match pattern -> replacement template). Applied to any string matching ^game:<pattern>$.
# {X} = rock/wood placeholder (either a literal MA attribute template like "{rock}" or a
# hardcoded species name like "granite"); {N} = the trailing tile index digit(s).
REMAPS = [
    (re.compile(r'^game:block/stone/rock/(\{[a-z]+\}|[a-z]+)(\d+)$'),
        lambda x, n: f'game:block/stone/rock/conquest/{x}/sides/{n}'),
    (re.compile(r'^game:block/stone/cobblestone/(\{[a-z]+\}|[a-z]+)(\d+)$'),
        lambda x, n: f'game:block/stone/cobblestone/{x}/sides/{n}'),
    (re.compile(r'^game:block/stone/brick/(\{[a-z]+\}|[a-z]+)(\d+)$'),
        lambda x, n: f'game:block/stone/brick/{x}/{n}'),
    (re.compile(r'^game:block/wood/planks/(\{[a-z]+\}|[a-z]+)(\d+)$'),
        lambda x, n: f'game:block/wood/planks/{x}/top/{n}'),
]


def clean(raw: str) -> str:
    raw = re.sub(r'//.*', '', raw)
    raw = re.sub(r',(\s*[\]}])', r'\1', raw)  # tolerate MA's trailing commas
    raw = re.sub(r'([{,]\s*)([A-Za-z_][A-Za-z0-9_]*)(\s*:)', r'\1"\2"\3', raw)  # bare keys
    return raw


def remap(value: str):
    for pat, fn in REMAPS:
        m = pat.match(value)
        if m:
            return fn(m.group(1), m.group(2))
    return None


def walk(obj, path, hits):
    if isinstance(obj, dict):
        for k, v in obj.items():
            walk(v, path + [k], hits)
    elif isinstance(obj, list):
        for i, v in enumerate(obj):
            walk(v, path + [str(i)], hits)
    elif isinstance(obj, str):
        new = remap(obj)
        if new is not None:
            hits.append((path, obj, new))


def container_for(path):
    """Return the JSON-pointer container path (list of segments) for a hit's full path -
    either '.../properties/textures[ByType]' (nested under a behaviors[] array index) or the
    top-level 'textures' dict."""
    for i, seg in enumerate(path):
        if seg in ('textures', 'texturesByType') and i > 0 and path[i - 1] == 'properties':
            return path[:i + 1]
    if path and path[0] == 'textures':
        return path[:1]
    raise ValueError(f"no known container for path {path}")


def set_nested(d, keys, value):
    cur = d
    for k in keys[:-1]:
        cur = cur.setdefault(k, {})
    cur[keys[-1]] = value


def main():
    files = sorted(glob.glob(f"{MEDIEVALARCHITECTURE_REF}/**/*.json", recursive=True))
    patch_ops = []
    total_hits = 0
    for f in files:
        raw = open(f, encoding='utf-8-sig').read()
        raw = clean(raw)
        try:
            data = json.loads(raw)
        except Exception as e:
            print(f"SKIP (parse error) {f}: {e}")
            continue
        hits = []
        walk(data, [], hits)
        if not hits:
            continue

        rel = os.path.relpath(f, MEDIEVALARCHITECTURE_REF)
        by_container = {}
        for path, old, new in hits:
            container = tuple(container_for(path))
            rest = path[len(container):]
            by_container.setdefault(container, {})
            set_nested(by_container[container], rest, new)
            total_hits += 1

        for container, value in by_container.items():
            patch_ops.append({
                "op": "addmerge",
                # MA's on-disk assets/confession/ folder has no modinfo domain override, so its
                # blocktypes register under the confession: domain (NOT game:, unlike VSSurvivalMod's
                # survival/ -> game remap) - confirmed by MA's own internal refs (lang keys, pageCode)
                # all being confession:-qualified.
                "file": f"confession:blocktypes/{rel}",
                "path": "/" + "/".join(container),
                "dependsOn": [{"modid": "medievalarchitecture"}],
                "value": value,
            })
        print(f"{rel}: {len(hits)} hits -> {len(by_container)} patch op(s)")

    print(f"\nTOTAL: {len(files)} files scanned, {total_hits} hits, {len(patch_ops)} patch ops")
    os.makedirs(os.path.dirname(OUT_PATH), exist_ok=True)
    with open(OUT_PATH, 'w', encoding='utf-8') as out:
        json.dump(patch_ops, out, indent='\t')
        out.write('\n')
    print(f"Wrote {OUT_PATH}")


if __name__ == "__main__":
    main()
