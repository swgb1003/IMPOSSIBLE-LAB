# IMPOSSIBLE LAB — 部品キット（docs/DECISIONS.md D-26・D-27）
# 発明の部品を Unity の基本形状の代わりに表示するメッシュ。大きさの基準は Unity の基本形状と同じ：
#   箱 1×1×1、球 直径1、円柱・カプセル 直径1・高さ2（Unity の Y＝Blender の Z）。DNA の scale をそのまま掛けて使う。
# 名前：KIT_<部品タイプ or any>_<形>（本体。DNA の素材の色で塗る）、…__ACC（光る飾り。機能の色で塗る）。
# 前（Unity の +Z）は Blender の +Y（bakeAxisConversion で取り込むと前後が反転するため。2026-11-16 に取り込み後のメッシュで確認）。
# 実行（MCP for Blender）：exec(open(r"D:/IMPOSSIBLE_LAB/ArtSource/blender/part_kit.py", encoding="utf-8").read())
import bpy, bmesh, math
from mathutils import Matrix, Vector

exec(open(r"D:/IMPOSSIBLE_LAB/ArtSource/blender/lab_common.py", encoding="utf-8").read())

M = materials()
C = collection("PartKit")
SEG = 48


def place(o, x):
    """一覧で見やすいよう横に並べる（書き出し時に原点へ戻す）。"""
    o.location = (x, 0, 0)
    o["kit_x"] = x
    return o


# ---- 共通：角を丸めた箱・円柱（部品タイプ専用のキットがないときに使う） ----
def any_box():
    return obj("KIT_any_box", C, M['white'], lambda bm: box(bm, (1, 1, 1), (0, 0, 0)), bevel=0.08, seg=3)


def any_cylinder():
    return obj("KIT_any_cylinder", C, M['white'], lambda bm: cyl(bm, 0.5, -1, 1, SEG), bevel=0.06, seg=3)


# ---- 本体（円柱）：上下の縁を丸めた筐体＋側面の青い表示灯の帯 ----
def body_cylinder():
    def build(bm):
        cyl(bm, 0.5, -1.0, 0.55, SEG)          # 下の胴
        cyl(bm, 0.47, 0.55, 0.62, SEG)         # 継ぎ目（わずかに細い）
        cyl(bm, 0.5, 0.62, 1.0, SEG)           # 上のふた
    main = obj("KIT_body_cylinder", C, M['white'], build, bevel=0.05, seg=3)
    acc = obj("KIT_body_cylinder__ACC", C, M['blue'], lambda bm: ring(bm, 0.505, 0.49, -0.35, -0.15, SEG), smooth=True)
    return main, acc


# ---- キャノピー（円柱）：縁を丸めた1枚の円盤（水色の半透明。白い縁は付けない） ----
def canopy_cylinder():
    return obj("KIT_canopy_cylinder", C, mat('MAT_PartCanopy', (0.70, 0.84, 0.95), 0.0, 0.25, alpha=0.85),
               lambda bm: cyl(bm, 0.5, -1.0, 1.0, 64), bevel=0.012, seg=3)


# ---- センサー（球）：監視カメラのような白い筐体（後ろは丸く、前は平ら）＋前面の大きな青いレンズ ----
def sensor_sphere():
    def build(bm):
        bmesh.ops.create_uvsphere(bm, u_segments=32, v_segments=16, radius=0.5)
        # 前面（+Y）を平らに切る
        for v in bm.verts:
            if v.co.y > 0.3:
                v.co.y = 0.3
    main = obj("KIT_sensor_sphere", C, M['white'], build, smooth=True, angle=60)
    def lens(bm):
        # 縁（白に近い灰）ではなく、レンズ全体を青く光らせる：外側の輪＋中央のふくらみ
        m = Matrix.Translation((0, 0.31, 0)) @ Matrix.Rotation(math.radians(90), 4, 'X')
        bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=32, radius1=0.36, radius2=0.33, depth=0.05, matrix=m)
        bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=12, radius=0.2,
                                  matrix=Matrix.Translation((0, 0.3, 0)) @ Matrix.Diagonal((1, 0.6, 1, 1)))
    acc = obj("KIT_sensor_sphere__ACC", C, M['blue'], lens, smooth=True, angle=40)
    return main, acc


# ---- 姿勢安定ユニット（箱）：角を丸めた白い箱＋前面の小さな表示灯 ----
def stabilizer_box():
    main = obj("KIT_stabilizer_box", C, M['white'], lambda bm: box(bm, (1, 1, 1), (0, 0, 0)), bevel=0.1, seg=3)
    # 目立たないよう、前面に小さな表示灯だけ付ける（筐体は素材の色＝白）
    acc = obj("KIT_stabilizer_box__ACC", C, M['orange'], lambda bm: box(bm, (0.3, 0.04, 0.12), (0.2, 0.51, 0.1)), bevel=0.01, seg=1)
    return main, acc


def build_all():
    x = 0.0
    for f in (any_box, any_cylinder, body_cylinder, canopy_cylinder, sensor_sphere, stabilizer_box):
        r = f()
        for o in (r if isinstance(r, tuple) else (r,)):
            place(o, x)
        x += 1.6
    # 第2弾（雨よけ装置以外の部品）
    exec(open(r"D:/IMPOSSIBLE_LAB/ArtSource/blender/part_kit_more.py", encoding="utf-8").read(), globals())
    build_more(x)
    # 第3弾（部品の拡充と品番の見た目、D-41）
    exec(open(r"D:/IMPOSSIBLE_LAB/ArtSource/blender/part_kit_parts3.py", encoding="utf-8").read(), globals())
    build_parts3(x + 1.6 * len(MORE))
    objs = list(C.objects)
    print("PartKit:", len(objs), "objects, triangles", tri_count(objs))
    for o in objs:
        print(" ", o.name, tri_count([o]))


def export(path=r"D:/IMPOSSIBLE_LAB/Assets/_ImpossibleLab/Art/Models/PartKit.fbx"):
    """キットだけを原点に戻して FBX に書き出す（研究室と同じ設定。Unity 側では (X,Y,Z) = (-bx, bz, -by)）。"""
    objs = list(C.objects)
    for o in objs:
        o.location = (0, 0, 0)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.hide_set(False)
        o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True, use_mesh_modifiers=True,
                             mesh_smooth_type='OFF', object_types={'MESH'}, add_leaf_bones=False, bake_anim=False, path_mode='STRIP')
    for o in objs:
        o.location = (o.get("kit_x", 0.0), 0, 0)
    print("exported", path)


build_all()
