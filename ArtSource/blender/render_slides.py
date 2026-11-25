# CentralLab.blend を Unity と同じ配置に並べ、スライド用の静止画をレンダーする（.blend は保存しない）。
# Unity (X,Y,Z) = (-bx, bz, -by)  →  Blender (bx, by, bz) = (-X, -Z, Y)
import bpy, math, os, sys
from mathutils import Vector

OUT = os.environ.get("ILAB_OUT", "")
SHOTS = os.environ.get("ILAB_SHOTS", "hero").split(",")
SCALE = int(os.environ.get("ILAB_SCALE", "50"))

sc = bpy.context.scene

def u2b(x, y, z):
    return Vector((-x, -z, y))

def objs(names):
    return [bpy.data.objects[n] for n in names]

def copy_group(names, loc, rot_z=0.0):
    """部品のまとまりを複製して置く（親子は保ったまま、最上位だけ動かす）。"""
    src = objs(names)
    m = {}
    for o in src:
        c = o.copy()
        if c.data:
            c.data = o.data  # メッシュは共有
        sc.collection.objects.link(c)
        c.hide_render = False
        c.hide_viewport = False
        m[o] = c
    for o, c in m.items():
        if o.parent in m:
            c.parent = m[o.parent]
            c.matrix_parent_inverse = o.matrix_parent_inverse.copy()
    for o, c in m.items():
        if o.parent not in m:
            c.location = loc
            c.rotation_euler = (0, 0, rot_z)
    return list(m.values())

def hide(names):
    for o in objs(names):
        o.hide_render = True
        o.hide_viewport = True

# --- 元の部品（原点に重なっている）を隠し、複製で配置する ---
ARM = ["ARM_Base", "ARM_Upper", "ARM_Fore"]
BENCH = ["PROP_Workbench"]
CASE = ["PROP_GalleryCase", "PROP_GalleryGlass"]
PLANT = ["PLANT_Leaves", "PLANT_PotWhite", "PLANT_PotDark"]
GATE = ["GATE_Frame", "GATE_DoorL", "GATE_DoorR"]
for g in (ARM, BENCH, CASE, PLANT, GATE):
    hide(g)
for o in bpy.data.collections["potted_plant_02"].objects:
    o.hide_render = True

# ロボットアーム：Unity (±2.1, 0, 0.6)。腕の先が台の中心を向くように回す
fore = bpy.data.objects["ARM_Fore"]
reach = fore.matrix_world.translation.copy(); reach.z = 0
base_ang = math.atan2(reach.y, reach.x) if reach.length > 1e-4 else 0.0
for side in (-1, 1):
    p = u2b(2.1 * side, 0, 0.6)
    to_c = Vector((-p.x, -p.y, 0))
    ang = math.atan2(to_c.y, to_c.x) - base_ang
    copy_group(ARM, p, ang)
# 作業台：Unity (-6.6, 0, -3 + 3i)
for i in range(3):
    copy_group(BENCH, u2b(-6.6, 0, -3 + 3 * i))
# 展示台：Unity (-2.6 + 2.6i, 0, 6.6)
for i in range(3):
    copy_group(CASE, u2b(-2.6 + 2.6 * i, 0, 6.6))
# 観葉植物
for i, (x, z) in enumerate([(-7.1, 7.0), (7.1, 7.0), (7.2, -3.2)]):
    copy_group(PLANT, u2b(x, 0, z), math.radians(-70 * i))

# --- レンダー設定 ---
r = sc.render
r.resolution_x, r.resolution_y = 2560, 1440
r.resolution_percentage = SCALE
r.image_settings.file_format = 'PNG'
try:
    sc.view_settings.view_transform = 'AgX'
    sc.view_settings.look = 'AgX - Medium High Contrast'
except Exception as e:
    print("view transform:", e)
for k, v in (("taa_render_samples", int(os.environ.get("ILAB_SAMPLES", "64"))), ("use_raytracing", True), ("use_shadows", True)):
    try:
        setattr(sc.eevee, k, v)
    except Exception as e:
        print("eevee", k, e)

# 光：窓（Blender −Y 側）から差す日光と、窓の外の空。全体の平板さを抑える
w = sc.world
w.use_nodes = True
bg = next(n for n in w.node_tree.nodes if n.type == 'BACKGROUND')
bg.inputs[0].default_value = (0.62, 0.74, 0.90, 1.0)
bg.inputs[1].default_value = float(os.environ.get("ILAB_WORLD", "0.45"))
sun_data = bpy.data.lights.new("SlideSun", 'SUN')
sun_data.energy = float(os.environ.get("ILAB_SUN", "3.2"))
sun_data.angle = math.radians(4)
sun_data.color = (1.0, 0.95, 0.88)
sun = bpy.data.objects.new("SlideSun", sun_data)
sc.collection.objects.link(sun)
sun.rotation_euler = (math.radians(55), 0, math.radians(20))

cam = sc.camera

def shoot(name, loc, target, lens=35, shift_x=0.0):
    cam.location = Vector(loc)
    d = Vector(target) - cam.location
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    cam.data.lens = lens
    cam.data.shift_x = shift_x
    r.filepath = os.path.join(OUT, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("WROTE", r.filepath)

SHOT_DEFS = {
    # 表紙：低い位置から中央装置を見上げ、左に文字の余白を取る
    "hero": dict(loc=(4.6, 6.2, 1.05), target=(0.0, 0.0, 1.55), lens=26, shift_x=-0.18),
    # 正面：研究室の奥行き（展示台と窓）
    "wide": dict(loc=(0.0, 7.2, 1.7), target=(0.0, 0.0, 1.2), lens=22),
    # 寄り：発明台のホログラム部
    "detail": dict(loc=(2.6, 3.6, 1.45), target=(0.0, 0.0, 1.0), lens=55),
    # 真上：設計図のような俯瞰
    "top": dict(loc=(0.0, 0.01, 4.2), target=(0.0, 0.0, 0.0), lens=24),
}
for s in SHOTS:
    shoot(s, **SHOT_DEFS[s])
