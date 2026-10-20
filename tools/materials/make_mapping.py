"""
Builds the map material table the game reads (Resources/MapMaterials.csv):
every texture in docs/maps/texture-inventory.csv gets a material from the
library and a mode, from overrides.csv (hand-picked by looking at the
textures) or else a default for its category.

    python3 tools/materials/make_mapping.py

Modes: replace (the material, tinted to the old texture's colour), detail
(the old texture with the material's grain, relief and sheen laid over it,
so stripes, panel lines and team markings stay), keep (unchanged).
"""
import csv
import os

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))

# Category defaults: detail mode, so an unreviewed texture keeps its look and only gains surface
DEFAULTS = {
    "concrete/plaster": "Concrete030", "tile/floor": "Concrete030", "stone/rock": "Rock030",
    "metal": "Metal046B", "door": "Metal046B", "trim/edge": "Metal046B", "tech/computer": "Metal046B",
    "ground/terrain": "Ground048", "brick": "Concrete042A", "roof": "Concrete042A",
    "wood": "Wood051", "unsorted": "Concrete030",
}


def keep(name, category):
    n = name.lower()
    return (n.startswith(("{", "!", "~", "+", "sky")) or "~" in n or n.startswith("sign") or
            n.startswith(("exit", "team")) or category in ("light", "sign/decal", "glass/window",
                                                           "grate/fence", "water/liquid", "special"))


def main():
    over = {}
    for line in open(os.path.join(HERE, "overrides.csv")):
        if line.startswith("#") or line.startswith("texture,") or not line.strip():
            continue
        t, m, mode = line.strip().split(",")
        over[t.lower()] = (m, mode)
    rows = list(csv.DictReader(open(os.path.join(ROOT, "docs", "maps", "texture-inventory.csv"))))
    out = []
    counts = {}
    for r in rows:
        name = r["texture"].lower()
        if name in over:
            m, mode = over[name]
        elif r["special"] or keep(name, r["category"]):
            m, mode = "", "keep"
        else:
            m, mode = DEFAULTS.get(r["category"], "Concrete030"), "detail"
        counts[mode] = counts.get(mode, 0) + 1
        out.append((name, m, mode))
    dst = os.path.join(ROOT, "Assets", "FortressUnited", "Resources", "MapMaterials.csv")
    with open(dst, "w") as f:
        f.write("texture,material,mode\n")
        for t, m, mode in sorted(set(out)):
            f.write(f"{t},{m},{mode}\n")
    missing = [t for t in over if t not in {o[0] for o in out}]
    print(f"wrote {dst}: {len(out)} textures {counts}; overrides not in the inventory: {missing}")


if __name__ == "__main__":
    main()
