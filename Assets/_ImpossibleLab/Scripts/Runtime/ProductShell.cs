using System.Collections.Generic;
using ImpossibleLab.Core;
using UnityEngine;

namespace ImpossibleLab
{
    /// <summary>
    /// 外装を一体の殻にする（見た目のみ。DNA・実験の計算・当たり判定・部品の選び方は変えない）。
    /// 合わせる部品（ShellPlan）の形の表示を止め、代わりに、つなぎ目に丸みを付けた1つの殻（ShellMesher）を本体の部品の下に置く。
    /// 殻の色と素材は本体のもの（本体が下の外装として濃く塗られていれば、ほかの合わせる部品の色）。飾り（キットの飾り・ネジ・通気口）はそのまま残す。
    /// 同じ形の殻は作り直さない（発明台・画面の写し・書き出しで3回組み立てるため）。
    /// </summary>
    public static class ProductShell
    {
        /// <summary>切り替え（見比べ用）。</summary>
        public static bool Enabled = true;
        public const string ShellName = "Shell";

        static readonly Dictionary<string, Mesh> _cache = new Dictionary<string, Mesh>();
        const int CacheLimit = 32;

        /// <summary>殻にした部品の ID（確かめ用）。殻を作らなければ空。</summary>
        public static List<string> Apply(InventionView view, InventionDna dna, string lowerShell)
        {
            var ids = new List<string>();
            if (!Enabled || view == null || dna == null) return ids;
            var parts = ShellPlan.Pick(dna);
            if (parts.Count < 2) return ids;
            var body = parts[0];
            if (!view.Parts.TryGetValue(body.Id, out var bodyT)) return ids;

            // 色と素材を借りる部品の見た目（下の外装として濃く塗った部品は避ける）
            MeshRenderer source = null;
            foreach (var p in parts)
            {
                if (p.Id == lowerShell) continue;
                if (view.Parts.TryGetValue(p.Id, out var t) && t.GetComponent<MeshRenderer>() is MeshRenderer r) { source = r; break; }
            }
            if (source == null) source = bodyT.GetComponent<MeshRenderer>();
            if (source == null) return ids;

            // 殻のメッシュ（発明の座標で作り、本体の部品の座標に直す）
            string key = ShellPlan.Key(parts);
            if (!_cache.TryGetValue(key, out var mesh) || mesh == null)
            {
                var solids = new List<ShellMesher.Solid>();
                foreach (var p in parts) solids.Add(ShellPlan.SolidOf(p));
                var built = ShellMesher.Build(solids, ShellPlan.Blend(parts));
                if (built.Vertices.Count == 0) return ids;
                // 本体の部品の Transform は位置・大きさ（回転なし）。殻の頂点をその座標に直す
                Vector3 pos = body.Position, scale = bodyT.localScale;
                var inv = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
                var rot = bodyT.localRotation;
                var invRot = Quaternion.Inverse(rot);
                var verts = new Vector3[built.Vertices.Count];
                var normals = new Vector3[built.Vertices.Count];
                for (int i = 0; i < verts.Length; i++)
                {
                    verts[i] = Vector3.Scale(invRot * (built.Vertices[i] - pos), inv);
                    // 法線は拡大の逆の転置で写す（親の拡大を掛けたときに元の向きに戻る）
                    normals[i] = Vector3.Scale(invRot * built.Normals[i], scale).normalized;
                }
                mesh = new Mesh { name = "Shell_" + body.Id };
                if (verts.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.vertices = verts;
                mesh.normals = normals;
                mesh.SetTriangles(built.Triangles, 0);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                if (_cache.Count >= CacheLimit) _cache.Clear();
                _cache[key] = mesh;
            }

            // 合わせる部品の形の表示を止める（飾りの子・Transform・選び方はそのまま）
            foreach (var p in parts)
                if (view.Parts.TryGetValue(p.Id, out var t) && t.GetComponent<MeshRenderer>() is MeshRenderer r) { r.enabled = false; ids.Add(p.Id); }

            var go = new GameObject(ShellName);
            go.transform.SetParent(bodyT, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var shell = go.AddComponent<MeshRenderer>();
            shell.sharedMaterial = source.sharedMaterial;
            shell.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            var mpb = new MaterialPropertyBlock();
            source.GetPropertyBlock(mpb);
            if (!mpb.isEmpty) shell.SetPropertyBlock(mpb);
            return ids;
        }
    }
}
