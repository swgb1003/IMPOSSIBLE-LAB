using System.Collections.Generic;
using System.Linq;
using ImpossibleLab.Core;
using UnityEngine;

namespace ImpossibleLab
{
    /// <summary>
    /// 組み立てた試作品（角の丸み・塗り分け・細部を当てたもの）を GLB に書き出す（docs/PUBLISH_LIBRARY.md 5章）。
    /// 部品ごとに1つのノード（名前＝部品の ID）にまとめ、その子に形を置く。同じ形（4つの車輪など）は1回だけ書いて使い回す。
    /// 読めないメッシュ・光のにじみの板は入れない。
    /// </summary>
    public static class ProductExport
    {
        /// <summary>1件の大きさの目安（Quest から USB 経由で送る）。</summary>
        public const int TargetBytes = 600 * 1024;
        /// <summary>部品に属さない見た目（あれば）をまとめるノードの名前。</summary>
        public const string ExtraNode = "_extra";

        public sealed class Result
        {
            public byte[] Glb;
            /// <summary>Vertices・Triangles は書いた数（使い回した形は1回分）。Instances は形を置いた数。</summary>
            public int Parts, Primitives, Vertices, Triangles, Materials, Instances;
            public readonly List<string> Warnings = new List<string>();
        }

        static readonly Vector3 StagePos = new Vector3(0f, -80f, 0f);

        /// <summary>DNA から見えない場所に写しを組み立てて書き出し、写しは消す。</summary>
        public static Result Export(InventionDna dna, LabAssets assets)
        {
            var stage = new GameObject("GlbExportStage").transform;
            stage.position = StagePos;
            try
            {
                var built = new InventionBuilder(assets).Build(dna, stage);
                if (built.View == null)
                {
                    var r = new Result();
                    r.Warnings.Add("組み立てられません：" + string.Join(" / ", built.Warnings.Take(2)));
                    return r;
                }
                return FromView(built.View);
            }
            finally { Object.Destroy(stage.gameObject); }
        }

        /// <summary>組み立て済みの試作品から書き出す（試用中の動き・関節の今の角度もそのまま入る）。</summary>
        public static Result FromView(InventionView view)
        {
            var result = new Result();
            var dna = view.Dna;
            var root = view.transform;
            var toRoot = root.worldToLocalMatrix;

            var partOf = new Dictionary<Transform, string>();
            foreach (var kv in view.Parts) partOf[kv.Value] = kv.Key;
            // 部品は DNA の順に並べる（サイトの部品の一覧と同じ順）
            var nodes = new List<GlbNode>();
            var nodeOf = new Dictionary<string, GlbNode>();
            foreach (var p in dna.Parts)
            {
                var n = new GlbNode { Name = p.Id };
                n.Extras["partId"] = p.Id;
                n.Extras["type"] = p.Type;
                nodes.Add(n);
                nodeOf[p.Id] = n;
            }
            var materials = new List<GlbMaterial>();
            var materialIndex = new Dictionary<string, int>();
            var meshes = new List<GlbMesh>();
            var shared = new Dictionary<string, int>();
            var mpb = new MaterialPropertyBlock();
            string shieldPart = dna.FindBehavior("rain_shield")?.TargetPart;

            foreach (var rend in root.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!rend.enabled || rend.gameObject.name == "LensGlow") continue;
                var mesh = rend.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                if (!mesh.isReadable) { result.Warnings.Add($"読めないメッシュを飛ばしました：{mesh.name}"); continue; }

                string owner = null;
                for (var t = rend.transform; t != null && t != root; t = t.parent)
                    if (partOf.TryGetValue(t, out owner)) break;
                // 雨よけの側面の覆いは、雨よけの部品の一部として扱う（機能モジュールが根元の下に作る）
                if (owner == null && rend.gameObject.name == "Skirt" && shieldPart != null && nodeOf.ContainsKey(shieldPart)) owner = shieldPart;
                if (owner == null || !nodeOf.TryGetValue(owner, out var node))
                {
                    if (!nodeOf.TryGetValue(ExtraNode, out node)) { node = new GlbNode { Name = ExtraNode }; nodes.Add(node); nodeOf[ExtraNode] = node; }
                }

                // 材質（部品ごとに色を上書きしていれば、その色）を区画ごとに決める
                var mats = rend.sharedMaterials;
                rend.GetPropertyBlock(mpb);
                var matOfSub = new int[mesh.subMeshCount];
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var mat = MaterialOf(mats.Length > 0 ? mats[Mathf.Min(sub, mats.Length - 1)] : null, mpb);
                    if (!materialIndex.TryGetValue(mat.Key, out int mi)) { mi = materials.Count; materials.Add(mat); materialIndex[mat.Key] = mi; }
                    matOfSub[sub] = mi;
                }

                var m = toRoot * rend.transform.localToWorldMatrix;
                result.Instances++;
                if (GlbWriter.IsTrs(m))
                {
                    // 同じメッシュ・同じ材質の形は1回だけ書き、置く場所（変換）だけを並べる（4つの車輪など）
                    string key = mesh.GetInstanceID() + ":" + string.Join(",", matOfSub);
                    if (!shared.TryGetValue(key, out int index))
                    {
                        index = meshes.Count;
                        meshes.Add(ToGlbMesh(mesh, Matrix4x4.identity, matOfSub, result));
                        shared[key] = index;
                    }
                    node.Instances.Add(new GlbInstance { Mesh = index, Matrix = m });
                }
                else
                {
                    // ゆがみのある変換（大きさを変えた親の下で回した子）は glTF の matrix にできないので、頂点に焼き込む
                    meshes.Add(ToGlbMesh(mesh, m, matOfSub, result));
                    node.Instances.Add(new GlbInstance { Mesh = meshes.Count - 1 });
                }
            }

            var extras = new Dictionary<string, string>
            {
                ["inventionId"] = dna.Id,
                ["revision"] = dna.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            result.Glb = GlbWriter.Write(nodes, meshes, materials, "invention", extras);
            result.Parts = dna.Parts.Count;
            result.Materials = materials.Count;
            return result;
        }

        /// <summary>Unity のメッシュを区画（材質）ごとの三角形の集まりにする。bake が単位行列でなければ頂点に焼き込む。</summary>
        static GlbMesh ToGlbMesh(Mesh mesh, Matrix4x4 bake, int[] matOfSub, Result stats)
        {
            var g = new GlbMesh();
            bool baked = !bake.isIdentity;
            var nm = bake.inverse.transpose;
            bool mirrored = bake.determinant < 0f;
            var verts = mesh.vertices;
            var normals = mesh.normals;
            bool hasNormals = normals != null && normals.Length == verts.Length;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                var tris = mesh.GetTriangles(sub);
                if (tris.Length < 3) continue;
                // この区画で使う頂点だけを詰めて持つ
                var remap = new Dictionary<int, int>();
                var pos = new List<Vector3>();
                var nor = new List<Vector3>();
                var idx = new int[tris.Length];
                for (int i = 0; i < tris.Length; i++)
                {
                    int v = tris[i];
                    if (!remap.TryGetValue(v, out int k))
                    {
                        k = pos.Count;
                        remap[v] = k;
                        pos.Add(baked ? bake.MultiplyPoint3x4(verts[v]) : verts[v]);
                        if (hasNormals) nor.Add(baked ? nm.MultiplyVector(normals[v]).normalized : normals[v]);
                    }
                    idx[i] = k;
                }
                // 鏡に映した変換を焼き込んだときは表と裏が入れ替わるので、Unity の並びに戻しておく
                if (mirrored) for (int i = 0; i + 2 < idx.Length; i += 3) { int x = idx[i + 1]; idx[i + 1] = idx[i + 2]; idx[i + 2] = x; }
                g.Primitives.Add(new GlbPrimitive { Positions = pos.ToArray(), Normals = hasNormals ? nor.ToArray() : null, Indices = idx, Material = matOfSub[sub] });
                stats.Primitives++;
                stats.Vertices += pos.Count;
                stats.Triangles += idx.Length / 3;
            }
            return g;
        }

        /// <summary>URP のマテリアルと、部品ごとに上書きした色（MaterialPropertyBlock）から材質を作る。</summary>
        static GlbMaterial MaterialOf(Material mat, MaterialPropertyBlock mpb)
        {
            var g = new GlbMaterial();
            if (mat != null)
            {
                if (mat.HasProperty("_BaseColor")) g.BaseColor = mat.GetColor("_BaseColor");
                else if (mat.HasProperty("_Color")) g.BaseColor = mat.GetColor("_Color");
                if (mat.HasProperty("_Metallic")) g.Metallic = mat.GetFloat("_Metallic");
                if (mat.HasProperty("_Smoothness")) g.Roughness = 1f - mat.GetFloat("_Smoothness");
                if (mat.IsKeywordEnabled("_EMISSION") && mat.HasProperty("_EmissionColor")) g.Emissive = mat.GetColor("_EmissionColor");
                g.Blend = mat.HasProperty("_Surface") && mat.GetFloat("_Surface") > 0.5f;
            }
            if (!mpb.isEmpty)
            {
                // 上書きは色だけ（透明度はマテリアルのまま。外観の色は不透明で入っている）
                if (mpb.HasColor("_BaseColor")) { var c = mpb.GetColor("_BaseColor"); g.BaseColor = new Color(c.r, c.g, c.b, g.BaseColor.a); }
                if (mpb.HasColor("_EmissionColor")) g.Emissive = mpb.GetColor("_EmissionColor");
            }
            g.Emissive = new Color(Mathf.Clamp01(g.Emissive.r), Mathf.Clamp01(g.Emissive.g), Mathf.Clamp01(g.Emissive.b), 1f);
            return g;
        }
    }
}
