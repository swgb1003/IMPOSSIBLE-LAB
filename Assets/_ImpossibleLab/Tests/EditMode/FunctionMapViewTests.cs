using System.Collections.Generic;
using System.Linq;
using ImpossibleLab.Core;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>6週目 S02：機能対応表の表示（要望ごとの状態・使う機能・確かめる実験計画）。</summary>
    public class FunctionMapViewTests
    {
        static InventionDna Parse(string json)
        {
            var r = DnaJson.Parse(json);
            Assert.IsTrue(r.IsValid, r.Summary);
            return r.Dna;
        }

        static InventionDna Sample(string file) => Parse(Resources.Load<TextAsset>("ImpossibleLabSamples/" + file).text);

        /// <summary>照明・通知の発明。要望3件（検証する・単純化・検証できない）。</summary>
        static InventionDna NightLight(string extraNeed = "") => Parse(@"{ ""schemaVersion"": 2, ""id"": ""t-map"", ""name"": ""夜道ライト"", ""purpose"": ""テスト"", ""tags"": [""lighting""], ""mount"": ""back"",
  ""parts"": [ { ""id"": ""body"", ""type"": ""body"", ""position"": [0, 0, 0], ""scale"": [0.1, 0.1, 0.1] }, { ""id"": ""lamp"", ""type"": ""light"", ""position"": [0, 0.05, 0], ""scale"": [0.04, 0.04, 0.04] },
             { ""id"": ""pad"", ""type"": ""heater"", ""position"": [0, 0, 0.05], ""scale"": [0.1, 0.1, 0.01] } ],
  ""behaviors"": [ { ""type"": ""illuminate"", ""targetPart"": ""lamp"", ""parameters"": { ""lumens"": 150, ""beamAngleDeg"": 60, ""tiltDeg"": 50 } },
                   { ""type"": ""alert"", ""targetPart"": ""lamp"", ""parameters"": { ""loudnessDb"": 0, ""lightLumens"": 30, ""flashHz"": 2 } },
                   { ""type"": ""heat"", ""targetPart"": ""pad"", ""parameters"": { ""powerW"": 10 } } ],
  ""functionMap"": [
    { ""need"": ""足元を照らす"", ""blocks"": [""illuminate""], ""status"": ""simulated"" },
    { ""need"": ""周りに知らせる"", ""blocks"": [""alert""], ""status"": ""simplified"", ""note"": ""点滅の気づきやすさは扱わない"" },
    { ""need"": ""背中を温める"", ""blocks"": [""heat""], ""status"": ""simulated"" },
    { ""need"": ""雨でも壊れない"", ""blocks"": [], ""status"": ""unverifiable"", ""note"": ""防水の実験はない"" }" + extraNeed + @" ],
  ""revision"": 1, ""parentRevision"": null }");

        [Test]
        public void AiMap_ShowsStatusBlocksAndThePlanThatChecksIt()
        {
            var dna = NightLight();
            var rows = FunctionMapView.Build(dna, PlanCandidates.Build(dna, null), out bool derived);
            Assert.IsFalse(derived);
            Assert.AreEqual(4, rows.Count);
            Assert.AreEqual("照明", rows[0].BlocksJa);
            Assert.AreEqual("暗い道で足元を照らす", rows[0].PlanTitle);
            Assert.AreEqual("暗い道で足元を照らす", rows[1].PlanTitle, "通知の届く距離は暗い道の実験で測る");
            Assert.AreEqual("寒い場所で座って過ごす", rows[2].PlanTitle);
            Assert.IsFalse(rows[3].Verifiable);
            Assert.IsNull(rows[3].PlanTitle);
            Assert.AreEqual("防水の実験はない", rows[3].Note);
            Assert.AreEqual((2, 1, 1), FunctionMapView.Count(rows));
        }

        [Test]
        public void VerifiableNeed_WithoutAnyPlan_IsFlagged()
        {
            var dna = NightLight();
            var onlyLight = PlanCandidates.Build(dna, null).Where(c => c.Plan.Scenario == ExperimentCatalog.Stand).ToList();
            var rows = FunctionMapView.Build(dna, onlyLight, out _);
            Assert.IsFalse(rows[0].NoPlan);
            Assert.IsTrue(rows[2].NoPlan, "加温を動かす実験が候補にない");
            Assert.IsFalse(rows[3].NoPlan, "検証できない要望は対象外");
        }

        [Test]
        public void SampleWithoutMap_GetsRowsPerBehavior()
        {
            var dna = Sample("handsfree_rain");
            Assert.IsEmpty(dna.FunctionMap);
            var rows = FunctionMapView.Build(dna, PlanCandidates.Build(dna, null), out bool derived);
            Assert.IsTrue(derived);
            Assert.AreEqual(dna.Behaviors.Count, rows.Count);
            var shield = rows.Single(r => r.Need == LabCatalog.Behavior("rain_shield").LabelJa);
            Assert.AreEqual("simulated", shield.Status);
            Assert.AreEqual("雨と風の中に立つ", shield.PlanTitle);
        }

        [Test]
        public void BlocksExercised_ForExistingLabs_UsesTheModelsBlocks()
        {
            var shoes = Sample("flying_shoes");
            var plan = PlanCandidates.Build(shoes, null)[0].Plan;
            var used = PlanCandidates.BlocksExercised(plan, shoes);
            foreach (var b in shoes.Behaviors.Select(x => x.Type).Where(t => t == "hover" || t == "move_forward" || t == "stabilize"))
                Assert.IsTrue(used.Contains(b), b);
            var light = NightLight();
            var stand = PlanCandidates.Build(light, null).First(c => c.Plan.Scenario == ExperimentCatalog.Stand).Plan;
            CollectionAssert.AreEquivalent(new[] { "illuminate", "alert" }, PlanCandidates.BlocksExercised(stand, light));
        }

        [Test]
        public void RemovedBehavior_MakesNeedUnverifiable_AndRowFollows()
        {
            var dna = NightLight();
            var p = new RevisionProposal { Source = "manual" };
            p.Operations.Add(new RevisionOperation { Op = RevisionOps.RemoveBehavior, BehaviorType = "heat" });
            var res = RevisionEngine.Apply(dna, p);
            Assert.IsTrue(res.Success, string.Join(" / ", res.Errors));
            var rows = FunctionMapView.Build(res.NewDna, PlanCandidates.Build(res.NewDna, null), out _);
            var heat = rows.Single(r => r.Need == "背中を温める");
            Assert.IsFalse(heat.Verifiable);
            StringAssert.Contains("外した", heat.Note);
        }
    }
}
