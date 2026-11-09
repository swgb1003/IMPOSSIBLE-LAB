using System.IO;
using System.Linq;
using ImpossibleLab.Core;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>運搬（ObstacleCourse）・移動（FlightDeck）の実験と、カテゴリ別の改良ルール。</summary>
    public class CourseTests
    {
        static InventionDna Load(string file)
        {
            var r = DnaJson.Parse(Resources.Load<TextAsset>("ImpossibleLabSamples/" + file).text);
            Assert.IsTrue(r.IsValid, r.Summary);
            return r.Dna;
        }

        static RainExperimentConfig Cfg(string env, string mode = LabModes.Dream, int seed = 7) => new RainExperimentConfig
        { Environment = env, Mode = mode, Seed = seed, ObstacleCount = 6, PayloadKg = 5f, TargetDistance = 7f, WindSpeed = 4f, WindDirectionDeg = 0f };

        static void Log(RainExperimentResult r) =>
            TestContext.WriteLine($"{r.Environment} {r.Config.Mode} passed={r.Passed} " + string.Join(" ", r.Metrics.Select(m => m.Key + "=" + m.Display)) + " | " + string.Join(" / ", r.Limits));

        [Test] public void Samples_AreValid_AndExperimentable()
        {
            foreach (var (file, cat) in new[] { ("carrier_robot", "carrier"), ("flying_shoes", "mobility") })
            {
                var d = Load(file);
                Assert.AreEqual(cat, d.Category);
                Assert.IsTrue(LabCatalog.Category(cat).ExperimentAvailable);
            }
        }

        [Test] public void Carrier_IsDeterministic_AndAvoidanceReducesCollisions()
        {
            var dna = Load("carrier_robot");
            var a = CourseSimulator.Run(dna, Cfg(LabEnvironments.ObstacleCourse)).Result;
            var b = CourseSimulator.Run(dna, Cfg(LabEnvironments.ObstacleCourse)).Result;
            Log(a);
            Assert.AreEqual(a.Metric("collisionCount").Value, b.Metric("collisionCount").Value);
            Assert.AreEqual(a.Metric("arrivalDelay").Value, b.Metric("arrivalDelay").Value);

            var p = OfflineRevisionRules.Propose(dna, "障害物をよけて");
            var res = RevisionEngine.Apply(dna, p);
            Assert.IsTrue(res.Success);
            Assert.AreEqual("avoid_obstacle", p.Operations[0].BehaviorType);
            // 配置はシードで変わるため、10通りの合計で比べる
            float before = 0f, after = 0f;
            for (int seed = 1; seed <= 10; seed++)
            {
                before += CourseSimulator.Run(dna, Cfg(LabEnvironments.ObstacleCourse, seed: seed)).Result.Metric("collisionCount").Value;
                after += CourseSimulator.Run(res.NewDna, Cfg(LabEnvironments.ObstacleCourse, seed: seed)).Result.Metric("collisionCount").Value;
            }
            TestContext.WriteLine($"collisions over 10 seeds: {before} -> {after}");
            Assert.Greater(before, 0f, "初期設計（検知距離0.2m）は障害物に当たる");
            Assert.Less(after, before);
            var a1 = CourseSimulator.Run(dna, Cfg(LabEnvironments.ObstacleCourse, seed: 3)).Result;
            var a2 = CourseSimulator.Run(res.NewDna, Cfg(LabEnvironments.ObstacleCourse, seed: 3)).Result;
            Assert.Greater(a1.Metric("collisionCount").Value, a2.Metric("collisionCount").Value);
            Assert.AreEqual("改善", ExperimentComparer.Compare(a1, a2).First(c => c.Label == "衝突回数").Verdict);
        }

        [Test] public void Carrier_Overload_CannotCarry()
        {
            var dna = Load("carrier_robot");
            var cfg = Cfg(LabEnvironments.ObstacleCourse); cfg.PayloadKg = 20f;
            var r = CourseSimulator.Run(dna, cfg).Result;
            Assert.IsFalse(r.Success);
            Assert.IsTrue(r.Limits.Any(l => l.Contains("運べない")));
            Assert.IsTrue(CourseSimulator.Hints(r).Any(h => h.Contains("重い荷物")));
        }

        [Test] public void Carrier_Reality_LimitsSpeed_AndUsesEnergy()
        {
            var dna = Load("carrier_robot");
            var fast = RevisionEngine.Apply(dna, OfflineRevisionRules.Propose(dna, "もっと速くついてきて")).NewDna;
            fast = RevisionEngine.Apply(fast, OfflineRevisionRules.Propose(fast, "もっと速くついてきて")).NewDna;
            var r = CourseSimulator.Run(fast, Cfg(LabEnvironments.ObstacleCourse, LabModes.Reality)).Result;
            Log(r);
            Assert.IsTrue(r.EnergyWh.HasValue && r.EnergyWh.Value > 0f);
            Assert.IsTrue(r.Limits.Any(l => l.Contains("制限")), "1.8 m/s は REALITY の速度上限を超える");
        }

        [Test] public void Flight_Dream_Flies_AndStabilizationHelps()
        {
            var dna = Load("flying_shoes");
            var a = CourseSimulator.Run(dna, Cfg(LabEnvironments.FlightDeck)).Result;
            Log(a);
            Assert.Less(a.Metric("landingError").Value, CourseSimulator.LandingTolerance, "目標に着地できる");
            var stable = RevisionEngine.Apply(dna, OfflineRevisionRules.Propose(dna, "風で揺れないようにして")).NewDna;
            var b = CourseSimulator.Run(stable, Cfg(LabEnvironments.FlightDeck)).Result;
            Log(b);
            Assert.Greater(b.Metric("stabilityScore").Value, a.Metric("stabilityScore").Value);
            Assert.Less(b.Metric("maxDeviation").Value, a.Metric("maxDeviation").Value);
        }

        [Test] public void Flight_Reality_RunsOutOfPower()
        {
            var dna = Load("flying_shoes");
            var r = CourseSimulator.Run(dna, Cfg(LabEnvironments.FlightDeck, LabModes.Reality)).Result;
            Log(r);
            Assert.IsFalse(r.Success);
            Assert.IsTrue(r.Limits.Any(l => l.Contains("電池切れ")));
            var fp = CourseSimulator.EstimateFlightPower(dna);
            Assert.IsTrue(fp.HasValue);
            Assert.Greater(fp.Value.RequiredKW, 10f, "人を浮かせるには大きな電力が必要");
            Assert.Less(fp.Value.AvailableSec, 60f);
        }

        [Test] public void Flight_WithoutHover_CannotTakeOff()
        {
            var dna = Load("flying_shoes");
            dna.Behaviors.First(b => b.Type == "hover").Parameters["lift"] = 0.05f;
            var r = CourseSimulator.Run(dna, Cfg(LabEnvironments.FlightDeck)).Result;
            Assert.IsFalse(r.Success);
            Assert.IsTrue(r.Limits.Any(l => l.Contains("浮上できない")));
        }

        [Test] public void CourseResult_SavesAndLoads()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ilab_course_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var repo = new InventionRepository(dir);
                var dna = Load("carrier_robot");
                var r = CourseSimulator.Run(dna, Cfg(LabEnvironments.ObstacleCourse, LabModes.Reality)).Result;
                repo.Save(new SavedInvention { Current = dna, Experiments = { r } });
                var loaded = repo.Load(dna.Id, out var err);
                Assert.IsNotNull(loaded, err);
                var lr = loaded.Experiments.Single();
                Assert.AreEqual(LabEnvironments.ObstacleCourse, lr.Environment);
                Assert.AreEqual(LabEnvironments.ObstacleCourse, lr.Config.Environment);
                Assert.AreEqual(r.Passed, lr.Passed);
                Assert.AreEqual(r.Metrics.Count, lr.Metrics.Count);
                Assert.AreEqual(r.Metric("collisionCount").Value, lr.Metric("collisionCount").Value, 1e-3f);
                Assert.IsTrue(lr.Config.SameConditions(r.Config));
                Assert.AreEqual("変化なし", ExperimentComparer.Compare(r, lr).First().Verdict);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test] public void RevisionRules_AreCategoryScoped()
        {
            var shoes = Load("flying_shoes");
            var p = OfflineRevisionRules.Propose(shoes, "もっと高く飛べるようにして");
            Assert.IsTrue(p.Operations.All(o => o.BehaviorType == "hover"), "雨具の「高く（頭上との距離）」は使わない");
            Assert.IsEmpty(p.Notes);
            var robot = Load("carrier_robot");
            var q = OfflineRevisionRules.Propose(robot, "防ぐ範囲を広げて");
            Assert.IsEmpty(q.Operations);
            Assert.IsTrue(q.Notes.Any(n => n.Contains("障害物の回避")), "運搬ロボットで使える改良を案内する");
        }

        /// <summary>実際の Claude API の応答（2026-11-09 記録）：検証を通り、実験でき、改良依頼の入力を作れること。</summary>
        [Test] public void RecordedAiDesigns_AreValidAndTestable()
        {
            foreach (var (cat, key) in new[] { ("carrier", "collisionCount"), ("mobility", "stabilityScore") })
            {
                string path = Path.Combine(Application.dataPath, $"_ImpossibleLab/Tests/EditMode/Fixtures/ai_design_{cat}.json");
                var o = ProxyProtocol.ParseDesign(200, File.ReadAllText(path), null);
                Assert.AreEqual(DesignStatus.Success, o.Status, o.Message);
                Assert.AreEqual(cat, o.Dna.Category);
                var cfg = Cfg(LabEnvironments.ForCategory(cat));
                var dream = CourseSimulator.Run(o.Dna, cfg).Result;
                var real = CourseSimulator.Run(o.Dna, Cfg(LabEnvironments.ForCategory(cat), LabModes.Reality)).Result;
                Log(dream); Log(real);
                Assert.IsNotNull(dream.Metric(key));
                Assert.IsTrue(dream.Metrics.All(m => !float.IsNaN(m.Value) && !float.IsInfinity(m.Value)));
                // 記録した改良応答（「もっと速く」）を適用し、同じ条件で再実験する
                string rpath = Path.Combine(Application.dataPath, $"_ImpossibleLab/Tests/EditMode/Fixtures/ai_revise_{cat}.json");
                var rev = ProxyProtocol.ParseRevision(200, File.ReadAllText(rpath), null);
                Assert.AreEqual(DesignStatus.Success, rev.Status, rev.Message);
                var applied = RevisionEngine.Apply(o.Dna, rev.Proposal);
                Assert.IsTrue(applied.Success, string.Join(" / ", applied.Errors));
                var after = CourseSimulator.Run(applied.NewDna, cfg).Result;
                Log(after);
                string timeKey = cat == "carrier" ? "arrivalDelay" : "completionTime";
                Assert.LessOrEqual(after.Metric(timeKey).Value, dream.Metric(timeKey).Value + 1e-3f, "速くする改良で時間が延びない");
            }
        }

        [Test] public void Appearance_ValidatesAndRoundTrips()
        {
            var dna = Load("carrier_robot");
            Assert.AreEqual(LabCatalog.DefaultAppearance, dna.Appearance, "外観の指定がない設計図は既定の外観");
            dna.Appearance = "night_navy";
            var back = DnaJson.Parse(DnaJson.ToJson(dna));
            Assert.IsTrue(back.IsValid, back.Summary);
            Assert.AreEqual("night_navy", back.Dna.Appearance);
            Assert.AreEqual("night_navy", back.Dna.Clone().Appearance);
            var bad = DnaJson.Parse(DnaJson.ToJson(dna).Replace("night_navy", "rainbow_neon"));
            Assert.IsFalse(bad.IsValid);
            Assert.IsTrue(bad.Errors.Any(e => e.Contains("外観")));
        }

        [Test] public void OfflineDesign_ProducesCarrierAndMobility()
        {
            var samples = new System.Collections.Generic.Dictionary<string, string>
            {
                ["carrier"] = Resources.Load<TextAsset>("ImpossibleLabSamples/carrier_robot").text,
                ["mobility"] = Resources.Load<TextAsset>("ImpossibleLabSamples/flying_shoes").text,
            };
            var svc = new OfflineDesignService(samples);
            var a = svc.GenerateAsync("自動でついてくる運搬ロボットを作って", null, default).Result;
            Assert.AreEqual(DesignStatus.Success, a.Status, a.Message);
            Assert.AreEqual("carrier", a.Dna.Category);
            var b = svc.GenerateAsync("空を飛べる靴を作って", null, default).Result;
            Assert.AreEqual(DesignStatus.Success, b.Status, b.Message);
            Assert.AreEqual("mobility", b.Dna.Category);
        }
    }
}
