using System.Linq;
using ImpossibleLab.Core;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>6週目 S05：合格条件ごとの判定と、結果ごとの要望の検証状況。</summary>
    public class ResultCriteriaTests
    {
        static InventionDna Parse(string json)
        {
            var r = DnaJson.Parse(json);
            Assert.IsTrue(r.IsValid, r.Summary);
            return r.Dna;
        }

        static InventionDna Sample(string file) => Parse(Resources.Load<TextAsset>("ImpossibleLabSamples/" + file).text);

        static InventionDna LightAndHeater() => Parse(@"{ ""schemaVersion"": 2, ""id"": ""t-res"", ""name"": ""テスト"", ""purpose"": ""テスト"", ""tags"": [""lighting""], ""mount"": ""back"",
  ""parts"": [ { ""id"": ""body"", ""type"": ""body"", ""position"": [0, 0, 0], ""scale"": [0.1, 0.1, 0.1] }, { ""id"": ""lamp"", ""type"": ""light"", ""position"": [0, 0.05, 0], ""scale"": [0.04, 0.04, 0.04] },
             { ""id"": ""pad"", ""type"": ""heater"", ""position"": [0, 0, 0.05], ""scale"": [0.1, 0.1, 0.01] } ],
  ""behaviors"": [ { ""type"": ""illuminate"", ""targetPart"": ""lamp"", ""parameters"": { ""lumens"": 150, ""beamAngleDeg"": 60, ""tiltDeg"": 50 } },
                   { ""type"": ""heat"", ""targetPart"": ""pad"", ""parameters"": { ""powerW"": 10 } } ],
  ""functionMap"": [
    { ""need"": ""足元を照らす"", ""blocks"": [""illuminate""], ""status"": ""simulated"" },
    { ""need"": ""背中を温める"", ""blocks"": [""heat""], ""status"": ""simulated"" },
    { ""need"": ""雨でも壊れない"", ""blocks"": [], ""status"": ""unverifiable"", ""note"": ""防水の実験はない"" } ],
  ""revision"": 1, ""parentRevision"": null }");

        static ExperimentPlan DarkPlan(InventionDna dna) => PlanCandidates.Build(dna, null).First(c => c.Plan.Scenario == ExperimentCatalog.Stand).Plan;

        [Test]
        public void ScenarioLab_DefaultCriteria_MatchTheVerdict()
        {
            var dna = LightAndHeater();
            var r = ScenarioRunner.RunPlan(dna, DarkPlan(dna)).Result;
            var checks = ResultCriteria.Evaluate(r);
            Assert.AreEqual(1, checks.Count, string.Join(" / ", checks.Select(c => c.Target)));
            Assert.AreEqual("footIlluminance", checks[0].Metric);
            Assert.IsTrue(checks[0].Measured);
            Assert.AreEqual(r.Passed, checks.All(c => c.Passed));
            StringAssert.Contains("lx", checks[0].Actual);
        }

        [Test]
        public void ScenarioLab_PlanCriteria_FailingOneIsShown()
        {
            var dna = LightAndHeater();
            var p = PlanEditor.Clone(DarkPlan(dna));
            p.Criteria.Add(new PlanCriterion { Metric = "footIlluminance", Op = ">=", Value = 5000f, Reason = "とても明るく" });
            var r = ScenarioRunner.RunPlan(dna, p).Result;
            var checks = ResultCriteria.Evaluate(r);
            Assert.AreEqual(1, checks.Count);
            Assert.IsFalse(checks[0].Passed);
            Assert.AreEqual("とても明るく", checks[0].Reason);
            Assert.IsFalse(r.Success);
            var hints = CourseSimulator.Hints(r);
            Assert.IsTrue(hints.Any(x => x.StartsWith("「足元の照度」が目標に届かない", System.StringComparison.Ordinal)), string.Join(" / ", hints));
        }

        [Test]
        public void ExistingLabs_UseTheSameCriteriaAsTheirModels()
        {
            var carrier = Sample("carrier_robot");
            var rc = CourseSimulator.Run(carrier, new RainExperimentConfig { Environment = LabEnvironments.ObstacleCourse, Seed = 1234 }).Result;
            var cc = ResultCriteria.Evaluate(rc);
            CollectionAssert.AreEqual(new[] { "collisionCount", "arrivalDelay" }, cc.Select(c => c.Metric));
            Assert.IsTrue(cc.All(c => c.Measured));
            if (cc.Any(c => !c.Passed)) Assert.IsFalse(rc.Success, "条件を1つでも満たさなければ不合格");

            var rain = Sample("handsfree_rain");
            var rr = new RainSimulator(rain, new RainExperimentConfig { Seed = 2026 }).RunToEnd();
            var rcks = ResultCriteria.Evaluate(rr);
            Assert.AreEqual("coverageRatio", rcks.Single().Metric);
            Assert.AreEqual(rr.Success, rcks.Single().Passed);
        }

        [Test]
        public void RequestRows_TellWhatThisExperimentChecked()
        {
            var dna = LightAndHeater();
            var cands = PlanCandidates.Build(dna, null);
            var r = ScenarioRunner.RunPlan(dna, DarkPlan(dna)).Result;
            var rows = FunctionMapView.ForResult(dna, cands, ResultCriteria.PlanOf(r), out _);
            Assert.IsTrue(rows.Single(x => x.Need == "足元を照らす").CoveredHere);
            var heat = rows.Single(x => x.Need == "背中を温める");
            Assert.IsFalse(heat.CoveredHere);
            Assert.AreEqual("寒い場所で座って過ごす", heat.PlanTitle, "別の実験で確かめる");
            Assert.IsFalse(rows.Single(x => x.Need == "雨でも壊れない").CoveredHere);
        }
    }
}
