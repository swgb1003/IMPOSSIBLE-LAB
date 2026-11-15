# 観葉植物：葉は Poly Haven potted_plant_02（CC0、Rico Cilliers）の葉だけを使い（土 5.4 万ポリゴン・素焼きの鉢は使わない）、
# 鉢は研究室に合わせた白い円筒の鉢を作る。原点＝床の中心。葉のマテリアル MAT_PlantLeaves は Unity で切り抜き（両面）にする。
# 前提：import_asset で potted_plant_02（1k）を読み込み済み（葉のオブジェクト potted_plant_02_leaves）。
# 実行：exec(open(r"D:/IMPOSSIBLE_LAB/ArtSource/blender/plant.py", encoding="utf-8").read())
import math
exec(open(r"D:/IMPOSSIBLE_LAB/ArtSource/blender/lab_common.py", encoding="utf-8").read())

M = materials()
C = collection('Plant')
leaf_src = bpy.data.objects['potted_plant_02_leaves']
POT_H = 0.46

leaves = leaf_src.copy()
leaves.data = leaf_src.data.copy()
leaves.name = 'PLANT_Leaves'
C.objects.link(leaves)
leaves.location = (leaf_src.location.x, leaf_src.location.y, leaf_src.location.z + POT_H - 0.30)
leaves.scale = (1.15, 1.15, 1.15)
leaves.data.materials.clear()
leaves.data.materials.append(mat('MAT_PlantLeaves', (0.25, 0.45, 0.2), 0.0, 0.5))
for p in leaves.data.polygons:
    p.use_smooth = True


def pot_white(bm):
    cone(bm, 0.24, 0.29, 0.04, POT_H - 0.03, 40)   # 上面は土より下


def pot_dark(bm):
    ring(bm, 0.25, 0.0001, 0.0, 0.04, 40)          # 足元
    ring(bm, 0.295, 0.27, POT_H - 0.06, POT_H, 40)  # 縁
    cyl(bm, 0.27, POT_H - 0.05, POT_H - 0.03, 40)   # 土の面（暗い）


pot = [obj('PLANT_PotWhite', C, M['white'], pot_white, bevel=0.006), obj('PLANT_PotDark', C, M['dark'], pot_dark, bevel=0.004)]
leaf_src.hide_set(True)
print('tris', tri_count([leaves] + pot))
