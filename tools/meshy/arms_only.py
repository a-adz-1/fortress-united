"""
First-person arms from a rigged character (run inside Blender).

    blender -b --factory-startup --python tools/meshy/arms_only.py -- \
        Assets/FortressUnited/Art/Characters/Medic/Medic_Blue.fbx \
        Assets/FortressUnited/Art/Characters/Medic/Medic_Blue_Arms.fbx

Keeps the parts of the mesh skinned mostly to the shoulders, arms and hands,
with the whole skeleton (so the game can bend the arms to hold weapons), and
deletes the rest. Animations are left out. The material keeps its name, so
the importer hooks up the same textures as the full character
(<Name>_BaseColor.png next to the source).
"""
import sys

import bmesh
import bpy

ARM_BONES = ("Shoulder", "Arm", "ForeArm", "Hand")   # Meshy rig names, with Left/Right in front

import math

import mathutils
import numpy as np

args = sys.argv[sys.argv.index("--") + 1:]
src, dst = args[:2]
FIST = "--fist" in args          # curl the fingers into a gripping fist (the rig has no finger bones)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
body = next(o for o in bpy.context.scene.objects if o.type == "MESH")
for o in list(bpy.context.scene.objects):
    if o not in (arm, body):
        bpy.data.objects.remove(o, do_unlink=True)
for a in list(bpy.data.actions):
    bpy.data.actions.remove(a)
if arm.animation_data:
    arm.animation_data.action = None
# Back to the rest (bind) pose: without this the bones keep the last animated pose
for pb in arm.pose.bones:
    pb.matrix_basis.identity()
bpy.context.view_layer.update()

arm_groups = {g.index for g in body.vertex_groups
              if any(g.name in (side + b,) for side in ("Left", "Right") for b in ARM_BONES)}
print("arm groups", sorted(body.vertex_groups[i].name for i in arm_groups))

keep = []
for v in body.data.vertices:
    total = sum(g.weight for g in v.groups)
    on_arm = sum(g.weight for g in v.groups if g.group in arm_groups)
    keep.append(total > 0 and on_arm / total > 0.5)

def curl_hand(side):
    """Bends a flat hand's fingers into a fist: two knuckle bends towards the palm."""
    me = body.data
    gi = body.vertex_groups[side + "Hand"].index
    idx = [v.index for v in me.vertices if keep[v.index]
           and sum(g.weight for g in v.groups if g.group == gi) > 0.5]
    if len(idx) < 20:
        return
    world = body.matrix_world
    pts = np.array([world @ me.vertices[i].co for i in idx])
    wrist = np.array(arm.matrix_world @ arm.data.bones[side + "Hand"].head_local)
    centre = pts.mean(0)
    u, sv, vt = np.linalg.svd(pts - centre)
    d = vt[0] if np.dot(vt[0], centre - wrist) > 0 else -vt[0]       # along the fingers
    nrm = vt[2]                                                       # out of the flat of the hand
    # Palm faces the body's middle in the A-pose: pick that side of the hand
    toward_mid = np.array([-np.sign(centre[0]), 0, 0])
    if np.dot(nrm, toward_mid) < 0:
        nrm = -nrm
    t = (pts - wrist) @ d
    L = t.max()
    across = np.cross(d, nrm)
    out = pts.copy()
    # Two knuckle bends towards the palm; the second knuckle rides on the first bend
    pivots = [wrist + d * (0.5 * L), wrist + d * (0.74 * L)]
    for n_, (k_frac, angle) in enumerate(((0.5, 85), (0.74, 75))):
        R = np.array(mathutils.Matrix.Rotation(math.radians(angle), 3, mathutils.Vector(across)))
        if (R @ d) @ nrm < 0:              # tips must move towards the palm (+nrm)
            R = np.array(mathutils.Matrix.Rotation(math.radians(-angle), 3, mathutils.Vector(across)))
        sel = t > k_frac * L
        pivot = pivots[n_]
        out[sel] = (out[sel] - pivot) @ R.T + pivot
        for m in range(n_ + 1, len(pivots)):
            pivots[m] = (pivots[m] - pivot) @ R.T + pivot
    inv = world.inverted()
    for i, p in zip(idx, out):
        me.vertices[i].co = inv @ mathutils.Vector(p)
    print(f"{side} hand curled ({len(idx)} vertices)")


if FIST:
    for side in ("Left", "Right"):
        curl_hand(side)

bm = bmesh.new()
bm.from_mesh(body.data)
bm.verts.ensure_lookup_table()
doomed = [bm.verts[i] for i, k in enumerate(keep) if not k]
bmesh.ops.delete(bm, geom=doomed, context="VERTS")
bm.to_mesh(body.data)
bm.free()
body.data.calc_loop_triangles()
print(f"kept {sum(keep)} of {len(keep)} vertices, {len(body.data.loop_triangles)} triangles")
body.name = body.name + "_Arms"

bpy.ops.object.select_all(action="DESELECT")
arm.select_set(True)
body.select_set(True)
bpy.ops.export_scene.fbx(
    filepath=dst, use_selection=True, object_types={"ARMATURE", "MESH"},
    apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
    mesh_smooth_type="OFF", path_mode="STRIP", embed_textures=False,
    add_leaf_bones=False, armature_nodetype="NULL", bake_anim=False)
print("wrote", dst)
