using System.Collections.Generic;
using System.Linq;
using ImpossibleLab.Core;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>
    /// 2週目：実験計画（ExperimentPlan）と ScenarioRunner。
    /// 移す前の計算（LegacyCourseSimulator）と、結果・制限・前提・再生用の軌跡まですべて一致することを確かめる。
    /// </summary>
    public class ScenarioRunnerTests
    {
        static InventionDna Load(string file) => DnaJson.Parse(Resources.Load<TextAsset>("ImpossibleLabSamples/" + file).text).Dna;

        static void AssertSame(CourseRun a, CourseRun b, string label)
        {
            var ra = a.Result; var rb = b.Result;
            Assert.AreEqual(ra.Passed, rb.Passed, label + " passed");
            Assert.AreEqual(ra.Criteria, rb.Criteria, label + " criteria");
            CollectionAssert.AreEqual(ra.Limits, rb.Limits, label + " limits");
            CollectionAssert.AreEqual(ra.Assumptions, rb.Assumptions, label + " assumptions");
            Assert.AreEqual(ra.EnergyWh, rb.EnergyWh, label + " energy");
            Assert.AreEqual(ra.DriftM, rb.DriftM, label + " drift");
            Assert.AreEqual(ra.Metrics.Count, rb.Metrics.Count, label + " metric count");
            for (int i = 0; i < ra.Metrics.Count; i++)
            {
                var x = ra.Metrics[i]; var y = rb.Metrics[i];
                Assert.AreEqual(x.Key, y.Key, label);
                Assert.AreEqual(x.Value, y.Value, label + " " + x.Key);
                Assert.AreEqual(x.LabelJa, y.LabelJa, label); Assert.AreEqual(x.Unit, y.Unit, label); Assert.AreEqual(x.Format, y.Format, label);
                Assert.AreEqual(x.LowerIsBetter, y.LowerIsBetter, label); Assert.AreEqual(x.Primary, y.Primary, label);
            }
            Assert.AreEqual(a.Frames.Count, b.Frames.Count, label + " frames");
            for (int i = 0; i < a.Frames.Count; i++)
            {
                var f = a.Frames[i]; var g = b.Frames[i];
                Assert.IsTrue(f.Time == g.Time && f.Device == g.Device && f.YawDeg == g.YawDeg && f.Leader == g.Leader && f.Collision == g.Collision, $"{label} frame {i}");
            }
            Assert.AreEqual(a.Layout.Route.Count, b.Layout.Route.Count, label);
            CollectionAssert.AreEqual(a.Layout.Obstacles, b.Layout.Obstacles, label);
        }

        [Test] public void Carrier_MatchesLegacyExactly()
        {
            var baseDna = Load("carrier_robot");
            var variants = new List<(string, InventionDna)> { ("sample", baseDna) };
            var wide = baseDna.Clone(); wide.FindBehavior("avoid_obstacle").Parameters["detectionRange"] = 1.0f; variants.Add(("avoid1.0", wide));
            var fast = baseDna.Clone(); fast.FindBehavior("follow_user").Parameters["speed"] = 2.5f; variants.Add(("fast", fast));
            var noAvoid = baseDna.Clone(); noAvoid.Behaviors.RemoveAll(b => b.Type == "avoid_obstacle"); variants.Add(("noAvoid", noAvoid));
            var noFollow = baseDna.Clone(); noFollow.Behaviors.RemoveAll(b => b.Type == "follow_user"); variants.Add(("noFollow", noFollow));
            int runs = 0;
            foreach (var (name, dna) in variants)
                foreach (int seed in new[] { 1, 7, 1234, 2026 })
                    foreach (int count in new[] { 0, 4, 10 })
                        foreach (float payload in new[] { 0f, 5f, 20f })
                            foreach (var mode in new[] { LabModes.Dream, LabModes.Reality })
                            {
                                var cfg = new RainExperimentConfig { Environment = LabEnvironments.ObstacleCourse, Mode = mode, Seed = seed, ObstacleCount = count, PayloadKg = payload };
                                AssertSame(LegacyCourseSimulator.Run(dna, cfg), CourseSimulator.Run(dna, cfg), $"{name}/{seed}/{count}/{payload}/{mode}");
                                runs++;
                            }
            TestContext.WriteLine($"carrier: {runs} runs identical");
        }

        [Test] public void Flight_MatchesLegacyExactly()
        {
            var baseDna = Load("flying_shoes");
            var variants = new List<(string, InventionDna)> { ("sample", baseDna) };
            var stable = baseDna.Clone(); stable.FindBehavior("stabilize").Parameters["stabilizationStrength"] = 0.8f; variants.Add(("stable", stable));
            var weak = baseDna.Clone(); weak.FindBehavior("hover").Parameters["lift"] = 0.05f; variants.Add(("noLift", weak));
            var noMove = baseDna.Clone(); noMove.Behaviors.RemoveAll(b => b.Type == "move_forward"); variants.Add(("noMove", noMove));
            var fast = baseDna.Clone(); fast.FindBehavior("move_forward").Parameters["thrust"] = 1f; fast.FindBehavior("move_forward").Parameters["speedLimit"] = 3f; variants.Add(("fast", fast));
            int runs = 0;
            foreach (var (name, dna) in variants)
                foreach (int seed in new[] { 1, 7, 2026 })
                    foreach (float wind in new[] { 0f, 4f, 10f })
                        foreach (float dir in new[] { 0f, 90f, 225f })
                            foreach (float dist in new[] { 4f, 10f })
                                foreach (var mode in new[] { LabModes.Dream, LabModes.Reality })
                                {
                                    var cfg = new RainExperimentConfig { Environment = LabEnvironments.FlightDeck, Mode = mode, Seed = seed, WindSpeed = wind, WindDirectionDeg = dir, TargetDistance = dist };
                                    AssertSame(LegacyCourseSimulator.Run(dna, cfg), CourseSimulator.Run(dna, cfg), $"{name}/{seed}/{wind}/{dir}/{dist}/{mode}");
                                    runs++;
                                }
            TestContext.WriteLine($"flight: {runs} runs identical");
        }

        [Test] public void FromConfig_ProducesValidPlans()
        {
            foreach (var (env, file) in new[] { (LabEnvironments.RainLab, "handsfree_rain"), (LabEnvironments.ObstacleCourse, "carrier_robot"), (LabEnvironments.FlightDeck, "flying_shoes") })
            {
                var plan = ExperimentPlan.FromConfig(new RainExperimentConfig { Environment = env });
                var errors = new List<string>();
                ExperimentPlanJson.Validate(plan, Load(file), errors);
                Assert.IsEmpty(errors, env + ": " + string.Join(" / ", errors));
                var back = ExperimentPlanJson.Parse(ExperimentPlanJson.ToJson(plan), Load(file));
                Assert.IsTrue(back.IsValid, back.Summary);
                Assert.AreEqual(plan.Scenario, back.Plan.Scenario);
                Assert.AreEqual(plan.Modules.Count, back.Plan.Modules.Count);
                Assert.AreEqual(plan.Criteria.Count, back.Plan.Criteria.Count);
            }
        }

        [Test] public void PlanToConfig_RoundTripsAllEnvironments()
        {
            foreach (var cfg in new[]
            {
                new RainExperimentConfig { Environment = LabEnvironments.RainLab, Mode = LabModes.Reality, RainIntensity = 35, WindSpeed = 6, WindDirectionDeg = 45, Seed = 9, DurationSec = 10 },
                new RainExperimentConfig { Environment = LabEnvironments.ObstacleCourse, ObstacleCount = 7, PayloadKg = 15, Seed = 3 },
                new RainExperimentConfig { Environment = LabEnvironments.FlightDeck, TargetDistance = 9, WindSpeed = 5, WindDirectionDeg = 180, Seed = 4 },
            })
            {
                var back = ExperimentPlan.FromConfig(cfg).ToConfig();
                Assert.IsTrue(cfg.Clamped().SameConditions(back), cfg.Describe() + " -> " + back.Describe());
            }
            // 雨の実験：計画経由でも同じ結果
            var dna = Load("handsfree_rain");
            var c0 = new RainExperimentConfig { RainIntensity = 30, WindSpeed = 4, Seed = 2026, DurationSec = 10 };
            var a = new RainSimulator(dna, c0).RunToEnd();
            var b = new RainSimulator(dna, ExperimentPlan.FromConfig(c0).ToConfig()).RunToEnd();
            Assert.AreEqual(a.WetnessScore, b.WetnessScore);
            Assert.AreEqual(a.CoverageRatio, b.CoverageRatio);
        }

        const string Plan = @"{ ""scenario"": ""walk_route"", ""mode"": ""reality"", ""durationSec"": 10, ""seed"": 42,
  ""modules"": [ { ""type"": ""obstacles"", ""count"": 6 }, { ""type"": ""payload"", ""kg"": 8 } ],
  ""metrics"": [ ""collisionCount"", ""arrivalDelay"", ""energyWh"" ],
  ""criteria"": [ { ""metric"": ""collisionCount"", ""op"": ""<="", ""value"": 0, ""reason"": ""ぶつからない"" } ] }";

        [Test] public void PlanJson_ValidPlanRunsAndFiltersMetrics()
        {
            var dna = Load("carrier_robot");
            var r = ExperimentPlanJson.Parse(Plan, dna);
            Assert.IsTrue(r.IsValid, r.Summary);
            var result = new RainExperimentResult { Environment = LabEnvironments.ObstacleCourse, Config = new RainExperimentConfig { Environment = LabEnvironments.ObstacleCourse, Mode = LabModes.Reality } };
            var run = ScenarioRunner.Run(dna, r.Plan, result);
            CollectionAssert.AreEqual(new[] { "collisionCount", "arrivalDelay", "energyWh" }, run.Result.Metrics.Select(m => m.Key), "計画で選んだ計測だけを出す");
            Assert.AreEqual(6, run.Layout.Obstacles.Count);
        }

        [TestCase("\"walk_route\"", "\"moonwalk\"", "シナリオ")]
        [TestCase("\"reality\"", "\"nightmare\"", "モード")]
        [TestCase("\"count\": 6", "\"count\": 60", "範囲外")]
        [TestCase("\"count\": 6", "\"radius\": 1", "未対応の値")]
        [TestCase("{ \"type\": \"payload\", \"kg\": 8 }", "{ \"type\": \"lava\", \"kg\": 8 }", "未対応の環境モジュール")]
        [TestCase("{ \"type\": \"payload\", \"kg\": 8 }", "{ \"type\": \"obstacles\", \"count\": 2 }", "重複")]
        [TestCase("\"energyWh\" ]", "\"teleportTime\" ]", "未対応の計測項目")]
        [TestCase("\"energyWh\" ]", "\"stabilityScore\" ]", "シナリオ")]
        [TestCase("\"mode\": \"reality\"", "\"mode\": \"dream\"", "REALITY")]
        [TestCase("\"op\": \"<=\"", "\"op\": \"==\"", "比較")]
        [TestCase("\"value\": 0,", "\"value\": 1000,", "範囲外")]
        [TestCase("\"metric\": \"collisionCount\"", "\"metric\": \"maxGap\"", "計測項目にありません")]
        [TestCase("\"seed\": 42,", "\"seed\": 42, \"script\": \"rm -rf\",", "未知のフィールド")]
        public void PlanJson_RejectsInvalid(string from, string to, string expect)
        {
            Assert.IsTrue(Plan.Contains(from), "置換元がない: " + from);
            var r = ExperimentPlanJson.Parse(Plan.Replace(from, to), Load("carrier_robot"));
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(r.Errors.Any(e => e.Contains(expect)), string.Join(" / ", r.Errors));
        }

        [Test] public void PlanJson_RequiresBlocksForMetrics()
        {
            var shoes = Load("flying_shoes");
            var r = ExperimentPlanJson.Parse(Plan, shoes);
            Assert.IsFalse(r.IsValid, "空飛ぶ靴には follow_user がないので衝突回数は測れない");
            Assert.IsTrue(r.Errors.Any(e => e.Contains("follow_user")));
            Assert.IsFalse(ExperimentPlanJson.Parse("{ not json", shoes).IsValid);
            Assert.IsFalse(ExperimentPlanJson.Parse(new string(' ', 9000) + "{}", shoes).IsValid);
        }
    }
}
