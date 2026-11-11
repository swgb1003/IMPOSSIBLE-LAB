using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImpossibleLab.Core;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>6週目 S04：実験計画の候補・編集・実行計画の作り方、組み立て式の実験の保存と読み込み。</summary>
    public class PlanEditorTests
    {
        static InventionDna Sample(string file)
        {
            var r = DnaJson.Parse(Resources.Load<TextAsset>("ImpossibleLabSamples/" + file).text);
            Assert.IsTrue(r.IsValid, r.Summary);
            return r.Dna;
        }

        static InventionDna Dna(string mount, string parts, string behaviors, string tags = "\"daily\"")
        {
            string json = $@"{{ ""schemaVersion"": 2, ""id"": ""t-plan"", ""name"": ""テスト"", ""purpose"": ""テスト"", ""tags"": [{tags}], ""mount"": ""{mount}"",
  ""parts"": [ {{ ""id"": ""body"", ""type"": ""body"", ""position"": [0, 0, 0], ""scale"": [0.2, 0.2, 0.2] }}{(parts.Length > 0 ? ", " + parts : "")} ],
  ""behaviors"": [ {behaviors} ], ""revision"": 1, ""parentRevision"": null }}";
            var r = DnaJson.Parse(json);
            Assert.IsTrue(r.IsValid, r.Summary);
            return r.Dna;
        }

        static string Part(string id, string type) => $@"{{ ""id"": ""{id}"", ""type"": ""{type}"", ""position"": [0, 0.1, 0], ""scale"": [0.05, 0.05, 0.05] }}";
        static string Beh(string type, string target, string pars) => $@"{{ ""type"": ""{type}"", ""targetPart"": ""{target}"", ""parameters"": {{ {pars} }} }}";

        static InventionDna NightLight() => Dna("back", Part("lamp", "light"),
            Beh("illuminate", "lamp", @"""lumens"": 150, ""beamAngleDeg"": 60, ""tiltDeg"": 50") + ", " + Beh("alert", "lamp", @"""loudnessDb"": 0, ""lightLumens"": 30, ""flashHz"": 2"), "\"lighting\"");
        static InventionDna Heater() => Dna("back", Part("pad", "heater"), Beh("heat", "pad", @"""powerW"": 10"), "\"thermal\"");
        static InventionDna Lifter() => Dna("ground", Part("arm", "arm"), Beh("lift_load", "arm", @"""maxLoadKg"": 10, ""liftHeightM"": 1.2, ""speed"": 0.3"));
        static InventionDna Watcher() => Dna("head", Part("eye", "sensor"), Beh("detect", "eye", @"""rangeM"": 6, ""fovDeg"": 120"));

        static List<string> Errors(ExperimentPlan p, InventionDna dna)
        {
            var e = new List<string>();
            ExperimentPlanJson.Validate(p, dna, e);
            return e;
        }

        // ---------- 候補 ----------
        [Test]
        public void ExistingSamples_FirstCandidateIsTheirLab()
        {
            Assert.AreEqual(LabEnvironments.RainLab, PlanCandidates.LegacyEnvironment(PlanCandidates.Build(Sample("handsfree_rain"), null)[0].Plan));
            Assert.AreEqual(LabEnvironments.ObstacleCourse, PlanCandidates.LegacyEnvironment(PlanCandidates.Build(Sample("carrier_robot"), null)[0].Plan));
            Assert.AreEqual(LabEnvironments.FlightDeck, PlanCandidates.LegacyEnvironment(PlanCandidates.Build(Sample("flying_shoes"), null)[0].Plan));
            Assert.IsTrue(PlanCandidates.Build(Sample("handsfree_rain"), null)[0].Legacy);
        }

        [Test]
        public void BlockInventions_GetStandardPlans_ThatValidate()
        {
            foreach (var (dna, scenario) in new[] { (NightLight(), ExperimentCatalog.Stand), (Heater(), ExperimentCatalog.SitStill), (Lifter(), ExperimentCatalog.LiftTask), (Watcher(), ExperimentCatalog.Stand) })
            {
                var list = PlanCandidates.Build(dna, null);
                Assert.IsNotEmpty(list, dna.Behaviors[0].Type);
                Assert.AreEqual(scenario, list[0].Plan.Scenario);
                Assert.IsFalse(list[0].Legacy);
                foreach (var c in list) Assert.IsEmpty(Errors(c.Plan, dna), c.Title);
            }
        }

        [Test]
        public void NoMeasurableFunction_NoCandidates()
        {
            Assert.IsEmpty(PlanCandidates.Build(Dna("hand", "", ""), null));
        }

        [Test]
        public void AiPlans_ComeFirst_InvalidOnesAreReported_SameShapeStandardIsDropped()
        {
            var dna = NightLight();
            var good = new ExperimentPlan { Scenario = ExperimentCatalog.Stand, Mode = LabModes.Dream };
            good.Modules.Add(new PlanModule { Type = "darkness", Params = { ["lux"] = 0.2f } });
            good.Criteria.Add(new PlanCriterion { Metric = "footIlluminance", Op = ">=", Value = 20f, Reason = "AI の目安" });
            var bad = new ExperimentPlan { Scenario = ExperimentCatalog.SitStill };
            bad.Modules.Add(new PlanModule { Type = "temperature" });
            bad.Criteria.Add(new PlanCriterion { Metric = "timeToTarget", Op = "<=", Value = 10f }); // 加温・冷却がない
            var rejected = new List<string>();
            var list = PlanCandidates.Build(dna, new[] { new ProposedPlan { Title = "真っ暗な夜道", Plan = good }, new ProposedPlan { Title = "温める", Plan = bad } }, rejected);
            Assert.IsTrue(list[0].FromAi);
            Assert.AreEqual("真っ暗な夜道", list[0].Title);
            Assert.AreEqual(1, rejected.Count);
            StringAssert.Contains("温める", rejected[0]);
            Assert.IsFalse(list.Skip(1).Any(c => PlanCandidates.SameShape(c.Plan, good)), "同じ組み合わせの標準の計画は省く");
            // 候補は元の AI 案と別のオブジェクト（編集しても AI 案は変わらない）
            Assert.AreNotSame(good, list[0].Plan);
        }

        // ---------- 値の増減 ----------
        [Test]
        public void StepParam_UsesStepsLevelsWrapAndClamp()
        {
            var p = new ExperimentPlan { Scenario = ExperimentCatalog.Stand };
            p.Modules.Add(new PlanModule { Type = "darkness", Params = { ["lux"] = 2f } });
            p.Modules.Add(new PlanModule { Type = "sunlight", Params = { ["azimuthDeg"] = 315f, ["elevationDeg"] = 90f } });
            p.Modules.Add(new PlanModule { Type = "targets", Params = { ["count"] = 6f } });

            Assert.IsTrue(PlanEditor.StepParam(p, "darkness", "lux", +1)); Assert.AreEqual(5f, p.Get("darkness", "lux"));
            Assert.IsTrue(PlanEditor.StepParam(p, "darkness", "lux", -1)); Assert.AreEqual(2f, p.Get("darkness", "lux"));
            p.Module("darkness").Params["lux"] = 3f; // 段階の間 → その向きの隣の段階
            Assert.IsTrue(PlanEditor.StepParam(p, "darkness", "lux", -1)); Assert.AreEqual(2f, p.Get("darkness", "lux"));

            Assert.IsTrue(PlanEditor.StepParam(p, "sunlight", "azimuthDeg", +1)); Assert.AreEqual(0f, p.Get("sunlight", "azimuthDeg"), 1e-4f, "方位は一周する");
            Assert.IsFalse(PlanEditor.StepParam(p, "sunlight", "elevationDeg", +1), "範囲の端では変わらない");
            Assert.IsTrue(PlanEditor.StepParam(p, "targets", "count", -1)); Assert.AreEqual(5f, p.Get("targets", "count"));
            Assert.IsFalse(PlanEditor.StepParam(p, "rain", "intensity", +1), "計画にないモジュールは変えない");
            Assert.IsEmpty(Errors(p, null));
        }

        [Test]
        public void StepSize_IsNiceWhenNotGiven()
        {
            Assert.AreEqual(5f, PlanEditor.StepSize(new ModuleParam { Min = 5, Max = 80 }), 1e-5f);
            Assert.AreEqual(0.05f, PlanEditor.StepSize(new ModuleParam { Min = 0, Max = 1 }), 1e-5f);
            Assert.AreEqual(1f, PlanEditor.StepSize(new ModuleParam { Min = 0, Max = 10, Integer = true }), 1e-5f);
        }

        // ---------- 追加・削除 ----------
        [Test]
        public void Addable_OnlyModulesThatChangeSomething()
        {
            var light = PlanCandidates.Build(NightLight(), null)[0].Plan;
            var add = PlanEditor.Addable(light, NightLight());
            CollectionAssert.DoesNotContain(add, "rain");
            CollectionAssert.DoesNotContain(add, "wind", "stand で風が効くのは雨だけ");
            CollectionAssert.DoesNotContain(add, "targets", "検知の機能がない");
            CollectionAssert.Contains(add, "sunlight", "体が受ける日射は機能なしでも測れる");

            var watch = PlanCandidates.Build(Watcher(), null)[0].Plan;
            CollectionAssert.Contains(PlanEditor.Addable(PlanEditor.Clone(watch).Also(x => x.Modules.RemoveAll(m => m.Type == "obstacles")), Watcher()), "obstacles");

            var rain = PlanCandidates.Build(Sample("handsfree_rain"), null)[0].Plan;
            Assert.IsEmpty(PlanEditor.Addable(rain, Sample("handsfree_rain")), "既存の実験場は値の変更のみ");
        }

        [Test]
        public void AddAndRemove_RespectRequiredModules()
        {
            var dna = NightLight();
            var p = PlanEditor.Clone(PlanCandidates.Build(dna, null)[0].Plan);
            Assert.IsTrue(PlanEditor.AddModule(p, "sunlight", dna));
            Assert.AreEqual(800f, p.Get("sunlight", "irradiance"), "追加したモジュールは初期値で入る");
            Assert.IsFalse(PlanEditor.AddModule(p, "sunlight", dna), "重複して追加しない");
            Assert.IsEmpty(Errors(p, dna));
            Assert.IsTrue(PlanEditor.RemoveModule(p, "darkness"));
            Assert.IsFalse(p.Has("darkness"));

            var heat = PlanCandidates.Build(Heater(), null)[0].Plan;
            Assert.IsFalse(PlanEditor.CanRemove(heat, "temperature"), "座って過ごす実験に気温は必須");
            Assert.IsFalse(PlanEditor.RemoveModule(heat, "temperature"));
        }

        // ---------- 実行する計画 ----------
        [Test]
        public void Effective_DropsMetricsTheModeCannotMeasure_AndKeepsOriginal()
        {
            var dna = NightLight();
            var p = PlanCandidates.Build(dna, null)[0].Plan;
            p.Metrics.AddRange(new[] { "footIlluminance", "blockPowerW" });
            p.Criteria.Add(new PlanCriterion { Metric = "blockPowerW", Op = "<=", Value = 5f });
            var removed = new List<string>();
            var eff = PlanEditor.Effective(p, LabModes.Dream, 77, dna, removed);
            Assert.AreEqual(77, eff.Seed);
            CollectionAssert.AreEqual(new[] { "footIlluminance" }, eff.Metrics);
            Assert.IsEmpty(eff.Criteria);
            Assert.AreEqual(1, removed.Count, string.Join(" / ", removed));
            StringAssert.Contains("REALITY", removed[0]);
            Assert.IsEmpty(Errors(eff, dna));
            Assert.AreEqual(2, p.Metrics.Count, "元の計画は変えない");

            var real = PlanEditor.Effective(p, LabModes.Reality, 77, dna);
            Assert.AreEqual(2, real.Metrics.Count);
            Assert.IsEmpty(Errors(real, dna));
        }

        [Test]
        public void EffectiveCriteria_FallsBackToMeasurableDefaults()
        {
            var dna = NightLight();
            var p = PlanCandidates.Build(dna, null)[0].Plan;
            var crit = PlanEditor.EffectiveCriteria(p, dna).Select(c => c.Metric).ToList();
            CollectionAssert.AreEquivalent(new[] { "footIlluminance", "alertRange" }, crit);
            // 実行結果の合否も同じ条件で決まる
            var run = ScenarioRunner.RunPlan(dna, PlanEditor.Effective(p, LabModes.Dream, 1, dna));
            StringAssert.Contains("足元の照度", run.Result.Criteria);
            StringAssert.Contains("通知の届く距離", run.Result.Criteria);
        }

        // ---------- 保存・読み込み・比較 ----------
        [Test]
        public void ScenarioLabResult_SavesPlan_AndLoadsComparable()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ilab-plan-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var dna = Heater();
                var plan = PlanEditor.Effective(PlanCandidates.Build(dna, null)[0].Plan, LabModes.Reality, 42, dna);
                var run = ScenarioRunner.RunPlan(dna, plan, "exp-1", "2026-11-11T10:00:00");
                Assert.IsTrue(run.Result.IsScenarioLab);
                var repo = new InventionRepository(dir);
                repo.Save(new SavedInvention { Current = dna, Experiments = { run.Result } });
                var loaded = repo.Load(dna.Id, out var err);
                Assert.IsNotNull(loaded, err);
                var r = loaded.Experiments.Single();
                Assert.IsTrue(r.IsScenarioLab);
                Assert.AreEqual(ExperimentCatalog.SitStill, r.Scenario);
                Assert.AreEqual(PlanCandidates.ConditionKey(plan), PlanCandidates.ConditionKey(r.Plan));
                Assert.AreEqual(LabModes.Reality, r.Plan.Mode);
                Assert.AreEqual(run.Result.Metric("timeToTarget").Value, r.Metric("timeToTarget").Value, 1e-3f);
                Assert.AreEqual(run.Result.Passed, r.Passed);
                Assert.IsTrue(r.SameConditionsAs(run.Result, compareMode: true));

                // 別のモード・別の値なら別の条件
                var dream = ScenarioRunner.RunPlan(dna, PlanEditor.Effective(plan, LabModes.Dream, 42, dna));
                Assert.IsTrue(dream.Result.SameConditionsAs(r, compareMode: false));
                Assert.IsFalse(dream.Result.SameConditionsAs(r, compareMode: true));
                var colder = PlanEditor.Clone(plan);
                PlanEditor.StepParam(colder, "temperature", "ambientC", -1);
                Assert.IsFalse(ScenarioRunner.RunPlan(dna, colder).Result.SameConditionsAs(r, compareMode: true));
                StringAssert.Contains("気温 5", r.DescribeConditions());
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test]
        public void ScenarioLabResult_WithBrokenPlan_IsSkippedOnLoad()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ilab-plan-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var dna = Heater();
                var run = ScenarioRunner.RunPlan(dna, PlanCandidates.Build(dna, null)[0].Plan);
                var repo = new InventionRepository(dir);
                repo.Save(new SavedInvention { Current = dna, Experiments = { run.Result } });
                string path = Path.Combine(dir, "inventions", dna.Id + ".json");
                File.WriteAllText(path, File.ReadAllText(path).Replace("\"sit_still\"", "\"teleport\""));
                var loaded = repo.Load(dna.Id, out var err);
                Assert.IsNotNull(loaded, err);
                Assert.IsEmpty(loaded.Experiments, "検証を通らない計画の実験記録は読み込まない");
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test]
        public void Hints_ForScenarioLab_UseLimits()
        {
            var dna = Heater();
            var p = PlanCandidates.Build(dna, null)[0].Plan;
            p.Module("temperature").Params["targetDeltaC"] = 20f; // 10 W では届かない
            var r = ScenarioRunner.RunPlan(dna, p).Result;
            Assert.IsFalse(r.Success);
            var h = CourseSimulator.Hints(r);
            Assert.IsTrue(h.Any(x => x.Contains("届かない")), string.Join(" / ", h));
        }
    }

    static class TestExt
    {
        public static T Also<T>(this T x, System.Action<T> f) { f(x); return x; }
    }
}
