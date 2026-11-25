using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ImpossibleLab.Core
{
    /// <summary>GLB の材質。色は Unity のマテリアルに書いた値（sRGB）で受け取り、書き出すときに glTF の線形の値へ直す。</summary>
    public sealed class GlbMaterial
    {
        public Color BaseColor = Color.white;
        public float Metallic, Roughness = 0.6f;
        public Color Emissive = Color.black;
        /// <summary>半透明（キャノピーなど）。</summary>
        public bool Blend;

        public string Key => $"{Hex(BaseColor)}/{Metallic:0.00}/{Roughness:0.00}/{Hex(Emissive)}/{Blend}";
        static string Hex(Color c) => $"{Mathf.RoundToInt(c.r * 255):X2}{Mathf.RoundToInt(c.g * 255):X2}{Mathf.RoundToInt(c.b * 255):X2}{Mathf.RoundToInt(c.a * 255):X2}";
    }

    /// <summary>三角形の集まり1つ（材質は1つ）。座標はメッシュ自身の Unity の座標（左手系・Y 上・+Z 前・m）。</summary>
    public sealed class GlbPrimitive
    {
        public Vector3[] Positions;
        public Vector3[] Normals;
        /// <summary>Unity の三角形の並び（表から見て時計回り）。</summary>
        public int[] Indices;
        public int Material;
    }

    /// <summary>形1つ。同じ形の部品（4つの車輪など）は1つの形を使い回し、ファイルを小さくする。</summary>
    public sealed class GlbMesh
    {
        public readonly List<GlbPrimitive> Primitives = new List<GlbPrimitive>();
    }

    /// <summary>形を置く1か所。Matrix は発明の根元から見た Unity の変換（回転・大きさ・位置に分けられるもの。IsTrs）。</summary>
    public sealed class GlbInstance
    {
        public int Mesh;
        public Matrix4x4 Matrix = Matrix4x4.identity;
    }

    /// <summary>部品1つ。サイトで部品を選んで光らせるため、名前は部品の ID にする。形は子のノードに置く。</summary>
    public sealed class GlbNode
    {
        public string Name = "";
        public readonly List<GlbInstance> Instances = new List<GlbInstance>();
        public readonly Dictionary<string, string> Extras = new Dictionary<string, string>();
    }

    /// <summary>書き出したファイルの中身の概要（確認用）。</summary>
    public sealed class GlbSummary
    {
        public JObject Json;
        public int BinLength, Nodes, Meshes, Primitives, Vertices, Triangles, Materials;
        /// <summary>形を置いたノードの数（同じ形を使い回した分も数える）。</summary>
        public int Instances;
        public List<string> NodeNames = new List<string>();
    }

    /// <summary>
    /// glTF 2.0 の GLB（JSON と頂点データを1ファイルにしたもの）を書く。外部のパッケージは使わない（docs/PUBLISH_LIBRARY.md 5章）。
    /// glTF は右手系なので、X を反転し、三角形の頂点の順を逆にする（Y 上・+Z 前・m はそのまま）。
    /// </summary>
    public static class GlbWriter
    {
        const uint Magic = 0x46546C67;      // "glTF"
        const uint ChunkJson = 0x4E4F534A;  // "JSON"
        const uint ChunkBin = 0x004E4942;   // "BIN\0"
        const int Float = 5126, UShort = 5123, UInt = 5125;
        const int ArrayBuffer = 34962, ElementArrayBuffer = 34963;

        /// <summary>
        /// 部品の並び（nodes）を1つの根のノードの子にして書く。部品のノードの子に、形（meshList）を置くノードを作る。
        /// rootExtras は根のノードの extras（発明の ID・版など）。三角形の無い部品も、ノードだけ残す（サイトの部品の一覧と数をそろえる）。
        /// </summary>
        public static byte[] Write(IList<GlbNode> nodes, IList<GlbMesh> meshList, IList<GlbMaterial> materials, string rootName, IDictionary<string, string> rootExtras = null)
        {
            var bin = new MemoryStream();
            var bufferViews = new JArray();
            var accessors = new JArray();
            var meshes = new JArray();
            var jsonNodes = new JArray();
            var children = new JArray();

            int View(byte[] data, int target)
            {
                Pad(bin, 4, 0);
                bufferViews.Add(new JObject { ["buffer"] = 0, ["byteOffset"] = (int)bin.Length, ["byteLength"] = data.Length, ["target"] = target });
                bin.Write(data, 0, data.Length);
                return bufferViews.Count - 1;
            }
            int Accessor(int view, int componentType, int count, string type, JArray min = null, JArray max = null)
            {
                var a = new JObject { ["bufferView"] = view, ["componentType"] = componentType, ["count"] = count, ["type"] = type };
                if (min != null) { a["min"] = min; a["max"] = max; }
                accessors.Add(a);
                return accessors.Count - 1;
            }

            // 根のノード（0 番）。子は部品のノード
            var root = new JObject { ["name"] = rootName ?? "invention", ["children"] = children };
            if (rootExtras != null && rootExtras.Count > 0) root["extras"] = JObject.FromObject(rootExtras);
            jsonNodes.Add(root);

            // 形を1回ずつ書く（三角形の無い形は書かず、-1 にする）
            var meshIndex = new int[meshList.Count];
            for (int mi = 0; mi < meshList.Count; mi++)
            {
                var prims = new JArray();
                foreach (var p in meshList[mi].Primitives)
                {
                    if (p.Positions == null || p.Positions.Length == 0 || p.Indices == null || p.Indices.Length < 3) continue;
                    int n = p.Positions.Length;
                    var pos = new byte[n * 12];
                    var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                    var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                    for (int i = 0; i < n; i++)
                    {
                        var v = p.Positions[i]; v.x = -v.x;
                        min = Vector3.Min(min, v); max = Vector3.Max(max, v);
                        PutFloat(pos, i * 12, v.x); PutFloat(pos, i * 12 + 4, v.y); PutFloat(pos, i * 12 + 8, v.z);
                    }
                    var attrs = new JObject
                    {
                        ["POSITION"] = Accessor(View(pos, ArrayBuffer), Float, n, "VEC3", new JArray(min.x, min.y, min.z), new JArray(max.x, max.y, max.z)),
                    };
                    if (p.Normals != null && p.Normals.Length == n)
                    {
                        var nor = new byte[n * 12];
                        for (int i = 0; i < n; i++)
                        {
                            var v = p.Normals[i];
                            v = v.sqrMagnitude > 1e-12f ? v.normalized : Vector3.up;
                            PutFloat(nor, i * 12, -v.x); PutFloat(nor, i * 12 + 4, v.y); PutFloat(nor, i * 12 + 8, v.z);
                        }
                        attrs["NORMAL"] = Accessor(View(nor, ArrayBuffer), Float, n, "VEC3");
                    }
                    int tris = p.Indices.Length / 3;
                    bool small = n <= ushort.MaxValue;
                    var idx = new byte[tris * 3 * (small ? 2 : 4)];
                    for (int t = 0; t < tris; t++)
                    {
                        // X を反転すると表と裏が入れ替わるので、2 番目と 3 番目を入れ替える
                        int a = p.Indices[t * 3], b = p.Indices[t * 3 + 2], c = p.Indices[t * 3 + 1];
                        if (small) { PutUShort(idx, t * 6, a); PutUShort(idx, t * 6 + 2, b); PutUShort(idx, t * 6 + 4, c); }
                        else { PutUInt(idx, t * 12, a); PutUInt(idx, t * 12 + 4, b); PutUInt(idx, t * 12 + 8, c); }
                    }
                    var prim = new JObject
                    {
                        ["attributes"] = attrs,
                        ["indices"] = Accessor(View(idx, ElementArrayBuffer), small ? UShort : UInt, tris * 3, "SCALAR"),
                        ["mode"] = 4,
                    };
                    if (p.Material >= 0 && p.Material < materials.Count) prim["material"] = p.Material;
                    prims.Add(prim);
                }
                if (prims.Count == 0) { meshIndex[mi] = -1; continue; }
                meshes.Add(new JObject { ["primitives"] = prims });
                meshIndex[mi] = meshes.Count - 1;
            }

            foreach (var node in nodes)
            {
                var jn = new JObject { ["name"] = node.Name ?? "" };
                if (node.Extras.Count > 0) jn["extras"] = JObject.FromObject(node.Extras);
                jsonNodes.Add(jn);
                children.Add(jsonNodes.Count - 1);
                var kids = new JArray();
                foreach (var inst in node.Instances)
                {
                    if (inst.Mesh < 0 || inst.Mesh >= meshIndex.Length || meshIndex[inst.Mesh] < 0) continue;
                    var ji = new JObject { ["mesh"] = meshIndex[inst.Mesh] };
                    if (!inst.Matrix.isIdentity) ji["matrix"] = MirroredMatrix(inst.Matrix);
                    jsonNodes.Add(ji);
                    kids.Add(jsonNodes.Count - 1);
                }
                if (kids.Count > 0) jn["children"] = kids;
            }
            if (children.Count == 0) root.Remove("children");

            var mats = new JArray();
            foreach (var m in materials)
            {
                var c = m.BaseColor.linear;
                var e = m.Emissive.linear;
                var jm = new JObject
                {
                    ["pbrMetallicRoughness"] = new JObject
                    {
                        ["baseColorFactor"] = new JArray(R(c.r), R(c.g), R(c.b), R(m.Blend ? m.BaseColor.a : 1f)),
                        ["metallicFactor"] = R(m.Metallic),
                        ["roughnessFactor"] = R(m.Roughness),
                    },
                };
                if (e.r > 0f || e.g > 0f || e.b > 0f) jm["emissiveFactor"] = new JArray(R(e.r), R(e.g), R(e.b));
                if (m.Blend) jm["alphaMode"] = "BLEND";
                mats.Add(jm);
            }

            Pad(bin, 4, 0);
            var gltf = new JObject
            {
                ["asset"] = new JObject { ["version"] = "2.0", ["generator"] = "IMPOSSIBLE LAB" },
                ["scene"] = 0,
                ["scenes"] = new JArray(new JObject { ["nodes"] = new JArray(0) }),
                ["nodes"] = jsonNodes,
            };
            if (meshes.Count > 0)
            {
                gltf["meshes"] = meshes;
                gltf["accessors"] = accessors;
                gltf["bufferViews"] = bufferViews;
                gltf["buffers"] = new JArray(new JObject { ["byteLength"] = (int)bin.Length });
            }
            if (mats.Count > 0) gltf["materials"] = mats;

            var json = new MemoryStream();
            var jsonBytes = new UTF8Encoding(false).GetBytes(gltf.ToString(Formatting.None));
            json.Write(jsonBytes, 0, jsonBytes.Length);
            Pad(json, 4, 0x20); // JSON の区切りは空白で埋める

            var outStream = new MemoryStream();
            using (var w = new BinaryWriter(outStream, Encoding.UTF8, true))
            {
                bool hasBin = bin.Length > 0;
                uint total = (uint)(12 + 8 + json.Length + (hasBin ? 8 + bin.Length : 0));
                w.Write(Magic); w.Write(2u); w.Write(total);
                w.Write((uint)json.Length); w.Write(ChunkJson); w.Write(json.ToArray());
                if (hasBin) { w.Write((uint)bin.Length); w.Write(ChunkBin); w.Write(bin.ToArray()); }
            }
            return outStream.ToArray();
        }

        /// <summary>GLB を読み、形が正しいかを確かめて概要を返す。正しくなければ理由を error に入れて null。</summary>
        public static GlbSummary Inspect(byte[] glb, out string error)
        {
            error = null;
            try
            {
                if (glb == null || glb.Length < 20) { error = "短すぎます"; return null; }
                if (BitConverter.ToUInt32(glb, 0) != Magic) { error = "glTF の印がありません"; return null; }
                if (BitConverter.ToUInt32(glb, 4) != 2) { error = "版が 2 ではありません"; return null; }
                if (BitConverter.ToUInt32(glb, 8) != glb.Length) { error = "全体の長さが合いません"; return null; }
                int jsonLen = (int)BitConverter.ToUInt32(glb, 12);
                if (BitConverter.ToUInt32(glb, 16) != ChunkJson || jsonLen % 4 != 0 || 20 + jsonLen > glb.Length) { error = "JSON の区切りが正しくありません"; return null; }
                var json = JObject.Parse(Encoding.UTF8.GetString(glb, 20, jsonLen));
                int binAt = 20 + jsonLen, binLen = 0;
                if (binAt < glb.Length)
                {
                    binLen = (int)BitConverter.ToUInt32(glb, binAt);
                    if (BitConverter.ToUInt32(glb, binAt + 4) != ChunkBin || binLen % 4 != 0 || binAt + 8 + binLen != glb.Length) { error = "頂点データの区切りが正しくありません"; return null; }
                }
                var s = new GlbSummary { Json = json, BinLength = binLen };
                var nodes = json["nodes"] as JArray ?? new JArray();
                var accessors = json["accessors"] as JArray ?? new JArray();
                var views = json["bufferViews"] as JArray ?? new JArray();
                s.Nodes = nodes.Count;
                s.NodeNames = nodes.Select(n => n.Value<string>("name") ?? "").ToList();
                s.Materials = (json["materials"] as JArray)?.Count ?? 0;
                s.Instances = nodes.Count(n => n["mesh"] != null);
                foreach (JObject v in views)
                {
                    int end = v.Value<int>("byteOffset") + v.Value<int>("byteLength");
                    if (end > binLen || v.Value<int>("byteOffset") % 4 != 0) { error = "頂点データの範囲が外れています"; return null; }
                }
                if (json["meshes"] is JArray meshes)
                {
                    s.Meshes = meshes.Count;
                    foreach (JObject m in meshes)
                        foreach (JObject p in (JArray)m["primitives"])
                        {
                            s.Primitives++;
                            var pos = (JObject)accessors[p["attributes"].Value<int>("POSITION")];
                            int vc = pos.Value<int>("count");
                            s.Vertices += vc;
                            if (pos["min"] == null || pos["max"] == null) { error = "POSITION に min/max がありません"; return null; }
                            var ia = (JObject)accessors[p.Value<int>("indices")];
                            s.Triangles += ia.Value<int>("count") / 3;
                            // 頂点の番号が範囲に収まるか
                            var iv = (JObject)views[ia.Value<int>("bufferView")];
                            int off = 20 + jsonLen + 8 + iv.Value<int>("byteOffset");
                            bool small = ia.Value<int>("componentType") == UShort;
                            for (int i = 0; i < ia.Value<int>("count"); i++)
                            {
                                int k = small ? BitConverter.ToUInt16(glb, off + i * 2) : (int)BitConverter.ToUInt32(glb, off + i * 4);
                                if (k >= vc) { error = "頂点の番号が範囲の外です"; return null; }
                            }
                        }
                }
                return s;
            }
            catch (Exception e) when (e is JsonException || e is InvalidCastException || e is ArgumentException || e is NullReferenceException || e is IndexOutOfRangeException)
            {
                error = "読めません：" + e.GetType().Name;
                return null;
            }
        }

        static double R(float v) => Math.Round(Mathf.Clamp01(v), 4);

        /// <summary>
        /// Unity の変換を glTF の座標へ：X を反転した空間で同じ変換になるよう S·M·S（S＝X だけ -1）にする。列の順に 16 個。
        /// 負の大きさ（鏡に映した変換）は glTF でも行列式が負になり、表と裏は読み込む側が入れ替える。
        /// </summary>
        public static JArray MirroredMatrix(Matrix4x4 m)
        {
            var a = new JArray();
            for (int c = 0; c < 4; c++)
                for (int r = 0; r < 4; r++)
                    a.Add(Math.Round((r == 0) != (c == 0) ? -m[r, c] : m[r, c], 6));
            return a;
        }

        /// <summary>回転・大きさ・位置に分けられる変換か（列どうしが直交＝ゆがみが無い）。glTF の matrix はこの形でなければならない。</summary>
        public static bool IsTrs(Matrix4x4 m)
        {
            if (Mathf.Abs(m.m30) > 1e-6f || Mathf.Abs(m.m31) > 1e-6f || Mathf.Abs(m.m32) > 1e-6f || Mathf.Abs(m.m33 - 1f) > 1e-6f) return false;
            Vector3 x = m.GetColumn(0), y = m.GetColumn(1), z = m.GetColumn(2);
            if (x.sqrMagnitude < 1e-16f || y.sqrMagnitude < 1e-16f || z.sqrMagnitude < 1e-16f) return false;
            x.Normalize(); y.Normalize(); z.Normalize();
            return Mathf.Abs(Vector3.Dot(x, y)) < 1e-4f && Mathf.Abs(Vector3.Dot(y, z)) < 1e-4f && Mathf.Abs(Vector3.Dot(z, x)) < 1e-4f;
        }

        static void Pad(Stream s, int align, byte fill)
        {
            while (s.Length % align != 0) s.WriteByte(fill);
        }

        static void PutFloat(byte[] b, int at, float v) { var x = BitConverter.GetBytes(v); Buffer.BlockCopy(x, 0, b, at, 4); }
        static void PutUShort(byte[] b, int at, int v) { b[at] = (byte)(v & 0xFF); b[at + 1] = (byte)((v >> 8) & 0xFF); }
        static void PutUInt(byte[] b, int at, int v) { var x = BitConverter.GetBytes((uint)v); Buffer.BlockCopy(x, 0, b, at, 4); }
    }
}
