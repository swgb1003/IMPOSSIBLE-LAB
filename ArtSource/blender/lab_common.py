# IMPOSSIBLE LAB — Blender 制作スクリプト共通部（Blender 5.x, MCP for Blender から exec で実行）
# 単位はメートル、Z 上。Unity へは FBX（-Z 前・Y 上）で書き出す。
# マテリアル名（MAT_*）は Unity 側で LabAssets のマテリアルへ置き換える（ProjectSetup の remap）。
import bpy, bmesh, math
from mathutils import Vector, Matrix

TAU = 2.0 * math.pi


def collection(name, clear=True):
    c = bpy.data.collections.get(name)
    if c is None:
        c = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(c)
    elif clear:
        for o in list(c.objects):
            bpy.data.objects.remove(o, do_unlink=True)
    return c


def mat(name, rgb, metal=0.0, rough=0.5, emit=None, strength=0.0, alpha=1.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    b.inputs['Base Color'].default_value = (*rgb, 1.0)
    b.inputs['Metallic'].default_value = metal
    b.inputs['Roughness'].default_value = rough
    if emit is not None:
        b.inputs['Emission Color'].default_value = (*emit, 1.0)
        b.inputs['Emission Strength'].default_value = strength
    b.inputs['Alpha'].default_value = alpha
    if alpha < 1.0:
        try:
            m.surface_render_method = 'BLENDED'
        except Exception:
            pass
    m.diffuse_color = (*rgb, alpha)
    return m


def materials():
    """参考画像のカラーマテリアル（コンクリート・金属・樹脂・ガラス・青・橙）に合わせる。"""
    return {
        'white': mat('MAT_White', (0.86, 0.88, 0.90), 0.0, 0.35),
        'dark': mat('MAT_Dark', (0.035, 0.045, 0.06), 0.6, 0.45),
        'metal': mat('MAT_Metal', (0.55, 0.58, 0.62), 0.85, 0.35),
        'blue': mat('MAT_Blue', (0.05, 0.55, 0.9), 0.0, 0.3, emit=(0.05, 0.6, 1.0), strength=4.0),
        'orange': mat('MAT_Orange', (0.95, 0.5, 0.1), 0.0, 0.4, emit=(1.0, 0.45, 0.08), strength=4.0),
        'glass': mat('MAT_Glass', (0.6, 0.8, 0.95), 0.0, 0.05, alpha=0.15),
        'light': mat('MAT_LightWhite', (1.0, 1.0, 1.0), 0.0, 0.5, emit=(1.0, 0.98, 0.95), strength=6.0),
    }


# ---------------- メッシュ生成（bmesh） ----------------
def ring(bm, r_out, r_in, z0, z1, seg, a0=0.0, a1=TAU):
    """円環（r_in>0）・円弧の板。a0〜a1 が一周でなければ端面を閉じる。"""
    full = abs((a1 - a0) - TAU) < 1e-6
    n = seg if full else seg + 1
    rows = []
    for i in range(n):
        a = a0 + (a1 - a0) * i / seg
        c, s = math.cos(a), math.sin(a)
        rows.append([bm.verts.new((r_out * c, r_out * s, z0)), bm.verts.new((r_out * c, r_out * s, z1)),
                     bm.verts.new((r_in * c, r_in * s, z1)), bm.verts.new((r_in * c, r_in * s, z0))])
    for i in range(seg):
        A, B = rows[i], rows[(i + 1) % n]
        for k in range(4):
            bm.faces.new((A[k], B[k], B[(k + 1) % 4], A[(k + 1) % 4]))
    if not full:
        bm.faces.new(list(reversed(rows[0])))
        bm.faces.new(rows[-1])


def cyl(bm, r, z0, z1, seg, caps=True):
    m = Matrix.Translation((0, 0, (z0 + z1) * 0.5))
    bmesh.ops.create_cone(bm, cap_ends=caps, cap_tris=False, segments=seg, radius1=r, radius2=r, depth=z1 - z0, matrix=m)


def cone(bm, r0, r1, z0, z1, seg, caps=True):
    m = Matrix.Translation((0, 0, (z0 + z1) * 0.5))
    bmesh.ops.create_cone(bm, cap_ends=caps, cap_tris=False, segments=seg, radius1=r0, radius2=r1, depth=z1 - z0, matrix=m)


def box(bm, size, loc, rot_z=0.0):
    m = Matrix.Translation(loc) @ Matrix.Rotation(rot_z, 4, 'Z') @ Matrix.Diagonal((*size, 1.0))
    bmesh.ops.create_cube(bm, size=1.0, matrix=m)


def tube_open(bm, r, z0, z1, seg):
    """側面だけの円筒（ガラス用・片面）。"""
    cyl(bm, r, z0, z1, seg, caps=False)


def obj(name, coll, material, build, bevel=0.0, seg=2, angle=30.0, smooth=True, fix=None):
    """1 オブジェクト＝1 マテリアル。面は平滑、angle より鋭い辺は硬い辺にする。bevel>0 なら角を面取り（法線を硬く）。"""
    bm = bmesh.new()
    build(bm)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if fix is not None:
        fix(bm)          # 閉じていない形（天井板など）の面の向きを直す
    lim = math.radians(angle)
    for f in bm.faces:
        f.smooth = smooth
    for e in bm.edges:
        if len(e.link_faces) == 2 and e.calc_face_angle(0.0) > lim:
            e.smooth = False
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    coll.objects.link(o)
    me.materials.append(material)
    if bevel > 0.0:
        b = o.modifiers.new('Bevel', 'BEVEL')
        b.width = bevel
        b.segments = seg
        b.limit_method = 'ANGLE'
        b.angle_limit = lim
        b.harden_normals = True
        b.miter_outer = 'MITER_ARC'
    return o


def tri_count(objs):
    dg = bpy.context.evaluated_depsgraph_get()
    total = 0
    for o in objs:
        e = o.evaluated_get(dg)
        me = e.to_mesh()
        total += sum(len(p.vertices) - 2 for p in me.polygons)
        e.to_mesh_clear()
    return total
