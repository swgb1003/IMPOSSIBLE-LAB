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

        // AT-15：「もっと早く開いて」と改良すると、ルールの値だけが変わり、再実験で反応時間が短くなる。「5秒開けておいて」で期待の達成率が上がる
        [Test] public void OfflineRevision_FasterOpen_And_HoldLonger()
        {
            var dna = Bin();
            var faster = OfflineRevisionRules.Propose(dna, "もっと早く開いて");
            Assert.AreEqual(1, faster.Operations.Count, string.Join(" / ", faster.Notes));
            Assert.AreEqual(RevisionOps.UpdateRule, faster.Operations[0].Op);
            Assert.AreEqual("speed", faster.Operations[0].RuleField);
            var r1 = RevisionEngine.Apply(dna, faster);
            Assert.IsTrue(r1.Success, string.Join(",", r1.Errors));
            StringAssert.Contains("動かす速さ", r1.Changes[0].Label);
            Assert.AreEqual(dna.FindPart("lid").Joint.Speed, r1.NewDna.FindPart("lid").Joint.Speed, "関節そのものは変えない（ルールの値だけ）");
            float before = ScenarioRunner.RunPlan(dna, Approach()).Result.Metric("reactionTime").Value;
            float after = ScenarioRunner.RunPlan(r1.NewDna, Approach()).Result.Metric("reactionTime").Value;
            Assert.Less(after, before);

            var hold = OfflineRevisionRules.Propose(dna, "5秒開けておいて");
            Assert.AreEqual("seconds", hold.Operations[0].RuleField);
            Assert.AreEqual(5f, hold.Operations[0].Value);
            var r2 = RevisionEngine.Apply(dna, hold);
            // 近くに5秒留まる実験では、3秒で閉じるより5秒開けておく方が、期待どおりの時間が長い
            float e1 = ScenarioRunner.RunPlan(dna, Approach(dwell: 5)).Result.Metric("expectationRate").Value;
            float e2 = ScenarioRunner.RunPlan(r2.NewDna, Approach(dwell: 5)).Result.Metric("expectationRate").Value;
            Assert.Greater(e2, e1);
            Assert.AreEqual("もっと早く開いて", OfflineRevisionRules.RequestForMetric(dna, "reactionTime"));
            CollectionAssert.Contains(OfflineRevisionRules.SuggestedRequestsFor(dna).ToList(), "もっと早く開いて");
        }

        [Test] public void MotionLight_Sample_LightsWhenApproached()
        {
            Assert.AreEqual("motion_light", OfflineDesignService.KindOf("近づくと点くライトが欲しい"));
            Assert.AreEqual("motion_bin", OfflineDesignService.KindOf("近づくと開くゴミ箱"));
            var r = DnaJson.Parse(Resources.Load<TextAsset>("ImpossibleLabSamples/motion_light").text);
            Assert.IsTrue(r.IsValid, r.Summary);
            var dna = r.Dna;
            var exp = ProgramExpectation.FromProgram(dna);
            Assert.AreEqual("illuminate", exp.Behavior, "関節がないので機能のオンを期待にする");
            var plan = PlanEditor.Clone(PlanCandidates.Standard(dna).First().Plan);
            Assert.AreEqual(ExperimentCatalog.ApproachDevice, plan.Scenario);
            var res = ScenarioRunner.RunPlan(dna, plan).Result;
            Assert.AreEqual(3f, res.Metric("ruleFiredCount").Value + 0f - 1f, "最初に消すルール1回＋近づいた3回");
            Assert.Less(res.Metric("reactionTime").Value, 0.1f, "照明はすぐ点く");
            Assert.Greater(res.Metric("expectationRate").Value, 0.9f, "5秒点けておくので、近くにいる間はほぼ点いている");
            Assert.IsTrue(res.Passed == true, string.Join(" / ", res.Limits));
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
