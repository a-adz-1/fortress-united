"""
Downloads the world material library (CC0 PBR sets from ambientCG, listed in
library.csv) and packs each one for Unity's URP Lit shader:

    python3 tools/materials/fetch.py [--res 1K|2K]

Writes Assets/FortressUnited/Resources/WorldMaterials/ (not in git):
    <Id>_Color.jpg    base colour
    <Id>_Normal.png   normal map (OpenGL convention, as Unity uses)
    <Id>_Mask.png     R metallic, G ambient occlusion, A smoothness
    <Id>_Detail.jpg   grey detail (mean 50% grey): the material's grain laid
                      over a map's own texture
    library.json      each material's size in metres, mean colour, smoothness
                      and metalness
Downloads are cached in ~/.cache/fortress-united/materials.
"""
import csv
import io
import json
import os
import sys
import urllib.request
import zipfile

from PIL import Image, ImageOps, ImageStat

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "Assets", "FortressUnited", "Resources", "WorldMaterials")
CACHE = os.path.expanduser("~/.cache/fortress-united/materials")
RES = sys.argv[sys.argv.index("--res") + 1] if "--res" in sys.argv else "1K"


def fetch(asset):
    os.makedirs(CACHE, exist_ok=True)
    path = os.path.join(CACHE, f"{asset}_{RES}-JPG.zip")
    if not os.path.exists(path):
        url = f"https://ambientcg.com/get?file={asset}_{RES}-JPG.zip"
        req = urllib.request.Request(url, headers={"User-Agent": "FortressUnited-materials/1.0"})
        with urllib.request.urlopen(req) as r, open(path + ".part", "wb") as f:
            f.write(r.read())
        os.replace(path + ".part", path)
    return zipfile.ZipFile(path)


def map_from(z, *suffixes):
    for s in suffixes:
        for n in z.namelist():
            if n.lower().endswith(s.lower()):
                return Image.open(io.BytesIO(z.read(n)))
    return None


def srgb_to_linear(c):
    c /= 255
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def pack(asset, size_m):
    z = fetch(asset)
    color = map_from(z, "_Color.jpg").convert("RGB")
    w, h = color.size
    color.save(os.path.join(OUT, f"{asset}_Color.jpg"), quality=92)
    normal = map_from(z, "_NormalGL.jpg", "_NormalGL.png")
    if normal is not None:
        normal.convert("RGB").save(os.path.join(OUT, f"{asset}_Normal.png"))
    rough = map_from(z, "_Roughness.jpg")
    metal = map_from(z, "_Metalness.jpg")
    ao = map_from(z, "_AmbientOcclusion.jpg")
    L = lambda im, v: (im.convert("L").resize((w, h)) if im is not None else Image.new("L", (w, h), v))
    smooth = ImageOps.invert(L(rough, 160))
    Image.merge("RGBA", (L(metal, 0), L(ao, 255), Image.new("L", (w, h), 0), smooth)).save(
        os.path.join(OUT, f"{asset}_Mask.png"))
    # Detail: luminance only, shifted so its mean is 50% grey (URP's neutral detail value)
    lum = color.convert("L")
    mean = ImageStat.Stat(lum).mean[0]
    detail = lum.point(lambda v: max(0, min(255, int(v - mean + 128))))
    detail.convert("RGB").save(os.path.join(OUT, f"{asset}_Detail.jpg"), quality=92)
    rgb = ImageStat.Stat(color.resize((64, 64))).mean
    return {"size": size_m, "mean": [round(srgb_to_linear(c), 4) for c in rgb],
            "smoothness": round(ImageStat.Stat(smooth).mean[0] / 255, 3),
            "metallic": round(ImageStat.Stat(L(metal, 0)).mean[0] / 255, 3)}


def main():
    os.makedirs(OUT, exist_ok=True)
    lib = {}
    for row in csv.DictReader(open(os.path.join(HERE, "library.csv"))):
        asset = row["id"]
        try:
            lib[asset] = dict(id=asset, **pack(asset, float(row["size_m"])))
            print("ok", asset)
        except Exception as e:      # one bad download shouldn't stop the rest
            print("FAILED", asset, e)
    json.dump({"items": list(lib.values())}, open(os.path.join(OUT, "library.json"), "w"), indent=1)
    print(f"{len(lib)} materials in {OUT}")


if __name__ == "__main__":
    main()
