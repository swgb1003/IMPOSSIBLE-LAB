using System.Linq;
using ImpossibleLab.Core;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>動作プログラムの実験（仕様 28.7、ゲート G-A2 の AT-13）：近づいて離れる・反応時間・期待の達成率。</summary>
    public class ProgramExperimentTests
    {
        static InventionDna Bin()
        {
            var r = DnaJson.Parse(Resources.Load<TextAsset>("ImpossibleLabSamples/auto_bin").text);
            Assert.IsTrue(r.IsValid, r.Summary);
            return r.Dna;
        }

        static ExperimentPlan Approach(string mode = LabModes.Dream, float count = 3, float speed = 1, float dwell = 2)
        {
            var c = PlanCandidates.Standard(Bin()).First();
            Assert.AreEqual(ExperimentCatalog.ApproachDevice, c.Plan.Scenario, "動作プログラムのある発明では最初の候補");
            var p = PlanEditor.Clone(c.Plan);
            p.Mode = mode;
            var m = p.Module("approach");
            m.Params["count"] = count; m.Params["speed"] = speed; m.Params["dwellSec"] = dwell;
            return p;
        }

        [Test] public void AutoBin_ApproachExperiment_MeasuresReactionAndExpectation()
        {
            var dna = Bin();
            var run = ScenarioRunner.RunPlan(dna, Approach());
            var r = run.Result;
            Assert.AreEqual(3f, r.Metric("ruleFiredCount").Value, "3回近づいて3回開く");
            // 開く速さ 120°/秒 で 100° → 約 0.83 秒
            Assert.AreEqual(100f / 120f, r.Metric("reactionTime").Value, 0.05f);
            float rate = r.Metric("expectationRate").Value;
            Assert.Greater(rate, 0.7f, "既定の期待（7割以上）を満たす");
            Assert.Less(rate, 1f, "3秒で閉じるので、近くにいる間ずっとではない");
            Assert.IsTrue(r.Passed == true, string.Join(" / ", r.Limits));
            Assert.IsNull(r.Metric("jointEnergyWh"), "DREAM では関節の電力量を出さない");
            // 再生用の記録にフタの角度が入っている
            Assert.AreEqual(new[] { "lid" }, r.JointIds.ToArray());
            Assert.IsTrue(run.Frames.Any(f => f.Joints != null && f.Joints[0] > 99f));
            Assert.IsTrue(r.Timeline.Any(t => t.Contains("open_lid") && t.Contains("start")));
        }

        [Test] public void ApproachExperiment_IsDeterministic_AndRealityAddsEnergy()
        {
            var a = ScenarioRunner.RunPlan(Bin(), Approach(LabModes.Reality));
            var b = ScenarioRunner.RunPlan(Bin(), Approach(LabModes.Reality));
            CollectionAssert.AreEqual(a.Result.Metrics.Select(m => m.Key + "=" + m.Value).ToList(), b.Result.Metrics.Select(m => m.Key + "=" + m.Value).ToList());
            CollectionAssert.AreEqual(a.Result.Timeline, b.Result.Timeline);
            Assert.Greater(a.Result.Metric("jointEnergyWh").Value, 0f);
        }

        [Test] public void FasterJoint_ShortensReactionTime()
        {
            var slow = Bin();
            var fast = Bin();
            fast.FindPart("lid").Joint.Speed = 240f;
            var rs = ScenarioRunner.RunPlan(slow, Approach()).Result.Metric("reactionTime").Value;
            var rf = ScenarioRunner.RunPlan(fast, Approach()).Result.Metric("reactionTime").Value;
            Assert.Less(rf, rs);
        }

        [Test] public void PlanWithExpect_RoundTripsAndValidates()
        {
            var dna = Bin();
            var p = Approach();
            p.Expect = new ProgramExpectation { Sensor = "target_distance", Op = "<", Value = 1f, Joint = "lid", AtLeast = 80f, MinRatio = 0.9f };
            var back = ExperimentPlanJson.Parse(ExperimentPlanJson.ToJson(p), dna);
            Assert.IsTrue(back.Plan != null, string.Join(" / ", back.Errors));
            Assert.AreEqual(80f, back.Plan.Expect.AtLeast);
            p.Expect.Joint = "arm";
            Assert.IsNull(ExperimentPlanJson.Parse(ExperimentPlanJson.ToJson(p), dna).Plan, "存在しない関節は拒否");
            // 動作プログラムのない発明では、動作の計測は測れない
            var rain = DnaJson.Parse(Resources.Load<TextAsset>("ImpossibleLabSamples/handsfree_rain").text).Dna;
            var errors = new System.Collections.Generic.List<string>();
            var noProg = Approach(); noProg.Metrics.Add("reactionTime");
            ExperimentPlanJson.Validate(noProg, rain, errors);
            Assert.IsTrue(errors.Any(e => e.Contains("動作プログラム")), string.Join(" / ", errors));
        }
    }
}
