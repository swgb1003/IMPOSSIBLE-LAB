using System.Collections.Generic;
using ImpossibleLab.Core;
using UnityEngine;

namespace ImpossibleLab
{
    /// <summary>
    /// 部品の形のメッシュ（FormMesher）。組み立てるときに作る DNA の形（ドーム・円すい・輪）と、
    /// 形の仕上げ（見た目のみ。DNA の形・実験の計算・当たり判定は変えない）。
    /// ・薄い円柱の覆い → 傘のドーム。部品の Transform の大きさ（展開の動きで半径が変わる）に合わせて伸び縮みするよう、部品の座標で作る。
    /// </summary>
    public static class ProductForm
    {
        /// <summary>切り替え（見比べ用）。</summary>
        public static bool Enabled = true;

        static readonly Dictionary<string, Mesh> _cache = new Dictionary<string, Mesh>();
        const int CacheLimit = 64;

        /// <summary>組み立てるときに作る形（ドーム・円すい・輪）のメッシュ。部品の Transform の大きさ（size）を掛けると実寸になる。</summary>
        public static Mesh MadeShapeMesh(string shape, Vector3 size)
        {
            string key = $"{shape}|{size.x:F4}|{size.y:F4}|{size.z:F4}";
            if (_cache.TryGetValue(key, out var hit) && hit != null) return hit;
            var m = FormMesher.MadeShape(shape, size);
            var mesh = new Mesh { name = "Shape_" + shape };
            mesh.SetVertices(m.Vertices);
            mesh.SetNormals(m.Normals);
            mesh.SetTriangles(m.Triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            if (_cache.Count >= CacheLimit) _cache.Clear();
            _cache[key] = mesh;
            return mesh;
        }

        /// <summary>形を差し替えた部品の ID（確かめ用）。</summary>
        public static List<string> Apply(InventionView view, InventionDna dna)
        {
            var ids = new List<string>();
            if (!Enabled || view == null || dna == null) return ids;
            foreach (var p in dna.Parts)
            {
                if (!FormMesher.IsDomeCanopy(p) || !view.Parts.TryGetValue(p.Id, out var t)) continue;
                var mf = t.GetComponent<MeshFilter>();
                if (mf == null) continue;
                // 部品の Transform は (直径, 厚さ/2, 直径)（円柱の基準は高さ 2。雨よけは機能の半径で直径を決め直す）。
                // 今の大きさの実寸で作って部品の座標に直す（あとで展開の動きで大きさが変わっても、同じ形のまま伸び縮みする）
                var s = t.localScale;
                if (Mathf.Abs(s.x) < 1e-5f || Mathf.Abs(s.y) < 1e-5f || Mathf.Abs(s.z) < 1e-5f) continue;
                float radius = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z)) * 0.5f, thick = Mathf.Abs(s.y) * 2f;
                float height = FormMesher.DomeHeight(radius);
                string key = $"dome|{s.x:F4}|{s.y:F4}|{s.z:F4}";
                if (!_cache.TryGetValue(key, out var mesh) || mesh == null)
                {
                    var d = FormMesher.Dome(radius, thick, height);
                    var verts = new Vector3[d.Vertices.Count];
                    var normals = new Vector3[d.Vertices.Count];
                    var inv = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);
                    for (int i = 0; i < verts.Length; i++)
                    {
                        verts[i] = Vector3.Scale(d.Vertices[i], inv);
                        normals[i] = Vector3.Scale(d.Normals[i], s).normalized;
                    }
                    mesh = new Mesh { name = "Dome_" + p.Id };
                    mesh.vertices = verts;
                    mesh.normals = normals;
                    mesh.SetTriangles(d.Triangles, 0);
                    mesh.RecalculateBounds();
                    mesh.RecalculateTangents();
                    if (_cache.Count >= CacheLimit) _cache.Clear();
                    _cache[key] = mesh;
                }
                mf.sharedMesh = mesh;
                // キットの飾り（縁など平らな円盤の形のもの）は、ドームの縁と合わないので隠す
                foreach (Transform child in t)
                    if (child.GetComponent<MeshRenderer>() is MeshRenderer r && child.name != "LensGlow") r.enabled = false;
                ids.Add(p.Id);
            }
            return ids;
        }
    }
}
