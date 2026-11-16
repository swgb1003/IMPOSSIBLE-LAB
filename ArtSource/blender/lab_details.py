# 研究室の小物（形の作り込み）：配管・ケーブルトレイ・通気口・操作パネル・床の点検口・ゲートの注意帯。
# 座標は Unity の値（lab_room.py と同じ）。部屋は 16×16 m・天井 5 m、左の壁 X=−8 に作業台（X −6.6、Z −3・0・+3）、
# 右の壁 X=+8 に実験ゲート（開口 Z −1.5〜+1.5）。原点は部屋の中心の床（Unity では LabRoom と同じ位置に置く）。
# 見た目だけ（当たり判定なし）。マテリアル名 MAT_* は Unity で LabAssets のマテリアルに置き換える。
# 実行：exec(open(r"D:/IMPOSSIBLE_LAB/ArtSource/blender/lab_details.py", encoding="utf-8").read())
import math
import bmesh
from mathutils import Vector, Matrix
exec(open(r"D:/IMPOSSIBLE_LAB/ArtSource/blender/lab_common.py", encoding="utf-8").read())

M = materials()
C = collection('LabDetails')
W = 8.0
BENCH_Z = (-3.0, 0.0, 3.0)


def U(x, y, z):
    return Vector((-x, -z, y))


def ubox(bm, cx, cy, cz, sx, sy, sz):
    box(bm, (sx, sz, sy), U(cx, cy, cz))


def upipe_y(bm, x, z, r, y0, y1, seg=12):
    """Unity の Y 方向（縦）の管。"""
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r, radius2=r, depth=y1 - y0,
                          matrix=Matrix.Translation(U(x, (y0 + y1) * 0.5, z)))


def upipe_z(bm, x, y, r, z0, z1, seg=12):
    """Unity の Z 方向（壁ぞいの横）の管。"""
    m = Matrix.Translation(U(x, y, (z0 + z1) * 0.5)) @ Matrix.Rotation(math.radians(90), 4, 'X')
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r, radius2=r, depth=z1 - z0, matrix=m)


# ---------------- 左の壁（X = −8、部屋側は +X） ----------------
WX = -W + 0.08   # 壁のパネルの表面（パネルは壁から 8cm 手前に出ている）


def tray(bm):
    # ケーブルトレイ：コの字の溝（底と2枚の側板）。天井近くを壁ぞいに
    y = 4.25
    ubox(bm, WX + 0.09, y - 0.05, 0, 0.18, 0.012, 13.0)
    ubox(bm, WX + 0.175, y, 0, 0.012, 0.11, 13.0)
    for z in (-6.2, -3.2, -0.2, 2.8, 5.8):                      # 壁の受け金具
        ubox(bm, WX + 0.09, y - 0.08, z, 0.18, 0.05, 0.04)


def conduits(bm):
    # 作業台へ下りる配管（2本ずつ）＋壁の留め具
    for bz in BENCH_Z:
        for dz, r in ((-0.85, 0.022), (-0.79, 0.016)):
            upipe_y(bm, WX + 0.04, bz + dz, r, 0.78, 4.2)
        for y in (1.6, 2.6, 3.6):
            ubox(bm, WX + 0.03, y, bz - 0.82, 0.05, 0.03, 0.14)


def cables_in_tray(bm):
    # トレイの中のケーブル（細い管を数本）
    for i, r in enumerate((0.012, 0.016, 0.01, 0.014)):
        upipe_z(bm, WX + 0.05 + i * 0.035, 4.22, r, -6.4, 6.4, 8)


def vents(bm):
    # 通気口：枠＋横の羽根（作業台の上の高い位置）
    for z in (-4.5, 4.5):
        ubox(bm, WX + 0.015, 3.5, z, 0.03, 0.42, 0.9)                 # 枠（奥の暗い板）
        for k in range(6):
            ubox(bm, WX + 0.04, 3.33 + k * 0.065, z, 0.04, 0.012, 0.84)  # 羽根


def vent_frames(bm):
    for z in (-4.5, 4.5):
        ubox(bm, WX + 0.03, 3.73, z, 0.05, 0.03, 0.96)
        ubox(bm, WX + 0.03, 3.27, z, 0.05, 0.03, 0.96)
        for dz in (-0.465, 0.465):
            ubox(bm, WX + 0.03, 3.5, z + dz, 0.05, 0.49, 0.03)


def bench_panels(bm):
    # 作業台ごとの壁の操作パネル（白い箱）
    for bz in BENCH_Z:
        ubox(bm, WX + 0.03, 1.35, bz + 0.95, 0.05, 0.5, 0.34)


def bench_panel_dark(bm):
    for bz in BENCH_Z:
        ubox(bm, WX + 0.06, 1.43, bz + 0.95, 0.012, 0.2, 0.26)            # 小さな画面の黒い枠


def bench_panel_lights(bm):
    for bz in BENCH_Z:
        for k in range(3):
            ubox(bm, WX + 0.06, 1.2, bz + 0.86 + k * 0.09, 0.015, 0.03, 0.05)  # 青く光るボタン


# ---------------- 右の壁（X = +8）：ゲート横の操作パネル ----------------
RX = W - 0.08   # 壁のパネルの表面


def gate_panel_white(bm):
    ubox(bm, RX - 0.04, 1.3, -2.3, 0.07, 0.7, 0.45)


def gate_panel_dark(bm):
    ubox(bm, RX - 0.08, 1.42, -2.3, 0.012, 0.3, 0.34)
    ubox(bm, RX - 0.08, 1.08, -2.3, 0.012, 0.12, 0.34)


def gate_panel_light(bm):
    ubox(bm, RX - 0.085, 1.42, -2.3, 0.004, 0.24, 0.28)               # 画面（青く光る）


# ---------------- 床 ----------------
def hazard(bm):
    # ゲートの手前の注意帯（橙の斜めの帯）。床から 2mm 浮かせる
    for k in range(10):
        z = -1.35 + k * 0.3
        bm_box = Matrix.Translation(U(W - 0.6, 0.003, z)) @ Matrix.Rotation(math.radians(35), 4, 'Z') @ Matrix.Diagonal((0.12, 0.42, 0.004, 1.0))
        bmesh.ops.create_cube(bm, size=1.0, matrix=bm_box)


def hazard_base(bm):
    ubox(bm, W - 0.6, 0.002, 0, 0.62, 0.004, 3.1)


def hatches(bm):
    # 作業台の足元の点検口（金属板）
    for bz in BENCH_Z:
        ubox(bm, -5.6, 0.004, bz + 1.2, 0.6, 0.008, 0.6)


def hatch_screws(bm):
    for bz in BENCH_Z:
        for dx in (-0.26, 0.26):
            for dz in (-0.26, 0.26):
                bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=8, radius1=0.012, radius2=0.012, depth=0.006,
                                      matrix=Matrix.Translation(U(-5.6 + dx, 0.01, bz + 1.2 + dz)))


# ---------------- 天井（下面 Y=5、中央に半径 3.95 m の吹き抜け、照明は Z=±6・X=±6） ----------------
CY = 5.0


def ceil_hatch(bm):
    # 点検口：白い板（天井から 5mm 下げる）
    for x, z in ((-7.0, -4.6), (7.0, 4.6), (-4.6, 7.0), (4.6, -7.0)):
        ubox(bm, x, CY - 0.006, z, 0.6, 0.01, 0.6)


def ceil_hatch_rim(bm):
    for x, z in ((-7.0, -4.6), (7.0, 4.6), (-4.6, 7.0), (4.6, -7.0)):
        for dx, dz, sx, sz in ((0, -0.31, 0.64, 0.02), (0, 0.31, 0.64, 0.02), (-0.31, 0, 0.02, 0.64), (0.31, 0, 0.02, 0.64)):
            ubox(bm, x + dx, CY - 0.008, z + dz, sx, 0.012, sz)


def ceil_speakers(bm):
    for x, z in ((-3.0, 6.9), (3.0, 6.9), (-3.0, -6.9), (3.0, -6.9), (-6.9, 1.5), (6.9, -1.5)):
        bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=24, radius1=0.12, radius2=0.1, depth=0.025,
                              matrix=Matrix.Translation(U(x, CY - 0.012, z)))


def ceil_sprinklers(bm):
    for x, z in ((5.0, 5.0), (-5.0, 5.0), (5.0, -5.0), (-5.0, -5.0)):
        upipe_y(bm, x, z, 0.012, CY - 0.06, CY, 8)
        bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=8, radius1=0.035, radius2=0.01, depth=0.02,
                              matrix=Matrix.Translation(U(x, CY - 0.07, z)))


def duct(bm):
    # 右の壁ぎわの空調ダクト（天井の下、ゲートの看板より上）
    ubox(bm, 7.35, 4.8, 0, 0.5, 0.34, 14.0)


def duct_grilles(bm):
    for z in (-5.0, 0.0, 5.0):
        ubox(bm, 7.09, 4.8, z, 0.02, 0.22, 0.8)
        for k in range(5):
            ubox(bm, 7.08, 4.71 + k * 0.045, z, 0.02, 0.012, 0.76)


def duct_bands(bm):
    for z in (-6.8, -3.4, -1.7, 1.7, 3.4, 6.8):
        ubox(bm, 7.35, 4.8, z, 0.52, 0.36, 0.04)


# ---------------- 窓ぎわ（Z=+8）：床の吹き出し口 ----------------
def window_grille(bm):
    ubox(bm, 0, 0.006, 7.6, 13.0, 0.012, 0.3)


def window_grille_slats(bm):
    for k in range(60):
        x = -6.4 + k * (12.8 / 59)
        ubox(bm, x, 0.014, 7.6, 0.03, 0.006, 0.26)


# ---------------- 床：作業台から発明台へのケーブルのカバー ----------------
def cable_covers(bm):
    for z in (-0.6, 0.6):
        ubox(bm, -4.3, 0.008, z, 3.2, 0.016, 0.14)


made = [
    obj('DET_Tray', C, M['metal'], tray, bevel=0.003),
    obj('DET_Conduits', C, M['metal'], conduits, bevel=0.002),
    obj('DET_Cables', C, M['dark'], cables_in_tray),
    obj('DET_Vents', C, M['dark'], vents, bevel=0.002),
    obj('DET_VentFrames', C, M['white'], vent_frames, bevel=0.004),
    obj('DET_BenchPanels', C, M['white'], bench_panels, bevel=0.01, seg=3),
    obj('DET_BenchPanelDark', C, M['dark'], bench_panel_dark, bevel=0.002),
    obj('DET_BenchPanelLights', C, M['blue'], bench_panel_lights, smooth=False),
    obj('DET_GatePanel', C, M['white'], gate_panel_white, bevel=0.01, seg=3),
    obj('DET_GatePanelDark', C, M['dark'], gate_panel_dark, bevel=0.002),
    obj('DET_GatePanelLight', C, M['blue'], gate_panel_light, smooth=False),
    obj('DET_HazardBase', C, M['dark'], hazard_base, smooth=False),
    obj('DET_Hazard', C, M['orange'], hazard, smooth=False),
    obj('DET_Hatches', C, M['metal'], hatches, bevel=0.003),
    obj('DET_HatchScrews', C, M['dark'], hatch_screws),
    obj('DET_CeilHatch', C, M['white'], ceil_hatch, smooth=False),
    obj('DET_CeilHatchRim', C, M['metal'], ceil_hatch_rim, smooth=False),
    obj('DET_CeilSpeakers', C, M['dark'], ceil_speakers),
    obj('DET_CeilSprinklers', C, M['metal'], ceil_sprinklers),
    obj('DET_Duct', C, M['white'], duct, bevel=0.02, seg=2),
    obj('DET_DuctGrilles', C, M['dark'], duct_grilles, smooth=False),
    obj('DET_DuctBands', C, M['metal'], duct_bands, bevel=0.004),
    obj('DET_WindowGrille', C, M['dark'], window_grille, smooth=False),
    obj('DET_WindowGrilleSlats', C, M['metal'], window_grille_slats, smooth=False),
    obj('DET_CableCovers', C, M['dark'], cable_covers, bevel=0.005),
]
print('objects', len(made), 'tris', tri_count(made))
