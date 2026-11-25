using System.Linq;
using ImpossibleLab.Core;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>6週目：機能ブロック第2弾（走行・坂）と、REALITY の共通層（総質量・装着部位の重さの目安）。</summary>
    public class DriveAndMassTests
    {
        static InventionDna Dna(string mount, string parts, string behaviors)
        {
            string json = $@"{{ ""schemaVersion"": 2, ""id"": ""t-drive"", ""name"": ""テスト"", ""purpose"": ""テスト"", ""tags"": [""daily""], ""mount"": ""{mount}"",
  ""parts"": [ {parts} ], ""behaviors"": [ {behaviors} ], ""revision"": 1, ""parentRevision"": null }}";
            var r = DnaJson.Parse(json);
            Assert.IsTrue(r.IsValid, r.Summary);
            return r.Dna;
        }

        static string Part(string id, string type, float x, float y, float z, string shape = null, string material = null) =>
            $@"{{ ""id"": ""{id}"", ""type"": ""{type}"", ""position"": [0, 0, 0], ""scale"": [{x}, {y}, {z}]{(shape != null ? $@", ""shape"": ""{shape}""" : "")}{(material != null ? $@", ""material"": ""{material}""" : "")} }}";

        static InventionDna Cart(float maxSlope = 10f, bool carry = false) => Dna("ground",
            Part("body", "body", 0.4f, 0.2f, 0.4f, "box") + ", " + Part("w1", "wheel", 0.15f, 0.05f, 0.15f),
            $@"{{ ""type"": ""drive"", ""targetPart"": ""w1"", ""parameters"": {{ ""speedLimit"": 1.0, ""maxSlopeDeg"": {maxSlope} }} }}" +
            (carry ? @", { ""type"": ""carry_load"", ""targetPart"": ""body"", ""parameters"": { ""capacity"": 10 } }" : ""));

        static ExperimentPlan Slope(float angle, string mode = LabModes.Dream, float payload = -1f)
        {
            var p = new ExperimentPlan { Scenario = ExperimentCatalog.ClimbSlope, Mode = mode, Seed = 1 };
            p.Modules.Add(new PlanModule { Type = "slope", Params = { ["angleDeg"] = angle, ["lengthM"] = 5f } });
            if (payload >= 0f) p.Modules.Add(new PlanModule { Type = "payload", Params = { ["kg"] = payload, ["sizeM"] = 0.3f } });
            return p;
        }

        // ---------- 総質量 ----------
        [Test]
        public void PartMass_ShellFoamFabricBattery()
        {
            float t = BlockCoefficients.V("shellThicknessM");
            var box = new PartSpec { Type = "body", Scale = new Vector3(0.2f, 0.2f, 0.2f), Shape = "box", Material = "plastic" };
            Assert.AreEqual(0.24f * t * 1050f, MassModel.Part(box).Kg, 1e-4f, "中空の殻：表面積×厚み×密度");
            var thin = new PartSpec { Type = "panel", Scale = new Vector3(0.2f, 0.001f, 0.2f), Shape = "box", Material = "plastic" };
            Assert.AreEqual(0.2f * 0.001f * 0.2f * 1050f, MassModel.Part(thin).Kg, 1e-5f, "殻より薄い部品は中身が詰まった体積");
            var foam = new PartSpec { Type = "cushion", Scale = new Vector3(0.1f, 0.1f, 0.1f), Shape = "box", Material = "foam" };
            Assert.AreEqual(0.001f * 30f, MassModel.Part(foam).Kg, 1e-5f);
            var cloth = new PartSpec { Type = "canopy", Scale = new Vector3(1f, 0.02f, 1f), Shape = "cylinder", Material = "fabric" };
            float face = Mathf.PI / 4f;
            Assert.AreEqual(face * BlockCoefficients.V("fabricArealDensity"), MassModel.Part(cloth).Kg, 0.02f, "布は片面の面積×面密度（縁の分だけ多め）");
            var bat = new PartSpec { Type = "battery", Scale = new Vector3(0.1f, 0.05f, 0.02f), Shape = "box" };
            Assert.AreEqual(0.0001f * BlockCoefficients.V("batteryDensity"), MassModel.Part(bat).Kg, 1e-4f);
        }

        [Test]
        public void TotalMass_HasRange_AndSamplesAreReasonable()
        {
            foreach (var file in new[] { "handsfree_rain", "carrier_robot", "flying_shoes" })
            {
                var dna = DnaJson.Parse(Resources.Load<TextAsset>("ImpossibleLabSamples/" + file).text).Dna;
                float lo = MassModel.TotalKg(dna, RealityCase.Optimistic), n = MassModel.TotalKg(dna), hi = MassModel.TotalKg(dna, RealityCase.Pessimistic);
                Assert.That(lo, Is.LessThanOrEqualTo(n), file);
                Assert.That(n, Is.LessThanOrEqualTo(hi), file);
                Assert.That(n, Is.InRange(0.05f, 50f), file + " の総質量が桁外れ");
            }
        }

        [Test]
        public void MountLoad_OnlyForBodyMounts_AndHeavyBackPackFails()
        {
            Assert.IsNull(MassModel.MountLimitKg("ground"));
            Assert.AreEqual(BlockCoefficients.V("mountLimitHead"), MassModel.MountLimitKg("head"));
            // 背中に鉄の箱（殻 2 mm で約 26 kg）＋照明 → 背負う重さの目安 6 kg を超える
            var heavy = Dna("back", Part("body", "body", 0.6f, 0.6f, 0.4f, "box", "steel") + ", " + Part("lamp", "light", 0.04f, 0.04f, 0.04f),
                @"{ ""type"": ""illuminate"", ""targetPart"": ""lamp"", ""parameters"": { ""lumens"": 150, ""beamAngleDeg"": 60, ""tiltDeg"": 50 } }");
            var p = new ExperimentPlan { Scenario = ExperimentCatalog.Stand, Mode = LabModes.Reality };
            p.Modules.Add(new PlanModule { Type = "darkness", Params = { ["lux"] = 2f } });
            var r = ScenarioRunner.RunPlan(heavy, p).Result;
            Assert.That(r.Metric("mountLoadRatio").Value, Is.GreaterThan(1f));
            Assert.IsTrue(r.Limits.Any(l => l.Contains("背中に付けるには重い")), string.Join(" / ", r.Limits));
            Assert.IsFalse(r.Success, "目安の合格条件（≤ 1）を満たさない");

            // 頭の上に浮いてついていく雨よけは、取り付け位置が頭でも体に載らないので目安を使わない
            var rain = DnaJson.Parse(Resources.Load<TextAsset>("ImpossibleLabSamples/handsfree_rain").text).Dna;
            Assert.AreEqual("head", rain.Mount);
            Assert.IsNull(MassModel.BodyLoadLimitKg(rain), "頭上追従は体に載らない");
            var rr = ScenarioRunner.RunPlan(rain, p).Result;
            Assert.IsNull(rr.Metric("mountLoadRatio"));
            Assert.IsFalse(rr.Limits.Any(l => l.Contains("に付けるには重い")), string.Join(" / ", rr.Limits));
            // 頭に付けるもの（頭上追従なし）は目安を使う
            var lamp = Dna("head", Part("lamp", "light", 0.04f, 0.04f, 0.04f),
                @"{ ""type"": ""illuminate"", ""targetPart"": ""lamp"", ""parameters"": { ""lumens"": 150, ""beamAngleDeg"": 60, ""tiltDeg"": 50 } }");
            Assert.AreEqual(BlockCoefficients.V("mountLimitHead"), MassModel.BodyLoadLimitKg(lamp));
            // DREAM では重さを測らない
            p.Mode = LabModes.Dream;
            var d = ScenarioRunner.RunPlan(heavy, p).Result;
            Assert.IsNull(d.Metric("totalMassKg"));
            Assert.IsTrue(d.Success);
        }

        // ---------- 走行（坂） ----------
        [Test]
        public void Climb_RampWithinLimit_TakesLengthOverSpeed()
        {
            var r = ScenarioRunner.RunPlan(Cart(), Slope(4.76f)).Result;
            Assert.AreEqual(1f, r.Metric("climbedRatio").Value, 1e-4f);
            Assert.AreEqual(5f, r.Metric("climbTime").Value, 1e-3f);
            Assert.IsTrue(r.Success);
            Assert.IsNull(r.Metric("drivePowerW"), "DREAM では電力を出さない");
        }

        [Test]
        public void Climb_TooSteep_OrNoDrive_Fails()
        {
            var steep = ScenarioRunner.RunPlan(Cart(maxSlope: 10f), Slope(12f)).Result;
            Assert.AreEqual(0f, steep.Metric("climbedRatio").Value);
            Assert.AreEqual(ExperimentCatalog.ClimbTimeout, steep.Metric("climbTime").Value);
            Assert.IsTrue(steep.Limits.Any(l => l.Contains("上れる角度")));
            Assert.IsFalse(steep.Success);

            var none = Dna("ground", Part("body", "body", 0.3f, 0.2f, 0.3f, "box"), "");
            var errors = new System.Collections.Generic.List<string>();
            ExperimentPlanJson.Validate(Slope(4.76f), none, errors);
            Assert.IsEmpty(errors, "合格条件なしの計画は機能がなくても通る（結果に理由を出す）");
            var rn = ScenarioRunner.RunPlan(none, Slope(4.76f)).Result;
            Assert.IsTrue(rn.Limits.Any(l => l.Contains("走行")));
            Assert.IsFalse(rn.Success);
        }

        [Test]
        public void Climb_Reality_PowerFollowsFormula_AndPayloadAddsPower()
        {
            var dna = Cart(carry: true);
            var a = ScenarioRunner.RunPlan(dna, Slope(4.76f, LabModes.Reality)).Result;
            var b = ScenarioRunner.RunPlan(dna, Slope(4.76f, LabModes.Reality, payload: 10f)).Result;
            float m = MassModel.TotalKg(dna), th = 4.76f * Mathf.Deg2Rad;
            float expect = m * RealityModel.Gravity * (Mathf.Sin(th) + BlockCoefficients.V("rollingResistance") * Mathf.Cos(th)) * 1f / BlockCoefficients.V("driveEfficiency");
            Assert.AreEqual(expect, a.Metric("drivePowerW").Value, 0.01f);
            Assert.That(b.Metric("drivePowerW").Value, Is.GreaterThan(a.Metric("drivePowerW").Value));
            Assert.IsNotNull(a.Metric("totalMassKg"));
            Assert.IsNull(a.Metric("mountLoadRatio"), "床を走るものは装着部位の目安を使わない");
        }

        [Test]
        public void Climb_Replay_EndsAtTheTopOfTheSlope()
        {
            var run = ScenarioRunner.RunPlan(Cart(), Slope(10f));
            var last = run.Frames.Last();
            Assert.AreEqual(5f * Mathf.Sin(10f * Mathf.Deg2Rad), last.Device.y, 1e-3f);
            Assert.AreEqual(5f, last.Aux, 1e-3f);
        }

        [Test]
        public void DriveInvention_GetsRampPlan_WithPayloadWhenItCarries()
        {
            var c = PlanCandidates.Build(Cart(carry: true), null).First(x => x.Plan.Scenario == ExperimentCatalog.ClimbSlope);
            Assert.AreEqual(BlockCoefficients.V("rampMaxSlopeDeg"), c.Plan.Get("slope", "angleDeg"), 1e-4f);
            Assert.IsTrue(c.Plan.Has("payload"));
            Assert.IsFalse(PlanCandidates.Build(Cart(), null).First(x => x.Plan.Scenario == ExperimentCatalog.ClimbSlope).Plan.Has("payload"));
            Assert.IsFalse(PlanEditor.CanRemove(c.Plan, "slope"), "坂は必須");
            // 段階：4.76 → 6 → 4.76
            var p = PlanEditor.Clone(c.Plan);
            Assert.IsTrue(PlanEditor.StepParam(p, "slope", "angleDeg", +1)); Assert.AreEqual(6f, p.Get("slope", "angleDeg"));
            Assert.IsTrue(PlanEditor.StepParam(p, "slope", "angleDeg", -1)); Assert.AreEqual(4.76f, p.Get("slope", "angleDeg"), 1e-4f);
        }
    }
}
