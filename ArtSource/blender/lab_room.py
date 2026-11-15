# 研究室の部屋（床・壁・窓・天井・サインの背板）。当たり判定は Unity 側の箱（SceneBuilder）を使い、ここは見た目だけ。
# 座標は Unity の値で書き、U() で Blender 座標へ直す（FBX 書き出し -Z 前・Y 上 → Unity (X,Y,Z) = (-bx, bz, -by)）。
#   部屋 16×16 m・天井 5 m。右の壁（Unity X=+8）に実験ゲートの開口（Z −1.5〜+1.5、高さ 3.5 m）。
#   奥の壁（Unity Z=+8）は全面ガラス窓（展示ケースの背景）。左の壁（X=−8）に IMPOSSIBLE LAB のサイン。
# 実行：exec(open(r"D:/IMPOSSIBLE_LAB/ArtSource/blender/lab_room.py", encoding="utf-8").read())
import math
from mathutils import Vector, Matrix
exec(open(r"D:/IMPOSSIBLE_LAB/ArtSource/blender/lab_common.py", encoding="utf-8").read())

M = materials()
M['floor'] = mat('MAT_Floor', (0.55, 0.56, 0.57), 0.0, 0.35)
M['wall'] = mat('MAT_Wall', (0.80, 0.82, 0.84), 0.0, 0.6)
C = collection('LabRoom')
H = 5.0          # 天井の高さ
W = 8.0          # 部屋の半幅


def U(x, y, z):
    return Vector((-x, -z, y))


def ubox(bm, cx, cy, cz, sx, sy, sz):
    """Unity 座標の中心・大きさで箱を作る（回転なし）。"""
    box(bm, (sx, sz, sy), U(cx, cy, cz))


def planar_uv(o, tile):
    """上向きの面に、ワールド座標 / tile の UV を付ける（床のテクスチャ用）。"""
    me = o.data
    uv = me.uv_layers.new(name='UVMap')
    for p in me.polygons:
        for li in p.loop_indices:
            v = me.vertices[me.loops[li].vertex_index].co
            uv.data[li].uv = (v.x / tile, v.y / tile)


# ---- 床 ----
floor = obj('ROOM_Floor', C, M['floor'], lambda bm: ubox(bm, 0, -0.05, 0, 2 * W, 0.1, 2 * W), smooth=False)
planar_uv(floor, 3.0)


def floor_lines(bm):
    # 4 m ごとの目地（暗い金属の細い帯）。中央装置の床リング（半径 2.62 m）の内側は避ける
    for k in (-4.0, 4.0):
        ubox(bm, k, 0.001, 0, 0.03, 0.002, 2 * W)
        ubox(bm, 0, 0.001, k, 2 * W, 0.002, 0.03)
    # 立ち位置から発明台への通路の縁取り（Z −8〜−2.6）
    for x in (-0.9, 0.9):
        ubox(bm, x, 0.001, -5.3, 0.03, 0.002, 5.4)


made = [floor, obj('ROOM_FloorLines', C, M['dark'], floor_lines, smooth=False)]

# ---- 壁（白いパネル＋継ぎ目＋巾木＋上部の間接照明） ----
PANEL_W, PANEL_H = 2.0, 1.25
SEAM = 0.012


def wall_panels(bm, axis, side, z_from, z_to, skip=None):
    """axis='x'：X=side の壁（Z 方向に並ぶ）／'z'：Z=side の壁（X 方向に並ぶ）。skip(u, y0, y1) が真のパネルは作らない。"""
    inward = -1.0 if side > 0 else 1.0
    n_u = int(round((z_to - z_from) / PANEL_W))
    for i in range(n_u):
        u0 = z_from + i * PANEL_W
        for j in range(4):
            y0 = 0.12 + j * PANEL_H
            y1 = min(y0 + PANEL_H, H - 0.42)
            if y1 <= y0:
                continue
            if skip and skip(u0 + PANEL_W * 0.5, y0, y1):
                continue
            cu, cy = u0 + PANEL_W * 0.5, (y0 + y1) * 0.5
            su, sy = PANEL_W - SEAM, (y1 - y0) - SEAM
            d = side + inward * 0.06
            if axis == 'x':
                ubox(bm, d, cy, cu, 0.04, sy, su)
            else:
                ubox(bm, cu, cy, d, su, sy, 0.04)


def gate_hole(u, y0, y1):
    return abs(u) < 2.0 and y0 < 3.7


def sign_area(u, y0, y1):
    return 0.0 < u < 4.0 and y0 > 1.5 and y1 < 4.2


def walls_white(bm):
    wall_panels(bm, 'x', -W, -W, W, skip=sign_area)          # 左
    wall_panels(bm, 'x', W, -W, W, skip=gate_hole)           # 右（ゲートの開口）
    wall_panels(bm, 'z', -W, -W, W)                          # 手前


def walls_back(bm):
    # 継ぎ目から見える下地（壁そのもの）
    ubox(bm, -W, H * 0.5, 0, 0.1, H, 2 * W)
    ubox(bm, W, H * 0.5, -4.75, 0.1, H, 6.5)
    ubox(bm, W, H * 0.5, 4.75, 0.1, H, 6.5)
    ubox(bm, W, 4.25, 0, 0.1, 1.5, 3.0)
    ubox(bm, 0, H * 0.5, -W, 2 * W, H, 0.1)


def skirting(bm):
    for (x, z, sx, sz) in ((-W + 0.09, 0, 0.04, 2 * W), (0, -W + 0.09, 2 * W, 0.04), (0, W - 0.3, 2 * W, 0.04),
                           (W - 0.09, -5.0, 0.04, 6.0), (W - 0.09, 5.0, 0.04, 6.0)):
        ubox(bm, x, 0.06, z, sx, 0.12, sz)
    # IMPOSSIBLE LAB サインの背板（暗い板）
    ubox(bm, -W + 0.1, 2.9, 2.0, 0.04, 2.3, 4.2)


def cove_light(bm):
    # 天井際の間接照明（左・右・手前の壁）
    for (x, z, sx, sz) in ((-W + 0.12, 0, 0.03, 2 * W - 0.4), (0, -W + 0.12, 2 * W - 0.4, 0.03),
                           (W - 0.12, -5.0, 0.03, 5.6), (W - 0.12, 5.0, 0.03, 5.6)):
        ubox(bm, x, H - 0.36, z, sx, 0.05, sz)
    # サインの背板の縁の青い線
    ubox(bm, -W + 0.125, 1.76, 2.0, 0.01, 0.02, 4.2)


made += [
    obj('ROOM_WallBack', C, M['dark'], walls_back, smooth=False),
    obj('ROOM_WallPanels', C, M['wall'], walls_white, bevel=0.004, seg=1),
    obj('ROOM_Skirting', C, M['dark'], skirting, smooth=False),
    obj('ROOM_CoveLight', C, M['light'], cove_light, smooth=False),
]

# ---- 奥の全面窓（柱・横桟・ガラス） ----
PILLARS = [-6.6, -4.4, -2.2, 0.0, 2.2, 4.4, 6.6]


def window_frame(bm):
    for x in PILLARS + [-7.85, 7.85]:
        ubox(bm, x, H * 0.5, W - 0.12, 0.22 if abs(x) < 7.5 else 0.3, H, 0.25)
    for y in (0.06, 1.0, 4.62, H - 0.1):                     # 横桟（床際・腰・上・天井際）
        ubox(bm, 0, y, W - 0.12, 2 * W, 0.12 if y > 0.1 else 0.12, 0.18)


def window_glass(bm):
    ubox(bm, 0, H * 0.5, W - 0.1, 2 * W, H, 0.02)


made += [
    obj('ROOM_WindowFrame', C, M['metal'], window_frame, bevel=0.006, seg=1),
    obj('ROOM_WindowGlass', C, M['glass'], window_glass, smooth=False),
]

# ---- 天井（白い天井板＋直線の照明＋中央の円形の吹き抜け） ----
HOLE_R = 3.95


def ceiling(bm):
    # 中央に円形の穴のある天井板：外周の正方形と穴の円をつなぐ（角度ごとの扇形を台形で分割）
    n = 64
    top, bot = H + 0.1, H
    for i in range(n):
        a0, a1 = TAU * i / n, TAU * (i + 1) / n

        def edge(a):
            c, s = math.cos(a), math.sin(a)
            k = W / max(abs(c), abs(s))
            return (c * k, s * k), (c * HOLE_R, s * HOLE_R)
        (o0, i0), (o1, i1) = edge(a0), edge(a1)
        vs = []
        for (x, z), y in ((o0, bot), (o1, bot), (i1, bot), (i0, bot), (o0, top), (o1, top), (i1, top), (i0, top)):
            vs.append(bm.verts.new(U(x, y, z)))
        f = [(0, 1, 2, 3), (7, 6, 5, 4), (3, 2, 6, 7)]
        for q in f:
            bm.faces.new([vs[k] for k in q])
    # 吹き抜けの筒と上の蓋
    ring(bm, HOLE_R + 0.1, HOLE_R, H, H + 0.8, 64)
    cyl(bm, HOLE_R + 0.1, H + 0.8, H + 0.9, 64)


def ceiling_lights(bm):
    # 直線の照明（Unity X 方向）。吹き抜けにかからない位置
    for z in (-6.0, 6.0):
        ubox(bm, 0, H - 0.01, z, 12.0, 0.02, 0.12)
    for x in (-6.0, 6.0):
        ubox(bm, x, H - 0.01, 0, 0.12, 0.02, 7.0)
    # 吹き抜けの上の円形の照明
    ring(bm, HOLE_R - 0.3, HOLE_R - 0.6, H + 0.79, H + 0.8, 64)


def ceiling_fix(bm):
    # 天井板の下面は下向き、上面は上向き（recalc は閉じていない形で向きがそろわない）
    bm.normal_update()
    for f in bm.faces:
        z = f.calc_center_median().z
        if (abs(z - H) < 1e-3 and f.normal.z > 0) or (abs(z - (H + 0.1)) < 1e-3 and f.normal.z < 0):
            f.normal_flip()


made += [
    obj('ROOM_Ceiling', C, M['wall'], ceiling, smooth=False, fix=ceiling_fix),
    obj('ROOM_CeilingLights', C, M['light'], ceiling_lights, smooth=False),
]

print('objects', len(made), 'tris', tri_count(made))
