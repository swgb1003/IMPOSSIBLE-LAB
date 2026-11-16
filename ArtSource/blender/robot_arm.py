# ロボットアーム（演出物）。関節ごとに別オブジェクトにし、原点を回転の中心に置く。
#   ARM_Base  ：原点＝床（台座・旋回台・支柱・肩の軸受け）
#   ARM_Upper ：原点＝肩（Z 0.95 m）。上腕と肘の関節。Unity の LabApp が X 軸まわりに回す
#   ARM_Fore  ：原点＝肘。前腕・手首・工具（先端は青く光る）
# 腕は Blender の -Y 方向へ伸ばす（Unity 側で、肘の位置から向きを求めて発明台へ向ける）。
# 実行：exec(open(r"D:/IMPOSSIBLE_LAB/ArtSource/blender/robot_arm.py", encoding="utf-8").read())
import math
from mathutils import Vector, Matrix
exec(open(r"D:/IMPOSSIBLE_LAB/ArtSource/blender/lab_common.py", encoding="utf-8").read())

M = materials()
C = collection('RobotArm')
SHOULDER = Vector((0.0, 0.0, 0.95))
ELBOW = Vector((0.0, -0.30, 1.78))
WRIST = Vector((0.0, -0.92, 1.52))


def beam(bm, p0, p1, w, h, extend=0.0):
    """p0→p1 を結ぶ角柱（幅 w・厚み h）。X 軸まわりの断面は w×h。"""
    d = (p1 - p0)
    L = d.length + extend * 2.0
    mid = (p0 + p1) * 0.5
    rot = d.to_track_quat('Z', 'X').to_matrix().to_4x4()
    m = Matrix.Translation(mid) @ rot @ Matrix.Diagonal((w, h, L, 1.0))
    bmesh.ops.create_cube(bm, size=1.0, matrix=m)


def axle(bm, center, r, length, seg=24):
    """X 軸向きの円柱（関節）。"""
    m = Matrix.Translation(center) @ Matrix.Rotation(math.pi / 2, 4, 'Y')
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r, radius2=r, depth=length, matrix=m)


def at(o, origin):
    """メッシュを動かさずに原点を origin へ移す。"""
    o.data.transform(Matrix.Translation(-origin))
    o.location = origin


def join(name, parts, origin):
    """部品（マテリアル別）を面取りを適用して 1 オブジェクトにまとめる。"""
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.convert(target='MESH')
    bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = name
    o.data.name = name
    at(o, origin)
    return o


def hose(bm, p0, p1, side, r=0.018, lift=0.0):
    """腕の横にそって走るケーブルの管（両端を少し内側へ）。side は X 方向のずれ。"""
    off = Vector((side, 0, lift))
    beam(bm, p0 + off, p1 + off, r * 2, r * 2)


def clamps(bm, p0, p1, side, n=2):
    d = p1 - p0
    for k in range(1, n + 1):
        c = p0 + d * (k / (n + 1)) + Vector((side * 0.55, 0, 0))
        beam(bm, c - d.normalized() * 0.015, c + d.normalized() * 0.015, abs(side) * 1.25 + 0.03, 0.07)


def base_bolts(bm):
    for k in range(10):
        a = TAU * k / 10
        bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=8, radius1=0.016, radius2=0.016, depth=0.014,
                              matrix=Matrix.Translation((0.32 * math.cos(a), 0.32 * math.sin(a), 0.107)))


# ---- 台座 ----
base = join('ARM_Base', [
    obj('b_dark', C, M['dark'], lambda bm: (cyl(bm, 0.34, 0.0, 0.10, 40), ring(bm, 0.23, 0.17, 0.30, 0.34, 32), axle(bm, SHOULDER, 0.15, 0.30)), bevel=0.006),
    obj('b_white', C, M['white'], lambda bm: (cone(bm, 0.30, 0.26, 0.10, 0.30, 40), cone(bm, 0.21, 0.17, 0.34, 0.80, 32),
                                              axle(bm, SHOULDER + Vector((0.165, 0, 0)), 0.17, 0.04), axle(bm, SHOULDER - Vector((0.165, 0, 0)), 0.17, 0.04)), bevel=0.008),
    obj('b_metal', C, M['metal'], lambda bm: box(bm, (0.12, 0.16, 0.22), (0, 0, 0.88)), bevel=0.006),
    obj('b_orange', C, M['orange'], lambda bm: ring(bm, 0.345, 0.30, 0.10, 0.112, 40)),
    obj('b_bolts', C, M['metal'], base_bolts),
], Vector((0, 0, 0)))

# ---- 上腕（肩から肘） ----
upper = join('ARM_Upper', [
    obj('u_white', C, M['white'], lambda bm: beam(bm, SHOULDER, ELBOW, 0.17, 0.20, extend=0.02), bevel=0.02, seg=3),
    obj('u_dark', C, M['dark'], lambda bm: (axle(bm, ELBOW, 0.11, 0.24), beam(bm, SHOULDER + (ELBOW - SHOULDER) * 0.3, SHOULDER + (ELBOW - SHOULDER) * 0.7, 0.175, 0.06),
                                            hose(bm, SHOULDER + (ELBOW - SHOULDER) * 0.08, ELBOW - (ELBOW - SHOULDER) * 0.08, 0.105)), bevel=0.004),
    obj('u_metal', C, M['metal'], lambda bm: (clamps(bm, SHOULDER, ELBOW, 0.105), axle(bm, ELBOW, 0.07, 0.27)), bevel=0.003),
], SHOULDER)

# ---- 前腕・手首・工具 ----
fore = join('ARM_Fore', [
    obj('f_white', C, M['white'], lambda bm: beam(bm, ELBOW, WRIST, 0.13, 0.15, extend=0.02), bevel=0.016, seg=3),
    obj('f_dark', C, M['dark'], lambda bm: (axle(bm, WRIST, 0.075, 0.17),
                                            bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=24, radius1=0.035, radius2=0.06, depth=0.14,
                                                                  matrix=Matrix.Translation(WRIST + Vector((0, 0, -0.11))))), bevel=0.004),
    obj('f_hose', C, M['dark'], lambda bm: hose(bm, ELBOW + (WRIST - ELBOW) * 0.1, WRIST - (WRIST - ELBOW) * 0.12, 0.085, r=0.014), bevel=0.003),
    obj('f_metal', C, M['metal'], lambda bm: clamps(bm, ELBOW, WRIST, 0.085, n=1), bevel=0.003),
    obj('f_blue', C, M['blue'], lambda bm: bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=16, radius1=0.012, radius2=0.034, depth=0.05,
                                                                  matrix=Matrix.Translation(WRIST + Vector((0, 0, -0.205))))),
], ELBOW)

# 肘（ARM_Fore）は肩（ARM_Upper）の子。Unity の SceneBuilder が ARM_Upper/ARM_Fore の親子を前提に肩を回す
fore.parent = upper
fore.matrix_parent_inverse = upper.matrix_world.inverted()
print('tris', tri_count([base, upper, fore]))
for o in (base, upper, fore):
    print(o.name, tuple(round(v, 3) for v in o.location))
