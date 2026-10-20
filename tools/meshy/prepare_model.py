"""
Make a Meshy export game-ready for Fortress United (run inside Blender).

    blender -b --factory-startup --python tools/meshy/prepare_model.py -- \
        --fbx path/to/meshy_export.fbx --name Medkit_Blue \
        --out Assets/FortressUnited/Art/Medkit --tris 30000 --width 0.42 \
        [--texsize 2048] [--screen 0.19,0.64,0,0.004] [--preview out.png]

What it does:
- Imports the FBX, joins it into one mesh, centres it and scales it so its
  width (X) is --width metres.
- Decimates to about --tris triangles (Meshy exports are 1-2 million).
- Writes textures next to the FBX, named for the Unity importer
  (Editor/ArtPostprocessor.cs): <Name>_BaseColor.png, <Name>_Normal.png and
  <Name>_MetallicSmoothness.png (R = metallic, A = 1 - roughness, as URP
  wants), plus <Name>_Emission.png when Meshy provided one.
- With --screen: adds Screen_Front and Screen_Back planes on the glass, found
  by casting a ray at the model. The value is width and height as fractions
  of the model's width/height, then x and z offsets of the centre (same
  fractions). Each plane has UVs 0-1 the right way up when seen from its own
  side, so the game can draw a live display on it.
- Exports <Name>.fbx (metres, Unity axes) and optionally a preview render.

The model is expected to face -Y in Blender (Meshy's default front).
"""
import argparse
import os
import sys

import bpy
import mathutils


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--fbx", required=True)
    p.add_argument("--name", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--tris", type=int, default=30000)
    p.add_argument("--width", type=float, required=True, help="real width in metres")
    p.add_argument("--texsize", type=int, default=2048)
    p.add_argument("--screen", default="")
    p.add_argument("--preview", default="")
    return p.parse_args(argv)


def find_texture(folder, suffix):
    """Meshy names textures <base>.png, <base>_normal.png, <base>_metallic.png, ..."""
    for f in sorted(os.listdir(folder)):
        low = f.lower()
        if not low.endswith(".png"):
            continue
        stem = low[:-4]
        if suffix == "" and not any(stem.endswith(s) for s in ("_normal", "_metallic", "_roughness", "_emission")):
            return os.path.join(folder, f)
        if suffix and stem.endswith(suffix):
            return os.path.join(folder, f)
    return None


def save_resized(src, dst, size):
    img = bpy.data.images.load(src)
    if img.size[0] > size or img.size[1] > size:
        img.scale(min(size, img.size[0]), min(size, img.size[1]))
    img.filepath_raw = dst
    img.file_format = "PNG"
    img.save()
    return img


def pack_metallic_smoothness(metal_path, rough_path, dst, size):
    """URP's metallic map: R = metallic, A = smoothness (1 - roughness)."""
    import numpy as np
    m = bpy.data.images.load(metal_path) if metal_path else None
    r = bpy.data.images.load(rough_path) if rough_path else None
    w = h = size
    for img in (m, r):
        if img is not None and (img.size[0] != w or img.size[1] != h):
            img.scale(w, h)
    metal = np.array(m.pixels[:], dtype=np.float32).reshape(-1, 4)[:, 0] if m else np.zeros(w * h, np.float32)
    rough = np.array(r.pixels[:], dtype=np.float32).reshape(-1, 4)[:, 0] if r else np.full(w * h, 0.5, np.float32)
    out = np.zeros((w * h, 4), dtype=np.float32)
    out[:, 0] = metal
    out[:, 1] = metal
    out[:, 2] = metal
    out[:, 3] = 1.0 - rough
    img = bpy.data.images.new("ms", w, h, alpha=True)
    img.pixels[:] = out.ravel()
    img.filepath_raw = dst
    img.file_format = "PNG"
    img.save()


def add_screen(name, corners, material):
    """A quad from corners in order bottom-left, bottom-right, top-right, top-left (seen from its front)."""
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(c) for c in corners], [], [(0, 1, 2, 3)])
    mesh.update()
    uv = mesh.uv_layers.new(name="UVMap")
    for loop, coord in zip(mesh.loops, ((0, 0), (1, 0), (1, 1), (0, 1))):
        uv.data[loop.index].uv = coord
    mesh.materials.append(material)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def main():
    a = parse_args()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=a.fbx)

    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    body = bpy.context.view_layer.objects.active
    body.name = a.name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    # Centre and scale to real size
    vs = [v.co for v in body.data.vertices]
    mins = mathutils.Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
    maxs = mathutils.Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
    centre = (mins + maxs) / 2
    scale = a.width / (maxs.x - mins.x)
    for v in body.data.vertices:
        v.co = (v.co - centre) * scale
    size = (maxs - mins) * scale
    print(f"size {size.x:.3f} x {size.y:.3f} x {size.z:.3f} m")

    # Decimate
    body.data.calc_loop_triangles()
    before = len(body.data.loop_triangles)
    if before > a.tris:
        mod = body.modifiers.new("Decimate", "DECIMATE")
        mod.ratio = a.tris / before
        bpy.ops.object.modifier_apply(modifier=mod.name)
    body.data.calc_loop_triangles()
    print(f"triangles {before} -> {len(body.data.loop_triangles)}")

    # Smooth shading, keeping edges sharper than 40 degrees
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    try:
        bpy.ops.object.shade_smooth_by_angle(angle=0.698)   # Blender 4.1+
    except (AttributeError, RuntimeError):
        bpy.ops.object.shade_smooth()
        if hasattr(body.data, "use_auto_smooth"):
            body.data.use_auto_smooth = True
            body.data.auto_smooth_angle = 0.698

    os.makedirs(a.out, exist_ok=True)
    folder = os.path.dirname(a.fbx)

    # Textures, named for Editor/ArtPostprocessor.cs
    base = find_texture(folder, "")
    if base:
        save_resized(base, os.path.join(a.out, f"{a.name}_BaseColor.png"), a.texsize)
    normal = find_texture(folder, "_normal")
    if normal:
        save_resized(normal, os.path.join(a.out, f"{a.name}_Normal.png"), a.texsize)
    metal = find_texture(folder, "_metallic")
    rough = find_texture(folder, "_roughness")
    if metal or rough:
        pack_metallic_smoothness(metal, rough, os.path.join(a.out, f"{a.name}_MetallicSmoothness.png"), a.texsize // 2)
    emission = find_texture(folder, "_emission")
    if emission:
        save_resized(emission, os.path.join(a.out, f"{a.name}_Emission.png"), a.texsize)

    # Material named for the importer; textures are hooked up in Unity
    mat = bpy.data.materials.new(f"{a.name}_Body")
    body.data.materials.clear()
    body.data.materials.append(mat)

    exported = [body]
    if a.screen:
        wf, hf, xf, zf = (float(x) for x in a.screen.split(","))
        w, h = size.x * wf, size.z * hf
        cx, cz = size.x * xf, size.z * zf
        screen_mat = bpy.data.materials.new("Screen")
        depsgraph = bpy.context.evaluated_depsgraph_get()
        reach = size.y * 4 + 1

        def hit(from_y, direction):
            ok, loc, *_ = bpy.context.scene.ray_cast(depsgraph, mathutils.Vector((cx, from_y, cz)), direction, distance=reach)
            if not ok:
                raise SystemExit(f"screen ray from y={from_y} missed the model")
            return loc.y

        gap = 0.0005  # just in front of the glass
        yf = hit(-reach / 2, mathutils.Vector((0, 1, 0))) - gap
        yb = hit(reach / 2, mathutils.Vector((0, -1, 0))) + gap
        x0, x1, z0, z1 = cx - w / 2, cx + w / 2, cz - h / 2, cz + h / 2
        # Front is seen from -Y: right is +X
        exported.append(add_screen("Screen_Front", [(x0, yf, z0), (x1, yf, z0), (x1, yf, z1), (x0, yf, z1)], screen_mat))
        # Back is seen from +Y: right is -X
        exported.append(add_screen("Screen_Back", [(x1, yb, z0), (x0, yb, z0), (x0, yb, z1), (x1, yb, z1)], screen_mat))
        print(f"screens {w:.3f} x {h:.3f} m at y {yf:.4f} / {yb:.4f}")

    bpy.ops.object.select_all(action="DESELECT")
    for o in exported:
        o.select_set(True)
    out_fbx = os.path.join(a.out, f"{a.name}.fbx")
    bpy.ops.export_scene.fbx(
        filepath=out_fbx, use_selection=True, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", mesh_smooth_type="OFF", path_mode="STRIP",
        bake_anim=False, add_leaf_bones=False)
    print(f"wrote {out_fbx} ({os.path.getsize(out_fbx) / 1e6:.1f} MB)")

    if a.preview:
        render_preview(a, exported, size)


def render_preview(a, objects, size):
    scene = bpy.context.scene
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items]
    scene.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in engines else "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = 900, 700
    # Show the textures on the body for the preview
    body = objects[0]
    mat = body.data.materials[0]
    mat.use_nodes = True
    tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
    path = os.path.join(a.out, f"{a.name}_BaseColor.png")
    if os.path.exists(path):
        tex.image = bpy.data.images.load(path)
        mat.node_tree.links.new(tex.outputs["Color"], mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
    for o in objects[1:]:
        m = o.data.materials[0]
        m.use_nodes = True
        bsdf = m.node_tree.nodes["Principled BSDF"]
        bsdf.inputs["Base Color"].default_value = (1, 0, 1, 1)  # magenta: where the live display goes
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    scene.collection.objects.link(sun)
    sun.rotation_euler = (0.6, 0.2, 0.4)
    sun.data.energy = 4
    world = bpy.data.worlds.new("w")
    scene.world = world
    world.color = (0.35, 0.35, 0.38)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = max(size) * 1.15
    cam.location = mathutils.Vector((0, -max(size) * 3, 0))
    cam.rotation_euler = (mathutils.Vector((0, 0, 0)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = a.preview
    bpy.ops.render.render(write_still=True)


main()
