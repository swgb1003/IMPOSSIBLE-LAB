using System.Collections.Generic;
using ImpossibleLab.Core;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>外装を一体の殻にする・形の仕上げ（見た目のみ）：合わせる部品の選び方、殻のメッシュ、覆いのドーム。</summary>
    public class ProductShellTests
    {
        static PartSpec Part(string id, string type, Vector3 pos, Vector3 scale, string shape = "box", string material = "plastic") =>
            new PartSpec { Id = id, Type = type, Position = pos, Scale = scale, Shape = shape, Material = material };

        static InventionDna Dna(params PartSpec[] parts)
        {
            var d = new InventionDna { Id = "t" };
            d.Parts.AddRange(parts);
            return d;
        }

        [Test] public void Pick_MergesTouchingHardHousings_BodyFirst()
        {
            var body = Part("base", "body", new Vector3(0, 0.1f, 0), new Vector3(0.6f, 0.2f, 0.5f), material: "aluminum");
            var battery = Part("bat", "battery", new Vector3(0, 0.21f, 0), new Vector3(0.2f, 0.02f, 0.15f));
            var wheel = Part("w", "wheel", new Vector3(0.3f, 0.03f, 0.25f), new Vector3(0.06f, 0.03f, 0.06f), "cylinder", "rubber");
            var far = Part("far", "container", new Vector3(0, 1.5f, 0), new Vector3(0.2f, 0.2f, 0.2f));
            var picked = ShellPlan.Pick(Dna(battery, wheel, far, body));
            CollectionAssert.AreEqual(new[] { "base", "bat" }, picked.ConvertAll(p => p.Id), "本体が先頭。車輪と離れた格納部は合わせない");
        }

        [Test] public void Pick_SkipsSoftMaterials_JointsAndLonelyBody()
        {
            var pack = Part("pack", "body", Vector3.zero, new Vector3(0.3f, 0.5f, 0.15f), material: "fabric");
            var bat = Part("bat", "battery", new Vector3(0, 0, 0.09f), new Vector3(0.1f, 0.1f, 0.03f));
            Assert.IsEmpty(ShellPlan.Pick(Dna(pack, bat)), "布の本体は殻にしない");

            var body = Part("b", "body", Vector3.zero, new Vector3(0.3f, 0.2f, 0.3f));
            var moving = Part("m", "battery", new Vector3(0, 0.12f, 0), new Vector3(0.1f, 0.04f, 0.1f));
            moving.Joint = new JointSpec();
            Assert.IsEmpty(ShellPlan.Pick(Dna(body, moving)), "関節で動く部品は合わせない（合わせる相手が無ければ作らない）");
        }

        [Test] public void Mesh_LiesOnSurface_AndFacesOutward()
        {
            var solids = new List<ShellMesher.Solid>
            {
                new ShellMesher.Solid { Center = Vector3.zero, Rotation = Quaternion.identity, Half = new Vector3(0.3f, 0.1f, 0.25f), Round = 0.02f },
                new ShellMesher.Solid { Center = new Vector3(0, 0.11f, 0), Rotation = Quaternion.identity, Half = new Vector3(0.1f, 0.01f, 0.08f), Round = 0.004f },
            };
            var r = ShellMesher.Build(solids, 0.02f, 40);
            Assert.Greater(r.Vertices.Count, 500);
            Assert.AreEqual(0, r.Triangles.Count % 3);
            for (int i = 0; i < r.Vertices.Count; i += 7)
                Assert.Less(Mathf.Abs(ShellMesher.Distance(solids, 0.02f, r.Vertices[i])), r.Cell * 0.2f, "頂点は表面の上");
            // 三角形の表（Unity の向き）が外を向く：重心から少し表へ出た点は外（距離が正）
            int outward = 0, total = 0;
            for (int i = 0; i < r.Triangles.Count; i += 3 * 11)
            {
                var a = r.Vertices[r.Triangles[i]]; var b = r.Vertices[r.Triangles[i + 1]]; var c = r.Vertices[r.Triangles[i + 2]];
                var n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-14f) continue;
                total++;
                if (ShellMesher.Distance(solids, 0.02f, (a + b + c) / 3f + n.normalized * r.Cell * 0.5f) > 0f) outward++;
            }
            Assert.Greater(total, 50);
            Assert.GreaterOrEqual(outward, total * 0.98f);
        }

        [Test] public void Dome_KeepsTheRim_AndRisesInTheMiddle()
        {
            var canopy = Part("c", "canopy", Vector3.zero, new Vector3(1.6f, 0.02f, 1.6f), "cylinder", "fabric");
            Assert.IsTrue(FormMesher.IsDomeCanopy(canopy));
            Assert.IsFalse(FormMesher.IsDomeCanopy(Part("c2", "canopy", Vector3.zero, new Vector3(0.3f, 0.2f, 0.3f), "cylinder", "fabric")), "厚い覆いは円柱のまま");
            float H = FormMesher.DomeHeight(0.8f);
            Assert.AreEqual(0.224f, H, 1e-4f);
            Assert.AreEqual(0.25f, FormMesher.DomeHeight(2f), 1e-4f, "大きい覆いでも 25 cm まで");
            var d = FormMesher.Dome(0.8f, 0.02f, H);
            float maxR = 0f, top = float.MinValue, bottom = float.MaxValue;
            foreach (var v in d.Vertices) { maxR = Mathf.Max(maxR, new Vector2(v.x, v.z).magnitude); top = Mathf.Max(top, v.y); bottom = Mathf.Min(bottom, v.y); }
            Assert.AreEqual(0.8f, maxR, 1e-3f, "縁は元の円盤と同じ半径");
            Assert.AreEqual(H + 0.01f, top, 1e-3f, "中央は縁から H だけ高い");
            Assert.AreEqual(-0.01f, bottom, 1e-3f, "縁の下は元の円盤の下と同じ");
            // 表の向き：いちばん上の頂点の法線は上を向く
            int apex = 0;
            for (int i = 1; i < d.Vertices.Count; i++) if (d.Vertices[i].y > d.Vertices[apex].y) apex = i;
            Assert.Greater(d.Normals[apex].y, 0.99f);
            Assert.AreEqual(0, d.Triangles.Count % 3);
        }

        [TestCase("dome")]
        [TestCase("cone")]
        [TestCase("ring")]
        public void MadeShapes_FillTheirBox_AndFaceOutward(string shape)
        {
            Assert.IsNotNull(LabCatalog.Shape(shape), "カタログにある（AI が選べる）");
            var size = new Vector3(0.6f, 0.15f, 0.5f);
            var m = FormMesher.MadeShape(shape, size);
            Vector3 lo = Vector3.one * 9f, hi = -Vector3.one * 9f;
            foreach (var v in m.Vertices) { lo = Vector3.Min(lo, v); hi = Vector3.Max(hi, v); }
            // 部品の Transform の大きさを掛けると DNA の大きさの箱に収まる（座標は ±0.5）
            Assert.AreEqual(0.5f, hi.x, 1e-3f); Assert.AreEqual(-0.5f, lo.x, 1e-3f);
            Assert.AreEqual(0.5f, hi.z, 1e-3f);
            Assert.LessOrEqual(hi.y, 0.5f + 1e-3f); Assert.GreaterOrEqual(lo.y, -0.5f - 1e-3f);
            if (shape != "ring") { Assert.AreEqual(0.5f, hi.y, 1e-3f); Assert.AreEqual(-0.5f, lo.y, 1e-3f); }
            // 表が外を向く：中心から頂点への向きと法線がおおむね同じ向き（ドームの内の面・輪の内側は中心を向くので、半分ほど）
            int ok = 0, n = 0;
            for (int i = 0; i < m.Vertices.Count; i += 5)
            {
                var v = Vector3.Scale(m.Vertices[i], size);
                if (v.sqrMagnitude < 1e-6f) continue;
                n++;
                if (Vector3.Dot(m.Normals[i], v) > 0f) ok++;
            }
            Assert.Greater(ok, n * (shape == "cone" ? 0.9f : 0.4f));
            FormMesher.Measure(shape, size, out float solid, out float area);
            Assert.Greater(solid, 0f); Assert.Greater(area, 0f);
            Assert.Less(solid, size.x * size.y * size.z, "外の箱より小さい");
        }

        [Test] public void SmoothUnion_FillsTheCorner()
        {
            // 2つの板が直角に接する角の内側は、なめらかな和では殻の中（丸みで埋まる）
            float hard = Mathf.Min(ShellMesher.RoundBox(new Vector3(0.012f, 0.012f, 0), new Vector3(0.01f, 1f, 1f), 0f), ShellMesher.RoundBox(new Vector3(0.012f, 0.012f, 0), new Vector3(1f, 0.01f, 1f), 0f));
            float smooth = ShellMesher.SmoothUnion(ShellMesher.RoundBox(new Vector3(0.012f, 0.012f, 0), new Vector3(0.01f, 1f, 1f), 0f), ShellMesher.RoundBox(new Vector3(0.012f, 0.012f, 0), new Vector3(1f, 0.01f, 1f), 0f), 0.02f);
            Assert.Greater(hard, 0f);
            Assert.Less(smooth, 0f);
        }
    }
}
