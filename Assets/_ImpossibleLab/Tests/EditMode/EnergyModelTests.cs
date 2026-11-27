using System.Collections.Generic;
using System.Linq;
using ImpossibleLab.Core;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>1日使う（電池の持ち）と部品代（docs/blocks.md 9章、docs/DECISIONS.md D-45）。</summary>
    public class EnergyModelTests
    {
        static PartSpec P(string id, string type, string model = "", Vector3? scale = null)
        {
            var m = PartModels.Find(model);
            return new PartSpec { Id = id, Type = type, Model = model, Shape = m?.Shape ?? "", Scale = scale ?? m?.Size ?? Vector3.one * 0.05f };
        }

        static PartSpec Hinged(PartSpec p) { p.Joint = new JointSpec { Parent = "body", Max = 90f, Speed = 90f }; return p; }

        static BehaviorSpec B(string type, string target, params (string k, float v)[] ps)
        {
            var b = new BehaviorSpec { Type = type, TargetPart = target };
            foreach (var (k, v) in ps) b.Parameters[k] = v;
            return b;
        }

        /// <summary>灯り 1 W・人感センサー 0.01 W・ギヤモーター 2 W（関節を動かす、動くときだけ）・18650 電池 1 本（10.8 Wh）。</summary>
        static InventionDna Lantern(string battery = "bat-18650x1", string solar = null)
        {
            var dna = new InventionDna { Id = "t-lantern", Name = "テスト", Purpose = "テスト", Mount = "back" };
            dna.Parts.Add(P("body", "body", scale: new Vector3(0.12f, 0.08f, 0.05f)));
            dna.Parts.Add(P("lamp", "light", "led-spot1w"));
            dna.Parts.Add(P("eye", "sensor", "sen-pir"));
            dna.Parts.Add(Hinged(P("m", "motor", "mot-gear-tt")));
            if (battery != null) dna.Parts.Add(P("bat", "battery", battery));
            if (solar != null) dna.Parts.Add(P("sun", "solar", solar));
            dna.Behaviors.Add(B("illuminate", "lamp", ("lumens", 100f), ("beamAngleDeg", 60f), ("tiltDeg", 45f)));
            return dna;
        }

        static ExperimentPlan Daily(float hours = 4f, float active = 20f, float sun = 0f, string mode = LabModes.Dream) => new ExperimentPlan
        {
            Scenario = ExperimentCatalog.DailyUse, Mode = mode, Seed = 7,
            Modules = { new PlanModule { Type = "usage", Params = { ["hoursPerDay"] = hours, ["activePercent"] = active, ["sunHours"] = sun } } },
        };

        static float Usable => RealityModel.Default.Get("usableFraction").Value;

        [Test] public void Evaluate_UsesModelPower_AndSplitsContinuousAndIntermittent()
        {
            var r = EnergyModel.Evaluate(Lantern(), 4f, 20f, 0f);
            float use = 1f + 0.01f + 2f * 0.2f;
            Assert.AreEqual(use, r.UsePowerW, 1e-4f, "灯り・センサーはずっと、モーターは 2 割だけ");
            Assert.AreEqual(use * 4f, r.DailyWh, 1e-4f);
            Assert.AreEqual(10.8f, r.NominalWh, 1e-3f);
            Assert.AreEqual(10.8f * Usable / use, r.BatteryHours, 1e-3f);
            Assert.AreEqual(10.8f * Usable / (use * 4f), r.ChargeDays, 1e-3f);
            Assert.IsTrue(r.Consumers.All(c => c.From == EnergyModel.Source.Model), "品番の消費電力を使う");
            Assert.IsFalse(r.Consumers.Single(c => c.PartId == "m").Continuous);
            Assert.IsNull(r.RunsOutAfterHours);
        }

        [Test] public void Evaluate_WithoutModels_UsesBehaviorValuesAndTypeMedians()
        {
            var dna = new InventionDna { Id = "t", Mount = "hand" };
            dna.Parts.Add(P("lamp", "light"));
            dna.Parts.Add(Hinged(P("m", "motor")));
            dna.Parts.Add(P("pad", "heater", "heat-pad-usb"));
            dna.Parts.Add(P("bat", "battery", scale: new Vector3(0.04f, 0.02f, 0.06f)));
            dna.Behaviors.Add(B("illuminate", "lamp", ("lumens", 150f)));
            dna.Behaviors.Add(B("heat", "pad", ("powerW", 4f)));
            var r = EnergyModel.Evaluate(dna, 2f, 50f, 0f);
            var lamp = r.Consumers.Single(c => c.PartId == "lamp");
            Assert.AreEqual(150f / BlockCoefficients.V("ledEfficacy"), lamp.Watts, 1e-4f, "照明は光の明るさ ÷ 発光効率");
            var motor = r.Consumers.Single(c => c.PartId == "m");
            Assert.AreEqual(EnergyModel.Source.TypeMedian, motor.From);
            Assert.AreEqual(EnergyModel.TypeMedian("motor", "powerW").Value, motor.Watts, 1e-4f);
            Assert.AreEqual(4f, r.Consumers.Single(c => c.PartId == "pad").Watts, 1e-4f, "加温は機能の電力（品番より優先）");
            var store = r.Stores.Single();
            Assert.AreEqual(EnergyModel.Source.Mass, store.From, "品番のない電池は重さ × エネルギー密度");
            Assert.AreEqual(MassModel.Part(dna.FindPart("bat")).Kg * RealityModel.Default.Get("batteryEnergyDensity").Value, store.Wh, 1e-3f);
        }

        [Test] public void Evaluate_ProgramSwitchedParts_AreIntermittent_AndUnmovedPartsAreNotCounted()
        {
            var dna = Lantern();
            dna.Parts.Add(P("spike", "leg"));
            dna.Program = new MotionProgram
            {
                Rules = { new ProgramRule { Id = "on", When = { new ProgramCondition { Sensor = "target_distance", Op = "<", Value = 2f } },
                                            Do = { new ProgramAction { Action = ProgramAction.SetBehavior, Behavior = "illuminate", On = true } } } },
            };
            var r = EnergyModel.Evaluate(dna, 4f, 20f, 0f);
            var lamp = r.Consumers.Single(c => c.PartId == "lamp");
            Assert.IsTrue(lamp.Switched);
            Assert.IsFalse(lamp.Continuous);
            Assert.AreEqual(0.01f + (1f + 2f) * 0.2f, r.UsePowerW, 1e-4f, "プログラムで入り切りする灯りは、動く時間の割合だけ電力を使う");
            CollectionAssert.AreEqual(new[] { "spike" }, r.Idle, "動かす機能も関節もない脚は数えない");
            Assert.IsFalse(r.Consumers.Any(c => c.PartId == "spike"));
            var res = ScenarioRunner.RunPlan(dna, Daily()).Result;
            Assert.IsTrue(res.Assumptions.Any(a => a.Contains("プログラムで入り切り")), string.Join(" / ", res.Assumptions));
            Assert.IsTrue(res.Assumptions.Any(a => a.StartsWith("脚は、動かす機能がないので")), string.Join(" / ", res.Assumptions));

            dna.Program.Rules[0].Enabled = false;
            Assert.IsTrue(EnergyModel.Evaluate(dna, 4f, 20f, 0f).Consumers.Single(c => c.PartId == "lamp").Continuous, "止めたルールは数えない");
            dna.Behaviors.Add(B("drive", "body"));
            Assert.IsEmpty(EnergyModel.Evaluate(dna, 4f, 20f, 0f).Idle, "走行の機能があれば脚も動かす");
        }

        [Test] public void Evaluate_NothingDrawsPower_IsNotSolarSelfSufficient()
        {
            var dna = new InventionDna { Id = "t-idle", Mount = "hand" };
            dna.Parts.Add(P("body", "body"));
            dna.Parts.Add(P("m", "motor", "mot-gear-tt"));
            dna.Parts.Add(P("bat", "battery", "bat-18650x1"));
            Assert.IsTrue(EnergyModel.UsesPower(dna));
            var r = EnergyModel.Evaluate(dna, 4f, 20f, 0f);
            Assert.AreEqual(0f, r.UsePowerW);
            Assert.IsFalse(r.SelfSufficient, "発電がないのに、発電で足りるとは言わない");
            var res = ScenarioRunner.RunPlan(dna, Daily()).Result;
            Assert.IsTrue(res.Limits.Any(l => l.Contains("電力を使う部品がない")), string.Join(" / ", res.Limits));
            Assert.IsFalse(res.Limits.Any(l => l.Contains("太陽電池")));
        }

        [Test] public void Evaluate_SolarCanCoverTheDay_AndNoBatteryGivesZero()
        {
            var solar = EnergyModel.Evaluate(Lantern(solar: "sol-6w"), 4f, 20f, 4f);
            Assert.AreEqual(6f * 4f * BlockCoefficients.V("solarDerate"), solar.SolarWh, 1e-4f);
            Assert.IsTrue(solar.SelfSufficient);
            Assert.AreEqual(EnergyModel.MaxDays, solar.ChargeDays);
            var none = EnergyModel.Evaluate(Lantern(battery: null), 4f, 20f, 0f);
            Assert.IsTrue(none.NoBattery);
            Assert.AreEqual(0f, none.ChargeDays);
            Assert.AreEqual(0f, none.BatteryHours);
        }

        [Test] public void Evaluate_SmallBattery_RunsOutDuringUse()
        {
            var r = EnergyModel.Evaluate(Lantern("bat-cr2032"), 4f, 20f, 0f);
            float hours = 0.66f * Usable / r.UsePowerW;
            Assert.AreEqual(hours, r.BatteryHours, 1e-3f);
            Assert.IsTrue(r.RunsOutAfterHours.HasValue);
            Assert.AreEqual(hours, r.RunsOutAfterHours.Value, 0.15f, "0.1 時間ごとの再生の記録で切れる時刻");
            Assert.AreEqual(100f, r.PercentAt(0f));
            Assert.AreEqual(0f, r.PercentAt(23f));
        }

        [Test] public void DailyUse_RunsAsAPlan_AndPassesWhenOneChargeLastsADay()
        {
            var run = ScenarioRunner.RunPlan(Lantern(), Daily());
            var res = run.Result;
            Assert.AreEqual(ExperimentCatalog.DailyUse, res.Scenario);
            float days = res.Metric("chargeDays").Value;
            Assert.Greater(days, 1f);
            Assert.IsTrue(res.Passed, res.Criteria + " / " + string.Join(" / ", res.Limits));
            StringAssert.Contains("充電1回で使える日数", res.Criteria);
            Assert.IsNotNull(res.Metric("batteryHours"));
            Assert.IsNotNull(res.Metric("usePowerW"));
            Assert.IsNull(res.Metric("partsCostJpy"), "部品代は REALITY だけ");
            // 再生：使い終わった後の残りは 100 − 1日の電力量 ÷ 使える容量
            float end = run.Frames.Last().Aux;
            float expect = 100f * (1f - res.Metric("dailyEnergyWh").Value / (10.8f * Usable));
            Assert.AreEqual(expect, end, 1f);
            Assert.IsTrue(res.Assumptions.Any(a => a.Contains("電気を使う部品")));

            var small = ScenarioRunner.RunPlan(Lantern("bat-cr2032"), Daily()).Result;
            Assert.IsFalse(small.Passed);
            Assert.IsTrue(small.Limits.Any(l => l.Contains("電池が切れる")), string.Join(" / ", small.Limits));
            var none = ScenarioRunner.RunPlan(Lantern(battery: null), Daily()).Result;
            Assert.IsTrue(none.Limits.Any(l => l.Contains("電池がない")));
        }

        [Test] public void Plan_NeedsAnElectricPart()
        {
            var plain = new InventionDna { Id = "t-plain", Mount = "hand" };
            plain.Parts.Add(P("body", "body"));
            Assert.IsFalse(EnergyModel.UsesPower(plain));
            var p = Daily();
            p.Metrics.Add("chargeDays");
            var errors = new List<string>();
            ExperimentPlanJson.Validate(p, plain, errors);
            Assert.IsTrue(errors.Any(e => e.Contains("電気を使う部品")), string.Join(" / ", errors));
            errors.Clear();
            ExperimentPlanJson.Validate(p, Lantern(), errors);
            Assert.IsEmpty(errors);
            Assert.IsFalse(PlanCandidates.Standard(plain).Any(c => c.Plan.Scenario == ExperimentCatalog.DailyUse));
            var std = PlanCandidates.Standard(Lantern()).Single(c => c.Plan.Scenario == ExperimentCatalog.DailyUse);
            Assert.AreEqual("1日使う", std.Title);
        }

        [Test] public void Cost_SumsModelPrices_UsesTypeMedians_AndSkipsUnknownTypes()
        {
            var dna = Lantern();
            dna.Parts.Add(P("w", "wheel"));
            var c = CostModel.Evaluate(dna);
            float models = new[] { "led-spot1w", "sen-pir", "mot-gear-tt", "bat-18650x1" }.Sum(id => PartModels.Find(id).PriceJpy);
            Assert.AreEqual(models + CostModel.TypeMedianJpy("wheel").Value, c.Jpy, 1e-2f);
            Assert.AreEqual(4, c.FromModel);
            Assert.AreEqual(1, c.FromMedian);
            CollectionAssert.AreEqual(new[] { "本体" }, c.Uncounted, "本体は品番がないので数えない");

            var real = ScenarioRunner.RunPlan(dna, Daily(mode: LabModes.Reality)).Result;
            Assert.AreEqual(c.Jpy, real.Metric("partsCostJpy").Value, 1e-2f);
            Assert.IsTrue(real.Assumptions.Any(a => a.StartsWith("部品代") && a.Contains("本体")), string.Join(" / ", real.Assumptions));
            Assert.IsNotNull(real.Metric("totalMassKg"), "1日使う実験でも総質量を出す");
        }

        [Test] public void FunctionMap_MetricsLinkANeedToThePlanThatMeasuresIt()
        {
            const string json = @"{ ""schemaVersion"": 3, ""id"": ""t-fm"", ""name"": ""t"", ""purpose"": ""t"", ""tags"": [""lighting""], ""mount"": ""back"",
  ""parts"": [ { ""id"": ""body"", ""type"": ""body"", ""position"": [0, 0, 0], ""scale"": [0.12, 0.08, 0.05] },
               { ""id"": ""lamp"", ""type"": ""light"", ""model"": ""led-spot1w"", ""position"": [0, 0.05, 0], ""scale"": [0.02, 0.016, 0.02] },
               { ""id"": ""bat"", ""type"": ""battery"", ""model"": ""bat-18650x1"", ""position"": [0, 0, 0], ""scale"": [0.0185, 0.065, 0.0185] } ],
  ""behaviors"": [ { ""type"": ""illuminate"", ""targetPart"": ""lamp"", ""parameters"": { ""lumens"": 100 } } ],
  ""functionMap"": [ { ""need"": ""足元を照らす"", ""blocks"": [""illuminate""], ""status"": ""simulated"" },
                     { ""need"": ""一晩もつ"", ""blocks"": [], ""metrics"": [""batteryHours"", ""chargeDays""], ""status"": ""simulated"" },
                     { ""need"": ""安く作れる"", ""blocks"": [], ""metrics"": [""partsCostJpy""], ""status"": ""simplified"", ""note"": ""値段の分かる部品だけ"" } ] }";
            var r = DnaJson.Parse(json);
            Assert.IsTrue(r.IsValid, r.Summary);
            CollectionAssert.AreEqual(new[] { "batteryHours", "chargeDays" }, r.Dna.FunctionMap[1].Metrics);
            StringAssert.Contains("\"metrics\"", DnaJson.ToJson(r.Dna));
            Assert.IsTrue(DnaJson.Parse(DnaJson.ToJson(r.Dna)).IsValid, "書き出したものを読み直せる");

            var candidates = PlanCandidates.Build(r.Dna, null);
            var rows = FunctionMapView.Build(r.Dna, candidates, out _);
            Assert.AreEqual("1日使う", rows[1].PlanTitle);
            Assert.AreEqual("電池で動ける時間・充電1回で使える日数", rows[1].MetricsJa);
            Assert.IsNotNull(rows[2].PlanTitle, "部品代は REALITY に切り替えれば測れる");
            var here = FunctionMapView.ForResult(r.Dna, candidates, Daily(), out _);
            Assert.IsTrue(here[1].CoveredHere);
            Assert.IsFalse(here[2].CoveredHere, "DREAM の実験では部品代を測らない");
            Assert.IsTrue(FunctionMapView.ForResult(r.Dna, candidates, Daily(mode: LabModes.Reality), out _)[2].CoveredHere);

            Assert.IsFalse(DnaJson.Parse(json.Replace(@"""partsCostJpy""", @"""nope""")).IsValid, "無い計測項目は使えない");
            Assert.IsFalse(DnaJson.Parse(json.Replace(@"""metrics"": [""partsCostJpy""], ", "")).IsValid, "機能も計測項目もない「検証する」要望は使えない");
        }
    }
}
