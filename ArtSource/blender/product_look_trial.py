# -*- coding: utf-8 -*-
# 製品らしく見せる決まりの試作：運搬ロボットの見本を「いま（引き伸ばし）」と「決まりを当てた形」で描いて並べる。
# 使い方：blender -b --python poc_product.py -- <dna.json> <out_dir> <name>
import bpy, bmesh, math, json, sys, os
from mathutils import Vector
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:]
DNA_PATH, OUT, NAME = argv[0], argv[1], argv[2]
dna = json.load(open(DNA_PATH, encoding="utf-8"))
os.makedirs(OUT, exist_ok=True)

DEFAULT_SHAPE = {"container": "box", "sensor": "sphere", "stabilizer": "box", "battery": "box", "frame": "box", "plate": "box"}
HOUSING = {"body", "container", "stabilizer", "battery", "frame", "storage"}

_lo = min(p["position"][1] - p["scale"][1] / 2 for p in dna["parts"])
LIFT = 0.3 - _lo if _lo < 0.05 else 0.0  # 頭に付ける道具などは原点より下にあるので、床から浮かせて撮る

def U(p):  # Unity (x 右, y 上, z 前) → Blender (x 右, y 奥, z 上)。前をカメラ側 (-Y) に向ける
    return Vector((p[0], -p[2], p[1] + LIFT))

def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)

def mat(name, col, rough=0.4, metal=0.0, emit=None, emit_str=0.0, coat=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (*col, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    if coat and "Coat Weight" in b.inputs: b.inputs["Coat Weight"].default_value = coat
    if emit:
        b.inputs["Emission Color"].default_value = (*emit, 1)
        b.inputs["Emission Strength"].default_value = emit_str
    return m

def link(obj): bpy.context.scene.collection.objects.link(obj); return obj

def mesh_obj(name, bm):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    return link(bpy.data.objects.new(name, me))

def apply_mods(obj):
    bpy.context.view_layer.objects.active = obj
    for o in bpy.context.selected_objects: o.select_set(False)
    obj.select_set(True)
    for m in list(obj.modifiers): bpy.ops.object.modifier_apply(modifier=m.name)

def bevel(obj, w, seg=4, angle=35):
    m = obj.modifiers.new("Bevel", "BEVEL"); m.width = w; m.segments = seg
    m.limit_method = "ANGLE"; m.angle_limit = math.radians(angle); m.harden_normals = False
    return m

def smooth_shade(obj):
    for p in obj.data.polygons: p.use_smooth = True

# ---------- 形 ----------
def prism(name, outline, h, z0=0.0):
    """外形の点列（xy）を高さ h だけ押し出す（底は z0）。"""
    bm = bmesh.new()
    bot = [bm.verts.new((x, y, z0)) for x, y in outline]
    top = [bm.verts.new((x, y, z0 + h)) for x, y in outline]
    n = len(outline)
    bm.faces.new(list(reversed(bot))); bm.faces.new(top)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new([bot[i], bot[j], top[j], top[i]])
    return mesh_obj(name, bm)

def ellipse(a, b, n=64): return [(a * math.cos(t), b * math.sin(t)) for t in np.linspace(0, 2 * math.pi, n, endpoint=False)]

def superellipse(a, b, e=4.0, n=96):
    pts = []
    for t in np.linspace(0, 2 * math.pi, n, endpoint=False):
        c, s = math.cos(t), math.sin(t)
        pts.append((a * math.copysign(abs(c) ** (2 / e), c), b * math.copysign(abs(s) ** (2 / e), s)))
    return pts

def rbox(a, b): return [(-a, -b), (a, -b), (a, b), (-a, b)]

# ---------- いま：単位の形の角を丸め、DNA の大きさで引き伸ばす ----------
def build_current(parts, mats):
    for p in parts:
        t = p["type"]; shape = p.get("shape") or DEFAULT_SHAPE.get(t, "cylinder")
        sx, sy, sz = p["scale"]
        if shape == "box":
            o = prism(p["id"], rbox(0.5, 0.5), 1.0, -0.5)
        elif shape == "sphere":
            bpy.ops.mesh.primitive_uv_sphere_add(radius=0.5, segments=32, ring_count=16); o = bpy.context.object
        else:
            o = prism(p["id"], ellipse(0.5, 0.5), 1.0, -0.5)
        if shape != "sphere": bevel(o, 0.08, 3); apply_mods(o)
        smooth_shade(o)
        if t == "wheel":
            o.rotation_euler = (0, math.radians(90), 0)
            o.scale = (sx, sz, sy)   # 円柱の軸（高さ＝幅 0.06）を回して x に
        else:
            o.scale = (sx, sz, sy)
        o.location = U(p["position"])
        o.data.materials.append(mats.get(t, mats["default"]))

# ---------- 決まりを当てた形 ----------
def housing_mesh(p):
    """外装の部品を実寸で作り、角の丸み（半径一定）を付ける。楕円の円柱は角の丸い四角（スーパー楕円）にする。"""
    t = p["type"]; shape = p.get("shape") or DEFAULT_SHAPE.get(t, "cylinder")
    sx, sy, sz = p["scale"]; c = U(p["position"])
    if shape == "box":
        o = prism(p["id"], rbox(sx / 2, sz / 2), sy, -sy / 2)
        bevel(o, min(0.03, min(sx, sy, sz) * 0.2), 5)
    elif abs(sx - sz) > 0.02:
        o = prism(p["id"], superellipse(sx / 2, sz / 2, 4.0), sy, -sy / 2)
        bevel(o, min(0.025, sy * 0.2), 5)
    else:
        o = prism(p["id"], ellipse(sx / 2, sz / 2), sy, -sy / 2)
        bevel(o, min(0.025, sy * 0.2), 5)
    apply_mods(o); o.location = c
    return o

def build_product(parts, mats):
    housing = [p for p in parts if p["type"] in HOUSING]
    objs = [housing_mesh(p) for p in housing]
    # 1. 外装を一体にする：重ね合わせ → 細かい格子で面を作り直す → 少しならす（つなぎ目に小さな丸み）
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.join()
    shell = bpy.context.object; shell.name = "Shell"
    r = shell.modifiers.new("Remesh", "REMESH"); r.mode = "VOXEL"; r.voxel_size = 0.004
    s = shell.modifiers.new("Smooth", "CORRECTIVE_SMOOTH"); s.factor = 0.5; s.iterations = 6; s.use_only_smooth = True
    dm = shell.modifiers.new("Dec", "DECIMATE"); dm.ratio = 0.05
    apply_mods(shell); smooth_shade(shell)
    # 2. 上下で塗り分けて継ぎ目を入れる（いちばん下の外装の上面の高さで分ける）
    low = min(housing, key=lambda p: p["position"][1])
    part_z = low["position"][1] + low["scale"][1] / 2
    shell.data.materials.append(mats["shell_dark"]); shell.data.materials.append(mats["shell_light"]); shell.data.materials.append(mats["seam"])
    for f in shell.data.polygons:
        z = f.center.z
        f.material_index = 2 if abs(z - part_z) < 0.003 else (1 if z > part_z else 0)
    # 外装の外接箱（つなぎの計算用）
    xs = [v.co.x for v in shell.data.vertices]; ys = [v.co.y for v in shell.data.vertices]; zs = [v.co.z for v in shell.data.vertices]
    sb = (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs))

    for p in parts:
        t = p["type"]; c = U(p["position"]); sx, sy, sz = p["scale"]
        if t == "wheel":
            # 3. 車輪：丸みのあるタイヤ＋金属のハブ＋外装までの軸
            rad, wid = sx / 2, sy
            bpy.ops.mesh.primitive_cylinder_add(radius=rad, depth=wid, vertices=64, location=c, rotation=(0, math.radians(90), 0))
            tyre = bpy.context.object; bevel(tyre, wid * 0.35, 6, 30); apply_mods(tyre); smooth_shade(tyre); tyre.data.materials.append(mats["rubber"])
            side = 1 if c.x > 0 else -1
            bpy.ops.mesh.primitive_cylinder_add(radius=rad * 0.55, depth=wid * 1.04, vertices=48, location=c, rotation=(0, math.radians(90), 0))
            hub = bpy.context.object; bevel(hub, 0.004, 2); apply_mods(hub); smooth_shade(hub); hub.data.materials.append(mats["alu"])
            inner = c.x - side * wid / 2; edge = sb[1] if side > 0 else sb[0]
            ln = abs(inner - edge) + 0.02
            bpy.ops.mesh.primitive_cylinder_add(radius=0.014, depth=ln, vertices=24, location=((inner + edge) / 2, c.y, c.z), rotation=(0, math.radians(90), 0))
            ax = bpy.context.object; smooth_shade(ax); ax.data.materials.append(mats["steel"])
        elif t == "sensor":
            # 4. センサー：レンズは引き伸ばさない（直径＝短い2辺の小さい方）。外装のいちばん近い面から台座でつなぎ、レンズは外へ向ける
            d = sorted((sx, sy, sz))[1] if sorted((sx, sy, sz))[0] < 0.035 else sorted((sx, sy, sz))[0]
            d = min(d, 0.09)
            faces = [(0, sb[0], -1), (0, sb[1], 1), (1, sb[2], -1), (1, sb[3], 1), (2, sb[4], -1), (2, sb[5], 1)]
            # 外にはみ出している距離が大きい面（＝センサーがその面の外側にある）を選ぶ
            ax, plane, sgn = max(faces, key=lambda f: (c[f[0]] - f[1]) * f[2])
            n = Vector((0, 0, 0)); n[ax] = sgn
            out = max(0.0, (c[ax] - plane) * sgn)
            base = c.copy(); base[ax] = plane - sgn * 0.01
            tip = c + n * 0.004 if out > 0.004 else base + n * 0.02
            mid = (base + tip) / 2; ln = (tip - base).length
            rot = n.to_track_quat("Z", "Y").to_euler()
            bpy.ops.mesh.primitive_cylinder_add(radius=d * 0.62, depth=ln, vertices=48, location=mid, rotation=rot)
            pod = bpy.context.object; bevel(pod, min(0.006, ln * 0.25), 3); apply_mods(pod); smooth_shade(pod); pod.data.materials.append(mats["shell_dark"])
            bpy.ops.mesh.primitive_uv_sphere_add(radius=d * 0.42, segments=48, ring_count=24, location=tip, rotation=rot)
            lens = bpy.context.object; lens.scale = (1, 1, 0.45); smooth_shade(lens); lens.data.materials.append(mats["lens"])
            bpy.ops.mesh.primitive_torus_add(major_radius=d * 0.5, minor_radius=d * 0.045, location=tip, rotation=rot)
            ring = bpy.context.object; smooth_shade(ring); ring.data.materials.append(mats["glow"])
        elif t == "canopy":
            # 6. 覆い：半透明の板＋縁の丸み一定。外装の上なら、外装から支柱でつなぐ
            o = prism(p["id"], ellipse(sx / 2, sz / 2, 128) if abs(sx - sz) < 0.02 else superellipse(sx / 2, sz / 2, 4.0, 128), sy, -sy / 2)
            bevel(o, sy * 0.45, 5, 30); apply_mods(o); o.location = c; smooth_shade(o); o.data.materials.append(mats["canopy"])
            bpy.ops.mesh.primitive_torus_add(major_radius=min(sx, sz) / 2 - 0.004, minor_radius=sy * 0.32, location=(c.x, c.y, c.z - sy * 0.1))
            rim = bpy.context.object; rim.scale = (sx / min(sx, sz), sz / min(sx, sz), 1); smooth_shade(rim); rim.data.materials.append(mats["shell_light"])
            if c.z - sy / 2 >= sb[5] - 0.01:
                gap = (c.z - sy / 2) - sb[5]
                bpy.ops.mesh.primitive_cylinder_add(radius=0.035, depth=gap + 0.02, vertices=48, location=(c.x, c.y, sb[5] + gap / 2))
                st = bpy.context.object; bevel(st, 0.004, 2); apply_mods(st); smooth_shade(st); st.data.materials.append(mats["shell_dark"])
                bpy.ops.mesh.primitive_cylinder_add(radius=0.06, depth=0.008, vertices=48, location=(c.x, c.y, c.z - sy / 2 - 0.004))
                fl = bpy.context.object; bevel(fl, 0.003, 2); apply_mods(fl); smooth_shade(fl); fl.data.materials.append(mats["alu"])
        elif t not in HOUSING:
            # キットのない部品：実寸で作り、丸みは一定（引き伸ばさない）
            o = housing_mesh(p); smooth_shade(o); o.data.materials.append(mats["shell_dark"])

    # 5. 大きさが変わらない細部：上の外装の側面に通気口、前に表示の帯、四隅にネジ
    top = max(housing, key=lambda p: p["position"][1]); tc = U(top["position"]); tsx, tsy, tsz = top["scale"]
    if min(tsx, tsy, tsz) < 0.15: return
    for side in (-1, 1):
        for k in range(5):
            bpy.ops.mesh.primitive_cube_add(size=1, location=(tc.x + side * (tsx / 2 + 0.0005), tc.y + 0.06 - k * 0.025, tc.z + 0.02))
            v = bpy.context.object; v.scale = (0.004, 0.012, 0.07); bevel(v, 0.0015, 2); apply_mods(v); v.data.materials.append(mats["seam"])
    bpy.ops.mesh.primitive_cube_add(size=1, location=(tc.x - tsx * 0.18, tc.y - tsz / 2 - 0.001, tc.z + tsy / 2 - 0.045))
    bar = bpy.context.object; bar.scale = (tsx * 0.32, 0.004, 0.012); bevel(bar, 0.002, 2); apply_mods(bar); bar.data.materials.append(mats["glow"])
    for ix in (-1, 1):
        for iy in (-1, 1):
            bpy.ops.mesh.primitive_cylinder_add(radius=0.006, depth=0.004, vertices=16, location=(tc.x + ix * (tsx / 2 - 0.035), tc.y + iy * (tsz / 2 - 0.035), tc.z + tsy / 2 + 0.001))
            sc = bpy.context.object; smooth_shade(sc); sc.data.materials.append(mats["steel"])

# ---------- 撮影 ----------
def stage(lo, hi, size):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"; sc.cycles.samples = 96; sc.cycles.use_denoising = True; sc.cycles.device = "CPU"
    sc.render.resolution_x = 900; sc.render.resolution_y = 760
    sc.view_settings.view_transform = "AgX"
    w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND"); bg.inputs[0].default_value = (0.78, 0.8, 0.82, 1); bg.inputs[1].default_value = 0.35
    bpy.ops.mesh.primitive_plane_add(size=20, location=(0, 0, 0))
    fl = bpy.context.object; fl.data.materials.append(mat("Floor", (0.82, 0.83, 0.85), 0.55))
    for loc, en, size in (((-1.6, -1.8, 2.2), 260, 2.0), ((2.0, -1.2, 1.2), 70, 2.5), ((0.5, 2.0, 2.0), 160, 1.5)):
        bpy.ops.object.light_add(type="AREA", location=loc); L = bpy.context.object; L.data.energy = en; L.data.size = size
        d = Vector((0, 0, (lo + hi) / 2)) - L.location; L.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    tgt = Vector((0, 0, (lo + hi) / 2))
    # 体に付ける道具（原点より下に部品がある）は、下の前から寄って本体を見る。置く物は斜め上から
    if LIFT > 0: tgt = Vector((0, 0, lo + 0.05)); cam_pos = tgt + Vector((0.35, -0.9, -0.28)).normalized() * 0.75
    else: cam_pos = tgt + Vector((0.55, -0.85, 0.42)).normalized() * max(1.0, size * 1.6)
    bpy.ops.object.camera_add(location=cam_pos); cam = bpy.context.object; sc.camera = cam
    cam.data.lens = 50
    d = tgt - cam.location; cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()

def canopy_mat():
    m = mat("Canopy", (0.75, 0.88, 0.97), 0.15)
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    if "Transmission Weight" in b.inputs: b.inputs["Transmission Weight"].default_value = 0.85
    return m

def materials():
    return {
        "default": mat("Pale", (0.72, 0.78, 0.84), 0.45),
        "body": mat("Body", (0.72, 0.78, 0.84), 0.45), "container": mat("Cont", (0.75, 0.8, 0.86), 0.45),
        "wheel": mat("Wheel", (0.03, 0.03, 0.035), 0.7), "sensor": mat("Sens", (0.1, 0.35, 0.9), 0.3, emit=(0.1, 0.4, 1.0), emit_str=2.0),
        "shell_light": mat("ShellLight", (0.86, 0.87, 0.88), 0.32, coat=0.3), "shell_dark": mat("ShellDark", (0.11, 0.12, 0.13), 0.45),
        "seam": mat("Seam", (0.015, 0.015, 0.018), 0.6), "rubber": mat("Rubber", (0.025, 0.025, 0.028), 0.75),
        "alu": mat("Alu", (0.8, 0.81, 0.83), 0.28, metal=1.0), "steel": mat("Steel", (0.6, 0.61, 0.63), 0.35, metal=1.0),
        "canopy": canopy_mat(), "lens": mat("Lens", (0.01, 0.015, 0.03), 0.05, coat=1.0), "glow": mat("Glow", (0.1, 0.45, 1.0), 0.3, emit=(0.15, 0.55, 1.0), emit_str=6.0),
    }

def render(build, path):
    clear()
    parts = dna["parts"]
    hi = max(p["position"][1] + p["scale"][1] / 2 for p in parts) + LIFT
    lo = min(p["position"][1] - p["scale"][1] / 2 for p in parts) + LIFT
    size = max(max(p["position"][0] + p["scale"][0] / 2 for p in parts) - min(p["position"][0] - p["scale"][0] / 2 for p in parts), hi - lo,
               max(p["position"][2] + p["scale"][2] / 2 for p in parts) - min(p["position"][2] - p["scale"][2] / 2 for p in parts))
    mats = materials(); build(parts, mats); stage(lo, hi, size)
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    tris = sum(len(o.data.loop_triangles) if o.type == "MESH" and not o.data.loop_triangles.__len__() == 0 else 0 for o in bpy.data.objects if o.type == "MESH")
    for o in bpy.data.objects:
        if o.type == "MESH": o.data.calc_loop_triangles()
    tris = sum(len(o.data.loop_triangles) for o in bpy.data.objects if o.type == "MESH" and o.name not in ("Plane",))
    print("TRIS", os.path.basename(path), tris)

a = os.path.join(OUT, NAME + "_current.png"); b = os.path.join(OUT, NAME + "_product.png")
render(build_current, a)
render(build_product, b)
# 2枚を横に並べる
ia, ib = bpy.data.images.load(a), bpy.data.images.load(b)
w, h = ia.size
pa = np.array(ia.pixels[:]).reshape(h, w, 4); pb = np.array(ib.pixels[:]).reshape(h, w, 4)
out = bpy.data.images.new("cmp", w * 2, h, alpha=True)
out.pixels[:] = np.concatenate([pa, pb], axis=1).ravel()
out.filepath_raw = os.path.join(OUT, NAME + "_compare.png"); out.file_format = "PNG"; out.save()
print("DONE", out.filepath_raw)
