using System.Linq;
using ImpossibleLab.Core;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>重心と転倒（地面を走る発明が坂で後ろに倒れるか）。</summary>
    public class StabilityTests
    {
        static PartSpec P(string id, string type, Vector3 pos, Vector3 scale, string shape = "box", string material = null) =>
            new PartSpec { Id = id, Type = type, Position = pos, Scale = scale, Shape = shape, Material = material };

        static InventionDna Dna(params PartSpec[] parts)
        {
            var dna = new InventionDna { Id = "t", Name = "t", Mount = "ground", Parts = parts.ToList() };
            dna.Behaviors.Add(new BehaviorSpec { Type = "drive", TargetPart = parts[0].Id, Parameters = { ["speedLimit"] = 1f, ["maxSlopeDeg"] = 30f } });
            return dna;
        }

        static PartSpec Wheel(string id, float x, float z) => P(id, "wheel", new Vector3(x, 0.05f, z), new Vector3(0.1f, 0.04f, 0.1f), "cylinder");

        /// <summary>前後 0.4 m の間に車輪が4つ、その上に箱（中心の高さ 0.2 m）。</summary>
        static InventionDna Cart(float boxY = 0.2f) => Dna(
            P("body", "body", new Vector3(0f, boxY, 0f), new Vector3(0.3f, 0.2f, 0.5f)),
            Wheel("w1", 0.17f, 0.2f), Wheel("w2", -0.17f, 0.2f), Wheel("w3", 0.17f, -0.2f), Wheel("w4", -0.17f, -0.2f));

        static ExperimentPlan Slope(float angle, float payloadKg = -1f, float size = 0.2f)
        {
            var p = new ExperimentPlan { Scenario = ExperimentCatalog.ClimbSlope, Mode = LabModes.Dream, Seed = 1 };
            p.Modules.Add(new PlanModule { Type = "slope", Params = { ["angleDeg"] = angle, ["lengthM"] = 5f } });
            if (payloadKg >= 0f) p.Modules.Add(new PlanModule { Type = "payload", Params = { ["kg"] = payloadKg, ["sizeM"] = size } });
            return p;
        }

        [Test] public void TipAngle_IsAtanOfBackDistanceOverHeight()
        {
            var dna = Cart();
            var r = StabilityModel.Evaluate(dna);
            Assert.IsTrue(r.Valid);
            Assert.AreEqual(0f, r.Floor, 1e-5f, "床は車輪の底");
            Assert.AreEqual(-0.2f, r.MinZ, 1e-5f, "後ろの接地は後ろの車輪の中心の前後位置");
            float h = r.Center.y - r.Floor, d = r.Center.z - r.MinZ;
            Assert.AreEqual(Mathf.Atan2(d, h) * Mathf.Rad2Deg, r.TipBackDeg, 1e-3f);
            Assert.Greater(r.TipBackDeg, 30f, "低く幅のある台車は倒れにくい");
            Assert.AreEqual(r.TipBackDeg, r.TipFrontDeg, 1e-3f, "前後対称");
        }

        [Test] public void HigherCenterOfMass_TipsAtSmallerAngle()
        {
            var low = StabilityModel.Evaluate(Cart(0.2f));
            var high = StabilityModel.Evaluate(Cart(0.8f));
            Assert.Less(high.TipBackDeg, low.TipBackDeg);
            var withLoad = StabilityModel.Evaluate(Cart(), payloadKg: 20f, payloadSizeM: 0.3f);
            Assert.Less(withLoad.TipBackDeg, low.TipBackDeg, "上に載せた荷物で重心が上がる");
        }

        [Test] public void PayloadGoesInsideContainer_WhenItFits_ElseOnTop()
        {
            var bag = Dna(P("case", "container", new Vector3(0f, 0.3f, 0f), new Vector3(0.4f, 0.5f, 0.3f)), Wheel("w1", 0.15f, 0.1f));
            Assert.AreEqual(0.05f + 0.1f, StabilityModel.PayloadCenter(bag, 0.2f).y, 1e-5f, "格納部の中の底");
            Assert.AreEqual(0.55f + 0.2f, StabilityModel.PayloadCenter(bag, 0.4f).y, 1e-5f, "収まらなければ上に載せる");
        }

        [Test] public void SlopePlan_PayloadSize_FitsInsideTheContainer()
        {
            // 奥行き 25 cm のキャリーバッグ：一辺 30 cm の箱は入らないので 20 cm にする
            var bag = Dna(P("case", "container", new Vector3(0f, 0.3f, 0f), new Vector3(0.4f, 0.55f, 0.25f)), Wheel("w1", 0.15f, 0.1f));
            bag.Behaviors.Add(new BehaviorSpec { Type = "carry_load", TargetPart = "case", Parameters = { ["capacity"] = 10f } });
            Assert.AreEqual(0.2f, StabilityModel.FitPayloadSize(bag, 0.3f), 1e-5f);
            Assert.AreEqual(0.15f, StabilityModel.FitPayloadSize(bag, 0.15f), 1e-5f, "入る大きさなら変えない");
            var plan = PlanCandidates.Build(bag, null).First(c => c.Plan.Scenario == ExperimentCatalog.ClimbSlope).Plan;
            float size = plan.Get("payload", "sizeM");
            Assert.AreEqual(0.2f, size, 1e-5f);
            var at = StabilityModel.PayloadCenter(bag, size);
            Assert.Less(at.y + size * 0.5f, 0.3f + 0.275f, "荷物は格納部の中");
            Assert.AreEqual(0.3f, StabilityModel.FitPayloadSize(Cart(), 0.3f), 1e-5f, "格納部の無い台車は上に載せる大きさのまま");
        }

        [Test] public void FrontWheelsOnly_TipsBackOnTheSlope_AndTheReplayFalls()
        {
            // 前に車輪が2つだけ、本体はその後ろに浮いている → 重心が後ろの接地より後ろ
            var dna = Dna(
                P("body", "body", new Vector3(0f, 0.3f, -0.1f), new Vector3(0.3f, 0.3f, 0.4f)),
                Wheel("w1", 0.17f, 0.15f), Wheel("w2", -0.17f, 0.15f));
            var st = StabilityModel.Evaluate(dna);
            Assert.AreEqual(0f, st.TipBackDeg, "平らな床でも倒れる");
            var run = ScenarioRunner.RunPlan(dna, Slope(4.76f));
            var r = run.Result;
            Assert.AreEqual(0f, r.Metric("climbedRatio").Value);
            Assert.AreEqual(0f, r.Metric("tipAngleDeg").Value, 1e-4f);
            Assert.IsTrue(r.Limits.Any(l => l.Contains("後ろに倒れる")));
            Assert.IsFalse(r.Success);
            Assert.Greater(run.Frames.Last().TipDeg, 5f, "記録の終わりでは後ろに倒れている");
            Assert.AreEqual(st.BackPivot, run.TipPivot);
        }

        [Test] public void StableCart_ClimbsAsBefore_AndReportsTipAngle()
        {
            var r = ScenarioRunner.RunPlan(Cart(), Slope(10f)).Result;
            Assert.AreEqual(1f, r.Metric("climbedRatio").Value, 1e-4f);
            Assert.Greater(r.Metric("tipAngleDeg").Value, 10f);
            Assert.IsTrue(r.Assumptions.Any(a => a.Contains("倒れる角度")));
        }

        [Test] public void TallLoad_OnTop_MakesTheCartTipOnASteepSlope()
        {
            // 背が高く、前後の車輪の間が狭い台車（坂の角度の範囲 0〜20° の中で倒れる）
            var dna = Dna(
                P("body", "body", new Vector3(0f, 0.45f, 0f), new Vector3(0.3f, 0.7f, 0.25f)),
                Wheel("w1", 0.17f, 0.1f), Wheel("w2", -0.17f, 0.1f), Wheel("w3", 0.17f, -0.1f), Wheel("w4", -0.17f, -0.1f));
            float tip = StabilityModel.Evaluate(dna, 30f, 0.3f).TipBackDeg;
            Assert.That(tip, Is.InRange(2f, 18f));
            var ok = ScenarioRunner.RunPlan(dna, Slope(tip - 1f, 30f, 0.3f)).Result;
            var bad = ScenarioRunner.RunPlan(dna, Slope(tip + 1f, 30f, 0.3f)).Result;
            Assert.AreEqual(1f, ok.Metric("climbedRatio").Value, 1e-4f);
            Assert.AreEqual(0f, bad.Metric("climbedRatio").Value);
        }

        [Test] public void NotOnTheGround_IsNotEvaluated()
        {
            var dna = Cart(); dna.Mount = "back";
            Assert.IsFalse(StabilityModel.Evaluate(dna).Valid);
        }

        [Test] public void Samples_TipAngles_AreLogged()
        {
            foreach (var ta in Resources.LoadAll<TextAsset>("ImpossibleLabSamples"))
            {
                var dna = DnaJson.Parse(ta.text).Dna;
                if (!StabilityModel.Applies(dna)) continue;
                var r = StabilityModel.Evaluate(dna);
                var load = StabilityModel.Evaluate(dna, 5f, 0.3f);
                Debug.Log($"[STABILITY] {ta.name} valid={r.Valid} back={r.TipBackDeg:0.0} front={r.TipFrontDeg:0.0} side={r.TipSideDeg:0.0} h={(r.Center.y - r.Floor):0.000} com=({r.Center.x:0.000},{r.Center.y:0.000},{r.Center.z:0.000}) z=[{r.MinZ:0.000},{r.MaxZ:0.000}] load5kg.back={load.TipBackDeg:0.0}");
                Assert.IsTrue(r.Valid, ta.name);
            }
        }
    }
}
