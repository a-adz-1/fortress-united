"""
Measure a GoldSrc first-person model (.mdl, version 10) for reference:
where the hand, forearm and weapon are in view space over each animation.

    python3 tools/reference/gs_viewmodel.py path/to/v_tfc_crowbar.mdl [--json out.json]

Prints each sequence and, per frame, the right hand, elbow (forearm start)
and weapon tip positions in view space, in metres, Unity axes (x right,
y up, z forward). Nothing from the file is copied into the game; the
numbers are used to match our own arms and swings to how TFC frames them.
"""
import json
import math
import struct
import sys

UNIT = 0.0254


def read(fmt, data, ofs):
    return struct.unpack_from(fmt, data, ofs)


def quat(a):
    # GoldSrc AngleQuaternion (angles in radians: x, y, z)
    sy, cy = math.sin(a[2] * 0.5), math.cos(a[2] * 0.5)
    sp, cp = math.sin(a[1] * 0.5), math.cos(a[1] * 0.5)
    sr, cr = math.sin(a[0] * 0.5), math.cos(a[0] * 0.5)
    return (sr * cp * cy - cr * sp * sy, cr * sp * cy + sr * cp * sy,
            cr * cp * sy - sr * sp * cy, cr * cp * cy + sr * sp * sy)


def qmat(q):
    x, y, z, w = q
    return [[1 - 2 * y * y - 2 * z * z, 2 * x * y - 2 * w * z, 2 * x * z + 2 * w * y],
            [2 * x * y + 2 * w * z, 1 - 2 * x * x - 2 * z * z, 2 * y * z - 2 * w * x],
            [2 * x * z - 2 * w * y, 2 * y * z + 2 * w * x, 1 - 2 * x * x - 2 * y * y]]


def mmul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]


def mvec(m, v):
    return [sum(m[i][k] * v[k] for k in range(3)) for i in range(3)]


def anim_value(data, base, k):
    """Decodes frame k from a run-length encoded animation value stream."""
    p = base
    while True:
        valid, total = data[p], data[p + 1]
        if total <= k:
            k -= total
            p += (valid + 1) * 2
            continue
        if valid > k:
            return read("<h", data, p + (k + 1) * 2)[0]
        return read("<h", data, p + valid * 2)[0]


def load(path):
    data = open(path, "rb").read()
    if data[:4] != b"IDST":
        raise SystemExit("not a studio model")
    numbones, boneindex = read("<ii", data, 140)
    numseq, seqindex = read("<ii", data, 164)
    bones = []
    for i in range(numbones):
        o = boneindex + i * 112
        name = data[o:o + 32].split(b"\0")[0].decode("latin1")
        parent = read("<i", data, o + 32)[0]
        value = read("<6f", data, o + 64)
        scale = read("<6f", data, o + 88)
        bones.append({"name": name, "parent": parent, "value": value, "scale": scale})
    seqs = []
    for s in range(numseq):
        o = seqindex + s * 176
        label = data[o:o + 32].split(b"\0")[0].decode("latin1")
        fps = read("<f", data, o + 32)[0]
        numframes = read("<i", data, o + 56)[0]
        animindex = read("<i", data, o + 124)[0]
        seqgroup = read("<i", data, o + 156)[0]
        seqs.append({"label": label, "fps": fps, "frames": numframes, "anim": animindex, "group": seqgroup})
    return data, bones, seqs


def hand_verts(data, hand, extra=()):
    """Vertices skinned to the hand bone (hand and the weapon it holds) and to any
    weapon bones (e.g. the knife's own bone), as (bone, position in bone space)."""
    wanted = {hand, *extra}
    numbodyparts, bodypartindex = read("<ii", data, 204)
    out = []
    for bp in range(numbodyparts):
        o = bodypartindex + bp * 76
        nummodels, base, modelindex = read("<iii", data, o + 64)
        for m in range(nummodels):
            mo = modelindex + m * 112
            numverts, vertinfoindex, vertindex = read("<iii", data, mo + 80)
            for v in range(numverts):
                b = data[vertinfoindex + v]
                if b in wanted:
                    out.append((b, read("<3f", data, vertindex + v * 12)))
    return out


def pose(data, bones, seq, frame):
    """World (view space) matrix and position of every bone at a frame."""
    out = []
    for b, bone in enumerate(bones):
        a = seq["anim"] + b * 12
        offs = read("<6H", data, a)
        v = []
        for j in range(6):
            val = bone["value"][j]
            if offs[j]:
                val += anim_value(data, a + offs[j], frame) * bone["scale"][j]
            v.append(val)
        rot = qmat(quat(v[3:6]))
        pos = v[0:3]
        if bone["parent"] >= 0:
            prot, ppos = out[bone["parent"]]
            rot2 = mmul(prot, rot)
            pos2 = [ppos[i] + mvec(prot, pos)[i] for i in range(3)]
            out.append((rot2, pos2))
        else:
            out.append((rot, pos))
    return out


def unity(p):
    # GoldSrc view space: x forward, y left, z up (units) -> Unity camera space metres
    return [round(-p[1] * UNIT, 4), round(p[2] * UNIT, 4), round(p[0] * UNIT, 4)]


def unity_axes(m):
    """The hand bone's x, y and z axes as Unity camera-space directions."""
    cols = [[m[0][c], m[1][c], m[2][c]] for c in range(3)]
    return [[round(-v[1], 3), round(v[2], 3), round(v[0], 3)] for v in cols]


def find(bones, *endings):
    for e in endings:
        for i, b in enumerate(bones):
            if b["name"].lower().endswith(e):
                return i
    return None


def main():
    path = sys.argv[1]
    data, bones, seqs = load(path)
    hand = find(bones, " r hand")
    fore = find(bones, " r forearm", " r arm2")
    upper = find(bones, " r upperarm", " r arm1")
    names = [b["name"] for b in bones]
    # weapon bones: anything outside the body rig that hangs off the hand (e.g. "knife01")
    weapon_bones = [i for i, b in enumerate(bones)
                    if "biped" not in b["name"].lower() and "bip01" not in b["name"].lower()]
    hv = hand_verts(data, hand, weapon_bones) if hand is not None else []
    # The weapon: the bone furthest from the hand among those not in the arm chain
    print("bones:", names)
    result = {"model": path.split("/")[-1], "bones": names, "sequences": []}
    for seq in seqs:
        if seq["group"] != 0:
            continue
        frames = []
        for f in range(seq["frames"]):
            p = pose(data, bones, seq, f)
            # weapon tip: the non-arm bone farthest forward
            tip = max((i for i, b in enumerate(bones) if "bip" not in b["name"].lower()),
                      key=lambda i: p[i][1][0], default=None)
            # Weapon tip: the hand-bound vertex farthest from the wrist
            if hv:
                hp = p[hand][1]
                pts = []
                for b, v in hv:
                    br, bp = p[b]
                    pts.append([bp[i] + mvec(br, v)[i] for i in range(3)])
                far = max(pts, key=lambda q: sum((q[i] - hp[i]) ** 2 for i in range(3)))
            frames.append({
                "far": unity(far) if hv else None,
                "hand": unity(p[hand][1]) if hand is not None else None,
                "hand_axes": unity_axes(p[hand][0]) if hand is not None else None,
                "shoulder": unity(p[upper][1]) if upper is not None else None,
                "elbow": unity(p[fore][1]) if fore is not None else None,
                "tip": unity(p[tip][1]) if tip is not None else None,
                "tipbone": bones[tip]["name"] if tip is not None else None,
            })
        result["sequences"].append({"label": seq["label"], "fps": seq["fps"], "frames": frames})
        h0 = frames[0]["hand"]
        print(f"{seq['label']:16s} {seq['frames']:3d} frames @ {seq['fps']:.0f} fps  hand0 {h0}  elbow0 {frames[0]['elbow']}  tip0 {frames[0]['tip']} ({frames[0]['tipbone']})")
    if "--json" in sys.argv:
        json.dump(result, open(sys.argv[sys.argv.index("--json") + 1], "w"), indent=1)


if __name__ == "__main__":
    main()
