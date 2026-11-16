# 研究室のモデルを Unity（Assets/_ImpossibleLab/Art/Models）へ FBX で書き出す。
# 設定：メートル・-Z 前・Y 上・変換をメッシュに焼き込む・モディファイア適用（Unity 側では (X,Y,Z) = (-bx, bz, -by)）。
# 実行：exec(open(r"D:/IMPOSSIBLE_LAB/ArtSource/blender/export_unity.py", encoding="utf-8").read())
import bpy

OUT = r"D:/IMPOSSIBLE_LAB/Assets/_ImpossibleLab/Art/Models/"
FILES = {
    'CentralDevice.fbx': lambda: [o.name for o in bpy.data.collections['CentralDevice'].objects],
    'RobotArm.fbx': lambda: ['ARM_Base', 'ARM_Upper', 'ARM_Fore'],
    'LabRoom.fbx': lambda: [o.name for o in bpy.data.collections['LabRoom'].objects],
    'Workbench.fbx': lambda: ['PROP_Workbench'],
    'GalleryCase.fbx': lambda: ['PROP_GalleryCase', 'PROP_GalleryGlass'],
    'Gate.fbx': lambda: ['GATE_Frame', 'GATE_DoorL', 'GATE_DoorR'],
    'Plant.fbx': lambda: ['PLANT_Leaves', 'PLANT_PotWhite', 'PLANT_PotDark'],
    'LabDetails.fbx': lambda: [o.name for o in bpy.data.collections['LabDetails'].objects],
}


def export(fn, names):
    bpy.ops.object.select_all(action='DESELECT')
    for n in names:
        o = bpy.data.objects[n]
        o.hide_set(False)
        o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=OUT + fn, use_selection=True, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True, use_mesh_modifiers=True,
                             mesh_smooth_type='OFF', object_types={'MESH'}, add_leaf_bones=False, bake_anim=False, path_mode='STRIP')


def export_all(only=None):
    for fn, names in FILES.items():
        if only is None or fn in only:
            export(fn, names())
    bpy.ops.wm.save_mainfile()
