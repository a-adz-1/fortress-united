"""
Make a rigged Meshy character game-ready for Fortress United (run inside Blender).

    blender -b --factory-startup --python tools/meshy/prepare_character.py -- \
        --fbx path/to/Meshy_AI_..._Animation_Walking_frame_rate_30.fbx \
        --name Medic_Blue --out Assets/FortressUnited/Art/Characters/Medic \
        [--texsize 2048] [--tris 40000] [--marker ChestScreen:Spine:1.236] \
        [--anim Jump=path/to/medic_blue_jump.glb ...]

What it does:
- Keeps the armature, skin weights and animation; strips embedded textures.
- Decimates only if the mesh is over --tris (Meshy rigs are usually ~20k).
- Names the animation after the file (..._Animation_Walking_... -> Walking).
- --anim NAME=FILE (repeatable, .glb or .fbx) adds the animation from another
  export of the same rig (Meshy downloads each animation separately); the
  file's own mesh and helpers are thrown away. All clips export as takes.
- Writes <Name>_BaseColor.png, <Name>_MetallicSmoothness.png (and _Normal /
  _Emission if present) for Editor/ArtPostprocessor.cs.
- --marker NAME:BONE:HEIGHT adds an empty on the front of the body at that
  height above the feet (found by casting a ray at the rest pose), parented
  to BONE so it moves with it. The game finds it by name (e.g. the chest
  screen that shows a body's flatline).
- Exports <Name>.fbx in metres with Unity axes.

The character must face -Y in Blender (Meshy's default front).
"""
import argparse
import os
import re
import sys

import bpy
import mathutils

sys.path.insert(0, os.path.dirname(__file__))
from prepare_model import find_texture, save_resized, pack_metallic_smoothness  # noqa: E402


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--fbx", required=True)
    p.add_argument("--name", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--texsize", type=int, default=2048)
    p.add_argument("--tris", type=int, default=40000)
    p.add_argument("--marker", action="append", default=[])
    p.add_argument("--anim", action="append", default=[])
    return p.parse_args(argv)


def fcurves(action):
    """An action's curves (Blender 4.4+ keeps them in layers / channel bags)."""
    if hasattr(action, "fcurves"):
        return list(action.fcurves)
    return [fc for layer in action.layers for strip in layer.strips for bag in strip.channelbags for fc in bag.fcurves]


def import_animation(name, path, arm):
    """Brings in the action from another export of the same rig, renamed NAME."""
    before_objects = set(bpy.data.objects)
    before_actions = set(bpy.data.actions)
    if path.lower().endswith((".glb", ".gltf")):
        bpy.ops.import_scene.gltf(filepath=path)
    else:
        bpy.ops.import_scene.fbx(filepath=path)
    new_actions = [a for a in bpy.data.actions if a not in before_actions]
    # Keep the action that animates the skeleton, drop everything else
    action = next((a for a in new_actions if any("pose.bones" in fc.data_path for fc in fcurves(a))), None)
    for o in [o for o in bpy.data.objects if o not in before_objects]:
        bpy.data.objects.remove(o, do_unlink=True)
    for a in new_actions:
        if a is not action:
            bpy.data.actions.remove(a)
    if action is None:
        raise SystemExit(f"{path}: no skeleton animation found")
    missing = {fc.data_path.split('"')[1] for fc in fcurves(action) if "pose.bones" in fc.data_path} - set(arm.data.bones.keys())
    if missing:
        raise SystemExit(f"{path}: bones not in the rig: {sorted(missing)}")
    action.name = name
    action.use_fake_user = True
    print(f"animation {name}: frames {tuple(round(f, 1) for f in action.frame_range)} from {os.path.basename(path)}")
    return action


def main():
    a = parse_args()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=a.fbx)
    scene = bpy.context.scene

    arm = next(o for o in scene.objects if o.type == "ARMATURE")
    body = next(o for o in scene.objects if o.type == "MESH")
    body.name = a.name
    arm.name = f"{a.name}_Rig"

    # Animation name from the file name
    m = re.search(r"Animation_([A-Za-z0-9]+)", os.path.basename(a.fbx))
    clip = m.group(1) if m else "Take"
    if arm.animation_data and arm.animation_data.action:
        action = arm.animation_data.action
        action.name = clip
        # The exported take uses the scene's name and frame range
        scene.name = clip
        scene.frame_start, scene.frame_end = (int(f) for f in action.frame_range)
        print(f"animation {clip}: frames {tuple(action.frame_range)}")

    if a.anim:
        scene.render.fps = 30   # glTF stores seconds; keep frames at 30 fps
        for spec in a.anim:
            nm, path = spec.split("=", 1)
            import_animation(nm, path, arm)
        if arm.animation_data and arm.animation_data.action:
            arm.animation_data.action.use_fake_user = True

    body.data.calc_loop_triangles()
    before = len(body.data.loop_triangles)
    if before > a.tris:
        mod = body.modifiers.new("Decimate", "DECIMATE")
        mod.ratio = a.tris / before
        bpy.context.view_layer.objects.active = body
        bpy.ops.object.modifier_move_to_index(modifier=mod.name, index=0)   # before the armature
        bpy.ops.object.modifier_apply(modifier=mod.name)
    body.data.calc_loop_triangles()
    print(f"triangles {before} -> {len(body.data.loop_triangles)}")

    # Textures
    os.makedirs(a.out, exist_ok=True)
    folder = os.path.dirname(a.fbx)
    base = find_texture(folder, "")
    if base:
        save_resized(base, os.path.join(a.out, f"{a.name}_BaseColor.png"), a.texsize)
    normal = find_texture(folder, "_normal")
    if normal:
        save_resized(normal, os.path.join(a.out, f"{a.name}_Normal.png"), a.texsize)
    metal, rough = find_texture(folder, "_metallic"), find_texture(folder, "_roughness")
    if metal or rough:
        pack_metallic_smoothness(metal, rough, os.path.join(a.out, f"{a.name}_MetallicSmoothness.png"), a.texsize // 2)
    emission = find_texture(folder, "_emission")
    if emission:
        save_resized(emission, os.path.join(a.out, f"{a.name}_Emission.png"), a.texsize)
    mat = bpy.data.materials.new(f"{a.name}_Body")
    body.data.materials.clear()
    body.data.materials.append(mat)

    # Markers, placed against the rest pose
    exported = [arm, body]
    arm.data.pose_position = "REST"
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    pts = [body.matrix_world @ mathutils.Vector(c) for c in body.bound_box]
    feet = min(p.z for p in pts)
    cx = (min(p.x for p in pts) + max(p.x for p in pts)) / 2
    for spec in a.marker:
        name, bone, height = spec.split(":")
        z = feet + float(height)
        ok, loc, normal_ws, *_ = scene.ray_cast(depsgraph, mathutils.Vector((cx, -5, z)), mathutils.Vector((0, 1, 0)))
        if not ok:
            raise SystemExit(f"marker {name}: ray at height {height} missed the body")
        empty = bpy.data.objects.new(name, None)
        empty.empty_display_size = 0.05
        scene.collection.objects.link(empty)
        empty.parent = arm
        empty.parent_type = "BONE"
        empty.parent_bone = bone
        bpy.context.view_layer.update()
        empty.matrix_world = mathutils.Matrix.Translation(loc + normal_ws * 0.004)
        exported.append(empty)
        print(f"marker {name} on {bone} at {tuple(round(v, 3) for v in loc)} normal {tuple(round(v, 2) for v in normal_ws)}")
    arm.data.pose_position = "POSE"

    bpy.ops.object.select_all(action="DESELECT")
    for o in exported:
        o.select_set(True)
    out_fbx = os.path.join(a.out, f"{a.name}.fbx")
    bpy.ops.export_scene.fbx(
        filepath=out_fbx, use_selection=True, object_types={"ARMATURE", "MESH", "EMPTY"},
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
        mesh_smooth_type="OFF", path_mode="STRIP", embed_textures=False,
        add_leaf_bones=False, armature_nodetype="NULL",
        bake_anim=True, bake_anim_use_all_actions=bool(a.anim), bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.5)
    print(f"wrote {out_fbx} ({os.path.getsize(out_fbx) / 1e6:.1f} MB)")


main()
