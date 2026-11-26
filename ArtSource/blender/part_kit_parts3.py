# IMPOSSIBLE LAB — 部品キット 第3弾（部品の拡充、docs/DECISIONS.md D-41）。part_kit.py の後に exec する（obj・box・cyl・cone・ring・M・C・place を使う）。
# 大きさの基準・前（Blender の +Y）・名前の決まりは part_kit.py と同じ。
#   KIT_<type>_<shape>          ：新しい種類の標準の見た目
#   KIT_<type>_<shape>-<variant>：品番の見た目（part_models.json の variant）
#   __ACC：機能の色で塗る飾り、それ以外の __X：FBX のマテリアルのまま
# 画面・反射材・スイッチのように向きで見え方が変わる部品は、上（+Z）と前（+Y）の両方に面を付ける（DNA の scale で薄い向きが変わるため）。
import math
import bmesh
from mathutils import Matrix, Vector

S3 = 32
SCREEN = mat('MAT_Screen', (0.02, 0.08, 0.16), 0.0, 0.2, emit=(0.1, 0.45, 0.85), strength=1.5)


def disc(bm, r, z, depth, seg=16, x=0.0, y=0.0, r1=None):
    """上下（Z）向きの短い円柱・円すい台（ねじ・穴・端子）。中心の高さ z。"""
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r, radius2=r if r1 is None else r1, depth=depth, matrix=_t(x, y, z))


def stadium(k, rz=0.5, seg=16):
    """両端が半円の輪（部品の座標）。k は 高さ÷長さ：部品の大きさを掛けたあとに両端が真円になるよう、横の半径を k 倍にする。"""
    ry = rz * k
    yc = 0.5 - ry
    pts = []
    for i in range(seg + 1):                                        # 後ろの半円（上 → 後ろ → 下）
        a = math.pi / 2 + math.pi * i / seg
        pts.append((-yc + ry * math.cos(a), rz * math.sin(a)))
    for i in range(seg + 1):                                        # 前の半円（下 → 前 → 上）
        a = -math.pi / 2 + math.pi * i / seg
        pts.append((yc + ry * math.cos(a), rz * math.sin(a)))
    return pts, yc, ry


def loop_band(bm, pts, w0, w1, tu, tv, plane="yz"):
    """閉じた道（反時計回り）に沿った帯。外側の点から内側へ（道の1つ目の座標で tu、2つ目で tv の）厚み、w0〜w1 の幅。"""
    n = len(pts)
    rows = []
    for i in range(n):
        (u0, v0), (u1, v1) = pts[i - 1], pts[(i + 1) % n]
        tu, tv = u1 - u0, v1 - v0
        ln = math.hypot(tu, tv) or 1.0
        nu, nv = tv / ln, -tu / ln                                  # 外向き
        u, v = pts[i]
        iu, iv = u - nu * tu, v - nv * tv
        def P(a, b, w):
            return bm.verts.new((w, a, b) if plane == "yz" else (a, b, w))
        rows.append([P(u, v, w0), P(u, v, w1), P(iu, iv, w1), P(iu, iv, w0)])
    for i in range(n):
        A, B = rows[i], rows[(i + 1) % n]
        for q in range(4):
            bm.faces.new((A[q], B[q], B[(q + 1) % 4], A[(q + 1) % 4]))


def _new(bm):
    return len(bm.verts)


def _xf(bm, n0, m):
    """n0 番目より後に作った頂点だけを動かす（回転・移動）。"""
    bm.verts.ensure_lookup_table()
    bmesh.ops.transform(bm, matrix=m, verts=[bm.verts[i] for i in range(n0, len(bm.verts))])


def _rx(a):
    return Matrix.Rotation(a, 4, 'X')


def _ry(a):
    return Matrix.Rotation(a, 4, 'Y')


def _t(x, y, z):
    return Matrix.Translation((x, y, z))


def cyl_y(bm, r, y0, y1, seg, x=0.0, z=0.0, r1=None):
    """前後（Y）向きの円柱・円すい台。"""
    m = _t(x, (y0 + y1) * 0.5, z) @ _rx(math.pi / 2)
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r, radius2=r if r1 is None else r1, depth=y1 - y0, matrix=m)


def cyl_x(bm, r, x0, x1, seg, y=0.0, z=0.0):
    """左右（X）向きの円柱。"""
    m = _t((x0 + x1) * 0.5, y, z) @ _ry(math.pi / 2)
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r, radius2=r, depth=x1 - x0, matrix=m)


def ring_x(bm, r_out, r_in, x0, x1, seg, a0=0.0, a1=math.tau, y=0.0, z=0.0):
    """左右（X）が軸の円環・円弧（Y-Z 面の輪）。"""
    n0 = _new(bm)
    ring(bm, r_out, r_in, x0, x1, seg, a0, a1)
    _xf(bm, n0, _t(0, y, z) @ _ry(math.pi / 2))


def ring_y(bm, r_out, r_in, y0, y1, seg, a0=0.0, a1=math.tau, x=0.0, z=0.0):
    """前後（Y）が軸の円環・円弧（X-Z 面の輪）。"""
    n0 = _new(bm)
    ring(bm, r_out, r_in, y0, y1, seg, a0, a1)
    _xf(bm, n0, _t(x, 0, z) @ _rx(-math.pi / 2))


def helix(bm, R, r, z0, z1, turns, seg_path=96, seg_tube=8):
    """ばねのコイル（半径 R、針金の太さの半径 r）。"""
    rows = []
    n = int(seg_path * turns)
    for i in range(n + 1):
        t = i / n
        a = math.tau * turns * t
        c = Vector((R * math.cos(a), R * math.sin(a), z0 + (z1 - z0) * t))
        out = Vector((math.cos(a), math.sin(a), 0))
        up = Vector((0, 0, 1))
        row = []
        for k in range(seg_tube):
            b = math.tau * k / seg_tube
            row.append(bm.verts.new(c + out * (r * math.cos(b)) + up * (r * math.sin(b))))
        rows.append(row)
    for i in range(n):
        A, B = rows[i], rows[i + 1]
        for k in range(seg_tube):
            bm.faces.new((A[k], B[k], B[(k + 1) % seg_tube], A[(k + 1) % seg_tube]))
    bm.faces.new(list(reversed(rows[0])))
    bm.faces.new(rows[-1])


def grid_on(bm, nx, ny, w, h, size, loc_z=0.5, y=None, front=False):
    """上（+Z）か前（+Y）の面に、小さな箱を格子に並べる。"""
    for i in range(nx):
        for j in range(ny):
            u = -w / 2 + w * (i + 0.5) / nx
            v = -h / 2 + h * (j + 0.5) / ny
            if front:
                box(bm, (size[0], size[2], size[1]), (u, 0.5 if y is None else y, v))
            else:
                box(bm, size, (u, v, loc_z))


# ======================================================================
# 新しい種類（標準の見た目）
# ======================================================================

# ---- ちょうつがい：2枚の羽根＋中央の筒（軸）＋ねじ穴 ----
def hinge_box():
    main = obj("KIT_hinge_box", C, M['white'], lambda bm: (box(bm, (0.44, 1, 0.8), (-0.28, 0, 0)), box(bm, (0.44, 1, 0.8), (0.28, 0, 0))), bevel=0.03, seg=1)
    def pin(bm):
        for k in range(5):
            cyl_y(bm, 0.11, -0.5 + k * 0.2 + 0.01, -0.5 + (k + 1) * 0.2 - 0.01, 16, z=0.0)
        cyl_y(bm, 0.04, -0.53, 0.53, 10)
    p = obj("KIT_hinge_box__PIN", C, M['metal'], pin, bevel=0.01, seg=1)
    def holes(bm):
        for x in (-0.33, 0.33):
            for y in (-0.28, 0.28):
                bmesh.ops.create_cone(bm, cap_ends=True, segments=12, radius1=0.07, radius2=0.05, depth=0.04, matrix=_t(x, y, 0.41))
    h = obj("KIT_hinge_box__HOLE", C, M['dark'], holes)
    return main, p, h


# ---- スライドレール：外の溝形レール＋中のレール（少し前に出る）＋玉の保持 ----
def slide_rail_box():
    def outer(bm):
        box(bm, (0.16, 1, 1), (-0.42, 0, 0))                       # 背の板
        box(bm, (1, 1, 0.14), (0, 0, 0.43))                        # 上のつば
        box(bm, (1, 1, 0.14), (0, 0, -0.43))                       # 下のつば
    main = obj("KIT_slide_rail_box", C, M['white'], outer, bevel=0.02, seg=1)
    def inner(bm):
        box(bm, (0.12, 0.9, 0.62), (-0.05, 0.12, 0))
        box(bm, (0.5, 0.9, 0.1), (0.12, 0.12, 0.26))
        box(bm, (0.5, 0.9, 0.1), (0.12, 0.12, -0.26))
    i = obj("KIT_slide_rail_box__RAIL", C, M['metal'], inner, bevel=0.01, seg=1)
    def holes(bm):
        for y in (-0.35, -0.05, 0.25):
            cyl_x(bm, 0.07, -0.51, -0.47, 12, y=y)
    h = obj("KIT_slide_rail_box__HOLE", C, M['dark'], holes)
    return main, i, h


# ---- 伸縮ポール：太い筒から細い筒が順に伸びる＋継ぎ目の締め具 ----
def telescopic_cylinder():
    main = obj("KIT_telescopic_cylinder", C, M['white'], lambda bm: (cyl(bm, 0.5, -1.0, -0.15, S3), cyl(bm, 0.4, -0.15, 0.45, S3), cyl(bm, 0.3, 0.45, 1.0, S3)), bevel=0.02, seg=1)
    def collars(bm):
        ring(bm, 0.56, 0.38, -0.22, -0.08, S3)
        ring(bm, 0.46, 0.28, 0.38, 0.52, S3)
        cyl(bm, 0.34, 0.95, 1.0, S3)
        cyl(bm, 0.54, -1.0, -0.94, S3)
    c = obj("KIT_telescopic_cylinder__COLLAR", C, M['dark'], collars, bevel=0.01, seg=1)
    return main, c


# ---- ばね：らせんの針金＋上下の平らな座 ----
def spring_cylinder():
    main = obj("KIT_spring_cylinder", C, M['white'], lambda bm: helix(bm, 0.4, 0.09, -0.9, 0.9, 6), smooth=True, angle=70)
    def seats(bm):
        ring(bm, 0.5, 0.3, -1.0, -0.9, S3)
        ring(bm, 0.5, 0.3, 0.9, 1.0, S3)
    s = obj("KIT_spring_cylinder__SEAT", C, M['metal'], seats, bevel=0.01, seg=1)
    return main, s


def spring_cylinder_gas():
    main = obj("KIT_spring_cylinder-gas", C, M['white'], lambda bm: cyl(bm, 0.5, -0.85, 0.15, S3), bevel=0.04, seg=2)
    def rod(bm):
        cyl(bm, 0.2, 0.15, 0.82, 20)
        cyl(bm, 0.32, 0.15, 0.22, 24)                               # 封じの輪
        for z in (-0.92, 0.9):                                      # 両端の取り付けの軸
            cyl_x(bm, 0.16, -0.3, 0.3, 14, z=z)
    r = obj("KIT_spring_cylinder-gas__ROD", C, M['metal'], rod, bevel=0.01, seg=1)
    return main, r


# ---- 歯車：歯のある円板＋中央のハブ（平歯車） ----
def gear_cylinder():
    def gear(bm):
        ring(bm, 0.42, 0.14, -1.0, 1.0, 48)
        for k in range(20):
            a = math.tau * k / 20
            box(bm, (0.12, 0.07, 2.0), (0.45 * math.cos(a), 0.45 * math.sin(a), 0), rot_z=a)
    main = obj("KIT_gear_cylinder", C, M['white'], gear, bevel=0.01, seg=1)
    def hub(bm):
        ring(bm, 0.2, 0.08, -1.1, 1.1, 24)
        for k in range(4):                                          # 軽くする穴の縁
            a = math.tau * k / 4 + math.pi / 4
            bmesh.ops.create_cone(bm, cap_ends=True, segments=14, radius1=0.07, radius2=0.07, depth=2.04, matrix=_t(0.28 * math.cos(a), 0.28 * math.sin(a), 0))
    h = obj("KIT_gear_cylinder__HUB", C, M['dark'], hub, bevel=0.005, seg=1)
    return main, h


# ---- 歯車の箱（ウォームギア）：箱＋横に出る入力の軸＋上の出力軸 ----
def gear_box():
    main = obj("KIT_gear_box", C, M['white'], lambda bm: box(bm, (1, 1, 0.9), (0, 0, -0.05)), bevel=0.06, seg=2)
    def shafts(bm):
        cyl_x(bm, 0.12, -0.6, 0.6, 16, y=0.0, z=-0.1)
        cyl(bm, 0.14, 0.38, 0.55, 18)
        box(bm, (0.12, 0.28, 0.06), (0, 0, 0.5))
    s = obj("KIT_gear_box__SHAFT", C, M['metal'], shafts, bevel=0.01, seg=1)
    def bolts(bm):
        for x in (-0.38, 0.38):
            for y in (-0.38, 0.38):
                bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=0.05, radius2=0.05, depth=0.04, matrix=_t(x, y, 0.41))
    b = obj("KIT_gear_box__BOLT", C, M['dark'], bolts)
    return main, s, b


def gear_box_belt():
    K = 0.25                                                        # 幅÷長さ（品番は 4 cm × 20 cm）
    pts, yc, ry = stadium(K, rz=0.5)
    def pulleys(bm):
        for y in (-yc, yc):
            n0 = _new(bm)
            cyl(bm, 0.4, -0.45, 0.45, 32)
            _xf(bm, n0, _t(0, y, 0) @ Matrix.Diagonal((1, K, 1, 1)))
    main = obj("KIT_gear_box-belt", C, M['white'], pulleys, bevel=0.01, seg=1)
    def belt(bm):
        # 横（X）と前後（Y）の面で輪になった帯。道の (u, v) を (x, y) に置く
        band = [(v, u) for (u, v) in reversed(pts)]
        loop_band(bm, band, -0.3, 0.3, 0.08, 0.08 * K, plane="xy")
    b = obj("KIT_gear_box-belt__BELT", C, M['dark'], belt, smooth=True, angle=40)
    def hubs(bm):
        for y in (-yc, yc):
            n0 = _new(bm)
            cyl(bm, 0.12, -0.5, 0.5, 16)
            _xf(bm, n0, _t(0, y, 0) @ Matrix.Diagonal((1, K, 1, 1)))
    h = obj("KIT_gear_box-belt__HUB", C, M['metal'], hubs)
    return main, b, h


# ---- ロック・留め金：本体＋横に出るかんぬき＋鍵穴 ----
def latch_box():
    main = obj("KIT_latch_box", C, M['white'], lambda bm: box(bm, (0.8, 1, 1), (-0.1, 0, 0)), bevel=0.06, seg=2)
    def bolt(bm):
        box(bm, (0.36, 0.5, 0.36), (0.42, 0, 0))
        for x in (-0.4, 0.22):
            bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=0.06, radius2=0.06, depth=0.04, matrix=_t(x, -0.3, 0.5))
    b = obj("KIT_latch_box__BOLT", C, M['metal'], bolt, bevel=0.02, seg=1)
    def key(bm):
        cyl_y(bm, 0.1, 0.48, 0.53, 16, x=-0.1, z=0.1)
        box(bm, (0.06, 0.05, 0.18), (-0.1, 0.505, -0.02))
    k = obj("KIT_latch_box__KEY", C, M['dark'], key)
    return main, b, k


# ---- クローラー：ゴムの帯の輪（溝つき）＋前後の駆動輪＋横の枠。品番の高さ÷長さ（約 0.25）で両端が丸く見えるように作る ----
def crawler_box():
    K = 0.25
    pts, yc, ry = stadium(K)
    def track(bm):
        loop_band(bm, pts, -0.45, 0.45, 0.1 * K, 0.1)
        for k in range(10):                                         # 上と下の帯の溝（突起）
            y = -yc + 2 * yc * (k + 0.5) / 10
            box(bm, (0.9, 0.035, 0.05), (0, y, -0.52))
            box(bm, (0.9, 0.035, 0.05), (0, y, 0.52))
    main = obj("KIT_crawler_box", C, M['white'], track, bevel=0.005, seg=1)
    def wheels(bm):
        for y in (-yc, yc):
            n0 = _new(bm)
            cyl_x(bm, 0.36, -0.4, 0.4, 28)
            _xf(bm, n0, _t(0, y, 0) @ Matrix.Diagonal((1, K, 1, 1)))
        for y in (-yc / 2, 0.0, yc / 2):                           # 下の転輪
            n0 = _new(bm)
            cyl_x(bm, 0.16, -0.38, 0.38, 20)
            _xf(bm, n0, _t(0, y, -0.26) @ Matrix.Diagonal((1, K, 1, 1)))
    w = obj("KIT_crawler_box__WHEEL", C, M['metal'], wheels, bevel=0.005, seg=1)
    def frame(bm):
        for x in (-0.47, 0.47):
            box(bm, (0.04, 2 * yc, 0.42), (x, 0, 0))
    f = obj("KIT_crawler_box__FRAME", C, M['dark'], frame, bevel=0.005, seg=1)
    return main, w, f


# ---- キャスター：上の取り付け板＋回る首＋二股の金具＋車輪 ----
def caster_cylinder():
    main = obj("KIT_caster_cylinder", C, M['white'], lambda bm: cyl_x(bm, 0.36, -0.2, 0.2, 28, z=-0.6), bevel=0.04, seg=2)
    def fork(bm):
        box(bm, (0.9, 0.9, 0.08), (0, 0, 0.94))                    # 取り付け板
        cyl(bm, 0.22, 0.6, 0.9, 20)                                  # 首
        for x in (-0.26, 0.26):
            box(bm, (0.06, 0.5, 0.9), (x, -0.08, 0.05 - 0.4))
        box(bm, (0.58, 0.5, 0.1), (0, -0.08, 0.52))
        cyl_x(bm, 0.07, -0.32, 0.32, 12, z=-0.6)
    f = obj("KIT_caster_cylinder__FORK", C, M['metal'], fork, bevel=0.01, seg=1)
    def hub(bm):
        cyl_x(bm, 0.18, -0.22, 0.22, 20, z=-0.6)
    h = obj("KIT_caster_cylinder__HUB", C, M['dark'], hub)
    return main, f, h


def caster_sphere_ball():
    main = obj("KIT_caster_sphere-ball", C, M['white'], lambda bm: bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=14, radius=0.36, matrix=_t(0, 0, -0.12)), smooth=True, angle=80)
    def cup(bm):
        ring(bm, 0.5, 0.38, -0.1, 0.35, 32)
        cyl(bm, 0.5, 0.35, 0.5, 32)
        for a in (0, math.pi):                                       # 取り付けの耳
            box(bm, (0.3, 0.2, 0.08), (0.55 * math.cos(a), 0, 0.46))
    c = obj("KIT_caster_sphere-ball__CUP", C, M['metal'], cup, bevel=0.01, seg=1)
    return main, c


# ---- 脚：筒＋上の関節＋下のゴムの足 ----
def leg_cylinder():
    main = obj("KIT_leg_cylinder", C, M['white'], lambda bm: cyl(bm, 0.38, -0.82, 0.82, S3), bevel=0.02, seg=1)
    def joint(bm):
        cyl(bm, 0.5, 0.78, 1.0, S3)
        ring(bm, 0.44, 0.36, -0.1, 0.02, S3)
    j = obj("KIT_leg_cylinder__JOINT", C, M['metal'], joint, bevel=0.02, seg=1)
    foot = obj("KIT_leg_cylinder__FOOT", C, M['dark'], lambda bm: cone(bm, 0.5, 0.36, -1.0, -0.8, S3), bevel=0.02, seg=1)
    return main, j, foot


# ---- ポンプ（円柱）：モーター缶＋上のポンプ頭＋吸い込み・吐き出しの口 ----
def pump_cylinder():
    main = obj("KIT_pump_cylinder", C, M['white'], lambda bm: (cyl(bm, 0.5, -1.0, 0.2, S3), cyl(bm, 0.46, 0.2, 0.75, S3)), bevel=0.03, seg=2)
    def ports(bm):
        cyl(bm, 0.12, 0.75, 1.0, 16)                                 # 上の吐き出し口
        cyl_y(bm, 0.12, 0.3, 0.75, 16, z=0.45)                       # 前の吸い込み口
        ring(bm, 0.5, 0.44, 0.15, 0.25, S3)
    p = obj("KIT_pump_cylinder__PORT", C, M['metal'], ports, bevel=0.01, seg=1)
    return main, p


# ---- ポンプ（箱）：横に寝たモーター＋ポンプ頭の箱＋口2つ ----
def pump_box():
    def body(bm):
        cyl_y(bm, 0.42, -0.5, 0.1, 28, z=-0.05)
        box(bm, (0.9, 0.38, 0.9), (0, 0.3, 0))
    main = obj("KIT_pump_box", C, M['white'], body, bevel=0.04, seg=2)
    def ports(bm):
        for x in (-0.22, 0.22):
            bmesh.ops.create_cone(bm, cap_ends=True, segments=14, radius1=0.09, radius2=0.08, depth=0.22, matrix=_t(x, 0.3, 0.55))
        ring_y(bm, 0.44, 0.38, -0.42, -0.36, 28, z=-0.05)
    p = obj("KIT_pump_box__PORT", C, M['metal'], ports, bevel=0.01, seg=1)
    return main, p


# ---- タンク：肩の丸いボトル＋上のふた＋横の水位の窓（青） ----
def tank_cylinder():
    def body(bm):
        cyl(bm, 0.5, -1.0, 0.55, 40)
        cone(bm, 0.5, 0.24, 0.55, 0.82, 40)
    main = obj("KIT_tank_cylinder", C, M['white'], body, bevel=0.05, seg=3)
    cap = obj("KIT_tank_cylinder__CAP", C, M['dark'], lambda bm: (cyl(bm, 0.26, 0.82, 1.0, 28)), bevel=0.02, seg=1)
    acc = obj("KIT_tank_cylinder__ACC", C, M['blue'], lambda bm: box(bm, (0.12, 0.04, 1.0), (0, 0.49, -0.3)), smooth=False)
    return main, cap, acc


# ---- 弁（箱）：本体の箱＋上のコイル（電磁弁）＋左右の口 ----
def valve_box():
    main = obj("KIT_valve_box", C, M['white'], lambda bm: box(bm, (1, 0.9, 0.5), (0, 0, -0.25)), bevel=0.05, seg=2)
    def coil(bm):
        cyl(bm, 0.36, 0.0, 0.5, 28)
        box(bm, (0.24, 0.2, 0.14), (0, 0.32, 0.25))                  # 配線の口
    c = obj("KIT_valve_box__COIL", C, M['dark'], coil, bevel=0.02, seg=1)
    def ports(bm):
        cyl_x(bm, 0.16, -0.62, -0.46, 18, z=-0.25)
        cyl_x(bm, 0.16, 0.46, 0.62, 18, z=-0.25)
    p = obj("KIT_valve_box__PORT", C, M['metal'], ports, bevel=0.01, seg=1)
    return main, c, p


def valve_cylinder():
    main = obj("KIT_valve_cylinder", C, M['white'], lambda bm: (cyl(bm, 0.5, -0.45, 0.45, S3), cone(bm, 0.5, 0.3, 0.45, 0.65, S3), cone(bm, 0.3, 0.5, -0.65, -0.45, S3)), bevel=0.03, seg=2)
    p = obj("KIT_valve_cylinder__PORT", C, M['metal'], lambda bm: (cyl(bm, 0.24, 0.65, 1.0, 20), cyl(bm, 0.24, -1.0, -0.65, 20)), bevel=0.01, seg=1)
    acc = obj("KIT_valve_cylinder__ACC", C, M['blue'], lambda bm: (box(bm, (0.06, 0.04, 0.4), (0, 0.5, 0)), box(bm, (0.16, 0.04, 0.06), (0, 0.5, 0.2))), smooth=False)
    return main, p, acc


# ---- ホース：細い管＋両端の金具 ----
def hose_cylinder():
    main = obj("KIT_hose_cylinder", C, M['white'], lambda bm: cyl(bm, 0.42, -0.94, 0.94, 24), smooth=True, angle=60)
    fit = obj("KIT_hose_cylinder__FIT", C, M['metal'], lambda bm: (ring(bm, 0.5, 0.2, 0.9, 1.0, 20), ring(bm, 0.5, 0.2, -1.0, -0.9, 20)), bevel=0.005, seg=1)
    return main, fit


# ---- ファン（箱）：四角い枠と丸い穴＋羽根＋ハブ＋四隅のねじ ----
def _fan_frame(bm, r_hole=0.44):
    # 四角い枠（丸い穴）：輪＋四隅を埋める三角の箱
    ring(bm, 0.5, r_hole, -0.5, 0.5, 48)
    for s_ in (-1, 1):
        box(bm, (1.0, 0.1, 1.0), (0, s_ * 0.45, 0))
        box(bm, (0.1, 1.0, 1.0), (s_ * 0.45, 0, 0))
        for t_ in (-1, 1):
            box(bm, (0.2, 0.2, 1.0), (s_ * 0.4, t_ * 0.4, 0))


def _fan_blades(bm, R, z0, z1, n=7):
    cyl(bm, R * 0.33, z0, z1, 24)
    for k in range(n):
        a = math.tau * k / n
        m = Matrix.Rotation(a, 4, 'Z') @ _t(R * 0.62, 0, (z0 + z1) / 2) @ _rx(math.radians(28)) @ Matrix.Diagonal((R * 0.62, 0.18 * R, (z1 - z0) * 0.25, 1.0))
        bmesh.ops.create_cube(bm, size=1.0, matrix=m)


def fan_box():
    main = obj("KIT_fan_box", C, M['white'], _fan_frame, bevel=0.02, seg=1)
    bl = obj("KIT_fan_box__BLADE", C, M['dark'], lambda bm: _fan_blades(bm, 0.43, -0.3, 0.3), bevel=0.005, seg=1)
    def screws(bm):
        for sx in (-0.41, 0.41):
            for sy in (-0.41, 0.41):
                bmesh.ops.create_cone(bm, cap_ends=True, segments=12, radius1=0.045, radius2=0.045, depth=1.04, matrix=_t(sx, sy, 0))
    s = obj("KIT_fan_box__SCREW", C, M['metal'], screws)
    return main, bl, s


def fan_box_blower():
    def body(bm):
        bmesh.ops.create_cone(bm, cap_ends=True, segments=40, radius1=0.44, radius2=0.44, depth=1.0, matrix=_t(-0.04, -0.04, 0))
        box(bm, (0.36, 0.5, 1.0), (0.3, 0.24, 0))                    # 吹き出し口へ広がる部分
    main = obj("KIT_fan_box-blower", C, M['white'], body, bevel=0.03, seg=2)
    def intake(bm):
        ring(bm, 0.26, 0.2, 0.5, 0.53, 28)
        _fan_blades(bm, 0.2, 0.42, 0.52, 9)
        box(bm, (0.3, 0.06, 0.8), (0.32, 0.5, 0))                    # 吹き出し口
    i = obj("KIT_fan_box-blower__INTAKE", C, M['dark'], intake, bevel=0.005, seg=1)
    return main, i


def fan_cylinder():
    main = obj("KIT_fan_cylinder", C, M['white'], lambda bm: ring(bm, 0.5, 0.42, -1.0, 1.0, 48), bevel=0.02, seg=2)
    bl = obj("KIT_fan_cylinder__BLADE", C, M['dark'], lambda bm: _fan_blades(bm, 0.41, -0.6, 0.6, 5), bevel=0.005, seg=1)
    def guard(bm):
        for k in range(3):
            n0 = _new(bm)
            box(bm, (1.0, 0.04, 0.06), (0, 0, 0.92))
            _xf(bm, n0, Matrix.Rotation(k * math.pi / 3, 4, 'Z'))
        ring(bm, 0.24, 0.2, 0.89, 0.95, 24)
    g = obj("KIT_fan_cylinder__GUARD", C, M['metal'], guard)
    return main, bl, g


# ---- 画面：縁（ベゼル）＋上と前の画面＋小さな表示灯 ----
def display_box():
    main = obj("KIT_display_box", C, M['dark'], lambda bm: box(bm, (1, 1, 1), (0, 0, 0)), bevel=0.04, seg=2)
    def screen(bm):
        box(bm, (0.86, 0.82, 0.02), (0, -0.02, 0.5))               # 上の画面
        box(bm, (0.86, 0.02, 0.82), (0, 0.5, -0.02))               # 前の画面
    sc = obj("KIT_display_box__SCREEN", C, SCREEN, screen, smooth=False)
    acc = obj("KIT_display_box__ACC", C, M['blue'], lambda bm: (box(bm, (0.04, 0.04, 0.02), (0.4, 0.44, 0.505))), smooth=False)
    return main, sc, acc


def display_box_matrix():
    main = obj("KIT_display_box-matrix", C, M['dark'], lambda bm: box(bm, (1, 1, 1), (0, 0, 0)), bevel=0.04, seg=2)
    def dots(bm):
        for i in range(8):
            for j in range(8):
                u, v = -0.39 + i * 0.111, -0.39 + j * 0.111
                bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=0.038, radius2=0.038, depth=0.02, matrix=_t(u, v, 0.505))
                bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=0.038, radius2=0.038, depth=0.02, matrix=_t(u, 0.505, v) @ _rx(math.pi / 2))
    acc = obj("KIT_display_box-matrix__ACC", C, M['blue'], dots, smooth=False)
    return main, acc


# ---- 振動モーター（円柱）：小さな缶＋軸の先の半月のおもり ----
def vibrator_cylinder():
    main = obj("KIT_vibrator_cylinder", C, M['white'], lambda bm: cyl(bm, 0.5, -1.0, 0.45, 24), bevel=0.04, seg=2)
    def weight(bm):
        cyl(bm, 0.1, 0.45, 0.65, 12)
        ring(bm, 0.46, 0.0001, 0.62, 1.0, 20, 0.0, math.pi)
    w = obj("KIT_vibrator_cylinder__WEIGHT", C, M['metal'], weight, bevel=0.01, seg=1)
    return main, w


def vibrator_box():
    main = obj("KIT_vibrator_box", C, M['white'], lambda bm: cyl(bm, 0.5, -0.5, 0.3, 32), bevel=0.05, seg=2)
    pad = obj("KIT_vibrator_box__PAD", C, M['dark'], lambda bm: cyl(bm, 0.36, 0.3, 0.5, 28), bevel=0.02, seg=1)
    return main, pad


# ---- 反射材：板＋上と前の再帰反射の格子（金属の光る面） ----
def reflector_box():
    main = obj("KIT_reflector_box", C, M['white'], lambda bm: box(bm, (1, 1, 1), (0, 0, 0)), bevel=0.02, seg=1)
    def prisms(bm):
        for i in range(10):
            for j in range(4):
                u, v = -0.45 + i * 0.1, -0.34 + j * 0.226
                box(bm, (0.07, 0.15, 0.02), (u, v, 0.505))
                box(bm, (0.07, 0.02, 0.15), (u, 0.505, v))
    p = obj("KIT_reflector_box__PRISM", C, M['metal'], prisms, smooth=False)
    return main, p


def reflector_cylinder():
    main = obj("KIT_reflector_cylinder", C, M['white'], lambda bm: cyl(bm, 0.5, -1.0, 0.7, 48), bevel=0.03, seg=2)
    def rings(bm):
        for k, r in enumerate((0.44, 0.33, 0.22, 0.11)):
            ring(bm, r, r - 0.06, 0.7, 0.95, 40)
        cyl(bm, 0.05, 0.7, 0.95, 12)
    p = obj("KIT_reflector_cylinder__PRISM", C, M['metal'], rings, smooth=False)
    return main, p


# ---- スイッチ（箱）：台＋上と前の押しボタン ----
def switch_box():
    main = obj("KIT_switch_box", C, M['white'], lambda bm: box(bm, (1, 1, 1), (0, 0, 0)), bevel=0.08, seg=2)
    def caps(bm):
        cyl(bm, 0.28, 0.5, 0.62, 24)
        cyl_y(bm, 0.28, 0.5, 0.62, 24)
    c = obj("KIT_switch_box__CAP", C, M['dark'], caps, bevel=0.03, seg=2)
    return main, c


def switch_cylinder_button():
    main = obj("KIT_switch_cylinder-button", C, M['white'], lambda bm: (cyl(bm, 0.5, -1.0, 0.0, 40), cyl(bm, 0.42, 0.0, 0.3, 40)), bevel=0.04, seg=2)
    def cap(bm):
        bmesh.ops.create_uvsphere(bm, u_segments=32, v_segments=12, radius=0.38, matrix=_t(0, 0, 0.3) @ Matrix.Diagonal((1, 1, 1.4, 1)))
        for v in list(bm.verts):
            if v.co.z < 0.3:
                v.co.z = 0.3
    acc = obj("KIT_switch_cylinder-button__ACC", C, M['orange'], cap, smooth=True, angle=80)
    return main, acc


def switch_cylinder_dial():
    def knob(bm):
        cyl(bm, 0.5, -1.0, 0.8, 40)
        for k in range(24):                                          # すべり止めの溝
            a = math.tau * k / 24
            box(bm, (0.06, 0.06, 1.6), (0.5 * math.cos(a), 0.5 * math.sin(a), -0.1), rot_z=a)
    main = obj("KIT_switch_cylinder-dial", C, M['white'], knob, bevel=0.02, seg=1)
    acc = obj("KIT_switch_cylinder-dial__ACC", C, M['blue'], lambda bm: box(bm, (0.06, 0.32, 0.04), (0, 0.24, 0.82)), smooth=False)
    return main, acc


# ---- 無線モジュール：基板＋金属のふた＋アンテナ ----
def radio_box():
    main = obj("KIT_radio_box", C, M['white'], lambda bm: box(bm, (1, 1, 0.3), (0, 0, -0.35)), bevel=0.02, seg=1)
    shield = obj("KIT_radio_box__SHIELD", C, M['metal'], lambda bm: box(bm, (0.8, 0.6, 0.5), (0, -0.15, 0.05)), bevel=0.02, seg=1)
    def ant(bm):
        for k in range(4):                                          # 基板のジグザグのアンテナ
            box(bm, (0.7, 0.04, 0.04), (0, 0.22 + k * 0.08, -0.18))
        box(bm, (0.04, 0.28, 0.04), (0.33, 0.34, -0.18))
    a = obj("KIT_radio_box__ACC", C, M['blue'], ant, smooth=False)
    return main, shield, a


# ---- ワイヤレス充電：円板＋銅の渦巻き（輪）＋縁 ----
def charger_cylinder():
    main = obj("KIT_charger_cylinder", C, M['white'], lambda bm: cyl(bm, 0.5, -1.0, 0.4, 48), bevel=0.06, seg=2)
    def coil(bm):
        for k in range(6):
            r = 0.42 - k * 0.05
            ring(bm, r, r - 0.025, 0.4, 0.7, 40)
    c = obj("KIT_charger_cylinder__COIL", C, M['metal'], coil, smooth=False)
    acc = obj("KIT_charger_cylinder__ACC", C, M['blue'], lambda bm: ring(bm, 0.505, 0.47, -0.2, 0.0, 40))
    return main, c, acc


def charger_box():
    main = obj("KIT_charger_box", C, M['white'], lambda bm: box(bm, (1, 1, 1), (0, 0, 0)), bevel=0.15, seg=3)
    def pins(bm):
        for x in (-0.2, 0.2):
            cyl_y(bm, 0.09, 0.45, 0.56, 14, x=x)
        ring_y(bm, 0.45, 0.38, 0.47, 0.53, 28)
    p = obj("KIT_charger_box__PIN", C, M['metal'], pins)
    return main, p


# ---- コンデンサ：缶＋上の十字の溝（金属）＋帯 ----
def capacitor_cylinder():
    main = obj("KIT_capacitor_cylinder", C, M['white'], lambda bm: cyl(bm, 0.5, -0.95, 0.9, 40), bevel=0.05, seg=2)
    def top(bm):
        cyl(bm, 0.44, 0.9, 0.96, 32)
        for k in range(2):
            n0 = _new(bm)
            box(bm, (0.7, 0.05, 0.08), (0, 0, 0.97))
            _xf(bm, n0, Matrix.Rotation(k * math.pi / 2, 4, 'Z'))
        for x in (-0.18, 0.18):
            bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=0.05, radius2=0.05, depth=0.2, matrix=_t(x, 0, -1.0))
    t = obj("KIT_capacitor_cylinder__TOP", C, M['metal'], top)
    band = obj("KIT_capacitor_cylinder__BAND", C, M['dark'], lambda bm: ring(bm, 0.51, 0.47, -0.3, 0.6, 40, -0.5, 0.5))
    return main, t, band


# ---- 放熱板：台の板＋並んだフィン ----
def heat_sink_box():
    def fins(bm):
        box(bm, (1, 1, 0.24), (0, 0, -0.38))
        for k in range(9):
            box(bm, (0.05, 1, 0.78), (-0.44 + k * 0.11, 0, 0.11))
    main = obj("KIT_heat_sink_box", C, M['white'], fins, bevel=0.008, seg=1)
    return (main,)


# ---- 断熱材：やわらかい板＋上の銀の面 ----
def insulation_box():
    main = obj("KIT_insulation_box", C, M['white'], lambda bm: box(bm, (1, 1, 0.9), (0, 0, -0.05)), bevel=0.12, seg=3)
    foil = obj("KIT_insulation_box__FOIL", C, M['metal'], lambda bm: box(bm, (0.98, 0.98, 0.1), (0, 0, 0.45)), bevel=0.04, seg=2)
    return main, foil


# ---- クリップ：三角の本体＋針金の持ち手 ----
def clip_box():
    def body(bm):
        # 前後（Y）に見て三角形の板を、左右に伸ばす
        bm2 = bm
        v = [bm2.verts.new((x, y, z)) for x in (-0.5, 0.5) for (y, z) in ((-0.45, -0.5), (0.45, -0.5), (0.0, 0.1))]
        bm2.faces.new((v[0], v[1], v[2])); bm2.faces.new((v[5], v[4], v[3]))
        for a, b in ((0, 1), (1, 2), (2, 0)):
            bm2.faces.new((v[a], v[a + 3], v[b + 3], v[b]))
    main = obj("KIT_clip_box", C, M['white'], body, bevel=0.03, seg=1)
    def wire(bm):
        for s in (-1, 1):
            n0 = _new(bm)
            ring(bm, 0.36, 0.3, -0.03, 0.03, 20, 0.0, math.pi)
            _xf(bm, n0, _t(0, s * 0.25, 0.35) @ _rx(math.pi / 2))
    w = obj("KIT_clip_box__WIRE", C, M['metal'], wire)
    return main, w


# ---- 吸盤：浅いお椀＋上のつまみ ----
def suction_cup_cylinder():
    def cup(bm):
        bmesh.ops.create_cone(bm, cap_ends=True, segments=48, radius1=0.5, radius2=0.18, depth=0.9, matrix=_t(0, 0, -0.55))
    main = obj("KIT_suction_cup_cylinder", C, M['white'], cup, bevel=0.04, seg=2, smooth=True, angle=50)
    knob = obj("KIT_suction_cup_cylinder__KNOB", C, M['dark'], lambda bm: (cyl(bm, 0.14, -0.1, 0.6, 16), cyl(bm, 0.22, 0.6, 1.0, 20)), bevel=0.02, seg=1)
    return main, knob


# ---- 磁石：円板＋極の境目の溝 ----
def magnet_cylinder():
    main = obj("KIT_magnet_cylinder", C, M['white'], lambda bm: cyl(bm, 0.5, -1.0, 1.0, 40), bevel=0.04, seg=2)
    groove = obj("KIT_magnet_cylinder__POLE", C, M['dark'], lambda bm: (ring(bm, 0.505, 0.48, -0.08, 0.08, 40), ring(bm, 0.2, 0.08, 0.98, 1.02, 24)))
    return main, groove


# ---- フック：S 字の曲がった棒 ----
def hook_box():
    def s_hook(bm):
        for (zc, a0, a1) in ((0.24, -math.pi / 2, math.pi), (-0.24, math.pi / 2, math.pi * 2)):
            n0 = _new(bm)
            ring(bm, 0.26, 0.12, -0.5, 0.5, 24, a0, a1)
            _xf(bm, n0, _t(0, 0, zc) @ _rx(math.pi / 2))
    main = obj("KIT_hook_box", C, M['white'], s_hook, bevel=0.02, seg=1)
    return (main,)


# ---- 面ファスナー：帯＋上の細かいかぎの面 ----
def velcro_box():
    main = obj("KIT_velcro_box", C, M['white'], lambda bm: box(bm, (1, 1, 1), (0, 0, 0)), bevel=0.04, seg=1)
    def hooks(bm):
        for i in range(24):
            for j in range(5):
                box(bm, (0.025, 0.1, 0.12), (-0.48 + i * 0.0417, -0.36 + j * 0.18, 0.52))
    h = obj("KIT_velcro_box__HOOKS", C, M['dark'], hooks, smooth=False)
    return main, h


# ---- 取っ手：両端の脚＋上の握り（ゴムの帯） ----
def handle_box():
    def body(bm):
        for x in (-0.42, 0.42):
            box(bm, (0.16, 0.8, 1.0), (x, 0, 0))
        box(bm, (1.0, 0.8, 0.3), (0, 0, 0.35))
    main = obj("KIT_handle_box", C, M['white'], body, bevel=0.08, seg=3)
    grip = obj("KIT_handle_box__GRIP", C, M['dark'], lambda bm: box(bm, (0.6, 0.84, 0.34), (0, 0, 0.35)), bevel=0.1, seg=3)
    return main, grip


# ---- 防水パッキン（箱＝すき間テープ）：丸い断面の帯 ----
def gasket_box():
    main = obj("KIT_gasket_box", C, M['white'], lambda bm: box(bm, (1, 1, 1), (0, 0, 0)), bevel=0.3, seg=4)
    return (main,)


# ---- メッシュの覆い：枠＋細い格子 ----
def mesh_cover_box():
    def frame(bm):
        for (sx, sy, x, y) in ((1, 0.06, 0, 0.47), (1, 0.06, 0, -0.47), (0.06, 1, 0.47, 0), (0.06, 1, -0.47, 0)):
            box(bm, (sx, sy, 1), (x, y, 0))
    main = obj("KIT_mesh_cover_box", C, M['white'], frame, bevel=0.01, seg=1)
    def grid(bm):
        for k in range(15):
            u = -0.44 + k * 0.0628
            box(bm, (0.012, 0.94, 0.7), (u, 0, 0))
            box(bm, (0.94, 0.012, 0.7), (0, u, 0))
    g = obj("KIT_mesh_cover_box__MESH", C, M['metal'], grid, smooth=False)
    return main, g


# ---- 透明な窓：板（DNA の素材＝ガラス）＋金属の縁 ----
def window_box():
    main = obj("KIT_window_box", C, M['glass'], lambda bm: box(bm, (0.96, 0.96, 1), (0, 0, 0)), bevel=0.01, seg=1)
    def frame(bm):
        for (sx, sy, x, y) in ((1, 0.04, 0, 0.48), (1, 0.04, 0, -0.48), (0.04, 1, 0.48, 0), (0.04, 1, -0.48, 0)):
            box(bm, (sx, sy, 1.04), (x, y, 0))
    f = obj("KIT_window_box__FRAME", C, M['metal'], frame, bevel=0.005, seg=1)
    return main, f


# ======================================================================
# 品番の見た目（前からある種類）
# ======================================================================

# ---- 電池 ----
def battery_box_aa():
    main = obj("KIT_battery_box-aa", C, M['dark'], lambda bm: (box(bm, (1, 1, 0.25), (0, 0, -0.375)), box(bm, (0.06, 1, 0.9), (-0.47, 0, 0)), box(bm, (0.06, 1, 0.9), (0.47, 0, 0))), bevel=0.03, seg=1)
    def cells(bm):
        for k in range(4):
            cyl_y(bm, 0.105, -0.42, 0.42, 20, x=-0.33 + k * 0.22, z=0.02)
    c = obj("KIT_battery_box-aa__CELL", C, M['white'], cells, bevel=0.01, seg=1)
    def ends(bm):
        for k in range(4):
            cyl_y(bm, 0.07, 0.42, 0.47, 14, x=-0.33 + k * 0.22, z=0.02)
            box(bm, (0.16, 0.04, 0.3), (-0.33 + k * 0.22, -0.47, 0.0))
    e = obj("KIT_battery_box-aa__TERM", C, M['metal'], ends)
    acc = obj("KIT_battery_box-aa__ACC", C, M['blue'], lambda bm: [box(bm, (0.12, 0.6, 0.02), (-0.33 + k * 0.22, 0, 0.13)) for k in range(4)], smooth=False)
    return main, c, e, acc


def battery_box_cell():
    main = obj("KIT_battery_box-cell", C, M['white'], lambda bm: box(bm, (1, 1, 1), (0, 0, 0)), bevel=0.14, seg=3)
    def tops(bm):
        for i in range(2):
            for j in range(2):
                bmesh.ops.create_cone(bm, cap_ends=True, segments=20, radius1=0.2, radius2=0.2, depth=0.05, matrix=_t(-0.24 + i * 0.48, -0.24 + j * 0.48, 0.5))
        box(bm, (0.7, 0.14, 0.02), (0, -0.24, 0.53))
        box(bm, (0.7, 0.14, 0.02), (0, 0.24, 0.53))
    t = obj("KIT_battery_box-cell__TERM", C, M['metal'], tops)
    acc = obj("KIT_battery_box-cell__ACC", C, M['blue'], lambda bm: box(bm, (1.02, 1.02, 0.1), (0, 0, -0.15)), smooth=False)
    return main, t, acc


def battery_cylinder_cell():
    main = obj("KIT_battery_cylinder-cell", C, M['white'], lambda bm: cyl(bm, 0.5, -0.94, 0.92, 40), bevel=0.04, seg=2)
    term = obj("KIT_battery_cylinder-cell__TERM", C, M['metal'], lambda bm: (cyl(bm, 0.44, 0.92, 0.96, 32), cyl(bm, 0.2, 0.96, 1.0, 20), cyl(bm, 0.44, -1.0, -0.94, 32)), bevel=0.005, seg=1)
    acc = obj("KIT_battery_cylinder-cell__ACC", C, M['blue'], lambda bm: ring(bm, 0.505, 0.48, 0.55, 0.75, 40))
    return main, term, acc


def battery_cylinder_coin():
    main = obj("KIT_battery_cylinder-coin", C, M['metal'], lambda bm: (cyl(bm, 0.5, -1.0, 0.4, 48), cyl(bm, 0.44, 0.4, 1.0, 48)), bevel=0.1, seg=2)
    mark = obj("KIT_battery_cylinder-coin__MARK", C, M['dark'], lambda bm: (box(bm, (0.24, 0.05, 0.04), (0, 0, 1.0)), box(bm, (0.05, 0.24, 0.04), (0, 0, 1.0))), smooth=False)
    return main, mark


def battery_box_cell9v():
    main = obj("KIT_battery_box-cell9v", C, M['white'], lambda bm: box(bm, (1, 1, 0.9), (0, 0, -0.05)), bevel=0.06, seg=2)
    def snaps(bm):
        bmesh.ops.create_cone(bm, cap_ends=True, segments=18, radius1=0.15, radius2=0.15, depth=0.12, matrix=_t(-0.22, 0, 0.46))
        bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=0.17, radius2=0.17, depth=0.12, matrix=_t(0.22, 0, 0.46))
        box(bm, (1.0, 1.0, 0.06), (0, 0, 0.38))
    s = obj("KIT_battery_box-cell9v__TERM", C, M['metal'], snaps)
    acc = obj("KIT_battery_box-cell9v__ACC", C, M['blue'], lambda bm: box(bm, (1.01, 1.01, 0.18), (0, 0, -0.2)), smooth=False)
    return main, s, acc


def battery_box_pouch():
    def pouch(bm):
        box(bm, (0.92, 0.84, 1.0), (0, -0.04, 0))
        box(bm, (1.0, 0.92, 0.25), (0, -0.04, 0))                   # 封じた縁のつば
    main = obj("KIT_battery_box-pouch", C, M['metal'], pouch, bevel=0.1, seg=3)
    def tabs(bm):
        for x in (-0.2, 0.2):
            box(bm, (0.12, 0.14, 0.12), (x, 0.45, 0))
    t = obj("KIT_battery_box-pouch__TERM", C, M['white'], tabs)
    wires = obj("KIT_battery_box-pouch__WIRE", C, M['dark'], lambda bm: [cyl_y(bm, 0.05, 0.5, 0.58, 10, x=x) for x in (-0.2, 0.2)])
    return main, t, wires


def battery_box_power_bank():
    main = obj("KIT_battery_box-power_bank", C, M['white'], lambda bm: box(bm, (1, 1, 1), (0, 0, 0)), bevel=0.2, seg=4)
    def ports(bm):
        box(bm, (0.36, 0.06, 0.26), (-0.15, 0.49, 0))
        box(bm, (0.2, 0.06, 0.2), (0.25, 0.49, 0))
    p = obj("KIT_battery_box-power_bank__PORT", C, M['dark'], ports)
    acc = obj("KIT_battery_box-power_bank__ACC", C, M['blue'], lambda bm: [box(bm, (0.04, 0.05, 0.04), (0.2 + k * 0.06, 0.38, 0.5)) for k in range(4)], smooth=False)
    return main, p, acc


# ---- 加熱部・冷却部 ----
def cooler_box_peltier():
    main = obj("KIT_cooler_box-peltier", C, M['white'], lambda bm: (box(bm, (1, 1, 0.3), (0, 0, 0.35)), box(bm, (1, 1, 0.3), (0, 0, -0.35))), bevel=0.02, seg=1)
    def cells(bm):
        for i in range(6):
            for j in range(6):
                box(bm, (0.1, 0.1, 0.42), (-0.38 + i * 0.152, -0.38 + j * 0.152, 0))
    c = obj("KIT_cooler_box-peltier__CELL", C, M['metal'], cells)
    wires = obj("KIT_cooler_box-peltier__WIRE", C, M['dark'], lambda bm: [cyl_y(bm, 0.05, 0.5, 0.75, 10, x=x) for x in (-0.12, 0.12)])
    acc = obj("KIT_cooler_box-peltier__ACC", C, M['blue'], lambda bm: box(bm, (0.9, 0.9, 0.02), (0, 0, -0.51)), smooth=False)
    return main, c, wires, acc


def heater_box_film():
    main = obj("KIT_heater_box-film", C, M['white'], lambda bm: box(bm, (1, 1, 1), (0, 0, 0)), bevel=0.04, seg=1)
    def trace(bm):
        n = 7
        for k in range(n):
            y = -0.4 + k * 0.8 / (n - 1)
            box(bm, (0.8, 0.04, 0.04), (0, y, 0.51))
            x = 0.4 if k % 2 == 0 else -0.4
            if k < n - 1:
                box(bm, (0.04, 0.8 / (n - 1) + 0.04, 0.04), (x, y + 0.4 / (n - 1), 0.51))
    acc = obj("KIT_heater_box-film__ACC", C, M['orange'], trace, smooth=False)
    return main, acc


def heater_box_ptc():
    main = obj("KIT_heater_box-ptc", C, M['white'], lambda bm: (box(bm, (1, 1, 0.12), (0, 0, 0.44)), box(bm, (1, 1, 0.12), (0, 0, -0.44)), box(bm, (0.08, 1, 1), (-0.46, 0, 0)), box(bm, (0.08, 1, 1), (0.46, 0, 0))), bevel=0.02, seg=1)
    def fins(bm):
        for k in range(10):
            box(bm, (0.035, 0.96, 0.76), (-0.38 + k * 0.084, 0, 0))
    f = obj("KIT_heater_box-ptc__FIN", C, M['metal'], fins)
    acc = obj("KIT_heater_box-ptc__ACC", C, M['orange'], lambda bm: [box(bm, (0.04, 0.9, 0.7), (-0.34 + k * 0.17, 0, 0)) for k in range(5)], smooth=False)
    return main, f, acc


# ---- 発光部 ----
def light_box_led_tape():
    main = obj("KIT_light_box-led_tape", C, M['white'], lambda bm: box(bm, (1, 1, 1), (0, 0, 0)), bevel=0.02, seg=1)
    def leds(bm):
        for k in range(16):
            x = -0.47 + k * 0.0627
            box(bm, (0.03, 0.5, 0.06), (x, 0, 0.52))
            box(bm, (0.03, 0.06, 0.5), (x, 0.52, 0))
    acc = obj("KIT_light_box-led_tape__ACC", C, M['blue'], leds, smooth=False)
    return main, acc


def light_box_panel():
    main = obj("KIT_light_box-panel", C, M['white'], lambda bm: box(bm, (1, 1, 1), (0, 0, 0)), bevel=0.03, seg=2)
    acc = obj("KIT_light_box-panel__ACC", C, M['blue'], lambda bm: (box(bm, (0.88, 0.88, 0.04), (0, 0, 0.5)), box(bm, (0.88, 0.04, 0.88), (0, 0.5, 0))), smooth=False)
    return main, acc


# ---- 駆動部 ----
def motor_box_servo():
    def body(bm):
        box(bm, (1, 1, 0.82), (0, 0, -0.09))
        box(bm, (1.36, 0.9, 0.08), (0, 0, 0.12))                   # 取り付けの耳
    main = obj("KIT_motor_box-servo", C, M['dark'], body, bevel=0.03, seg=1)
    def horn(bm):
        bmesh.ops.create_cone(bm, cap_ends=True, segments=18, radius1=0.15, radius2=0.15, depth=0.2, matrix=_t(0.22, 0, 0.42))
        box(bm, (0.12, 0.8, 0.06), (0.22, 0, 0.52))
    h = obj("KIT_motor_box-servo__HORN", C, M['white'], horn, bevel=0.01, seg=1)
    def ears(bm):
        for x in (-0.6, 0.6):
            bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=0.05, radius2=0.05, depth=0.1, matrix=_t(x, 0, 0.12))
    e = obj("KIT_motor_box-servo__HOLE", C, M['metal'], ears)
    return main, h, e


def motor_box_stepper():
    def body(bm):
        for k, z in enumerate((-0.42, -0.14, 0.14, 0.42)):
            box(bm, (1, 1, 0.24), (0, 0, z))
    main = obj("KIT_motor_box-stepper", C, M['dark'], body, bevel=0.12, seg=1)
    def mid(bm):
        box(bm, (0.98, 0.98, 0.32), (0, 0, 0))
        cyl(bm, 0.16, 0.5, 0.56, 20)
        cyl(bm, 0.06, 0.5, 0.8, 12)
    m = obj("KIT_motor_box-stepper__METAL", C, M['metal'], mid, bevel=0.05, seg=1)
    return main, m


def motor_cylinder_brushless():
    def bell(bm):
        cyl(bm, 0.5, -0.4, 0.8, 40)
        for k in range(12):                                          # 冷却の穴の縁
            a = math.tau * k / 12
            box(bm, (0.1, 0.06, 0.04), (0.35 * math.cos(a), 0.35 * math.sin(a), 0.81), rot_z=a)
    main = obj("KIT_motor_cylinder-brushless", C, M['metal'], bell, bevel=0.03, seg=1)
    def base(bm):
        cyl(bm, 0.44, -1.0, -0.4, 36)
        cyl(bm, 0.08, 0.8, 1.0, 12)
    b = obj("KIT_motor_cylinder-brushless__BASE", C, M['dark'], base, bevel=0.02, seg=1)
    acc = obj("KIT_motor_cylinder-brushless__ACC", C, M['blue'], lambda bm: ring(bm, 0.505, 0.48, 0.1, 0.18, 40))
    return main, b, acc


def motor_cylinder_gear():
    main = obj("KIT_motor_cylinder-gear", C, M['white'], lambda bm: cyl(bm, 0.46, -1.0, 0.1, S3), bevel=0.02, seg=1)
    def gearbox(bm):
        cyl(bm, 0.5, 0.1, 0.78, S3)
        for k in range(3):
            ring(bm, 0.52, 0.48, 0.22 + k * 0.18, 0.28 + k * 0.18, S3)
        cyl(bm, 0.1, 0.78, 1.0, 12)
        box(bm, (0.2, 0.08, 0.2), (0, 0.07, 0.9))                    # D 形の軸の平らな面
    g = obj("KIT_motor_cylinder-gear__GEAR", C, M['metal'], gearbox, bevel=0.01, seg=1)
    return main, g


def motor_cylinder_hub():
    def tire(bm):
        ring(bm, 0.5, 0.36, -0.9, 0.9, 40)
        for k in range(20):
            a = math.tau * k / 20
            box(bm, (0.04, 0.06, 1.6), (0.5 * math.cos(a), 0.5 * math.sin(a), 0), rot_z=a)
    main = obj("KIT_motor_cylinder-hub", C, M['dark'], tire, bevel=0.03, seg=2)
    hub = obj("KIT_motor_cylinder-hub__HUB", C, M['metal'], lambda bm: (cyl(bm, 0.37, -0.75, 0.75, 40), cyl(bm, 0.12, -1.0, 1.0, 16)), bevel=0.02, seg=1)
    return main, hub


def motor_cylinder_linear():
    main = obj("KIT_motor_cylinder-linear", C, M['white'], lambda bm: (cyl(bm, 0.5, -1.0, 0.1, S3)), bevel=0.04, seg=2)
    def rod(bm):
        cyl(bm, 0.24, 0.1, 0.86, 20)
        cyl(bm, 0.34, 0.86, 1.0, 20)                                  # 先端の取り付け金具
        cyl_x(bm, 0.12, -0.36, 0.36, 12, z=0.93)
        cyl_x(bm, 0.12, -0.52, 0.52, 12, z=-0.93)
    r = obj("KIT_motor_cylinder-linear__ROD", C, M['metal'], rod, bevel=0.01, seg=1)
    side = obj("KIT_motor_cylinder-linear__MOTOR", C, M['dark'], lambda bm: cyl_y(bm, 0.3, 0.2, 0.75, 24, z=-0.7), bevel=0.02, seg=1)
    return main, r, side


# ---- センサー ----
def sensor_box_camera():
    main = obj("KIT_sensor_box-camera", C, M['white'], lambda bm: box(bm, (1, 1, 1), (0, 0, 0)), bevel=0.1, seg=3)
    def barrel(bm):
        cyl_y(bm, 0.34, 0.46, 0.62, 32, z=0.02)
    b = obj("KIT_sensor_box-camera__BARREL", C, M['dark'], barrel, bevel=0.02, seg=1)
    def lens(bm):
        cyl_y(bm, 0.22, 0.6, 0.65, 28, z=0.02)
        box(bm, (0.08, 0.02, 0.08), (0.36, 0.505, 0.36))
    acc = obj("KIT_sensor_box-camera__ACC", C, M['blue'], lens, smooth=True, angle=40)
    return main, b, acc


# ---- 距離が分かるステレオカメラ：横に長い筐体＋左右2つのレンズ（品番の縦横比 3.6：1 で丸く見えるよう、横を縮めて作る） ----
def sensor_box_stereo():
    K = 1 / 3.6
    main = obj("KIT_sensor_box-stereo", C, M['white'], lambda bm: box(bm, (1, 1, 1), (0, 0, 0)), bevel=0.08, seg=2)
    def lenses(bm):
        for x in (-0.33, 0.33):
            n0 = _new(bm)
            cyl_y(bm, 0.36, 0.44, 0.58, 28)
            _xf(bm, n0, _t(x, 0, 0) @ Matrix.Diagonal((K, 1, 1, 1)))
    b = obj("KIT_sensor_box-stereo__BARREL", C, M['dark'], lenses, bevel=0.01, seg=1)
    def glass(bm):
        for x in (-0.33, 0.33):
            n0 = _new(bm)
            cyl_y(bm, 0.24, 0.56, 0.6, 24)
            _xf(bm, n0, _t(x, 0, 0) @ Matrix.Diagonal((K, 1, 1, 1)))
        box(bm, (0.04, 0.02, 0.12), (0, 0.505, 0.2))
    acc = obj("KIT_sensor_box-stereo__ACC", C, M['blue'], glass, smooth=True, angle=40)
    return main, b, acc


def sensor_box_climate():
    main = obj("KIT_sensor_box-climate", C, M['white'], lambda bm: box(bm, (1, 1, 1), (0, 0, 0)), bevel=0.08, seg=2)
    def vents(bm):
        for k in range(5):
            box(bm, (0.6, 0.04, 0.06), (0, 0.5, -0.3 + k * 0.13))
            box(bm, (0.6, 0.06, 0.04), (0, -0.25 + k * 0.12, 0.5))
    v = obj("KIT_sensor_box-climate__VENT", C, M['dark'], vents, smooth=False)
    acc = obj("KIT_sensor_box-climate__ACC", C, M['blue'], lambda bm: box(bm, (0.12, 0.04, 0.12), (0.36, 0.5, 0.36)), smooth=False)
    return main, v, acc


def sensor_box_gps():
    main = obj("KIT_sensor_box-gps", C, M['dark'], lambda bm: box(bm, (1, 1, 0.5), (0, 0, -0.25)), bevel=0.05, seg=2)
    patch = obj("KIT_sensor_box-gps__PATCH", C, M['white'], lambda bm: box(bm, (0.78, 0.78, 0.5), (0, 0, 0.25)), bevel=0.04, seg=2)
    acc = obj("KIT_sensor_box-gps__ACC", C, M['blue'], lambda bm: (bmesh.ops.create_cone(bm, cap_ends=True, segments=16, radius1=0.1, radius2=0.1, depth=0.06, matrix=_t(0, 0, 0.52))), smooth=True)
    return main, patch, acc


def sensor_box_imu():
    main = obj("KIT_sensor_box-imu", C, M['white'], lambda bm: box(bm, (1, 1, 0.6), (0, 0, -0.2)), bevel=0.04, seg=1)
    chip = obj("KIT_sensor_box-imu__CHIP", C, M['dark'], lambda bm: box(bm, (0.44, 0.44, 0.4), (0, 0, 0.2)), bevel=0.02, seg=1)
    def pins(bm):
        for k in range(6):
            box(bm, (0.08, 0.06, 0.1), (-0.4 + k * 0.16, 0.47, -0.25))
    p = obj("KIT_sensor_box-imu__PIN", C, M['metal'], pins)
    acc = obj("KIT_sensor_box-imu__ACC", C, M['blue'], lambda bm: bmesh.ops.create_cone(bm, cap_ends=True, segments=12, radius1=0.05, radius2=0.05, depth=0.04, matrix=_t(-0.14, -0.14, 0.41)), smooth=True)
    return main, chip, p, acc


def sensor_box_load_cell():
    main = obj("KIT_sensor_box-load_cell", C, M['white'], lambda bm: box(bm, (1, 1, 1), (0, 0, 0)), bevel=0.04, seg=1)
    def holes(bm):
        for x in (-0.38, -0.26, 0.26, 0.38):
            bmesh.ops.create_cone(bm, cap_ends=True, segments=14, radius1=0.035, radius2=0.035, depth=1.04, matrix=_t(x, 0, 0))
        for x in (-0.06, 0.06):                                     # 中央のくびれの穴（前後に抜ける）
            cyl_y(bm, 0.12, -0.52, 0.52, 24, x=x)
    h = obj("KIT_sensor_box-load_cell__HOLE", C, M['dark'], holes)
    acc = obj("KIT_sensor_box-load_cell__ACC", C, M['blue'], lambda bm: box(bm, (0.2, 0.3, 0.02), (0, 0, 0.51)), smooth=False)
    return main, h, acc


def sensor_box_microphone():
    main = obj("KIT_sensor_box-microphone", C, M['white'], lambda bm: cyl(bm, 0.5, -0.5, 0.5, 48), bevel=0.05, seg=2)
    def holes(bm):
        for k in range(4):
            a = math.tau * k / 4 + math.pi / 4
            bmesh.ops.create_cone(bm, cap_ends=True, segments=16, radius1=0.07, radius2=0.07, depth=0.04, matrix=_t(0.32 * math.cos(a), 0.32 * math.sin(a), 0.5))
    h = obj("KIT_sensor_box-microphone__HOLE", C, M['dark'], holes)
    acc = obj("KIT_sensor_box-microphone__ACC", C, M['blue'], lambda bm: ring(bm, 0.16, 0.11, 0.49, 0.53, 28))
    return main, h, acc


def sensor_box_rain():
    main = obj("KIT_sensor_box-rain", C, M['dark'], lambda bm: box(bm, (1, 1, 1), (0, 0, 0)), bevel=0.04, seg=1)
    def comb(bm):
        box(bm, (0.06, 0.86, 0.04), (-0.44, 0, 0.51))
        box(bm, (0.06, 0.86, 0.04), (0.44, 0, 0.51))
        for k in range(9):
            y = -0.4 + k * 0.1
            x0 = -0.44 if k % 2 == 0 else 0.44
            box(bm, (0.82, 0.035, 0.04), (x0 * 0.06, y, 0.51))
    c = obj("KIT_sensor_box-rain__TRACE", C, M['metal'], comb, smooth=False)
    acc = obj("KIT_sensor_box-rain__ACC", C, M['blue'], lambda bm: box(bm, (0.06, 0.06, 0.03), (0.44, -0.44, 0.51)), smooth=False)
    return main, c, acc


def sensor_box_tof():
    main = obj("KIT_sensor_box-tof", C, M['white'], lambda bm: box(bm, (1, 1, 1), (0, 0, 0)), bevel=0.06, seg=2)
    def windows(bm):
        for x in (-0.18, 0.18):
            box(bm, (0.22, 0.04, 0.22), (x, 0.5, 0.05))
            box(bm, (0.22, 0.22, 0.04), (x, 0.05, 0.5))
    w = obj("KIT_sensor_box-tof__WIN", C, M['dark'], windows, smooth=False)
    acc = obj("KIT_sensor_box-tof__ACC", C, M['blue'], lambda bm: (box(bm, (0.1, 0.045, 0.1), (-0.18, 0.505, 0.05)), box(bm, (0.1, 0.1, 0.045), (-0.18, 0.05, 0.505))), smooth=False)
    return main, w, acc


def sensor_box_ultrasonic():
    main = obj("KIT_sensor_box-ultrasonic", C, M['white'], lambda bm: box(bm, (1, 1, 1), (0, 0, 0)), bevel=0.05, seg=1)
    def eyes(bm):
        for x in (-0.27, 0.27):
            cyl_y(bm, 0.21, 0.45, 0.95, 28, x=x)
    e = obj("KIT_sensor_box-ultrasonic__EYE", C, M['metal'], eyes, bevel=0.02, seg=1)
    def mesh_(bm):
        for x in (-0.27, 0.27):
            cyl_y(bm, 0.17, 0.95, 0.98, 24, x=x)
    m = obj("KIT_sensor_box-ultrasonic__MESH", C, M['dark'], mesh_)
    acc = obj("KIT_sensor_box-ultrasonic__ACC", C, M['blue'], lambda bm: box(bm, (0.08, 0.04, 0.08), (0, 0.505, 0.34)), smooth=False)
    return main, e, m, acc


def sensor_cylinder_climate():
    main = obj("KIT_sensor_cylinder-climate", C, M['metal'], lambda bm: (cyl(bm, 0.5, -0.7, 0.75, 20), cone(bm, 0.5, 0.2, -0.95, -0.7, 20)), bevel=0.02, seg=1)
    cap = obj("KIT_sensor_cylinder-climate__CAP", C, M['dark'], lambda bm: cyl(bm, 0.56, 0.75, 1.0, 20), bevel=0.02, seg=1)
    acc = obj("KIT_sensor_cylinder-climate__ACC", C, M['blue'], lambda bm: ring(bm, 0.58, 0.5, 0.6, 0.72, 20))
    return main, cap, acc


def sensor_cylinder_lidar():
    main = obj("KIT_sensor_cylinder-lidar", C, M['white'], lambda bm: (cyl(bm, 0.5, -1.0, -0.2, 48), cyl(bm, 0.46, 0.2, 1.0, 48)), bevel=0.05, seg=2)
    win = obj("KIT_sensor_cylinder-lidar__WIN", C, M['dark'], lambda bm: cyl(bm, 0.43, -0.22, 0.22, 48), bevel=0.01, seg=1)
    acc = obj("KIT_sensor_cylinder-lidar__ACC", C, M['blue'], lambda bm: (ring(bm, 0.44, 0.4, -0.05, 0.05, 48), box(bm, (0.12, 0.04, 0.18), (0, 0.44, 0.0))), smooth=False)
    return main, win, acc


def sensor_cylinder_microphone():
    main = obj("KIT_sensor_cylinder-microphone", C, M['metal'], lambda bm: cyl(bm, 0.5, -1.0, 0.8, 32), bevel=0.06, seg=2)
    acc = obj("KIT_sensor_cylinder-microphone__ACC", C, M['blue'], lambda bm: ring(bm, 0.505, 0.47, 0.5, 0.62, 32))
    top = obj("KIT_sensor_cylinder-microphone__MESH", C, M['dark'], lambda bm: (cyl(bm, 0.4, 0.8, 0.95, 28), [bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=0.04, radius2=0.04, depth=0.04, matrix=_t(0.2 * math.cos(math.tau * k / 6), 0.2 * math.sin(math.tau * k / 6), 0.97)) for k in range(6)]))
    return main, top, acc


def sensor_cylinder_ultrasonic():
    main = obj("KIT_sensor_cylinder-ultrasonic", C, M['white'], lambda bm: (cyl(bm, 0.5, -1.0, 0.6, 40), cyl(bm, 0.42, 0.6, 0.9, 40)), bevel=0.04, seg=2)
    face = obj("KIT_sensor_cylinder-ultrasonic__FACE", C, M['dark'], lambda bm: cyl(bm, 0.38, 0.9, 1.0, 36), bevel=0.01, seg=1)
    acc = obj("KIT_sensor_cylinder-ultrasonic__ACC", C, M['blue'], lambda bm: ring(bm, 0.505, 0.47, 0.2, 0.3, 40))
    return main, face, acc


def sensor_sphere_pir():
    def dome(bm):
        bmesh.ops.create_uvsphere(bm, u_segments=12, v_segments=8, radius=0.46, matrix=_t(0, 0, -0.05))
        for v in list(bm.verts):
            if v.co.z < -0.05:
                v.co.z = -0.05
    main = obj("KIT_sensor_sphere-pir", C, M['white'], dome, smooth=False)
    base = obj("KIT_sensor_sphere-pir__BASE", C, M['dark'], lambda bm: cyl(bm, 0.5, -0.5, -0.05, 32), bevel=0.03, seg=1)
    acc = obj("KIT_sensor_sphere-pir__ACC", C, M['blue'], lambda bm: ring(bm, 0.505, 0.47, -0.2, -0.12, 32))
    return main, base, acc


def speaker_cylinder_buzzer():
    main = obj("KIT_speaker_cylinder-buzzer", C, M['dark'], lambda bm: cyl(bm, 0.5, -1.0, 0.8, 40), bevel=0.06, seg=2)
    def top(bm):
        ring(bm, 0.14, 0.08, 0.78, 0.86, 24)
        for k in range(3):
            r = 0.42 - k * 0.08
            ring(bm, r, r - 0.02, 0.8, 0.84, 40)
    t = obj("KIT_speaker_cylinder-buzzer__TOP", C, M['metal'], top)
    return main, t


PARTS3 = (
    hinge_box, slide_rail_box, telescopic_cylinder, spring_cylinder, spring_cylinder_gas, gear_cylinder, gear_box, gear_box_belt,
    latch_box, crawler_box, caster_cylinder, caster_sphere_ball, leg_cylinder, pump_cylinder, pump_box, tank_cylinder,
    valve_box, valve_cylinder, hose_cylinder, fan_box, fan_box_blower, fan_cylinder, display_box, display_box_matrix,
    vibrator_cylinder, vibrator_box, reflector_box, reflector_cylinder, switch_box, switch_cylinder_button, switch_cylinder_dial,
    radio_box, charger_cylinder, charger_box, capacitor_cylinder, heat_sink_box, insulation_box, clip_box, suction_cup_cylinder,
    magnet_cylinder, hook_box, velcro_box, handle_box, gasket_box, mesh_cover_box, window_box,
    battery_box_aa, battery_box_cell, battery_cylinder_cell, battery_cylinder_coin, battery_box_cell9v, battery_box_pouch, battery_box_power_bank,
    cooler_box_peltier, heater_box_film, heater_box_ptc, light_box_led_tape, light_box_panel,
    motor_box_servo, motor_box_stepper, motor_cylinder_brushless, motor_cylinder_gear, motor_cylinder_hub, motor_cylinder_linear,
    sensor_box_camera, sensor_box_stereo, sensor_box_climate, sensor_box_gps, sensor_box_imu, sensor_box_load_cell, sensor_box_microphone, sensor_box_rain,
    sensor_box_tof, sensor_box_ultrasonic, sensor_cylinder_climate, sensor_cylinder_lidar, sensor_cylinder_microphone,
    sensor_cylinder_ultrasonic, sensor_sphere_pir, speaker_cylinder_buzzer,
)


def build_parts3(x0):
    x = x0
    for f in PARTS3:
        r = f()
        for o in (r if isinstance(r, tuple) else (r,)):
            place(o, x)
        x += 1.6
    return x
