"""
Cut a Meshy sentry gun into moving parts for Fortress United (run inside Blender).

    blender -b --factory-startup --python tools/meshy/prepare_sentry.py -- \
        --glb path/to/Meshy_AI_model.glb --level 1 --name Sentry_L1 \
        --out Assets/FortressUnited/Art/Sentry [--tris 40000] [--preview out.png]

The model is centred on its base, turned so it faces -Y (Unity +Z), thinned,
then split by region into a hierarchy the game drives:

    Sentry_LN
      Base                      static
      Turret                    turns left/right   (origin on the yaw axis)
        Gun / Gun_L, Gun_R      tilts up/down      (origin on the pitch axle)
          Barrel...             kicks back / spins (origin on its axis)
            Muzzle...           marker at the tip
          Sensor (L1/L2)        the targeting head
            Eye                 marker on the lens
        Rockets (L3)            the rocket pod
          RocketMuzzle          marker

Level 1/2 (yoke): the axle line is found from the yoke arm tops, and Meshy's
twist of the gun relative to the yoke is measured from the barrels and taken
out. Level 3 (column): the gatling pods tilt on their own centres and their
barrel clusters spin.

Textures come out of the GLB as <Name>_Red_*.png, plus a blue team copy
(<Name>_Blue_BaseColor.png) made by turning the red markings blue, so both
teams get exactly the same shape.
"""
import argparse
import colorsys
import math
import os
import sys

import bmesh
import bpy
import mathutils
import numpy as np


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--glb", required=True)
    p.add_argument("--level", type=int, required=True)
    p.add_argument("--name", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--tris", type=int, default=60000)
    p.add_argument("--texsize", type=int, default=2048)
    p.add_argument("--preview", default="")
    return p.parse_args(argv)


# Region settings per level, in metres after centring (feet at z=0, base
# centred on the yaw axis, facing -Y). Measured from gridded renders.
LEVELS = {
    1: dict(kind="yoke", ring=0.46, hub_z=0.95, gun_z=1.06, hose_dx=0.25, sensor_z=1.37, forward="-Y"),
    2: dict(kind="yoke", ring=0.48, hub_z=1.03, gun_z=1.15, hose_dx=0.25, sensor_z=1.50, forward="-Y"),
    3: dict(kind="column", ring=0.74, pod_x=0.30, pod_z=(0.76, 1.22), rockets_z=1.32, forward="-X"),
}


# ---------------------------------------------------------------- textures

def textures_from_glb(name, out, size):
    """Writes BaseColor / Normal / MetallicSmoothness from the GLB's material."""
    mat = next(m for m in bpy.data.materials if m.use_nodes and any(n.type == "BSDF_PRINCIPLED" for n in m.node_tree.nodes))
    base = normal = mr = None
    for n in mat.node_tree.nodes:
        if n.type != "TEX_IMAGE" or n.image is None:
            continue
        for link in n.outputs["Color"].links:
            to = link.to_node
            if to.type == "BSDF_PRINCIPLED" and link.to_socket.name == "Base Color":
                base = n.image
            elif to.type == "NORMAL_MAP":
                normal = n.image
            elif to.type in ("SEPRGB", "SEPARATE_COLOR", "SEPXYZ"):
                mr = n.image
    paths = {}

    def save(img, suffix, transform=None):
        """Saves a new image (img.save() on a GLB's packed image would write the original)."""
        work = img.copy()
        if work.size[0] > size:
            work.scale(size, size)
        w, h = work.size
        px = np.array(work.pixels[:], dtype=np.float32).reshape(-1, 4)
        bpy.data.images.remove(work)
        if transform is not None:
            px = transform(px)
        out_img = bpy.data.images.new(f"{name}{suffix}", w, h, alpha=True)
        out_img.pixels[:] = px.ravel()
        path = os.path.join(out, f"{name}{suffix}.png")
        out_img.filepath_raw = path
        out_img.file_format = "PNG"
        out_img.save()
        paths[suffix] = path
        return out_img

    if base:
        save(base, "_Red_BaseColor")
        save(base, "_Blue_BaseColor", red_to_blue)
    if normal:
        save(normal, "_Normal")
    if mr:
        # glTF: G = roughness, B = metallic. URP: R = metallic, A = smoothness.
        def pack(px):
            out_px = np.empty_like(px)
            out_px[:, 0] = out_px[:, 1] = out_px[:, 2] = px[:, 2]
            out_px[:, 3] = 1 - px[:, 1]
            return out_px
        save(mr, "_MetallicSmoothness", pack)
    print("textures", sorted(paths))
    return paths


def red_to_blue(px):
    """Team recolour: saturated reds become the blue team's blue."""
    r, g, b = px[:, 0], px[:, 1], px[:, 2]
    mx = np.max(px[:, :3], axis=1)
    mn = np.min(px[:, :3], axis=1)
    sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    reddish = (r == mx) & (sat > 0.35) & (mx > 0.12) & (g < r * 0.75) & (b < r * 0.75)
    out_px = px.copy()
    # Same brightness and saturation, hue moved to the team blue
    v = mx[reddish]
    s = sat[reddish]
    h = 189 / 360   # TeamColours.Blue
    i = int(h * 6)
    f = h * 6 - i
    p_ = v * (1 - s)
    q = v * (1 - s * f)
    t = v * (1 - s * (1 - f))
    # h in sector 3: (p, q, v)
    out_px[reddish, 0] = p_
    out_px[reddish, 1] = q
    out_px[reddish, 2] = v
    print(f"recoloured {int(reddish.sum())} red pixels to blue")
    return out_px


# ---------------------------------------------------------------- mesh

def centres(obj):
    me = obj.data
    c = np.empty(len(me.polygons) * 3, dtype=np.float32)
    me.polygons.foreach_get("center", c)
    return c.reshape(-1, 3)


def verts(obj):
    me = obj.data
    c = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", c)
    return c.reshape(-1, 3)


def split(obj, mask, name):
    """Moves the faces where mask is True into a new object."""
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    obj.data.polygons.foreach_set("select", mask.astype(bool))
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_mode(type="FACE")
    bpy.ops.object.mode_set(mode="OBJECT")
    obj.data.polygons.foreach_set("select", mask.astype(bool))
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.separate(type="SELECTED")
    bpy.ops.object.mode_set(mode="OBJECT")
    new = next(o for o in bpy.context.selected_objects if o is not obj)
    new.name = name
    new.data.name = name
    print(f"part {name}: {int(mask.sum())} faces")
    return new


def decimate(obj, tris):
    obj.data.calc_loop_triangles()
    n = len(obj.data.loop_triangles)
    if n > tris:
        m = obj.modifiers.new("Decimate", "DECIMATE")
        m.ratio = max(0.001, tris / n)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=m.name)
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)


def set_origin(obj, point):
    """Moves the object's origin to a world point, leaving the mesh in place."""
    point = mathutils.Vector(point)
    obj.data.transform(mathutils.Matrix.Translation(-(point - obj.matrix_world.translation)))
    obj.matrix_world.translation = point


def parent(child, par):
    m = child.matrix_world.copy()
    child.parent = par
    child.matrix_world = m


def marker(name, point, par):
    e = bpy.data.objects.new(name, None)
    e.empty_display_size = 0.03
    bpy.context.scene.collection.objects.link(e)
    e.matrix_world = mathutils.Matrix.Translation(point)
    parent(e, par)
    print(f"marker {name} at {tuple(round(v, 3) for v in point)}")
    return e


def tip(obj, along=(0, -1, 0)):
    """Centre of the faces furthest along a direction (a muzzle)."""
    c = centres(obj) @ obj.matrix_world.to_3x3().transposed() + np.array(obj.matrix_world.translation)
    d = c @ np.array(along)
    far = c[d > d.max() - 0.03]
    return mathutils.Vector(far.mean(axis=0))


def bounds_centre(obj):
    v = verts(obj) @ obj.matrix_world.to_3x3().transposed() + np.array(obj.matrix_world.translation)
    return mathutils.Vector((v.min(axis=0) + v.max(axis=0)) / 2)


# ---------------------------------------------------------------- main

def main():
    a = parse_args()
    cfg = LEVELS[a.level]
    os.makedirs(a.out, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=a.glb)
    body = next(o for o in bpy.context.scene.objects if o.type == "MESH")
    for o in list(bpy.context.scene.objects):
        if o is not body and o.type == "MESH":
            bpy.data.objects.remove(o)
    bpy.context.view_layer.objects.active = body
    body.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    textures_from_glb(a.name, a.out, a.texsize)

    # Centre on the base, feet at 0, facing -Y
    v = verts(body)
    zmin = v[:, 2].min()
    low = v[v[:, 2] < zmin + 0.12]
    cx, cy = (low[:, 0].min() + low[:, 0].max()) / 2, (low[:, 1].min() + low[:, 1].max()) / 2
    body.data.transform(mathutils.Matrix.Translation((-cx, -cy, -zmin)))
    if cfg["forward"] == "-X":
        body.data.transform(mathutils.Matrix.Rotation(math.radians(90), 4, "Z"))   # -X becomes -Y

    # glTF splits vertices along UV seams; weld them so decimating doesn't open cracks
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(body.data)
    nv = len(bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bm.to_mesh(body.data)
    print(f"welded {nv - len(bm.verts)} duplicate vertices")
    bm.free()

    # Thin it first (3M faces is slow to cut), parts get thinned again after
    decimate(body, a.tris * 8)

    c = centres(body)
    body.name = "Base"
    root = bpy.data.objects.new(a.name, None)
    bpy.context.scene.collection.objects.link(root)
    ring = cfg["ring"]

    if cfg["kind"] == "yoke":
        # Where the yoke arm tops are (front to back)
        arm_faces = c[(np.abs(c[:, 2] - cfg["hub_z"]) < 0.06) & (np.abs(c[:, 0]) > 0.28)]
        arm_y0, arm_y1 = np.percentile(arm_faces[:, 1], [5, 95])
        # Gun: everything above the yoke tops, plus hoses hanging between the
        # arms or in front of / behind them
        low_band = c[:, 2] >= cfg["hub_z"] - 0.08
        gun_mask = (c[:, 2] >= cfg["gun_z"]) | (low_band & (np.abs(c[:, 0]) < cfg["hose_dx"])) \
            | (low_band & ((c[:, 1] < arm_y0 - 0.05) | (c[:, 1] > arm_y1 + 0.05)))
        gun = split(body, gun_mask, "Gun")
        c = centres(body)
        turret = split(body, c[:, 2] >= ring, "Turret")

        # Pitch axle: through the yoke arm tops (outside the hose band), at hub height
        tc = centres(turret)
        arms = tc[(np.abs(tc[:, 2] - cfg["hub_z"]) < 0.06) & (np.abs(tc[:, 0]) > 0.28)]
        axle = mathutils.Vector(((arms[:, 0].min() + arms[:, 0].max()) / 2, float(np.median(arms[:, 1])), cfg["hub_z"]))
        print(f"axle at {tuple(round(x, 3) for x in axle)} from {len(arms)} arm faces")

        # Meshy twisted the gun relative to the yoke: measure from the barrels, straighten
        gv = verts(gun)
        horiz = np.hypot(gv[:, 0] - axle.x, gv[:, 1] - axle.y)
        far = gv[horiz > horiz.max() - 0.25]
        d = far[:, :2].mean(axis=0) - np.array([axle.x, axle.y])
        twist = math.degrees(math.atan2(d[0], -d[1]))   # 0 when pointing -Y, + towards +X
        print(f"gun twist {twist:.1f} degrees, straightening")
        gun.data.transform(mathutils.Matrix.Translation(axle) @ mathutils.Matrix.Rotation(math.radians(-twist), 4, "Z")
                           @ mathutils.Matrix.Translation(-axle))

        # Barrels: thin parts well in front of the axle
        gc = centres(gun)
        gv = verts(gun)
        front_y = gv[:, 1].min()
        barrel_mask = (gc[:, 1] < axle.y - 0.55) & (np.abs(gc[:, 0] - axle.x) < 0.14)
        barrel = split(gun, barrel_mask, "Barrel")
        gc = centres(gun)
        sensor = split(gun, gc[:, 2] >= cfg["sensor_z"], "Sensor")

        set_origin(turret, (0, 0, ring))
        set_origin(gun, axle)
        bc = bounds_centre(barrel)
        set_origin(barrel, (bc.x, verts(barrel)[:, 1].max(), bc.z))   # rear of the barrel
        set_origin(sensor, bounds_centre(sensor))
        parent(turret, root)
        parent(body, root)
        parent(gun, turret)
        parent(barrel, gun)
        parent(sensor, gun)

        # One muzzle per barrel tube (twin on L2, stacked or side by side)
        bc_all = centres(barrel) + np.array(barrel.matrix_world.translation)
        tipy = bc_all[:, 1].min()
        length = bc_all[:, 1].max() - tipy
        front = bc_all[bc_all[:, 1] < tipy + 0.3 * length]      # front third: separate tubes
        tips = bc_all[bc_all[:, 1] < tipy + 0.08]
        # Two tubes show as two clusters with a clear gap between them
        def widest_gap(values):
            v = np.sort(values)
            i = int(np.argmax(np.diff(v))) if len(v) > 1 else 0
            return (v[i + 1] - v[i], (v[i + 1] + v[i]) / 2) if len(v) > 1 else (0, 0)
        gaps = {ax: widest_gap(front[:, ax]) for ax in (0, 2)}
        axis = max(gaps, key=lambda ax: gaps[ax][0])
        if gaps[axis][0] > 0.015:
            mid = gaps[axis][1]
            tips = front[front[:, 1] < tipy + 0.08 + 0.1]
            names = ("L", "R") if axis == 0 else ("Low", "High")
            for side, sel in ((names[0], tips[:, axis] < mid), (names[1], tips[:, axis] >= mid)):
                marker(f"Muzzle_{side}", mathutils.Vector(tips[sel].mean(axis=0)), barrel)
        else:
            marker("Muzzle", mathutils.Vector(tips.mean(axis=0)), barrel)
        marker("Eye", tip(sensor), sensor)
        parts = {"Base": body, "Turret": turret, "Gun": gun, "Barrel": barrel, "Sensor": sensor}
        budget = {"Base": 0.28, "Turret": 0.24, "Gun": 0.32, "Barrel": 0.06, "Sensor": 0.10}

    else:  # column (L3)
        pz0, pz1 = cfg["pod_z"]
        rockets = split(body, c[:, 2] >= cfg["rockets_z"], "Rockets")
        c = centres(body)
        pod_r = split(body, (c[:, 0] < -cfg["pod_x"]) & (c[:, 2] >= pz0) & (c[:, 2] <= pz1), "Gun_R")
        c = centres(body)
        pod_l = split(body, (c[:, 0] > cfg["pod_x"]) & (c[:, 2] >= pz0) & (c[:, 2] <= pz1), "Gun_L")
        c = centres(body)
        turret = split(body, c[:, 2] >= ring, "Turret")
        set_origin(turret, (0, 0, ring))
        parent(turret, root)
        parent(body, root)
        parts = {"Base": body, "Turret": turret, "Rockets": rockets}
        budget = {"Base": 0.26, "Turret": 0.22, "Rockets": 0.10}
        for side, pod in (("L", pod_l), ("R", pod_r)):
            # Tilt about the middle of the pod itself (not counting the long barrel)
            pcs = centres(pod)
            pc = mathutils.Vector(np.median(pcs, axis=0))
            set_origin(pod, pc)
            parent(pod, turret)
            # Barrel cluster: the part of the pod well forward of its centre
            cc = centres(pod) + np.array(pod.matrix_world.translation)
            pv = verts(pod) + np.array(pod.matrix_world.translation)
            ymin = pv[:, 1].min()
            bmask = cc[:, 1] < pc.y - 0.3
            barrel = split(pod, bmask, f"Barrel_{side}")
            bv = verts(barrel) + np.array(barrel.matrix_world.translation)
            axis_pt = mathutils.Vector(((bv[:, 0].min() + bv[:, 0].max()) / 2, bv[:, 1].max(), (bv[:, 2].min() + bv[:, 2].max()) / 2))
            set_origin(barrel, axis_pt)
            parent(barrel, pod)
            marker(f"Muzzle_{side}", tip(barrel), barrel)
            parts[f"Gun_{side}"] = pod
            parts[f"Barrel_{side}"] = barrel
            budget[f"Gun_{side}"] = 0.15
            budget[f"Barrel_{side}"] = 0.06
        set_origin(rockets, bounds_centre(rockets))
        parent(rockets, turret)
        marker("RocketMuzzle", tip(rockets), rockets)

    total = 0
    for nm, obj in parts.items():
        total += decimate(obj, int(a.tris * budget[nm]))
    print(f"triangles total {total}")

    # Smooth shading with sharp edges kept, one material named for the importer
    mat = bpy.data.materials.new(f"{a.name}_Body")
    for obj in parts.values():
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        try:
            bpy.ops.object.shade_smooth_by_angle(angle=0.698)
        except (AttributeError, RuntimeError):
            bpy.ops.object.shade_smooth()
        obj.data.materials.clear()
        obj.data.materials.append(mat)

    bpy.ops.object.select_all(action="DESELECT")
    for o in [root] + list(root.children_recursive):
        o.select_set(True)
    out_fbx = os.path.join(a.out, f"{a.name}.fbx")
    bpy.ops.export_scene.fbx(
        filepath=out_fbx, use_selection=True, object_types={"MESH", "EMPTY"},
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
        mesh_smooth_type="OFF", path_mode="STRIP", bake_anim=False, add_leaf_bones=False)
    print(f"wrote {out_fbx} ({os.path.getsize(out_fbx) / 1e6:.1f} MB)")

    if a.preview:
        preview(a, parts)


def preview(a, parts):
    """Each part in its own colour, from the front-left."""
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "BLENDER_EEVEE"
    sc.render.resolution_x = sc.render.resolution_y = 700
    w = bpy.data.worlds.new("w"); sc.world = w; w.color = (0.45, 0.45, 0.47)
    sun = bpy.data.objects.new("s", bpy.data.lights.new("s", "SUN")); sc.collection.objects.link(sun)
    sun.rotation_euler = (0.8, 0.2, 0.6); sun.data.energy = 3
    colours = [(0.8, 0.8, 0.8, 1), (0.2, 0.5, 1, 1), (1, 0.3, 0.2, 1), (1, 0.85, 0.1, 1), (0.3, 0.9, 0.4, 1), (0.8, 0.3, 0.9, 1), (0.2, 0.9, 0.9, 1), (1, 0.6, 0.2, 1)]
    for i, obj in enumerate(parts.values()):
        m = bpy.data.materials.new(f"prev{i}"); m.use_nodes = True
        m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = colours[i % len(colours)]
        obj.data.materials.clear(); obj.data.materials.append(m)
    # A small white ball on each moving part's pivot
    for nm, obj in parts.items():
        if nm == "Base":
            continue
        bpy.ops.mesh.primitive_uv_sphere_add(radius=0.025, location=obj.matrix_world.translation)
    cam = bpy.data.objects.new("c", bpy.data.cameras.new("c")); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.lens = 40
    cam.location = (-2.2, -2.6, 1.9)
    cam.rotation_euler = (mathutils.Vector((0, 0, 0.8)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    sc.render.filepath = a.preview
    bpy.ops.render.render(write_still=True)
    cam.location = (3.2, 0, 1.0)
    cam.rotation_euler = (mathutils.Vector((0, 0, 0.9)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    sc.render.filepath = a.preview.replace(".png", "_side.png")
    bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    main()
