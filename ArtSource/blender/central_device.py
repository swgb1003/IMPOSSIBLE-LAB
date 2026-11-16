# 中央装置（発明台・ホログラム投影部・天井ユニット・床リング）
# 寸法は SceneBuilder の既存配置に合わせる（Unity 座標の原点＝研究室の中心、床 0 m）。
#   発明台：直径 3.0 m・天板 0.9 m／投影面 0.93 m／ホログラム円筒 直径 1.6 m・上端 2.5 m
#   天井ユニット：下面 3.9 m・上面 4.75 m（天井梁の下）／床リング：半径 2.6 m まで
# 実行：exec(open(r"D:/IMPOSSIBLE_LAB/ArtSource/blender/central_device.py", encoding="utf-8").read())
import math
import bmesh
from mathutils import Matrix
exec(open(r"D:/IMPOSSIBLE_LAB/ArtSource/blender/lab_common.py", encoding="utf-8").read())

M = materials()
C = collection('CentralDevice')
made = []

# ---- 発明台（台座） ----
PANELS = 12
GAP = math.radians(3.0)
STEP = TAU / PANELS


def pedestal_core(bm):
    cyl(bm, 1.44, 0.0, 0.84, 48)                     # 奥の暗い芯（パネルの隙間から見える）


def pedestal_panels(bm):
    for i in range(PANELS):
        a0 = i * STEP + GAP * 0.5
        a1 = (i + 1) * STEP - GAP * 0.5
        ring(bm, 1.50, 1.43, 0.12, 0.36, 6, a0, a1)   # 下段
        ring(bm, 1.50, 1.43, 0.60, 0.80, 6, a0, a1)   # 上段


def pedestal_vents(bm):
    # 上下の白パネルの間の通気口：横向きのルーバー（板）
    for i in range(PANELS):
        a0 = i * STEP + GAP * 0.5 + math.radians(2.0)
        a1 = (i + 1) * STEP - GAP * 0.5 - math.radians(2.0)
        for k in range(5):
            z = 0.39 + k * 0.045
            ring(bm, 1.485, 1.45, z, z + 0.022, 5, a0, a1)


def _radial(bm, r, a, z, rad, depth, seg=8):
    """半径 r・角度 a・高さ z に、外向きの短い円柱（ネジの頭など）。"""
    m = Matrix.Translation((r * math.cos(a), r * math.sin(a), z)) @ Matrix.Rotation(a, 4, 'Z') @ Matrix.Rotation(math.pi / 2, 4, 'Y')
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=rad, radius2=rad, depth=depth, matrix=m)


def panel_screws(bm):
    # パネルの四隅のネジ（上下2段 × 12枚 × 4）
    for i in range(PANELS):
        a0 = i * STEP + GAP * 0.5 + 0.045
        a1 = (i + 1) * STEP - GAP * 0.5 - 0.045
        for (z0, z1) in ((0.12, 0.36), (0.60, 0.80)):
            for a in (a0, a1):
                for z in (z0 + 0.03, z1 - 0.03):
                    _radial(bm, 1.502, a, z, 0.009, 0.006)


def panel_leds(bm):
    # 上段のパネルごとの小さな表示灯（横長）
    for i in range(PANELS):
        a = (i + 0.5) * STEP
        m = Matrix.Translation((1.503 * math.cos(a), 1.503 * math.sin(a), 0.70)) @ Matrix.Rotation(a, 4, 'Z') @ Matrix.Diagonal((0.006, 0.12, 0.012, 1.0))
        bmesh.ops.create_cube(bm, size=1.0, matrix=m)


def plinth(bm):
    ring(bm, 1.56, 0.0001, 0.0, 0.09, 48)            # 足元の台（暗い金属）
    ring(bm, 1.58, 1.40, 0.80, 0.86, 48)             # 上端の縁


def accent_base(bm):
    ring(bm, 1.535, 1.50, 0.09, 0.115, 48)           # 足元の橙の光の帯


def deck(bm):
    ring(bm, 1.52, 1.18, 0.86, 0.885, 48)            # 天板の外側（白）


def table_metal(bm):
    ring(bm, 1.18, 0.80, 0.86, 0.90, 48)             # 天板の内側（金属）
    ring(bm, 0.80, 0.70, 0.90, 0.925, 48)            # 投影面の縁
    for r in (0.30, 0.50):                           # 投影面の同心円（細い溝の代わりの低い輪）
        ring(bm, r + 0.012, r, 0.928, 0.934, 48)


def emitter(bm):
    cyl(bm, 0.70, 0.90, 0.928, 48)                   # 投影面（青く光る）


made += [
    obj('CL_PedestalCore', C, M['dark'], pedestal_core),
    obj('CL_PedestalPanels', C, M['white'], pedestal_panels, bevel=0.006),
    obj('CL_PedestalVents', C, M['dark'], pedestal_vents),
    obj('CL_Plinth', C, M['dark'], plinth, bevel=0.008),
    obj('CL_AccentBase', C, M['orange'], accent_base),
    obj('CL_Deck', C, M['white'], deck, bevel=0.006),
    obj('CL_TableMetal', C, M['metal'], table_metal, bevel=0.004),
    obj('CL_HoloEmitter', C, M['blue'], emitter),
    obj('CL_PanelScrews', C, M['metal'], panel_screws),
    obj('CL_PanelLeds', C, M['blue'], panel_leds, smooth=False),
]

# ---- ホログラム投影の円筒（ガラス＋縦の光の線＋上端の輪） ----
HOLO_TOP = 2.50


def holo_glass(bm):
    tube_open(bm, 0.80, 0.93, HOLO_TOP, 48)


def holo_bars(bm):
    for i in range(12):
        a = i * TAU / 12 + TAU / 24
        box(bm, (0.006, 0.012, HOLO_TOP - 0.95), (0.815 * math.cos(a), 0.815 * math.sin(a), (0.95 + HOLO_TOP) * 0.5), a)


def holo_top(bm):
    ring(bm, 0.87, 0.76, HOLO_TOP, HOLO_TOP + 0.06, 48)


def holo_top_light(bm):
    ring(bm, 0.80, 0.77, HOLO_TOP - 0.006, HOLO_TOP, 48)


made += [
    obj('CL_HoloGlass', C, M['glass'], holo_glass),
    obj('CL_HoloBars', C, M['blue'], holo_bars),
    obj('CL_HoloTop', C, M['metal'], holo_top, bevel=0.004),
    obj('CL_HoloTopLight', C, M['blue'], holo_top_light),
]

# ---- 天井ユニット（吊り下げの円筒・下面の光の輪・投射口・支柱・天井の大きな光の輪） ----
CZ0, CZ1 = 3.90, 4.75


def ceiling_drum(bm):
    ring(bm, 1.80, 1.20, CZ0 + 0.10, CZ1, 64)        # 胴
    ring(bm, 1.86, 1.20, CZ0 + 0.30, CZ0 + 0.36, 64) # 帯（段）
    ring(bm, 1.86, 1.20, CZ1 - 0.20, CZ1 - 0.14, 64)
    for i in range(24):                              # 縦のリブ
        a = i * TAU / 24
        box(bm, (0.08, 0.05, CZ1 - CZ0 - 0.55), (1.82 * math.cos(a), 1.82 * math.sin(a), (CZ0 + 0.36 + CZ1 - 0.20) * 0.5), a)


def ceiling_under(bm):
    ring(bm, 1.80, 0.95, CZ0, CZ0 + 0.10, 64)        # 下面の暗い板
    cone(bm, 0.95, 0.60, CZ0 - 0.25, CZ0, 48)         # 投射口（下へ細くなる）
    for i in range(4):                               # 天井への支柱
        a = i * TAU / 4 + TAU / 8
        box(bm, (0.12, 0.12, 0.6), (1.5 * math.cos(a), 1.5 * math.sin(a), CZ1 + 0.3), a)


def ring_light(bm):
    ring(bm, 1.60, 1.10, CZ0 - 0.012, CZ0, 64)       # 下面の光の輪（イベント時に明るくする：LabApp.RingLight）


def projector_lens(bm):
    cyl(bm, 0.55, CZ0 - 0.262, CZ0 - 0.25, 48)       # 投射口のレンズ


def ceiling_halo(bm):
    ring(bm, 3.90, 3.55, 4.74, 4.76, 96)             # 天井の大きな光の輪


made += [
    obj('CL_CeilingDrum', C, M['white'], ceiling_drum, bevel=0.008),
    obj('CL_CeilingUnder', C, M['dark'], ceiling_under, bevel=0.006),
    obj('CL_RingLight', C, M['blue'], ring_light),
    obj('CL_ProjectorLens', C, M['blue'], projector_lens),
    obj('CL_CeilingHalo', C, M['light'], ceiling_halo),
]

# ---- 床のリング（格子と光の線） ----


def floor_grate_base(bm):
    ring(bm, 2.50, 1.62, 0.0, 0.006, 96)


def floor_grate_bars(bm):
    n = 96
    for i in range(n):
        a = i * TAU / n
        r = (2.48 + 1.66) * 0.5
        box(bm, (2.48 - 1.66, 0.025, 0.008), (r * math.cos(a), r * math.sin(a), 0.010), a)
    ring(bm, 2.55, 2.48, 0.0, 0.016, 96)
    ring(bm, 1.66, 1.58, 0.0, 0.016, 96)


def floor_glow(bm):
    ring(bm, 2.62, 2.56, 0.0, 0.010, 96)             # 外周の橙の光の線


made += [
    obj('CL_FloorGrateBase', C, M['dark'], floor_grate_base),
    obj('CL_FloorGrateBars', C, M['metal'], floor_grate_bars),
    obj('CL_FloorGlow', C, M['orange'], floor_glow),
]

print('objects', len(made), 'tris', tri_count(made))
