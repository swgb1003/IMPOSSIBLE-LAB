using System;
using System.Collections.Generic;
using System.Linq;
using ImpossibleLab.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>GLB の書き出し（docs/PUBLISH_LIBRARY.md 5章）。</summary>
    public class GlbWriterTests
    {
        static readonly List<GlbMaterial> OneMaterial = new List<GlbMaterial> { new GlbMaterial() };

        static GlbMesh TriangleMesh(Vector3 offset, int material = 0)
        {
            var m = new GlbMesh();
            m.Primitives.Add(new GlbPrimitive
            {
                Positions = new[] { offset, offset + Vector3.up, offset + Vector3.right },
                Normals = new[] { Vector3.left, Vector3.left, Vector3.left },
                Indices = new[] { 0, 1, 2 }, // Unity：-Z から見て時計回り
                Material = material,
            });
            return m;
        }

        static GlbNode Part(string name, params GlbInstance[] instances)
        {
            var n = new GlbNode { Name = name };
            n.Extras["partId"] = name;
            n.Instances.AddRange(instances);
            return n;
        }

        static GlbInstance At(int mesh, Matrix4x4? m = null) => new GlbInstance { Mesh = mesh, Matrix = m ?? Matrix4x4.identity };

        static GlbSummary Check(byte[] glb)
        {
            var s = GlbWriter.Inspect(glb, out var err);
            Assert.IsNotNull(s, err);
            Assert.AreEqual(0, glb.Length % 4, "全体は 4 バイトの倍数");
            return s;
        }

        static int DataStart(byte[] glb) => 20 + (int)BitConverter.ToUInt32(glb, 12) + 8;

        [Test] public void Header_Chunks_AndCounts()
        {
            var glb = GlbWriter.Write(new[] { Part("body", At(0)), Part("arm", At(1)) }, new[] { TriangleMesh(Vector3.zero), TriangleMesh(Vector3.up) }, OneMaterial,
                "invention", new Dictionary<string, string> { ["inventionId"] = "rain-1" });
            var s = Check(glb);
            Assert.AreEqual(0x46546C67u, BitConverter.ToUInt32(glb, 0));
            Assert.AreEqual(5, s.Nodes, "根＋部品2つ＋形を置くノード2つ");
            Assert.AreEqual(2, s.Meshes);
            Assert.AreEqual(2, s.Instances);
            Assert.AreEqual(6, s.Vertices);
            Assert.AreEqual(2, s.Triangles);
            Assert.AreEqual(1, s.Materials);
            var nodes = (JArray)s.Json["nodes"];
            Assert.AreEqual("rain-1", nodes[0]["extras"].Value<string>("inventionId"));
            var parts = nodes[0]["children"].Values<int>().Select(i => nodes[i]).ToList();
            Assert.AreEqual(new[] { "body", "arm" }, parts.Select(p => p.Value<string>("name")).ToArray(), "部品は渡した順");
            Assert.AreEqual("body", parts[0]["extras"].Value<string>("partId"));
            Assert.AreEqual(0, nodes[parts[0]["children"][0].Value<int>()].Value<int>("mesh"), "形は部品の子に置く");
            Assert.IsNull(nodes[parts[0]["children"][0].Value<int>()]["matrix"], "単位行列は書かない");
            Assert.AreEqual(0, s.Json["scenes"][0]["nodes"][0].Value<int>());
        }

        [Test] public void SameMesh_IsWrittenOnce_ForEveryPlace()
        {
            var wheels = Enumerable.Range(0, 4).Select(i => Part("wheel" + i, At(0, Matrix4x4.Translate(new Vector3(i % 2 == 0 ? -0.2f : 0.2f, 0.1f, i < 2 ? 0.2f : -0.2f))))).ToArray();
            var s = Check(GlbWriter.Write(wheels, new[] { TriangleMesh(Vector3.zero) }, OneMaterial, "invention"));
            Assert.AreEqual(1, s.Meshes);
            Assert.AreEqual(4, s.Instances);
            Assert.AreEqual(3, s.Vertices, "頂点は1回分だけ");
        }

        [Test] public void X_IsMirrored_AndWindingIsReversed()
        {
            var glb = GlbWriter.Write(new[] { Part("p", At(0)) }, new[] { TriangleMesh(new Vector3(0.5f, 0, 0)) }, OneMaterial, "invention");
            var s = Check(glb);
            var acc = (JArray)s.Json["accessors"];
            var prim = s.Json["meshes"][0]["primitives"][0];
            var pos = (JObject)acc[prim["attributes"].Value<int>("POSITION")];
            // X は反転：0.5〜1.5 → -1.5〜-0.5
            Assert.AreEqual(-1.5f, pos["min"][0].Value<float>(), 1e-5f);
            Assert.AreEqual(-0.5f, pos["max"][0].Value<float>(), 1e-5f);
            Assert.AreEqual(1f, pos["max"][1].Value<float>(), 1e-5f);
            // 頂点の番号は 0, 2, 1 の順になる（glTF は反時計回りが表）
            var iacc = (JObject)acc[prim.Value<int>("indices")];
            int off = DataStart(glb) + s.Json["bufferViews"][iacc.Value<int>("bufferView")].Value<int>("byteOffset");
            Assert.AreEqual(5123, iacc.Value<int>("componentType"));
            Assert.AreEqual(new[] { 0, 2, 1 }, new[] { BitConverter.ToUInt16(glb, off), BitConverter.ToUInt16(glb, off + 2), BitConverter.ToUInt16(glb, off + 4) }.Select(x => (int)x).ToArray());
            // 法線の X も反転（Unity の左向き -X → glTF では +X）
            var nacc = (JObject)acc[prim["attributes"].Value<int>("NORMAL")];
            int noff = DataStart(glb) + s.Json["bufferViews"][nacc.Value<int>("bufferView")].Value<int>("byteOffset");
            Assert.AreEqual(1f, BitConverter.ToSingle(glb, noff), 1e-6f);
        }

        [Test] public void Matrix_IsMirroredLikeThePoints()
        {
            // Unity：右へ 0.3 m 動かし、Y まわりに 90° 回す。glTF の座標では左へ 0.3 m、Y まわりに -90°
            var m = Matrix4x4.TRS(new Vector3(0.3f, 0.1f, 0.2f), Quaternion.Euler(0, 90, 0), new Vector3(1f, 2f, 3f));
            var a = GlbWriter.MirroredMatrix(m).Values<float>().ToArray();
            Assert.AreEqual(16, a.Length);
            Assert.AreEqual(-0.3f, a[12], 1e-5f); Assert.AreEqual(0.1f, a[13], 1e-5f); Assert.AreEqual(0.2f, a[14], 1e-5f);
            // 同じ点を、Unity で変換してから X を反転したもの ＝ X を反転してから glTF の行列で変換したもの
            var p = new Vector3(0.4f, -0.2f, 0.7f);
            var u = m.MultiplyPoint3x4(p); u.x = -u.x;
            var q = new Vector3(-p.x, p.y, p.z);
            var g = new Vector3(a[0] * q.x + a[4] * q.y + a[8] * q.z + a[12], a[1] * q.x + a[5] * q.y + a[9] * q.z + a[13], a[2] * q.x + a[6] * q.y + a[10] * q.z + a[14]);
            Assert.Less((u - g).magnitude, 1e-4f);
        }

        [Test] public void IsTrs_RejectsSkew()
        {
            Assert.IsTrue(GlbWriter.IsTrs(Matrix4x4.TRS(Vector3.one, Quaternion.Euler(10, 20, 30), new Vector3(1, 2, -3))));
            // 縦横で違う大きさの親の下で、子を回すとゆがむ
            var skew = Matrix4x4.Scale(new Vector3(1f, 3f, 1f)) * Matrix4x4.Rotate(Quaternion.Euler(0, 0, 45));
            Assert.IsFalse(GlbWriter.IsTrs(skew));
        }

        [Test] public void Material_IsLinear_AndTransparentBlends()
        {
            var mats = new List<GlbMaterial>
            {
                new GlbMaterial { BaseColor = new Color(0.5f, 0.5f, 0.5f, 1f), Metallic = 0.8f, Roughness = 0.3f },
                new GlbMaterial { BaseColor = new Color(0.74f, 0.87f, 0.97f, 0.85f), Blend = true, Emissive = new Color(0.2f, 0.6f, 1f) },
            };
            var s = Check(GlbWriter.Write(new[] { Part("a", At(0)), Part("b", At(1)) }, new[] { TriangleMesh(Vector3.zero, 0), TriangleMesh(Vector3.one, 1) }, mats, "invention"));
            var m0 = s.Json["materials"][0];
            Assert.AreEqual(Mathf.GammaToLinearSpace(0.5f), m0["pbrMetallicRoughness"]["baseColorFactor"][0].Value<float>(), 1e-3f);
            Assert.AreEqual(1f, m0["pbrMetallicRoughness"]["baseColorFactor"][3].Value<float>(), 1e-6f, "不透明は透明度 1");
            Assert.AreEqual(0.8f, m0["pbrMetallicRoughness"]["metallicFactor"].Value<float>(), 1e-4f);
            Assert.IsNull(m0["alphaMode"]);
            Assert.IsNull(m0["emissiveFactor"]);
            var m1 = s.Json["materials"][1];
            Assert.AreEqual("BLEND", m1.Value<string>("alphaMode"));
            Assert.AreEqual(0.85f, m1["pbrMetallicRoughness"]["baseColorFactor"][3].Value<float>(), 1e-4f, "透明度は線形に直さない");
            Assert.IsNotNull(m1["emissiveFactor"]);
        }

        [Test] public void PartWithoutTriangles_KeepsItsNode()
        {
            var s = Check(GlbWriter.Write(new[] { Part("a", At(0)), Part("hidden"), Part("empty", At(1)) }, new[] { TriangleMesh(Vector3.zero), new GlbMesh() }, OneMaterial, "invention"));
            Assert.IsTrue(s.NodeNames.Contains("hidden") && s.NodeNames.Contains("empty"));
            Assert.AreEqual(1, s.Meshes, "三角形の無い形は書かない");
            Assert.AreEqual(1, s.Instances);
        }

        [Test] public void NothingToDraw_IsStillAValidFile()
        {
            var s = Check(GlbWriter.Write(new GlbNode[0], new GlbMesh[0], new List<GlbMaterial>(), "invention"));
            Assert.AreEqual(1, s.Nodes);
            Assert.AreEqual(0, s.BinLength);
        }

        [Test] public void JapaneseNames_KeepTheJsonChunkAligned()
        {
            var n = Part("本体", At(0));
            n.Extras["role"] = "雨を受け止める（直径 1.2 m）";
            var s = Check(GlbWriter.Write(new[] { n }, new[] { TriangleMesh(Vector3.zero) }, OneMaterial, "発明"));
            Assert.AreEqual("本体", s.NodeNames[1]);
            Assert.AreEqual("雨を受け止める（直径 1.2 m）", s.Json["nodes"][1]["extras"].Value<string>("role"));
        }

        [Test] public void ManyVertices_UseUInt32Indices()
        {
            int n = 70000;
            var p = new GlbPrimitive { Positions = new Vector3[n], Normals = Enumerable.Repeat(Vector3.up, n).ToArray(), Indices = new int[(n / 3) * 3] };
            for (int i = 0; i < n; i++) p.Positions[i] = new Vector3(i * 0.001f, 0, (i % 3) * 0.001f);
            for (int i = 0; i < p.Indices.Length; i++) p.Indices[i] = i;
            var mesh = new GlbMesh();
            mesh.Primitives.Add(p);
            var s = Check(GlbWriter.Write(new[] { Part("big", At(0)) }, new[] { mesh }, OneMaterial, "invention"));
            var iacc = s.Json["accessors"][s.Json["meshes"][0]["primitives"][0].Value<int>("indices")];
            Assert.AreEqual(5125, iacc.Value<int>("componentType"));
            Assert.AreEqual(n, s.Vertices);
        }

        [Test] public void Inspect_RejectsBrokenFiles()
        {
            var glb = GlbWriter.Write(new[] { Part("a", At(0)) }, new[] { TriangleMesh(Vector3.zero) }, OneMaterial, "invention");
            Assert.IsNull(GlbWriter.Inspect(new byte[10], out _));
            var badMagic = (byte[])glb.Clone(); badMagic[0] = 0;
            Assert.IsNull(GlbWriter.Inspect(badMagic, out _));
            var cut = glb.Take(glb.Length - 4).ToArray();
            Assert.IsNull(GlbWriter.Inspect(cut, out var e3));
            Assert.IsNotNull(e3);
        }
    }
}
