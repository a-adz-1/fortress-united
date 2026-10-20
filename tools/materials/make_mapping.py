"""
Builds the map material table the game reads (Resources/MapMaterials.csv):
every texture in docs/maps/texture-inventory.csv gets a material from the
library and a mode, from overrides.csv (hand-picked by looking at the
textures) or else a default for its category.

    python3 tools/materials/make_mapping.py

Modes (the futuristic look):
- panel: the procedural sci-fi surface (SciFiSurface.shader): walls, floors,
  ceilings, anything built (concrete, plaster, tiles, metal, brick, wood)
- metal: the same with smaller, darker metal panels (doors, trims, tech)
- replace: a PBR material tinted to the old texture's colour (rock, ground,
  sand, snow: natural surfaces)
- light: the old texture glowing (light fixtures); sign: the old texture,
  softly lit like a screen; glass: see-through; keep: unchanged
  (see-through grates, special textures); water is chosen by the game ('!')
The team column marks team-coloured textures (their panels and glow take
the team's colours).
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


NATURAL = ("Rock", "Ground", "Gravel", "Snow")
METALLIC = ("door", "trim/edge", "tech/computer", "metal")


def style(name, category, material, mode):
    """The futuristic look's mode for a texture, from what it is."""
    n = name.lower()
    if category == "light" or "~" in n or n.startswith("light"):
        return "light"
    if category == "sign/decal" or n.startswith(("sign", "exit", "team")):
        return "sign"
    if category == "glass/window" or n.startswith("glass"):
        return "glass"
    if mode == "keep" or n.startswith(("{", "!", "sky")) or category in ("grate/fence", "water/liquid", "special"):
        return "keep"
    if material.startswith(NATURAL) or category in ("stone/rock", "ground/terrain"):
        return "replace"
    if material.startswith(("Metal", "Rust", "PaintedMetal", "SheetMetal")) or category in METALLIC:
        return "metal"
    return "panel"


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
        mode = style(name, r["category"], m, mode)
        if mode == "replace" and not m.startswith(NATURAL):
            m = {"stone/rock": "Rock030", "ground/terrain": "Ground048"}.get(r["category"], "Rock030")
        counts[mode] = counts.get(mode, 0) + 1
        out.append((name, m, mode, r["team"]))
    dst = os.path.join(ROOT, "Assets", "FortressUnited", "Resources", "MapMaterials.csv")
    with open(dst, "w") as f:
        f.write("texture,material,mode,team\n")
        for t, m, mode, team in sorted(set(out)):
            f.write(f"{t},{m},{mode},{team}\n")
    missing = [t for t in over if t not in {o[0] for o in out}]
    print(f"wrote {dst}: {len(out)} textures {counts}; overrides not in the inventory: {missing}")


if __name__ == "__main__":
    main()
