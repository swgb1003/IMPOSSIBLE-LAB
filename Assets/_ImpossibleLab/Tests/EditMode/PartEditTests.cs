using System.Linq;
using ImpossibleLab.Core;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>S03 の部品の形・素材の切り替え（update_part）。改良の版として記録し、REALITY の総質量に反映される。</summary>
    public class PartEditTests
    {
        static InventionDna Heater()
        {
            var r = DnaJson.Parse(Resources.Load<TextAsset>("ImpossibleLabSamples/back_heater").text);
            Assert.IsTrue(r.IsValid, r.Summary);
            return r.Dna;
        }

        static RevisionResult Apply(InventionDna dna, string partId, string shape = null, string material = null)
        {
            var p = new RevisionProposal { Source = "manual" };
            p.Operations.Add(new RevisionOperation { Op = RevisionOps.UpdatePart, PartId = partId, Shape = shape, Material = material });
            return RevisionEngine.Apply(dna, p);
        }

        [Test] public void Material_NewRevision_HeavierAndOriginalUnchanged()
        {
            var dna = Heater();
            var pad = dna.FindPart("back-pad");
            Assert.AreEqual("fabric", pad.MaterialOrDefault);
            float before = MassModel.TotalKg(dna);
            var r = Apply(dna, "back-pad", material: "steel");
            Assert.IsTrue(r.Success, string.Join(",", r.Errors));
            Assert.AreEqual(dna.Revision + 1, r.NewDna.Revision);
            Assert.AreEqual(dna.Revision, r.NewDna.ParentRevision);
            Assert.AreEqual("steel", r.NewDna.FindPart("back-pad").Material);
            Assert.AreEqual("fabric", pad.MaterialOrDefault, "元の版は変えない");
            Assert.Greater(MassModel.TotalKg(r.NewDna), before);
            Assert.AreEqual(1, r.Changes.Count);
            StringAssert.EndsWith("素材", r.Changes[0].Label);
            Assert.AreEqual("布", r.Changes[0].Before);
            Assert.AreEqual("鉄", r.Changes[0].After);
        }

        [Test] public void Shape_ChangesAndSurvivesSaveFormat()
        {
            var dna = Heater();
            var r = Apply(dna, "back-pad", shape: "cylinder");
            Assert.IsTrue(r.Success, string.Join(",", r.Errors));
            var again = DnaJson.Parse(DnaJson.ToJson(r.NewDna));
            Assert.IsTrue(again.IsValid, again.Summary);
            Assert.AreEqual("cylinder", again.Dna.FindPart("back-pad").ShapeOrDefault);
            Assert.AreEqual("円柱", r.Changes[0].After);
        }

        [Test] public void Invalid_Or_NoChange_IsRejected()
        {
            var dna = Heater();
            Assert.IsFalse(Apply(dna, "back-pad", material: "gold").Success);
            Assert.IsFalse(Apply(dna, "back-pad", shape: "torus").Success);
            Assert.IsFalse(Apply(dna, "no-such-part", material: "steel").Success);
            Assert.IsFalse(Apply(dna, "back-pad", material: "fabric").Success, "同じ素材は変化なし");
        }

        [Test] public void AiProposal_CannotUseUpdatePart()
        {
            Assert.IsFalse(RevisionOps.IsValid(RevisionOps.UpdatePart));
            Assert.IsFalse(RevisionEngine.TryParseProposal("{\"summary\":\"x\",\"operations\":[{\"op\":\"update_part\",\"partId\":\"back-pad\"}]}", out _, out _));
        }
    }
}
