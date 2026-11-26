using System.Linq;
using ImpossibleLab.Core;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>部品の品番（PartModels）と、拡充した部品の種類（docs/DECISIONS.md D-41）。</summary>
    public class PartModelsTests
    {
        [Test] public void Data_Loads_AndEveryModelFitsItsType()
        {
            var all = PartModels.All;
            Assert.Greater(all.Count, 250, "品番は数百");
            Assert.AreEqual(all.Count, all.Select(m => m.Id).Distinct().Count(), "品番の ID は重ならない");
            foreach (var m in all)
            {
                var t = LabCatalog.Part(m.Type);
                Assert.IsNotNull(t, m.Id);
                Assert.IsNotNull(LabCatalog.Shape(m.Shape), m.Id);
                Assert.Greater(m.MassKg, 0f, m.Id);
                Assert.Greater(m.PriceJpy, 0f, m.Id);
                Assert.LessOrEqual(m.Id.Length, PartModels.MaxIdLength, m.Id);
                for (int i = 0; i < 3; i++)
                    Assert.That(m.Size[i], Is.InRange(t.MinScale, t.MaxScale), $"{m.Id} の大きさ {m.Size} が {m.Type} の範囲 {t.MinScale}〜{t.MaxScale} の外");
            }
        }

        [Test] public void NewTypes_HaveGroups_AndModels()
        {
            var groups = LabCatalog.PartGroups.Select(g => g.Id).ToHashSet();
            foreach (var t in LabCatalog.PartTypes) Assert.IsTrue(groups.Contains(t.Group), t.Type + " のまとまり");
            Assert.GreaterOrEqual(LabCatalog.PartTypes.Length, 54, "前からの 21 種類＋拡充の 33 種類");
            foreach (var t in new[] { "hinge", "pump", "display", "switch", "heat_sink", "clip", "gasket", "crawler", "fan", "radio" })
                Assert.Greater(PartModels.OfType(t).Count(), 0, t + " の品番");
            Assert.AreEqual("sensing", LabCatalog.Part("sensor").Group);
            Assert.Greater(PartModels.OfType("sensor").Select(m => m.Variant).Distinct().Count(), 8, "センサーは見た目の違う品番がそろう");
        }

        const string Lamp50 = "[{\"type\":\"illuminate\",\"targetPart\":\"lamp\",\"parameters\":{\"lumens\":50,\"beamAngleDeg\":60,\"tiltDeg\":45}}]";

        static string Dna(string partExtra, string behaviors = Lamp50) =>
            "{\"schemaVersion\":3,\"id\":\"t\",\"name\":\"t\",\"purpose\":\"t\",\"tags\":[\"lighting\"],\"mount\":\"fixed\"," +
            "\"parts\":[{\"id\":\"body\",\"type\":\"body\",\"shape\":\"box\",\"material\":\"plastic\",\"position\":[0,0,0],\"scale\":[0.2,0.1,0.2]}," +
            "{\"id\":\"lamp\",\"type\":\"light\",\"shape\":\"cylinder\",\"material\":\"plastic\",\"position\":[0,0.07,0],\"scale\":[0.02,0.016,0.02]" + partExtra + "}]," +
            "\"behaviors\":" + behaviors + ",\"functionMap\":[{\"need\":\"照らす\",\"blocks\":[\"illuminate\"],\"status\":\"simulated\"}]}";

        [Test] public void Dna_ReadsAndWritesModel_AndCapsTheSpec()
        {
            var r = DnaJson.Parse(Dna(",\"model\":\"led-spot1w\"", "[{\"type\":\"illuminate\",\"targetPart\":\"lamp\",\"parameters\":{\"lumens\":800,\"beamAngleDeg\":60,\"tiltDeg\":45}}]"));
            Assert.IsTrue(r.IsValid, r.Summary);
            var lamp = r.Dna.FindPart("lamp");
            Assert.AreEqual("led-spot1w", lamp.Model);
            Assert.AreEqual(100f, r.Dna.FindBehavior("illuminate").Parameters["lumens"], 1e-3f, "品番の光束（100 lm）までにする");
            Assert.IsTrue(r.Warnings.Any(w => w.Contains("品番")));
            StringAssert.Contains("\"model\": \"led-spot1w\"", DnaJson.ToJson(r.Dna));
            // 重さは品番の値（大きさが同じなら同じ）
            Assert.AreEqual(0.006f, MassModel.Part(lamp).Kg, 1e-4f);
        }

        [Test] public void Dna_DropsUnknownOrWrongTypeModel_AndAlignsShape()
        {
            var r = DnaJson.Parse(Dna(",\"model\":\"no-such-model\""));
            Assert.IsTrue(r.IsValid, r.Summary);
            Assert.AreEqual("", r.Dna.FindPart("lamp").Model);
            r = DnaJson.Parse(Dna(",\"model\":\"bat-aa4\""));
            Assert.AreEqual("", r.Dna.FindPart("lamp").Model, "電池の品番は照明に付けない");
            r = DnaJson.Parse(Dna(",\"model\":\"led-tape30\""));
            Assert.AreEqual("box", r.Dna.FindPart("lamp").ShapeOrDefault, "品番の形（LED テープは箱）にそろえる");
        }

        [Test] public void Revision_ChangesModel_AndDropsItWhenShapeChanges()
        {
            var dna = DnaJson.Parse(Dna("")).Dna;
            var res = RevisionEngine.Apply(dna, new RevisionProposal { Operations = { new RevisionOperation { Op = RevisionOps.UpdatePart, PartId = "lamp", Model = "led-cob20" } } });
            Assert.IsTrue(res.Errors.Count == 0, string.Join(" / ", res.Errors));
            var lamp = res.NewDna.FindPart("lamp");
            Assert.AreEqual("led-cob20", lamp.Model);
            Assert.AreEqual("box", lamp.ShapeOrDefault);
            res = RevisionEngine.Apply(res.NewDna, new RevisionProposal { Operations = { new RevisionOperation { Op = RevisionOps.UpdatePart, PartId = "lamp", Shape = "cylinder" } } });
            Assert.AreEqual("", res.NewDna.FindPart("lamp").Model, "形を変えたら品番を外す");
            res = RevisionEngine.Apply(res.NewDna, new RevisionProposal { Operations = { new RevisionOperation { Op = RevisionOps.UpdatePart, PartId = "lamp", Model = "bat-aa4" } } });
            Assert.IsTrue(res.Errors.Count > 0, "種類の違う品番は使えない");
        }

        [Test] public void SizeRatio_ScalesMass()
        {
            var m = PartModels.Find("bat-18650x1");
            var p = new PartSpec { Id = "b", Type = "battery", Shape = "cylinder", Scale = m.Size * Mathf.Pow(2f, 1f / 3f), Model = m.Id };
            Assert.AreEqual(2f, PartModels.SizeRatio(p, m), 1e-3f);
            Assert.AreEqual(m.MassKg * 2f, MassModel.Part(p).Kg, 1e-4f);
        }
    }
}
