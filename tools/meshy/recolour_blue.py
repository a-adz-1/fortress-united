"""
Make the blue in team textures match the team blue (TeamColours.Blue, hue 189:
the cyan of the conc grenade concept).

    python3 tools/meshy/recolour_blue.py shift <texture.png> [...]
        Turns the blues and teals painted into a texture (hues 150-265) to hue 189,
        keeping each pixel's brightness and saturation, in place.

    python3 tools/meshy/recolour_blue.py from-red <red.png> <blue.png>
        Writes a blue team copy of a red team texture (saturated reds to
        hue 189), e.g. the sentry skins.
"""
import sys

import numpy as np
from PIL import Image

TARGET_HUE = 189 / 360


def rgb_to_hsv(rgb):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx = rgb.max(-1)
    mn = rgb.min(-1)
    d = mx - mn
    h = np.zeros_like(mx)
    m = d > 1e-6
    rm = m & (mx == r)
    gm = m & (mx == g) & ~rm
    bm = m & ~rm & ~gm
    h[rm] = ((g - b)[rm] / d[rm]) % 6
    h[gm] = (b - r)[gm] / d[gm] + 2
    h[bm] = (r - g)[bm] / d[bm] + 4
    h = h / 6
    s = np.where(mx > 1e-6, d / np.maximum(mx, 1e-6), 0)
    return h, s, mx


def hsv_to_rgb(h, s, v):
    i = np.floor(h * 6).astype(int) % 6
    f = h * 6 - np.floor(h * 6)
    p = v * (1 - s)
    q = v * (1 - s * f)
    t = v * (1 - s * (1 - f))
    out = np.zeros(h.shape + (3,), dtype=np.float32)
    for k, (a, b, c) in enumerate(((v, t, p), (q, v, p), (p, v, t), (p, q, v), (t, p, v), (v, p, q))):
        sel = i == k
        out[sel, 0], out[sel, 1], out[sel, 2] = a[sel], b[sel], c[sel]
    return out


def recolour(path_in, path_out, hue_lo, hue_hi, min_sat):
    im = Image.open(path_in)
    mode = im.mode
    px = np.asarray(im.convert("RGBA"), dtype=np.float32) / 255
    h, s, v = rgb_to_hsv(px[..., :3])
    lo, hi = hue_lo / 360, hue_hi / 360
    in_range = ((h >= lo) & (h <= hi)) if lo <= hi else ((h >= lo) | (h <= hi))
    # Soft weight so edges of painted areas blend instead of fringing
    w = in_range * np.clip((s - min_sat) / 0.15, 0, 1) * np.clip((v - 0.06) / 0.1, 0, 1)
    new = hsv_to_rgb(np.full_like(h, TARGET_HUE), s, v)
    px[..., :3] = px[..., :3] * (1 - w[..., None]) + new * w[..., None]
    out = Image.fromarray((np.clip(px, 0, 1) * 255 + 0.5).astype(np.uint8), "RGBA")
    if mode != "RGBA":
        out = out.convert(mode if mode in ("RGB", "L") else "RGB")
    out.save(path_out)
    print(f"{path_out}: recoloured {float((w > 0.5).mean()) * 100:.1f}% of pixels")


def main():
    if len(sys.argv) < 3:
        raise SystemExit(__doc__)
    if sys.argv[1] == "shift":
        for p in sys.argv[2:]:
            recolour(p, p, 150, 265, 0.25)
    elif sys.argv[1] == "from-red":
        recolour(sys.argv[2], sys.argv[3], 340, 20, 0.35)
    else:
        raise SystemExit(__doc__)


if __name__ == "__main__":
    main()
