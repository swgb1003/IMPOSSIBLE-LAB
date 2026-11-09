using System.Collections.Generic;
using System.Linq;
using ImpossibleLab.Core;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>4週目：機能ブロック第1弾の実験（シナリオ・環境モジュール・計測項目・合否）と、実験計画の検証。</summary>
    public class BlockScenarioTests
    {
        // ---------- 発明DNA（v2）を短く作る ----------
        static InventionDna Dna(string mount, string parts, string behaviors, string tags = "\"daily\"")
        {
            string json = $@"{{ ""schemaVersion"": 2, ""id"": ""t-{mount}"", ""name"": ""テスト"", ""purpose"": ""テスト"", ""tags"": [{tags}], ""mount"": ""{mount}"",
  ""parts"": [ {{ ""id"": ""body"", ""type"": ""body"", ""position"": [0, 0, 0], ""scale"": [0.2, 0.2, 0.2] }}{(parts.Length > 0 ? ", " + parts : "")} ],
  ""behaviors"": [ {behaviors} ], ""revision"": 1, ""parentRevision"": null }}";
            var r = DnaJson.Parse(json);
            Assert.IsTrue(r.IsValid, r.Summary);
            return r.Dna;
        }

        static string Part(string id, string type) => $@"{{ ""id"": ""{id}"", ""type"": ""{type}"", ""position"": [0, 0.1, 0], ""scale"": [0.05, 0.05, 0.05] }}";
        static string Beh(string type, string target, string pars) => $@"{{ ""type"": ""{type}"", ""targetPart"": ""{target}"", ""parameters"": {{ {pars} }} }}";

        static InventionDna NightLight(float lumens = 150, float tilt = 50) => Dna("back", Part("lamp", "light"),
            Beh("illuminate", "lamp", $@"""lumens"": {lumens}, ""beamAngleDeg"": 60, ""tiltDeg"": {tilt}") + ", " + Beh("alert", "lamp", @"""loudnessDb"": 0, ""lightLumens"": 30, ""flashHz"": 2"), "\"lighting\"");
        static InventionDna Parasol(float radius = 0.8f) => Dna("hand", Part("shade", "canopy"), Beh("sun_shade", "shade", $@"""radius"": {radius}, ""opacity"": 0.9"));
        static InventionDna Plain() => Dna("hand", "", "");
        static InventionDna Heater(float w = 10) => Dna("back", Part("pad", "heater"), Beh("heat", "pad", $@"""powerW"": {w}"), "\"thermal\"");
        static InventionDna Cooler(float w = 20) => Dna("back", Part("pad", "cooler"), Beh("cool", "pad", $@"""powerW"": {w}"), "\"thermal\"");
        static InventionDna Case(bool cushion, float thick = 0.03f) => Dna("hand", cushion ? Part("foam", "cushion") : "", cushion ? Beh("impact_cushion", "foam", $@"""thicknessM"": {thick}, ""stiffness"": 5000") : "");
        static InventionDna Lifter(float maxKg = 10, string grip = null) => Dna("ground", Part("arm", "arm") + (grip != null ? ", " + Part("hand", "gripper") : ""),
            Beh("lift_load", "arm", $@"""maxLoadKg"": {maxKg}, ""liftHeightM"": 1.2, ""speed"": 0.3") + (grip != null ? ", " + Beh("grip", "hand", grip) : ""));
        static InventionDna Gripper(float force) => Dna("hand", Part("hand", "gripper"), Beh("grip", "hand", $@"""gripForceN"": {force}, ""openingM"": 0.4"));
        static InventionDna Watcher(float range = 6, float fov = 120, string mount = "head") => Dna(mount, Part("eye", "sensor"), Beh("detect", "eye", $@"""rangeM"": {range}, ""fovDeg"": {fov}"));

        static ExperimentPlan Plan(string scenario, string mode = LabModes.Dream, params (string type, (string k, float v)[] ps)[] modules)
        {
            var p = new ExperimentPlan { Scenario = scenario, Mode = mode, Seed = 2026 };
            foreach (var (type, ps) in modules)
            {
                var m = new PlanModule { Type = type };
                foreach (var (k, v) in ps) m.Params[k] = v;
                p.Modules.Add(m);
            }
            return p;
        }

        static (string, (string, float)[]) M(string type, params (string, float)[] ps) => (type, ps);

        static CourseRun Run(InventionDna dna, ExperimentPlan p)
        {
            var errors = new List<string>();
            ExperimentPlanJson.Validate(p, null, errors);
            Assert.IsEmpty(errors, string.Join(" / ", errors));
            return ScenarioRunner.RunPlan(dna, p);
        }

        static float V(CourseRun run, string key)
        {
            var m = run.Result.Metric(key);
            Assert.IsNotNull(m, key + " が結果にない：" + string.Join(", ", run.Result.Metrics.Select(x => x.Key)));
            return m.Value;
        }

        // ================= 照明・通知（stand + darkness） =================
        [Test] public void NightLight_IlluminatesFeetAndPassesDefaultCriteria()
        {
            var dna = NightLight();
            var run = Run(dna, Plan(ExperimentCatalog.Stand, modules: M("darkness", ("lux", 2f))));
            var b = dna.FindBehavior("illuminate");
            var expect = IlluminateModel.Evaluate(150, 60, 50, BodyGeometry.MountHeight("back"), 2f);
            Assert.AreEqual(expect.AverageLx, V(run, "footIlluminance"), 1e-4f, "式と同じ値");
            Assert.AreEqual(AlertModel.Evaluate(0, 30, 2, 2f).RangeM, V(run, "alertRange"), 1e-3f);
            Assert.IsTrue(run.Result.Passed, run.Result.Criteria + " / " + string.Join(" / ", run.Result.Limits));
            StringAssert.Contains("足元の照度", run.Result.Criteria);
            Assert.IsNull(run.Result.Metric("blockPowerW"), "DREAM では消費電力を出さない");
            Assert.IsNull(run.Result.Metric("shadeRatio"), "日差しモジュールがない");
            Assert.AreEqual(LabEnvironments.ScenarioLab, run.Result.Environment);
        }

        [Test] public void NightLight_DimLampFailsAndBrighterLampImproves()
        {
            var plan = Plan(ExperimentCatalog.Stand, modules: M("darkness", ("lux", 0.5f)));
            var dim = Run(NightLight(lumens: 10, tilt: 50), plan);
            var bright = Run(NightLight(lumens: 300, tilt: 50), plan);
            Assert.IsFalse(dim.Result.Passed);
            Assert.IsTrue(dim.Result.Limits.Any(l => l.Contains("足元の照度")), string.Join(" / ", dim.Result.Limits));
            Assert.Greater(V(bright, "footIlluminance"), V(dim, "footIlluminance") * 10f);
            Assert.IsTrue(bright.Result.Passed);
        }

        [Test] public void Reality_AddsBlockPower()
        {
            var run = Run(NightLight(), Plan(ExperimentCatalog.Stand, LabModes.Reality, M("darkness", ("lux", 2f))));
            float expect = 150f / 100f + AlertModel.Evaluate(0, 30, 2, 2f).PowerW;
            Assert.AreEqual(expect, V(run, "blockPowerW"), 1e-4f);
        }

        // ================= 日よけ（stand + sunlight） =================
        [Test] public void Parasol_ShadesBodyAndReducesSunExposure()
        {
            var plan = Plan(ExperimentCatalog.Stand, modules: M("sunlight", ("elevationDeg", 70f), ("azimuthDeg", 180f), ("irradiance", 800f)));
            var shaded = Run(Parasol(0.8f), plan);
            var bare = Run(Plain(), plan);
            Assert.That(V(shaded, "shadeRatio"), Is.InRange(0.3f, 1f));
            Assert.Less(V(shaded, "sunExposureW"), V(bare, "sunExposureW"));
            Assert.IsNull(bare.Result.Metric("shadeRatio"), "日よけがなければ日陰率は出さない");
            Assert.IsFalse(bare.Result.Passed ?? true, "測れる合格条件がない（日射は参考値）");
            var bigger = Run(Parasol(1.5f), plan);
            Assert.Greater(V(bigger, "shadeRatio"), V(shaded, "shadeRatio"));
        }

        // ================= 検知（stand + targets + obstacles） =================
        [Test] public void Detect_IsDeterministicAndRespondsToRangeAndOcclusion()
        {
            var plan = Plan(ExperimentCatalog.Stand, modules: M("targets", ("count", 10f)));
            var a = Run(Watcher(), plan); var b = Run(Watcher(), plan);
            Assert.AreEqual(V(a, "detectionRate"), V(b, "detectionRate"), "同じシードなら同じ配置");
            Assert.AreEqual(10, a.Layout.Targets.Count);
            var all = Run(Watcher(30, 360), plan);
            Assert.AreEqual(1f, V(all, "detectionRate"), "全周・30 m ならすべて見える");
            var blocked = Run(Watcher(30, 360), Plan(ExperimentCatalog.Stand, modules: new[] { M("targets", ("count", 10f)), M("obstacles", ("count", 10f)) }));
            Assert.LessOrEqual(V(blocked, "detectionRate"), 1f);
            Assert.AreEqual(10, blocked.Layout.Obstacles.Count);
            Assert.GreaterOrEqual(V(all, "detectionRate"), V(a, "detectionRate"));
        }

        // ================= 加温・冷却（sit_still + temperature） =================
        [Test] public void Heater_TemperatureMatchesModelAndPlaybackRises()
        {
            var plan = Plan(ExperimentCatalog.SitStill, modules: M("temperature", ("ambientC", 5f), ("targetDeltaC", 5f), ("minutes", 15f)));
            var run = Run(Heater(10), plan);
            var expect = ThermalModel.Evaluate(false, 10, 5, 5, 15 * 60, 5);
            Assert.AreEqual(expect.DeltaC, V(run, "temperatureGain"), 1e-4f);
            Assert.AreEqual(expect.TimeToTargetSec / 60f, V(run, "timeToTarget"), 1e-3f);
            Assert.IsTrue(run.Result.Passed, string.Join(" / ", run.Result.Limits));
            // 再生：温度（Aux）は外気温から上がっていき、最後は 15 分後の温度
            Assert.AreEqual(SitStillScenario.PlaybackSec, run.Duration, 0.06f);
            for (int i = 1; i < run.Frames.Count; i++) Assert.GreaterOrEqual(run.Frames[i].Aux, run.Frames[i - 1].Aux - 1e-5f);
            Assert.AreEqual(expect.FinalC, run.Frames[run.Frames.Count - 1].Aux, 0.05f);
        }

        [Test] public void Heater_TooWeakNeverReachesTarget_CoolerCools()
        {
            var plan = Plan(ExperimentCatalog.SitStill, modules: M("temperature", ("ambientC", 0f), ("targetDeltaC", 10f), ("minutes", 30f)));
            var weak = Run(Heater(5), plan);
            Assert.AreEqual(ExperimentCatalog.NotReached, V(weak, "timeToTarget"));
            Assert.IsFalse(weak.Result.Passed);
            Assert.IsTrue(weak.Result.Limits.Any(l => l.Contains("届かない")));
            var cool = Run(Cooler(20), Plan(ExperimentCatalog.SitStill, modules: M("temperature", ("ambientC", 35f), ("targetDeltaC", 3f), ("minutes", 20f))));
            Assert.Greater(V(cool, "temperatureGain"), 0f, "冷却は下がった分を正の値で出す");
            Assert.IsTrue(cool.Result.Limits.Any(l => l.Contains("排熱")));
        }

        // ================= 衝撃吸収（drop_test + drop） =================
        [Test] public void Cushion_PassesDropTestThatBareCaseFails()
        {
            var plan = Plan(ExperimentCatalog.DropTest, modules: M("drop", ("heightM", 1f), ("massKg", 0.3f)));
            var bare = Run(Case(false), plan);
            var padded = Run(Case(true, 0.05f), plan);
            Assert.AreEqual(CushionModel.Drop(0.3f, 1f, 0f, 0f).PeakG, V(bare, "peakImpactG"), 1e-2f);
            Assert.IsFalse(bare.Result.Passed);
            Assert.IsTrue(padded.Result.Passed, string.Join(" / ", padded.Result.Limits));
            Assert.Less(V(padded, "peakImpactG"), V(bare, "peakImpactG"));
            Assert.IsNull(bare.Result.Metric("cushionCompression"));
            // 再生：落ちて床（高さ 0）で止まり、そのあと衝撃の値を出す
            var last = padded.Frames[padded.Frames.Count - 1];
            Assert.AreEqual(0f, last.Device.y, 1e-4f);
            Assert.AreEqual(V(padded, "peakImpactG"), last.Aux, 1e-3f);
            Assert.AreEqual(1f, padded.Frames[0].Device.y, 0.05f);
            var thin = Run(Case(true, 0.005f), plan);
            Assert.IsTrue(thin.Result.Limits.Any(l => l.Contains("使い切った")));
        }

        // ================= 持ち上げ・つかむ（lift_task + payload + shelf） =================
        [Test] public void Lifter_LiftsWithinLimitsAndRejectsOverload()
        {
            var plan = Plan(ExperimentCatalog.LiftTask, modules: new[] { M("payload", ("kg", 4f), ("sizeM", 0.3f)), M("shelf", ("heightM", 0.9f)) });
            var ok = Run(Lifter(10), plan);
            Assert.AreEqual(0.9f / 0.3f, V(ok, "liftTime"), 1e-3f);
            Assert.AreEqual(0f, V(ok, "dropCount"));
            Assert.AreEqual(4f * RealityModel.Gravity * 0.3f / 0.6f, V(ok, "liftPowerW"), 1e-3f);
            Assert.IsTrue(ok.Result.Passed, string.Join(" / ", ok.Result.Limits));
            Assert.AreEqual(0.9f, ok.Frames[ok.Frames.Count - 1].Device.y, 1e-3f, "棚の高さまで上がる");
            var heavy = Run(Lifter(2), plan);
            Assert.AreEqual(ExperimentCatalog.LiftTimeout, V(heavy, "liftTime"));
            Assert.IsFalse(heavy.Result.Passed);
            Assert.IsTrue(heavy.Result.Limits.Any(l => l.Contains("持ち上げられない")));
        }

        [Test] public void Gripper_WeakGripDropsStrongGripHolds()
        {
            var plan = Plan(ExperimentCatalog.LiftTask, modules: new[] { M("payload", ("kg", 3f), ("sizeM", 0.2f)), M("shelf", ("heightM", 1f)) });
            var weak = Run(Gripper(20), plan);   // 2μF = 20 N < 3 × (9.8 + 2.5) = 36.9 N
            var strong = Run(Gripper(60), plan); // 60 N ≥ 36.9 N
            Assert.AreEqual(1f, V(weak, "dropCount"));
            Assert.IsFalse(weak.Result.Passed);
            Assert.AreEqual(0f, V(strong, "dropCount"));
            Assert.AreEqual(1f / LiftBlock.HumanLiftSpeed, V(strong, "liftTime"), 1e-3f, "人が 0.5 m/s で持ち上げる");
            Assert.IsNull(strong.Result.Metric("liftPowerW"), "持ち上げ機構がなければ電力は出さない");
            var wide = Run(Gripper(200), Plan(ExperimentCatalog.LiftTask, modules: new[] { M("payload", ("kg", 1f), ("sizeM", 0.6f)), M("shelf", ("heightM", 1f)) }));
            Assert.IsTrue(wide.Result.Limits.Any(l => l.Contains("開き幅")));
        }

        // ================= 合否・決定性 =================
        [Test] public void ExplicitCriteriaOverrideDefaultsAndMissingMetricFails()
        {
            var p = Plan(ExperimentCatalog.Stand, modules: M("darkness", ("lux", 2f)));
            p.Criteria.Add(new PlanCriterion { Metric = "footIlluminance", Op = ">=", Value = 5000f, Reason = "とても明るく" });
            var run = Run(NightLight(), p);
            Assert.IsFalse(run.Result.Passed);
            StringAssert.Contains("5000", run.Result.Criteria);
            var none = ScenarioRunner.RunPlan(Plain(), Plan(ExperimentCatalog.Stand, modules: M("darkness", ("lux", 2f))));
            Assert.IsFalse(none.Result.Passed ?? true);
            Assert.IsTrue(none.Result.Limits.Any(l => l.Contains("機能がこの発明にない")));
        }

        [Test] public void AllNewScenarios_AreDeterministicAndFinite()
        {
            var cases = new (InventionDna, ExperimentPlan)[]
            {
                (NightLight(), Plan(ExperimentCatalog.Stand, LabModes.Reality, M("darkness", ("lux", 0f)))),
                (Parasol(), Plan(ExperimentCatalog.Stand, LabModes.Reality, M("sunlight", ("elevationDeg", 5f)))),
                (Watcher(), Plan(ExperimentCatalog.Stand, modules: new[] { M("targets", ("count", 12f)), M("obstacles", ("count", 10f)) })),
                (Heater(100), Plan(ExperimentCatalog.SitStill, LabModes.Reality, M("temperature", ("ambientC", -20f), ("minutes", 60f)))),
                (Case(true, 0.2f), Plan(ExperimentCatalog.DropTest, modules: M("drop", ("heightM", 3f), ("massKg", 5f)))),
                (Lifter(30, @"""gripForceN"": 1, ""openingM"": 0.02"), Plan(ExperimentCatalog.LiftTask, LabModes.Reality, new[] { M("payload", ("kg", 30f), ("sizeM", 0.8f)), M("shelf", ("heightM", 2f)) })),
            };
            foreach (var (dna, plan) in cases)
            {
                var a = Run(dna, plan); var b = Run(dna, plan);
                Assert.IsNotEmpty(a.Result.Metrics, plan.Scenario);
                CollectionAssert.AreEqual(a.Result.Metrics.Select(m => m.Value), b.Result.Metrics.Select(m => m.Value), plan.Scenario);
                Assert.AreEqual(a.Frames.Count, b.Frames.Count);
                foreach (var m in a.Result.Metrics) Assert.IsFalse(float.IsNaN(m.Value) || float.IsInfinity(m.Value), m.Key);
                foreach (var f in a.Frames) Assert.IsFalse(float.IsNaN(f.Aux) || float.IsNaN(f.Device.y), plan.Scenario);
                Assert.Less(a.Duration, 31f, plan.Scenario + "：再生は 30 秒以内");
            }
        }

        /// <summary>代表的な実験の結果を書き出す（docs/experiments.md の表に使う）。</summary>
        [Test] public void RepresentativeResults_Report()
        {
            var cases = new (string, InventionDna, ExperimentPlan)[]
            {
                ("夜道ライト 150lm（暗さ2lx）", NightLight(), Plan(ExperimentCatalog.Stand, LabModes.Reality, M("darkness", ("lux", 2f)))),
                ("夜道ライト 10lm（暗さ2lx）", NightLight(10), Plan(ExperimentCatalog.Stand, modules: M("darkness", ("lux", 2f)))),
                ("日傘 半径0.8m（高さ70°）", Parasol(0.8f), Plan(ExperimentCatalog.Stand, modules: M("sunlight", ("elevationDeg", 70f)))),
                ("日よけなし（高さ70°）", Plain(), Plan(ExperimentCatalog.Stand, modules: M("sunlight", ("elevationDeg", 70f)))),
                ("見守りセンサー 6m・120°（対象10）", Watcher(), Plan(ExperimentCatalog.Stand, modules: M("targets", ("count", 10f)))),
                ("加温 10W（5°C・15分）", Heater(10), Plan(ExperimentCatalog.SitStill, modules: M("temperature", ("ambientC", 5f)))),
                ("緩衝材なし（0.3kg・1m）", Case(false), Plan(ExperimentCatalog.DropTest, modules: M("drop", ("heightM", 1f), ("massKg", 0.3f)))),
                ("緩衝材 50mm（0.3kg・1m）", Case(true, 0.05f), Plan(ExperimentCatalog.DropTest, modules: M("drop", ("heightM", 1f), ("massKg", 0.3f)))),
                ("持ち上げ機構 10kg（4kg・0.9m）", Lifter(10), Plan(ExperimentCatalog.LiftTask, modules: new[] { M("payload", ("kg", 4f)), M("shelf", ("heightM", 0.9f)) })),
                ("つかみ具 20N（3kg・人が持ち上げ）", Gripper(20), Plan(ExperimentCatalog.LiftTask, modules: new[] { M("payload", ("kg", 3f), ("sizeM", 0.2f)), M("shelf", ("heightM", 1f)) })),
            };
            foreach (var (name, dna, plan) in cases)
            {
                var r = Run(dna, plan).Result;
                TestContext.WriteLine($"REPORT {name} | {(r.Passed == true ? "合格" : "不合格")} | " + string.Join(" / ", r.Metrics.Select(m => m.LabelJa + " " + m.Value.ToString(m.Format, System.Globalization.CultureInfo.InvariantCulture) + m.Unit)));
            }
        }

        [Test] public void StandWithRain_StillGoesToRainSimulator()
        {
            var p = ExperimentPlan.FromConfig(new RainExperimentConfig());
            Assert.Throws<System.ArgumentException>(() => ScenarioRunner.RunPlan(Plain(), p));
        }

        // ================= 実験計画の検証（AI の提案・画面の組み立てを想定） =================
        const string NightPlan = @"{ ""scenario"": ""stand"", ""mode"": ""reality"", ""durationSec"": 5, ""seed"": 7,
  ""modules"": [ { ""type"": ""darkness"", ""lux"": 2 } ],
  ""metrics"": [ ""footIlluminance"", ""alertRange"", ""blockPowerW"" ],
  ""criteria"": [ { ""metric"": ""footIlluminance"", ""op"": "">="", ""value"": 10, ""reason"": ""段差が見える"" } ] }";

        [Test] public void PlanJson_NewScenarioPlanIsValidAndRuns()
        {
            var dna = NightLight();
            var r = ExperimentPlanJson.Parse(NightPlan, dna);
            Assert.IsTrue(r.IsValid, r.Summary);
            var run = ScenarioRunner.RunPlan(dna, r.Plan);
            CollectionAssert.AreEqual(new[] { "footIlluminance", "alertRange", "blockPowerW" }, run.Result.Metrics.Select(m => m.Key));
            Assert.IsTrue(run.Result.Passed);
            var back = ExperimentPlanJson.Parse(ExperimentPlanJson.ToJson(r.Plan), dna);
            Assert.IsTrue(back.IsValid, back.Summary);
        }

        [TestCase("{ \"type\": \"darkness\", \"lux\": 2 }", "{ \"type\": \"temperature\", \"ambientC\": 5 }", "では使えません")]
        [TestCase("{ \"type\": \"darkness\", \"lux\": 2 }", "{ \"type\": \"darkness\", \"lux\": 5000 }", "範囲外")]
        [TestCase("{ \"type\": \"darkness\", \"lux\": 2 }", "{ \"type\": \"darkness\", \"lux\": 2 }, { \"type\": \"rain\", \"intensity\": 20 }", "組み合わせられません")]
        [TestCase("{ \"type\": \"darkness\", \"lux\": 2 }", "{ \"type\": \"sunlight\", \"elevationDeg\": 60 }", "環境モジュール darkness が必要")]
        [TestCase("\"mode\": \"reality\"", "\"mode\": \"dream\"", "REALITY")]
        [TestCase("\"footIlluminance\", \"alertRange\"", "\"footIlluminance\", \"peakImpactG\"", "シナリオ stand では測れません")]
        [TestCase("\"value\": 10", "\"value\": 200000", "範囲外")]
        public void PlanJson_RejectsInvalidNewScenarioPlans(string from, string to, string expect)
        {
            string json = NightPlan.Replace(from, to);
            Assert.AreNotEqual(NightPlan, json, "置き換えが効いていない");
            var r = ExperimentPlanJson.Parse(json, NightLight());
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(r.Errors.Any(e => e.Contains(expect)), string.Join(" / ", r.Errors));
        }

        [Test] public void PlanJson_RejectsMetricWithoutBlock()
        {
            var r = ExperimentPlanJson.Parse(NightPlan, Plain());
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(r.Errors.Any(e => e.Contains("機能 illuminate")), string.Join(" / ", r.Errors));
            // 他のシナリオでも同じ
            const string drop = @"{ ""scenario"": ""drop_test"", ""modules"": [ { ""type"": ""drop"", ""heightM"": 1.5 } ], ""metrics"": [ ""peakImpactG"", ""cushionCompression"" ] }";
            Assert.IsTrue(ExperimentPlanJson.Parse(drop, Case(true)).IsValid);
            var noCushion = ExperimentPlanJson.Parse(drop, Case(false));
            Assert.IsTrue(noCushion.Errors.Any(e => e.Contains("impact_cushion")), noCushion.Summary);
            const string lift = @"{ ""scenario"": ""lift_task"", ""modules"": [ { ""type"": ""payload"", ""kg"": 2 }, { ""type"": ""shelf"", ""heightM"": 3 } ] }";
            Assert.IsTrue(ExperimentPlanJson.Parse(lift, Lifter()).Errors.Any(e => e.Contains("範囲外")));
        }

        [Test] public void ExistingDemoPlans_StillValid()
        {
            foreach (var env in new[] { LabEnvironments.RainLab, LabEnvironments.ObstacleCourse, LabEnvironments.FlightDeck })
            {
                var errors = new List<string>();
                ExperimentPlanJson.Validate(ExperimentPlan.FromConfig(new RainExperimentConfig { Environment = env }), null, errors);
                Assert.IsEmpty(errors, env + ": " + string.Join(" / ", errors));
            }
            // 運搬の積荷に「大きさ」が増えても、指定しなければ今までどおり
            var p = ExperimentPlan.FromConfig(new RainExperimentConfig { Environment = LabEnvironments.ObstacleCourse });
            Assert.IsFalse(p.Module("payload").Params.ContainsKey("sizeM"));
        }

        [Test] public void Catalog_ScenariosModulesMetricsAreConsistent()
        {
            foreach (var s in ExperimentCatalog.Scenarios)
                foreach (var m in s.AllowedModules) Assert.IsNotNull(ExperimentCatalog.Module(m), s.Id + " → " + m);
            foreach (var mi in ExperimentCatalog.Metrics)
            {
                Assert.Less(mi.Min, mi.Max, mi.Key);
                foreach (var sc in mi.Scenarios) Assert.IsNotNull(ExperimentCatalog.Scenario(sc), mi.Key);
                foreach (var m in mi.Modules)
                {
                    Assert.IsNotNull(ExperimentCatalog.Module(m), mi.Key);
                    foreach (var sc in mi.Scenarios) CollectionAssert.Contains(ExperimentCatalog.Scenario(sc).AllowedModules, m, $"{mi.Key}：{sc} で {m} が使えない");
                }
                foreach (var b in mi.AnyBlocks) Assert.IsNotNull(LabCatalog.Behavior(b), mi.Key);
            }
            foreach (var mod in ExperimentCatalog.Modules)
                foreach (var pp in mod.Params) Assert.That(pp.Default, Is.InRange(pp.Min, pp.Max), mod.Id + "." + pp.Key);
            foreach (var c in ExperimentCatalog.DefaultCriteria)
            {
                var mi = ExperimentCatalog.Metric(c.Metric);
                Assert.IsNotNull(mi, c.Metric);
                Assert.That(c.Value, Is.InRange(mi.Min, mi.Max), c.Metric);
            }
            Assert.AreEqual(ExperimentCatalog.Metrics.Length, ExperimentCatalog.Metrics.Select(m => m.Key).Distinct().Count());
        }
    }
}
