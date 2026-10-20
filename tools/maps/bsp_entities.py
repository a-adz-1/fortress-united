"""
Edits a compiled map (.bsp, Half-Life v30) without recompiling: its
entities and texture names. For trying Fortress United settings on an
existing map, or retrofitting one whose source is lost.

    python3 tools/maps/bsp_entities.py MAP.bsp list [classname]
    python3 tools/maps/bsp_entities.py MAP.bsp OUT.bsp set-world KEY VALUE [KEY VALUE ...]
    python3 tools/maps/bsp_entities.py MAP.bsp OUT.bsp add CLASSNAME KEY VALUE [KEY VALUE ...]
    python3 tools/maps/bsp_entities.py MAP.bsp OUT.bsp rename-class OLD NEW [N]
    python3 tools/maps/bsp_entities.py MAP.bsp OUT.bsp rename-texture OLD NEW
    python3 tools/maps/bsp_entities.py MAP.bsp find-texture NAME

Examples:
    ... 2fort.bsp 2fort_fu.bsp set-world fu_time dusk fu_signs neon
    ... 2fort.bsp 2fort_fu.bsp add fu_sign origin "-1200 900 -100" angles "0 90 0" text "BLUE BASE" team_no 1
    ... 2fort.bsp 2fort_fu.bsp rename-texture sign_capt fu_scr_blue
rename-class turns an existing brush entity into another (e.g. a func_nobuild
volume into fu_norail); a new brush volume needs the map recompiled.
Only edit maps you have the right to change; keep the original.
"""
import re
import struct
import sys


def lumps(d):
    if struct.unpack_from("<i", d, 0)[0] != 30:
        sys.exit("not a Half-Life (v30) BSP")
    return [list(struct.unpack_from("<ii", d, 4 + i * 8)) for i in range(15)]


def parse_entities(text):
    ents = []
    for block in re.findall(r"\{(.*?)\}", text, re.S):
        ents.append(re.findall(r'"([^"]*)"\s*"([^"]*)"', block))
    return ents


def write_entities(ents):
    out = []
    for kv in ents:
        out.append("{\n" + "".join(f'"{k}" "{v}"\n' for k, v in kv) + "}\n")
    return "".join(out) + "\0"


def save(d, L, ents, dst):
    """Writes the map with a new entity lump appended at the end (other lumps untouched)."""
    data = write_entities(ents).encode("latin1")
    out = bytearray(d)
    while len(out) % 4:
        out += b"\0"
    off = len(out)
    out += data
    struct.pack_into("<ii", out, 4, off, len(data))
    open(dst, "wb").write(out)
    print("wrote", dst)


def textures(d, L):
    o, _ = L[2]
    n = struct.unpack_from("<i", d, o)[0]
    for i in range(n):
        to = struct.unpack_from("<i", d, o + 4 + i * 4)[0]
        if to >= 0:
            yield o + to, d[o + to:o + to + 16].split(b"\0")[0].decode("latin1")


def main():
    a = sys.argv[1:]
    if len(a) < 2:
        sys.exit(__doc__)
    src = a[0]
    d = open(src, "rb").read()
    L = lumps(d)
    eo, el = L[0]
    ents = parse_entities(d[eo:eo + el].decode("latin1"))
    # Reading: MAP command ...; editing: MAP OUT command ...
    if a[1] in ("list", "find-texture"):
        cmd, dst, rest = a[1], None, a[2:]
    else:
        if len(a) < 3:
            sys.exit(__doc__)
        dst, cmd, rest = a[1], a[2], a[3:]
    if cmd == "list":
        for i, kv in enumerate(ents):
            k = dict(kv)
            if rest and k.get("classname") != rest[0]:
                continue
            print(i, " ".join(f'{x}="{y}"' for x, y in kv))
        return
    if cmd == "find-texture":
        # Where faces with this texture are: their centres, for placing entities near them
        tex_offsets = {name.lower(): i for i, (_, name) in enumerate(textures(d, L))}
        want = rest[0].lower()
        fo, fl = L[7]; to_, _ = L[6]; eo2, _ = L[12]; so, _ = L[13]; vo, _ = L[3]; po, _ = L[1]
        names = [n.lower() for _, n in textures(d, L)]
        for f in range(fl // 20):
            plane, side, fe, ne, ti = struct.unpack_from("<HHiHH", d, fo + f * 20)
            tex = struct.unpack_from("<i", d, to_ + ti * 40 + 32)[0]
            if tex < 0 or tex >= len(names) or names[tex] != want:
                continue
            pts = []
            for k in range(ne):
                se = struct.unpack_from("<i", d, so + (fe + k) * 4)[0]
                v0, v1 = struct.unpack_from("<HH", d, eo2 + abs(se) * 4)
                pts.append(struct.unpack_from("<3f", d, vo + (v0 if se >= 0 else v1) * 12))
            c = [sum(p[i] for p in pts) / len(pts) for i in range(3)]
            nrm = struct.unpack_from("<3f", d, po + plane * 20)
            if side: nrm = tuple(-x for x in nrm)
            print(f"face {f}: centre {c[0]:.0f} {c[1]:.0f} {c[2]:.0f}  normal {nrm[0]:.2f} {nrm[1]:.2f} {nrm[2]:.2f}")
        return
    if cmd == "set-world":
        world = next(kv for kv in ents if dict(kv).get("classname") == "worldspawn")
        for k, v in zip(rest[0::2], rest[1::2]):
            world[:] = [(x, y) for x, y in world if x != k] + [(k, v)]
        save(d, L, ents, dst)
    elif cmd == "add":
        cls, kvs = rest[0], rest[1:]
        ents.append([("classname", cls)] + list(zip(kvs[0::2], kvs[1::2])))
        save(d, L, ents, dst)
    elif cmd == "rename-class":
        old, new = rest[0], rest[1]
        limit = int(rest[2]) if len(rest) > 2 else 1 << 30
        n = 0
        for kv in ents:
            if dict(kv).get("classname") == old and n < limit:
                kv[:] = [(x, new if x == "classname" else y) for x, y in kv]
                n += 1
        print("renamed", n)
        save(d, L, ents, dst)
    elif cmd == "rename-texture":
        old, new = rest[0].lower(), rest[1]
        if len(new) > 15:
            sys.exit("texture names are at most 15 characters")
        out = bytearray(d)
        n = 0
        for off, name in textures(d, L):
            if name.lower() == old:
                out[off:off + 16] = new.encode("latin1").ljust(16, b"\0")
                n += 1
        print("renamed", n)
        open(dst, "wb").write(out)
        print("wrote", dst)
    else:
        sys.exit(__doc__)


if __name__ == "__main__":
    main()
