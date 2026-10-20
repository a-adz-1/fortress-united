"""
Glow textures for the demoman's pipe bombs (run inside Blender, after
prepare_model.py has made Art/Grenades/PipeBomb.fbx and its textures).

    blender -b --factory-startup --python tools/meshy/pipe_glow.py -- \
        Assets/FortressUnited/Art/Grenades PipeBomb path/to/meshy_pipe.glb

From the user's reference: the outer bands and end rings glow in the team
colour, and the window down the middle and the end-cap centres glow in the
bomb type's colour: green for detonated pipe bombs (right click), blue for
grenade launcher grenades. The bands are found by their light grey
paint; the window (the recess on one side of the middle section) and the
end-cap centres by shape, on the full-detail Meshy model (it shares its
texture layout with the thinned one, so the masks fit both).
Writes <Name>_Emission_<Team>_<Type>.png for Red/Blue x Green/Blue (colour
emission, so the game picks one per pipe) and <Name>_Emission.png (blue,
green) as the default the importer hooks up.
"""
import os
import sys

import bpy
import numpy as np
from PIL import Image, ImageDraw

TEAM = {"Red": (241, 71, 66), "Blue": (17, 167, 247)}       # Game/TeamColours.cs glow
TYPE = {"Green": (75, 241, 20), "Blue": (17, 167, 247)}      # green from the pipe reference

folder, name, source = sys.argv[sys.argv.index("--") + 1:][:3]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=source)
obj = next(o for o in bpy.context.scene.objects if o.type == "MESH")
me = obj.data
uv = me.uv_layers.active.data

base = np.asarray(Image.open(os.path.join(folder, name + "_BaseColor.png")).convert("RGB"), dtype=np.float32) / 255
size = base.shape[0]

# Shape: long axis is the largest dimension
co = np.array([v.co[:] for v in me.vertices])
axis = int(np.argmax(co.max(0) - co.min(0)))
others = [i for i in range(3) if i != axis]
centre = (co.max(0) + co.min(0)) / 2
half = (co.max(0) - co.min(0))[axis] / 2
radius = np.hypot(co[:, others[0]] - centre[others[0]], co[:, others[1]] - centre[others[1]]).max()

typemask = Image.new("L", (size, size), 0)
draw = ImageDraw.Draw(typemask)
n_window = n_cap = 0
pc = np.empty(len(me.polygons) * 3, dtype=np.float32)
me.polygons.foreach_get("center", pc)
pc = pc.reshape(-1, 3) - centre
along = np.abs(pc[:, axis]) / half
r = np.hypot(pc[:, others[0]], pc[:, others[1]]) / radius
ang = np.degrees(np.arctan2(pc[:, others[1]], pc[:, others[0]]))
window = (along < 0.42) & (ang > 0) & (ang < 178) & (r < 0.775)   # the recessed window
cap = (along > 0.9) & (r < 0.3)                                   # end-cap centres
n_window, n_cap = int(window.sum()), int(cap.sum())
uvs = np.empty(len(uv) * 2, dtype=np.float32)
uv.foreach_get("uv", uvs)
uvs = uvs.reshape(-1, 2)
starts = np.empty(len(me.polygons), dtype=np.int32)
me.polygons.foreach_get("loop_start", starts)
counts = np.empty(len(me.polygons), dtype=np.int32)
me.polygons.foreach_get("loop_total", counts)
for i in np.nonzero(window | cap)[0]:
    q = uvs[starts[i]:starts[i] + counts[i]]
    draw.polygon([(u * size, (1 - v) * size) for u, v in q], fill=255)
print(f"window faces {n_window}, end-cap faces {n_cap}")
typemask = np.asarray(typemask, dtype=np.float32) / 255

# Bands: light grey paint (bright, unsaturated), not where the type glow is
mx, mn = base.max(2), base.min(2)
sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
teammask = np.clip((mx - 0.6) / 0.12, 0, 1) * np.clip((0.25 - sat) / 0.1, 0, 1) * (1 - typemask)
print(f"team glow {float((teammask > 0.5).mean()) * 100:.1f}%, type glow {float((typemask > 0.5).mean()) * 100:.1f}%")

for tname, tcol in TEAM.items():
    for kname, kcol in TYPE.items():
        out = teammask[..., None] * np.array(tcol) + typemask[..., None] * np.array(kcol)
        Image.fromarray(np.clip(out, 0, 255).astype(np.uint8)).save(os.path.join(folder, f"{name}_Emission_{tname}_{kname}.png"))
Image.open(os.path.join(folder, f"{name}_Emission_Blue_Green.png")).save(os.path.join(folder, f"{name}_Emission.png"))
print("wrote emission textures")
