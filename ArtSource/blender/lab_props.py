# 研究室の小物：作業台（DESIGN STUDIO）・展示ケース（INVENTION GALLERY）・実験ゲート（EXPERIMENT GATE）。
# それぞれ原点＝床の中心。座標は Unity のローカル値で書き U() で Blender 座標へ直す（lab_room.py と同じ）。
#   作業台：奥行き X（+X が部屋の中央側＝人が座る側）・幅 Z 2.2 m。左の壁際に 3 台並べる
#   展示ケース：台座＋ガラスの円筒（既存の配置：奥の窓の前に 3 台）
#   実験ゲート：開口は X −1.5〜+1.5・高さ 3.4 m、部屋側は −Z。扉 GATE_DoorL / GATE_DoorR は原点＝扉の中心（Unity が横に動かす）
# 実行：exec(open(r"D:/IMPOSSIBLE_LAB/ArtSource/blender/lab_props.py", encoding="utf-8").read())
import math
from mathutils import Vector, Matrix
exec(open(r"D:/IMPOSSIBLE_LAB/ArtSource/blender/lab_common.py", encoding="utf-8").read())

M = materials()
M['screen'] = mat('MAT_Screen', (0.02, 0.08, 0.16), 0.0, 0.2, emit=(0.1, 0.45, 0.85), strength=1.5)  # Unity では Screen マテリアル


def U(x, y, z):
    return Vector((-x, -z, y))


def ubox(bm, cx, cy, cz, sx, sy, sz, rot_y=0.0):
    box(bm, (sx, sz, sy), U(cx, cy, cz), rot_z=-rot_y)


def ucyl(bm, cx, cz, r, y0, y1, seg=32):
    m = Matrix.Translation(U(cx, (y0 + y1) * 0.5, cz))
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r, radius2=r, depth=y1 - y0, matrix=m)


def at(o, origin):
    o.data.transform(Matrix.Translation(-origin))
    o.location = origin


def join(name, parts, origin=Vector((0, 0, 0))):
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


# ================= 作業台 =================
C = collection('Workbench')


def wb_white(bm):
    ubox(bm, 0.05, 0.76, 0, 1.0, 0.04, 2.2)                  # 天板
    ubox(bm, -0.33, 0.40, 0.62, 0.42, 0.66, 0.7)             # 袖の引き出し
    ucyl(bm, 0.75, 0.3, 0.2, 0.42, 0.47, 24)                 # 丸椅子の座面


def wb_dark(bm):
    for z in (-1.02, 1.02):                                  # 脚（板）
        ubox(bm, 0.05, 0.37, z, 0.9, 0.74, 0.05)
    ubox(bm, -0.42, 0.45, 0, 0.03, 0.5, 2.0)                 # 幕板
    for z in (-0.45, 0.45):                                  # モニター（枠・脚）
        ubox(bm, -0.3, 1.13, z, 0.04, 0.42, 0.74, rot_y=math.radians(8 if z < 0 else -8))
        ubox(bm, -0.33, 0.86, z, 0.04, 0.14, 0.05)
        ubox(bm, -0.33, 0.785, z, 0.18, 0.012, 0.22)
    ubox(bm, 0.12, 0.79, 0, 0.16, 0.015, 0.46)               # キーボード
    ucyl(bm, 0.75, 0.3, 0.03, 0.0, 0.42, 12)                 # 丸椅子の柱
    ucyl(bm, 0.75, 0.3, 0.2, 0.0, 0.025, 24)                 # 丸椅子の脚


def wb_screen(bm):
    for z in (-0.45, 0.45):
        ubox(bm, -0.268, 1.13, z, 0.006, 0.38, 0.70, rot_y=math.radians(8 if z < 0 else -8))


def wb_blue(bm):
    ubox(bm, 0.555, 0.74, 0, 0.01, 0.012, 2.2)               # 天板の縁の青い線


bench = join('PROP_Workbench', [
    obj('w1', C, M['white'], wb_white, bevel=0.006),
    obj('w2', C, M['dark'], wb_dark, bevel=0.003),
    obj('w3', C, M['screen'], wb_screen, smooth=False),
    obj('w4', C, M['blue'], wb_blue, smooth=False),
])

# ================= 展示ケース =================
C2 = collection('GalleryCase')


def gc_white(bm):
    ring(bm, 0.52, 0.0001, 0.08, 0.66, 40)                   # 台座
    ring(bm, 0.50, 0.0001, 1.92, 2.0, 40)                    # 上の蓋


def gc_dark(bm):
    ring(bm, 0.55, 0.0001, 0.0, 0.08, 40)                    # 足元
    ring(bm, 0.53, 0.30, 0.30, 0.34, 40)                     # 台座の帯
    ring(bm, 0.48, 0.0001, 0.66, 0.70, 40)                   # 展示面


def gc_light(bm):
    ring(bm, 0.535, 0.47, 0.655, 0.665, 40)                  # 台座の上縁の光
    ring(bm, 0.44, 0.38, 1.912, 1.92, 40)                    # 蓋の下の光


def gc_glass(bm):
    tube_open(bm, 0.46, 0.70, 1.92, 40)


case = join('PROP_GalleryCase', [
    obj('g1', C2, M['white'], gc_white, bevel=0.006),
    obj('g2', C2, M['dark'], gc_dark, bevel=0.004),
    obj('g3', C2, M['blue'], gc_light, smooth=False),
])
case_glass = obj('PROP_GalleryGlass', C2, M['glass'], gc_glass)

# ================= 実験ゲート =================
C3 = collection('Gate')


def gate_frame_white(bm):
    for x in (-1.85, 1.85):                                  # 左右の柱
        ubox(bm, x, 1.85, 0, 0.5, 3.7, 0.8)
    ubox(bm, 0, 3.85, 0, 4.2, 0.5, 0.8)                      # 上の梁
    for x in (-1, 1):                                        # 上の角の斜めの板
        ubox(bm, x * 1.45, 3.45, -0.05, 0.6, 0.12, 0.7, rot_y=0.0)


def gate_frame_dark(bm):
    ubox(bm, 0, 1.75, 0.28, 3.2, 3.5, 0.06)                  # 扉の奥の暗い枠
    for x in (-1.62, 1.62):                                  # 開口の内側の縁
        ubox(bm, x, 1.75, -0.1, 0.06, 3.5, 0.6)
    ubox(bm, 0, 3.52, -0.1, 3.3, 0.06, 0.6)
    ubox(bm, 0, 0.02, -0.25, 3.6, 0.04, 1.2)                 # 敷居
    ubox(bm, 0, 4.25, -0.42, 2.6, 0.32, 0.04)                # サインの背板（Unity の文字は z −0.3 → 手前に出す）


def gate_lights(bm):
    for x in (-1.585, 1.585):                                # 開口の縁の青い線
        ubox(bm, x, 1.75, -0.41, 0.012, 3.3, 0.012)
    ubox(bm, 0, 3.485, -0.41, 3.15, 0.012, 0.012)
    for x in (-1.85, 1.85):                                  # 柱の縦の光
        ubox(bm, x, 1.5, -0.405, 0.06, 2.4, 0.01)


def gate_orange(bm):
    ubox(bm, 0, 0.045, -0.85, 3.2, 0.006, 0.05)              # 床の停止線


made_gate = [
    obj('GATE_FrameWhite', C3, M['white'], gate_frame_white, bevel=0.01),
    obj('GATE_FrameDark', C3, M['dark'], gate_frame_dark, bevel=0.004),
    obj('GATE_Lights', C3, M['blue'], gate_lights, smooth=False),
    obj('GATE_FloorLine', C3, M['orange'], gate_orange, smooth=False),
]
frame = join('GATE_Frame', made_gate)


def door(side):
    x = side * 0.6

    def dark(bm):
        ubox(bm, x, 1.7, 0, 1.18, 3.38, 0.12)

    def white(bm):
        ubox(bm, x + side * 0.1, 1.7, -0.065, 0.5, 3.1, 0.02)  # 中央寄りの白い帯

    def blue(bm):
        ubox(bm, side * 0.03, 1.7, -0.07, 0.012, 3.0, 0.01)    # 合わせ目の青い線
    o = join('GATE_Door' + ('L' if side < 0 else 'R'), [
        obj('d1', C3, M['dark'], dark, bevel=0.006),
        obj('d2', C3, M['white'], white, bevel=0.004),
        obj('d3', C3, M['blue'], blue, smooth=False),
    ], U(x, 1.7, 0))
    return o


doorL, doorR = door(-1), door(1)

print('tris bench', tri_count([bench]), 'case', tri_count([case, case_glass]), 'gate', tri_count([frame, doorL, doorR]))
