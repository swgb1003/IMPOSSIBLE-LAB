using UnityEngine;

namespace ImpossibleLab
{
    /// <summary>S03 で選んでいる部品を囲む枠（細い棒 12 本）。見た目だけで、当たり判定は持たない。</summary>
    public static class PartHighlight
    {
        const float Margin = 0.015f;   // 部品から枠までのすき間（m）
        const float Thickness = 0.006f; // 枠の棒の太さ（m）

        /// <summary>root の子として、target の見た目の範囲（root の座標系）を囲む枠を作る。</summary>
        public static GameObject Create(Transform root, Transform target, Material mat)
        {
            var b = LocalBounds(root, target);
            var go = new GameObject("PartHighlight");
            go.transform.SetParent(root, false);
            go.transform.localPosition = b.center;
            Vector3 e = b.extents + Vector3.one * Margin;
            for (int axis = 0; axis < 3; axis++)
                for (int i = 0; i < 4; i++)
                {
                    float s1 = (i & 1) == 0 ? -1f : 1f, s2 = (i & 2) == 0 ? -1f : 1f;
                    Vector3 pos, size;
                    switch (axis)
                    {
                        case 0: pos = new Vector3(0, s1 * e.y, s2 * e.z); size = new Vector3(e.x * 2f + Thickness, Thickness, Thickness); break;
                        case 1: pos = new Vector3(s1 * e.x, 0, s2 * e.z); size = new Vector3(Thickness, e.y * 2f + Thickness, Thickness); break;
                        default: pos = new Vector3(s1 * e.x, s2 * e.y, 0); size = new Vector3(Thickness, Thickness, e.z * 2f + Thickness); break;
                    }
                    var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    bar.name = "Edge";
                    Object.Destroy(bar.GetComponent<Collider>());
                    bar.transform.SetParent(go.transform, false);
                    bar.transform.localPosition = pos;
                    bar.transform.localScale = size;
                    var r = bar.GetComponent<Renderer>();
                    if (mat != null) r.sharedMaterial = mat;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            return go;
        }

        /// <summary>target のメッシュの範囲を root の座標系で求める（root の拡大縮小は 1 の前提）。</summary>
        static Bounds LocalBounds(Transform root, Transform target)
        {
            var mf = target.GetComponent<MeshFilter>();
            var mb = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
            var m = root.worldToLocalMatrix * target.localToWorldMatrix;
            var b = new Bounds(m.MultiplyPoint3x4(mb.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                b.Encapsulate(m.MultiplyPoint3x4(c));
            }
            return b;
        }
    }
}
