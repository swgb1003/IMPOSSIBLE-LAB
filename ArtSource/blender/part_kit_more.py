# IMPOSSIBLE LAB — 部品キット 第2弾（雨よけ装置以外の部品）。part_kit.py の後に exec する（obj・box・cyl・ring・M・C・place を使う）。
# 大きさの基準・前（Blender の +Y）・名前の決まりは part_kit.py と同じ。
#   __ACC：機能の色で塗る飾り（照明・加熱・冷却・センサーなどは機能の色、それ以外は FBX の色＝青）
#   __X（ACC 以外）：FBX のマテリアルのまま（金属の金具・暗い溝など）
import math
import bmesh
from mathutils import Matrix

SEG2 = 32


def _cyl_x(bm, r, x0, x1, seg, y=0.0, z=0.0):
    """X 軸向きの円柱（ボルト・軸など）。"""
    m = Matrix.Translation(((x0 + x1) * 0.5, y, z)) @ Matrix.Rotation(math.pi / 2, 4, 'Y')
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r, radius2=r, depth=x1 - x0, matrix=m)


def _cyl_y(bm, r, y0, y1, seg, x=0.0, z=0.0):
    """Y 軸向き（前後）の円柱（レンズ・ボタンなど）。"""
    m = Matrix.Translation((x, (y0 + y1) * 0.5, z)) @ Matrix.Rotation(math.pi / 2, 4, 'X')
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r, radius2=r, depth=y1 - y0, matrix=m)


# ---- 推進ユニット（円柱）：ダクト（輪）＋中央のハブと羽根＋下の光る輪 ----
def thruster_cylinder():
    main = obj("KIT_thruster_cylinder", C, M['white'], lambda bm: ring(bm, 0.5, 0.42, -1.0, 1.0, SEG2), bevel=0.02, seg=2)
    def fan(bm):
        cyl(bm, 0.12, -0.3, 0.3, 24)                                   # ハブ
        for k in range(5):                                             # 羽根
            a = math.tau * k / 5
            m = Matrix.Rotation(a, 4, 'Z') @ Matrix.Translation((0.26, 0, 0)) @ Matrix.Rotation(math.radians(25), 4, 'X') @ Matrix.Diagonal((0.3, 0.09, 0.08, 1.0))
            bmesh.ops.create_cube(bm, size=1.0, matrix=m)
        for k in range(3):                                             # ハブを支える腕
            a = math.tau * k / 3 + 0.3
            m = Matrix.Rotation(a, 4, 'Z') @ Matrix.Translation((0.3, 0, -0.75)) @ Matrix.Diagonal((0.26, 0.04, 0.08, 1.0))
            bmesh.ops.create_cube(bm, size=1.0, matrix=m)
        cyl(bm, 0.07, -0.85, -0.6, 16)
    fan_o = obj("KIT_thruster_cylinder__FAN", C, M['dark'], fan, bevel=0.01, seg=1)
    glow = obj("KIT_thruster_cylinder__ACC", C, M['blue'], lambda bm: ring(bm, 0.43, 0.4, -0.98, -0.9, SEG2))
    return main, fan_o, glow


# ---- 車輪（円柱。組み立てで横倒し）：ゴムのタイヤ＋金属のホイール＋ボルト ----
def wheel_cylinder():
    def tire(bm):
        ring(bm, 0.5, 0.33, -0.85, 0.85, SEG2)
        for k in range(16):                                            # 溝（トレッド）
            a = math.tau * k / 16
            m = Matrix.Rotation(a, 4, 'Z') @ Matrix.Translation((0.5, 0, 0)) @ Matrix.Diagonal((0.04, 0.06, 1.5, 1.0))
            bmesh.ops.create_cube(bm, size=1.0, matrix=m)
    main = obj("KIT_wheel_cylinder", C, M['dark'], tire, bevel=0.02, seg=2)
    def hub(bm):
        cyl(bm, 0.34, -0.7, 0.7, SEG2)
        cyl(bm, 0.12, 0.7, 0.85, 20)
        cyl(bm, 0.12, -0.85, -0.7, 20)
    hub_o = obj("KIT_wheel_cylinder__HUB", C, M['metal'], hub, bevel=0.015, seg=2)
    def bolts(bm):
        for k in range(5):
            a = math.tau * k / 5
            bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=10, radius1=0.03, radius2=0.03, depth=0.08,
                                  matrix=Matrix.Translation((0.22 * math.cos(a), 0.22 * math.sin(a), 0.74)))
    bolt_o = obj("KIT_wheel_cylinder__BOLT", C, M['white'], bolts)
    return main, hub_o, bolt_o


# ---- ベルト・取り付け具（箱）：帯＋金属のバックル ----
def strap_box():
    main = obj("KIT_strap_box", C, M['white'], lambda bm: box(bm, (1, 1, 1), (0, 0, 0)), bevel=0.06, seg=2)
    def buckle(bm):
        # 前面の中央に、四角い枠のバックル
        for (sx, sz, x, z) in ((0.36, 0.08, 0, 0.32), (0.36, 0.08, 0, -0.32), (0.08, 0.72, -0.16, 0), (0.08, 0.72, 0.16, 0)):
            box(bm, (sx, 0.12, sz), (x, 0.52, z))
        box(bm, (0.06, 0.1, 0.6), (0, 0.5, 0))                         # 中央の棒
    bk = obj("KIT_strap_box__BUCKLE", C, M['metal'], buckle, bevel=0.01, seg=1)
    return main, bk


# ---- 電池（箱）：角を丸めた箱＋上の端子＋前面の残量表示（青） ----
def battery_box():
    main = obj("KIT_battery_box", C, M['white'], lambda bm: box(bm, (1, 1, 1), (0, 0, 0)), bevel=0.1, seg=3)
    def terminals(bm):
        for x in (-0.22, 0.22):
            bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=16, radius1=0.07, radius2=0.07, depth=0.1,
                                  matrix=Matrix.Translation((x, 0, 0.53)))
    term = obj("KIT_battery_box__TERM", C, M['metal'], terminals)
    def gauge(bm):
        for k in range(4):
            box(bm, (0.12, 0.03, 0.2), (-0.21 + k * 0.14, 0.51, 0.1))
    acc = obj("KIT_battery_box__ACC", C, M['blue'], gauge, smooth=False)
    return main, term, acc


# ---- 本体（箱）：角を丸めた筐体＋継ぎ目＋前面の青い表示帯 ----
def body_box():
    def build(bm):
        box(bm, (1, 1, 0.78), (0, 0, -0.11))
        box(bm, (0.96, 0.96, 0.06), (0, 0, 0.31))                       # 継ぎ目（わずかに細い）
        box(bm, (1, 1, 0.16), (0, 0, 0.42))
    main = obj("KIT_body_box", C, M['white'], build, bevel=0.05, seg=3)
    acc = obj("KIT_body_box__ACC", C, M['blue'], lambda bm: box(bm, (0.6, 0.02, 0.05), (0, 0.505, 0.1)), smooth=False)
    return main, acc


# ---- 骨組み（箱）：面取りした角材＋両端のボルト ----
def frame_box():
    main = obj("KIT_frame_box", C, M['metal'], lambda bm: box(bm, (1, 1, 1), (0, 0, 0)), bevel=0.04, seg=1)
    def bolts(bm):
        for z in (-0.42, 0.42):
            for x in (-0.25, 0.25):
                _cyl_y(bm, 0.06, 0.48, 0.54, 10, x=x, z=z)
    b = obj("KIT_frame_box__BOLT", C, M['dark'], bolts)
    return main, b


# ---- 緩衝材（箱・球）：ふっくら丸めた形 ----
def cushion_box():
    return obj("KIT_cushion_box", C, M['white'], lambda bm: box(bm, (1, 1, 1), (0, 0, 0)), bevel=0.22, seg=4)


def cushion_sphere():
    def build(bm):
        bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=14, radius=0.5)
        # 縫い目のように、赤道を少しへこませる
        for v in bm.verts:
            if abs(v.co.z) < 0.04:
                v.co.x *= 0.96; v.co.y *= 0.96
    return obj("KIT_cushion_sphere", C, M['white'], build, smooth=True, angle=80)


# ---- 板（箱）：薄い板＋縁の金属のふち ----
def panel_box():
    main = obj("KIT_panel_box", C, M['white'], lambda bm: box(bm, (0.96, 0.96, 1), (0, 0, 0)), bevel=0.03, seg=2)
    def trim(bm):
        for (sx, sy, x, y) in ((1, 0.04, 0, 0.48), (1, 0.04, 0, -0.48), (0.04, 1, 0.48, 0), (0.04, 1, -0.48, 0)):
            box(bm, (sx, sy, 1.02), (x, y, 0))
    t = obj("KIT_panel_box__TRIM", C, M['metal'], trim, bevel=0.005, seg=1)
    return main, t


# ---- 加熱部（箱）：薄い本体＋前面の光る線（橙） ----
def heater_box():
    main = obj("KIT_heater_box", C, M['white'], lambda bm: box(bm, (1, 1, 1), (0, 0, 0)), bevel=0.08, seg=3)
    def coils(bm):
        for k in range(5):
            box(bm, (0.8, 0.03, 0.06), (0, 0.51, -0.32 + k * 0.16))
    acc = obj("KIT_heater_box__ACC", C, M['orange'], coils, smooth=False)
    return main, acc


# ---- 冷却部（箱）：本体＋前面のフィン（金属）＋青い表示 ----
def cooler_box():
    main = obj("KIT_cooler_box", C, M['white'], lambda bm: box(bm, (1, 1, 1), (0, 0, 0)), bevel=0.06, seg=2)
    def fins(bm):
        for k in range(7):
            box(bm, (0.06, 0.1, 0.8), (-0.36 + k * 0.12, 0.52, 0))
    f = obj("KIT_cooler_box__FIN", C, M['metal'], fins)
    acc = obj("KIT_cooler_box__ACC", C, M['blue'], lambda bm: box(bm, (0.5, 0.02, 0.05), (0, 0.51, 0.45)), smooth=False)
    return main, f, acc


# ---- つかみ具（箱）：台＋2本の指（金属） ----
def gripper_box():
    main = obj("KIT_gripper_box", C, M['white'], lambda bm: box(bm, (1, 1, 0.45), (0, 0, 0.27)), bevel=0.06, seg=2)
    def fingers(bm):
        for x in (-0.33, 0.33):
            box(bm, (0.16, 0.5, 0.55), (x, 0.1, -0.22))
            box(bm, (0.2, 0.3, 0.1), (x * 0.92, 0.25, -0.45))               # 指先
    f = obj("KIT_gripper_box__FINGER", C, M['metal'], fingers, bevel=0.02, seg=1)
    acc = obj("KIT_gripper_box__ACC", C, M['blue'], lambda bm: box(bm, (0.3, 0.02, 0.06), (0, 0.505, 0.3)), smooth=False)
    return main, f, acc


# ---- 格納部（箱）：箱＋上の縁＋前面の持ち手のくぼみ ----
def container_box():
    def build(bm):
        box(bm, (1, 1, 0.9), (0, 0, -0.05))
        box(bm, (1.04, 1.04, 0.08), (0, 0, 0.44))                       # 上の縁
    main = obj("KIT_container_box", C, M['white'], build, bevel=0.03, seg=2)
    handle = obj("KIT_container_box__HANDLE", C, M['dark'], lambda bm: box(bm, (0.36, 0.04, 0.08), (0, 0.5, 0.25)), bevel=0.01, seg=1)
    return main, handle


# ---- 駆動部（円柱）：モーター缶＋冷却フィン＋軸 ----
def motor_cylinder():
    def build(bm):
        cyl(bm, 0.5, -0.8, 0.8, SEG2)
        for k in range(5):
            ring(bm, 0.53, 0.49, -0.6 + k * 0.28, -0.52 + k * 0.28, SEG2)
    main = obj("KIT_motor_cylinder", C, M['white'], build, bevel=0.015, seg=1)
    def shaft(bm):
        cyl(bm, 0.12, 0.8, 1.0, 16)
        cyl(bm, 0.3, -1.0, -0.8, SEG2)
    s = obj("KIT_motor_cylinder__SHAFT", C, M['metal'], shaft, bevel=0.01, seg=1)
    return main, s


# ---- 照明（円柱）：筐体＋下面の光るレンズ ----
def light_cylinder():
    main = obj("KIT_light_cylinder", C, M['white'], lambda bm: cyl(bm, 0.5, -0.85, 1.0, SEG2), bevel=0.05, seg=2)
    acc = obj("KIT_light_cylinder__ACC", C, M['blue'], lambda bm: cyl(bm, 0.4, -1.0, -0.84, SEG2), smooth=True)
    return main, acc


# ---- 音を出す部品（円柱）：筐体＋前面（上）の格子 ----
def speaker_cylinder():
    main = obj("KIT_speaker_cylinder", C, M['white'], lambda bm: cyl(bm, 0.5, -1.0, 0.85, SEG2), bevel=0.04, seg=2)
    def grille(bm):
        for k, r in enumerate((0.42, 0.32, 0.22, 0.12)):
            ring(bm, r, r - 0.05, 0.85, 0.93, SEG2)
        cyl(bm, 0.05, 0.85, 0.95, 12)
    g = obj("KIT_speaker_cylinder__GRILLE", C, M['dark'], grille)
    return main, g


# ---- センサー（円柱）：筐体＋前面のレンズ（青） ----
def sensor_cylinder():
    main = obj("KIT_sensor_cylinder", C, M['white'], lambda bm: cyl(bm, 0.5, -1.0, 1.0, SEG2), bevel=0.05, seg=2)
    acc = obj("KIT_sensor_cylinder__ACC", C, M['blue'], lambda bm: _cyl_y(bm, 0.28, 0.48, 0.54, 24, z=0.3), smooth=True)
    return main, acc


# ---- 姿勢安定ユニット（球）：球＋赤道の帯（機能の色） ----
def stabilizer_sphere():
    main = obj("KIT_stabilizer_sphere", C, M['white'], lambda bm: bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=14, radius=0.5), smooth=True, angle=80)
    acc = obj("KIT_stabilizer_sphere__ACC", C, M['orange'], lambda bm: ring(bm, 0.505, 0.47, -0.04, 0.04, 24))
    return main, acc


# ---- 太陽電池（箱）：パネル＋セルの格子（金属の線） ----
def solar_box():
    main = obj("KIT_solar_box", C, M['glass'], lambda bm: box(bm, (1, 1, 1), (0, 0, 0)), bevel=0.01, seg=1)
    def grid(bm):
        for k in range(5):                                             # 上面（+Z）のセルの境目だけ
            box(bm, (1.0, 0.012, 0.03), (0, -0.4 + k * 0.2, 0.5))
            box(bm, (0.012, 1.0, 0.03), (-0.4 + k * 0.2, 0, 0.5))
    g = obj("KIT_solar_box__GRID", C, M['metal'], grid, smooth=False)
    return main, g


MORE = (thruster_cylinder, wheel_cylinder, strap_box, battery_box, body_box, frame_box, cushion_box, cushion_sphere,
        panel_box, heater_box, cooler_box, gripper_box, container_box, motor_cylinder, light_cylinder, speaker_cylinder,
        sensor_cylinder, stabilizer_sphere, solar_box)


def build_more(x0):
    x = x0
    for f in MORE:
        r = f()
        for o in (r if isinstance(r, tuple) else (r,)):
            place(o, x)
        x += 1.6
    return x
