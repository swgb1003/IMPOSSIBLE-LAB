using System.Collections.Generic;
using System.Linq;
using ImpossibleLab.Core;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>AI の設計の部品の配置を整える（浮き・深いめり込み・左右のずれ）。</summary>
    public class LayoutFixerTests
    {
        static PartSpec P(string id, string type, Vector3 pos, Vector3 scale, string shape = "") =>
            new PartSpec { Id = id, Type = type, Position = pos, Scale = scale, Shape = shape };

        static InventionDna Dna(params PartSpec[] parts) => new InventionDna { Id = "t", Name = "t", Parts = parts.ToList() };

        [Test] public void FloatingWheels_AttachToBody_AndStaySymmetric()
        {
            var dna = Dna(
                P("body", "body", Vector3.zero, new Vector3(0.4f, 0.12f, 0.5f), "box"),
                P("wl", "wheel", new Vector3(-0.3f, -0.05f, 0.15f), new Vector3(0.16f, 0.05f, 0.16f)),
                P("wr", "wheel", new Vector3(0.32f, -0.04f, 0.15f), new Vector3(0.16f, 0.05f, 0.16f)));
            Assert.Greater(LayoutFixer.Apply(dna), 0);
            var wl = dna.FindPart("wl"); var wr = dna.FindPart("wr");
            Assert.LessOrEqual(LayoutFixer.Gap(wl, dna.FindPart("body")), LayoutFixer.Touch, "車輪が本体に触れる");
            Assert.LessOrEqual(LayoutFixer.Gap(wr, dna.FindPart("body")), LayoutFixer.Touch);
            Assert.AreEqual(-wl.Position.x, wr.Position.x, 1e-4f, "左右対称");
            Assert.AreEqual(wl.Position.y, wr.Position.y, 1e-4f);
            Assert.AreEqual(0, LayoutFixer.Apply(dna), "2回目は何も動かさない");
        }

        [Test] public void Canopy_JointParts_AndFarParts_AreKept()
        {
            var lid = P("lid", "panel", new Vector3(0, 0.5f, 0), new Vector3(0.3f, 0.02f, 0.3f), "box");
            lid.Joint = new JointSpec { Type = JointSpec.Hinge, Axis = "x", Min = 0, Max = 90, Speed = 60 };
            var dna = Dna(
                P("core", "body", Vector3.zero, new Vector3(0.2f, 0.08f, 0.2f)),
                P("shade", "canopy", new Vector3(0, 0.2f, 0), new Vector3(1.2f, 0.03f, 1.2f)),
                lid,
                P("far", "light", new Vector3(1.5f, 0, 0), new Vector3(0.05f, 0.05f, 0.05f)));
            LayoutFixer.Apply(dna);
            Assert.AreEqual(new Vector3(0, 0.2f, 0), dna.FindPart("shade").Position);
            Assert.AreEqual(new Vector3(0, 0.5f, 0), dna.FindPart("lid").Position);
            Assert.AreEqual(new Vector3(1.5f, 0, 0), dna.FindPart("far").Position, "30 cm より遠いものは意図して離したとみなす");
        }

        [Test] public void DeepIntersection_PushedOut_ButInternalPartKept()
        {
            var dna = Dna(
                P("body", "body", Vector3.zero, new Vector3(0.4f, 0.2f, 0.4f), "box"),
                P("panel", "panel", new Vector3(0, 0, 0.17f), new Vector3(0.2f, 0.1f, 0.1f), "box"),
                P("cell", "battery", new Vector3(0, 0, -0.05f), new Vector3(0.1f, 0.05f, 0.1f), "box"));
            LayoutFixer.Apply(dna);
            var panel = dna.FindPart("panel");
            float pen = 0.2f + 0.05f - panel.Position.z;
            Assert.AreEqual(0.3f * 0.1f, pen, 1e-4f, "食い込みは厚みの3割");
            Assert.AreEqual(new Vector3(0, 0, -0.05f), dna.FindPart("cell").Position, "中に入った電池は内蔵として残す");
        }

        [Test] public void Revision_MovesOnlyAddedParts()
        {
            var dna = Dna(
                P("body", "body", Vector3.zero, new Vector3(0.3f, 0.1f, 0.3f), "box"),
                P("old", "light", new Vector3(0, 0.2f, 0), new Vector3(0.05f, 0.05f, 0.05f)),
                P("new", "sensor", new Vector3(0, 0, 0.25f), new Vector3(0.05f, 0.05f, 0.05f)));
            LayoutFixer.Apply(dna, new List<string> { "new" });
            Assert.AreEqual(new Vector3(0, 0.2f, 0), dna.FindPart("old").Position);
            Assert.LessOrEqual(LayoutFixer.Gap(dna.FindPart("new"), dna.FindPart("body")), LayoutFixer.Touch);
        }

        [Test] public void Samples_StayValid_AndFixIsStable()
        {
            foreach (var t in Resources.LoadAll<TextAsset>("ImpossibleLabSamples"))
            {
                var r = DnaJson.Parse(t.text);
                if (!r.IsValid) continue;
                LayoutFixer.Apply(r.Dna);
                Assert.IsTrue(DnaJson.Parse(DnaJson.ToJson(r.Dna)).IsValid, t.name);
                Assert.AreEqual(0, LayoutFixer.Apply(r.Dna), t.name + "：2回目は何も動かさない");
            }
        }
    }
}
