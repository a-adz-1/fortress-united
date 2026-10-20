"""
Builds fortress_united.wad: the Fortress United textures for mappers (fu_*),
as a Half-Life WAD3 that J.A.C.K., Hammer and TrenchBroom all read. Each
texture is a preview of what it becomes in the game (docs/mapping-guide.md);
the game swaps in the real material by the texture's name.

    python3 tools/maps/make_wad.py

Writes tools/maps/fortress_united.wad, a copy in the TrenchBroom game folder,
and one in Assets/StreamingAssets (the game reads the textures' colours from it). Uses the team colours and
logos, and the world material library if fetched (tools/materials/fetch.py)
for the natural surfaces; without it those are flat colours.
"""
import os
import struct

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
LIB = os.path.join(ROOT, "Assets", "FortressUnited", "Resources", "WorldMaterials")
LOGOS = os.path.join(ROOT, "Assets", "FortressUnited", "Resources", "Teams")

TEAM_PAINT = {"blue": (15, 150, 232), "red": (182, 31, 31), "gold": (214, 160, 20), "violet": (150, 40, 200)}
TEAM_GLOW = {"blue": (17, 167, 247), "red": (241, 71, 66), "gold": (255, 196, 33), "violet": (204, 61, 255)}
KEY = (0, 0, 255)       # see-through colour in "{" textures (palette index 255)


def panel(base, seam, size=128, band=None):
    """Light panelling: plates with dark seams; an optional team band along the bottom."""
    im = Image.new("RGB", (size, size), base)
    d = ImageDraw.Draw(im)
    if band:
        d.rectangle([0, size * 2 // 3, size, size], fill=band)
        d.line([0, size * 2 // 3, size, size * 2 // 3], fill=tuple(min(255, c + 90) for c in band), width=2)
    for x in (0, size // 2):
        d.line([x, 0, x, size], fill=seam, width=2)
    for y in (0, size // 3, size * 2 // 3):
        d.line([0, y, size, y], fill=seam, width=2)
    return im


def light(colour, size=64):
    im = Image.new("RGB", (size, size), (24, 25, 28))
    d = ImageDraw.Draw(im)
    b = size // 12
    d.rectangle([b, b, size - 1 - b, size - 1 - b], fill=colour)
    inner = tuple(min(255, c + 40) for c in colour)
    d.rectangle([size // 4, size // 4, size * 3 // 4, size * 3 // 4], fill=inner)
    return im


def glass(tint, size=64):
    im = Image.new("RGB", (size, size), tint)
    d = ImageDraw.Draw(im)
    for k in range(-size, size, 24):
        d.line([k, size, k + size, 0], fill=tuple(min(255, c + 50) for c in tint), width=3)
    return im


def logo(team, size=128):
    im = Image.new("RGB", (size, size), (8, 9, 12))
    src = "Blue" if team in ("blue", "gold", "violet") else "Red"
    path = os.path.join(LOGOS, f"Logo_{src}.png")
    if os.path.exists(path):
        art = Image.open(path).convert("RGBA")
        art.thumbnail((size - 16, size - 16))
        # Tint the white artwork in the team colour
        r, g, b = TEAM_GLOW[team]
        a = art.split()[3]
        tinted = Image.new("RGBA", art.size, (r, g, b, 255))
        tinted.putalpha(a)
        im.paste(tinted, ((size - art.width) // 2, (size - art.height) // 2), tinted)
    return im


def screen(team, size=128):
    im = Image.new("RGB", (size, size // 2 * 2), (6, 10, 16))
    d = ImageDraw.Draw(im)
    c = TEAM_GLOW[team]
    d.rectangle([0, 0, size, 4], fill=c)
    d.rectangle([0, size - 5, size, size], fill=c)
    d.rectangle([size // 4 - 10, size // 3, size // 4 + 10, size * 2 // 3], fill=TEAM_GLOW["blue"])
    d.rectangle([size * 3 // 4 - 10, size // 3, size * 3 // 4 + 10, size * 2 // 3], fill=TEAM_GLOW["red"])
    d.rectangle([size // 2 - 2, size // 2 - 8, size // 2 + 2, size // 2 - 4], fill=(200, 200, 200))
    d.rectangle([size // 2 - 2, size // 2 + 4, size // 2 + 2, size // 2 + 8], fill=(200, 200, 200))
    return im


def natural(lib_id, fallback, size=128):
    path = os.path.join(LIB, f"{lib_id}_Color.jpg")
    if os.path.exists(path):
        return Image.open(path).convert("RGB").resize((size, size), Image.LANCZOS)
    return Image.new("RGB", (size, size), fallback)


def grate(size=64):
    im = Image.new("RGB", (size, size), KEY)
    d = ImageDraw.Draw(im)
    for x in range(0, size, 16):
        d.rectangle([x, 0, x + 3, size], fill=(40, 42, 48))
    d.rectangle([0, 0, size, 3], fill=(40, 42, 48))
    return im


def liquid(colour, size=64):
    im = Image.new("RGB", (size, size), colour)
    d = ImageDraw.Draw(im)
    for y in range(4, size, 12):
        d.arc([-8, y - 6, size // 2, y + 6], 0, 180, fill=tuple(min(255, c + 40) for c in colour), width=2)
        d.arc([size // 2, y, size + 8, y + 12], 0, 180, fill=tuple(min(255, c + 40) for c in colour), width=2)
    return im


def textures():
    t = {
        "fu_panel": panel((180, 184, 192), (90, 92, 98)),
        "fu_panel_dark": panel((60, 62, 68), (30, 31, 34)),
        "fu_metal": panel((80, 82, 90), (40, 41, 45), size=64),
        "fu_trim": panel((70, 72, 80), (35, 36, 40), size=64),
    }
    for team in TEAM_PAINT:
        t[f"fu_team_{team}"] = panel((180, 184, 192), (90, 92, 98), band=TEAM_PAINT[team])
        t[f"fu_logo_{team}"] = logo(team)
        t[f"fu_scr_{team}"] = screen(team)
    for name, c in (("white", (240, 244, 250)), ("warm", (255, 222, 175)), ("cool", (200, 230, 255)),
                    ("blue", TEAM_GLOW["blue"]), ("red", TEAM_GLOW["red"]), ("amber", (255, 170, 40))):
        t[f"fu_light_{name}"] = light(c)
    t["fu_glass"] = glass((120, 160, 180))
    t["fu_glass_blue"] = glass((60, 120, 200))
    t["fu_glass_red"] = glass((190, 70, 70))
    for name, lib, fb in (("rock", "Rock030", (90, 88, 84)), ("cliff", "Rock029", (130, 90, 60)),
                          ("dirt", "Ground048", (100, 70, 50)), ("grass", "Ground037", (80, 110, 50)),
                          ("sand", "Ground054", (190, 170, 120)), ("snow", "Snow010A", (230, 235, 245)),
                          ("gravel", "Gravel023", (150, 148, 140)), ("asphalt", "Asphalt031", (70, 70, 72)),
                          ("bricks", "Bricks066", (120, 120, 125)), ("wood", "Wood051", (110, 75, 45))):
        t[f"fu_{name}"] = natural(lib, fb)
    t["{fu_grate"] = grate()
    t["!fu_water"] = liquid((30, 120, 140))
    t["!fu_slime"] = liquid((70, 130, 40))
    t["!fu_lava"] = liquid((230, 90, 20))
    return t


def miptex(name, im):
    """A WAD3 miptex lump: header, four mip levels, the 256-colour palette."""
    w, h = im.size
    see_through = name.startswith("{")
    colours = 255 if see_through else 256
    pal_img = im.quantize(colors=colours, method=Image.Quantize.MEDIANCUT)
    palette = pal_img.getpalette()[: colours * 3]
    palette += [0, 0, 0] * (256 - len(palette) // 3)
    if see_through:
        # The key colour must be the last palette entry: remap it there
        palette[255 * 3: 256 * 3] = list(KEY)
        src = im.load()
        px = pal_img.load()
        for y in range(h):
            for x in range(w):
                if src[x, y] == KEY:
                    px[x, y] = 255
    levels = [pal_img]
    for k in range(1, 4):
        lw, lh = max(1, w >> k), max(1, h >> k)
        levels.append(pal_img.resize((lw, lh), Image.NEAREST))
    data = bytearray()
    header = struct.pack("<16sII", name.encode("ascii")[:15].ljust(16, b"\0"), w, h)
    offs = []
    pos = 40
    pixel_data = b""
    for lv in levels:
        offs.append(pos)
        b = lv.tobytes()
        pixel_data += b
        pos += len(b)
    data += header + struct.pack("<4I", *offs) + pixel_data
    data += struct.pack("<H", 256) + bytes(palette) + b"\0\0"
    return bytes(data)


def main():
    lumps = []
    for name, im in textures().items():
        assert len(name) <= 15, name
        w, h = im.size
        im = im.resize(((w // 16) * 16, (h // 16) * 16))
        lumps.append((name, miptex(name, im)))
    out = bytearray(b"WAD3" + struct.pack("<II", len(lumps), 0))
    directory = bytearray()
    for name, data in lumps:
        directory += struct.pack("<IIIbbH16s", len(out), len(data), len(data), 0x43, 0, 0,
                                 name.encode("ascii").ljust(16, b"\0"))
        out += data
    struct.pack_into("<I", out, 8, len(out))
    out += directory
    for dst in (os.path.join(HERE, "fortress_united.wad"),
                os.path.join(ROOT, "tools", "TrenchBroom", "FortressUnited", "fortress_united.wad"),
                os.path.join(ROOT, "Assets", "StreamingAssets", "fortress_united.wad")):
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        open(dst, "wb").write(out)
        print("wrote", dst, len(out), "bytes,", len(lumps), "textures")


if __name__ == "__main__":
    main()
