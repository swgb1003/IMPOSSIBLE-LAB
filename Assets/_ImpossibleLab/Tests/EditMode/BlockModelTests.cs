using System.Collections.Generic;
using System.Linq;
using ImpossibleLab.Core;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>機能ブロック 第1弾（3週目）の簡易モデル：手計算との一致・範囲・NaN が出ないこと・カタログ。</summary>
    public class BlockModelTests
    {
        const float G = RealityModel.Gravity;
        static readonly string[] NewBlocks = { "illuminate", "heat", "cool", "sun_shade", "impact_cushion", "grip", "lift_load", "detect", "alert" };

        // ---------------- 照明 ----------------
        [Test] public void Illuminate_MatchesHandCalculation()
        {
            // 150 lm・全角 60° → 立体角 2π(1−cos30°) = 0.8418 sr → 178.2 cd
            float i = IlluminateModel.Intensity(150f, 60f);
            Assert.AreEqual(178.19f, i, 0.05f);
            var lamp = new Vector3(0, 1, 0);
            var down = Vector3.down;
            Assert.AreEqual(i, IlluminateModel.IlluminanceAt(lamp, down, i, 60f, Vector3.zero), 1e-3f, "真下 1 m：E = I");
            // 0.5 m 先：d² = 1.25、cosα = 1/√1.25（光軸から 26.6° で円錐の中）
            Assert.AreEqual(i * (1f / Mathf.Sqrt(1.25f)) / 1.25f, IlluminateModel.IlluminanceAt(lamp, down, i, 60f, new Vector3(0, 0, 0.5f)), 1e-3f);
            Assert.AreEqual(0f, IlluminateModel.IlluminanceAt(lamp, down, i, 60f, new Vector3(0, 0, 1f)), "光軸から 45° は円錐の外");
        }

        [Test] public void Illuminate_TiltCoverageAndPower()
        {
            // 高さ 1 m・下向き 45°・全角 60°：床の 0.5〜2.5 m（見下ろす角 63°〜22°）はすべて 15°〜75° の円錐に入る
            var tilted = IlluminateModel.Evaluate(150f, 60f, 45f, 1f);
            Assert.AreEqual(1f, tilted.CoveredRatio, 1e-6f);
            var level = IlluminateModel.Evaluate(150f, 60f, 0f, 1f);
            Assert.Greater(level.CoveredRatio, 0f); Assert.Less(level.CoveredRatio, 1f);
            Assert.AreEqual(1.5f, tilted.PowerW, 1e-4f, "150 lm ÷ 100 lm/W");
            var doubled = IlluminateModel.Evaluate(300f, 60f, 45f, 1f);
            Assert.AreEqual(2f * tilted.AverageLx, doubled.AverageLx, 1e-3f, "照度は光束に比例");
            var withAmbient = IlluminateModel.Evaluate(150f, 60f, 45f, 1f, ambientLx: 5f);
            Assert.AreEqual(tilted.AverageLx + 5f, withAmbient.AverageLx, 1e-3f);
            Assert.AreEqual(tilted.MinLx + 5f, withAmbient.MinLx, 1e-3f);
        }

        // ---------------- 加温・冷却 ----------------
        [Test] public void Thermal_MatchesAnalyticSolution()
        {
            // 10 W × 効率 0.9 = 9 W、G = 1.5 W/K → 定常 +6 °C、τ = 600/1.5 = 400 秒
            var h = ThermalModel.Evaluate(false, 10f, 5f, 5f, 400f, 5f, 600f, 1.5f);
            Assert.AreEqual(9f, h.HeatFlowW, 1e-4f);
            Assert.AreEqual(11f, h.SteadyC, 1e-4f);
            Assert.AreEqual(6f * (1f - Mathf.Exp(-1f)), h.DeltaC, 1e-3f, "τ 秒で定常の 63%");
            Assert.AreEqual(-400f * Mathf.Log(1f - 5f / 6f), h.TimeToTargetSec, 0.05f, "+5 °C まで 約717秒");
            var never = ThermalModel.Evaluate(false, 10f, 5f, 5f, 400f, 7f, 600f, 1.5f);
            Assert.AreEqual(-1f, never.TimeToTargetSec, "定常の +6 °C を超える目標には届かない");
            // 冷却 10 W × 成績係数 0.5 = −5 W → 定常 −3.33 °C
            var c = ThermalModel.Evaluate(true, 10f, 30f, 30f, 100000f, 2f, 600f, 1.5f);
            Assert.AreEqual(-5f, c.HeatFlowW, 1e-4f);
            Assert.AreEqual(30f - 5f / 1.5f, c.FinalC, 1e-3f);
            Assert.Greater(c.TimeToTargetSec, 0f);
            Assert.AreEqual(h.FinalC, ThermalModel.TemperatureAt(400f, 5f, h.SteadyC, 400f), 1e-4f);
        }

        // ---------------- 日よけ ----------------
        [Test] public void SunShade_OverheadSunIsBlockedByOpacity()
        {
            var top = new Vector3(0, BodyGeometry.Height + 0.35f, 0);
            var r = SunShadeModel.Evaluate(0.6f, 0.9f, top, 90f, 0f);
            Assert.AreEqual(0.9f, r.ShadeRatio, 1e-3f, "真上の太陽：頭頂だけが日に当たり、すべて日よけの影");
            Assert.AreEqual(Mathf.PI * BodyGeometry.Radius * BodyGeometry.Radius, r.SunlitArea, 1e-3f, "頭頂の面積");
            Assert.AreEqual(0f, SunShadeModel.Evaluate(0.6f, 0.9f, top, -5f, 0f).ShadeRatio, "太陽が沈んでいる");
        }

        [Test] public void SunShade_LargerShadeGivesMoreShade()
        {
            var top = new Vector3(0, BodyGeometry.Height + 0.35f, 0);
            float prev = -1f;
            foreach (float radius in new[] { 0.2f, 0.4f, 0.8f, 1.5f })
            {
                var r = SunShadeModel.Evaluate(radius, 1f, top, 45f, 30f);
                Assert.That(r.ShadeRatio, Is.InRange(0f, 1f));
                Assert.Greater(r.ShadeRatio, prev, "半径 " + radius);
                prev = r.ShadeRatio;
            }
            // 太陽が低いほど体の側面に日が当たり、同じ日よけでも影になる割合は下がる
            Assert.Greater(SunShadeModel.Evaluate(0.8f, 1f, top, 70f, 0f).ShadeRatio, SunShadeModel.Evaluate(0.8f, 1f, top, 25f, 0f).ShadeRatio);
        }

        // ---------------- 衝撃吸収 ----------------
        [Test] public void Cushion_UndampedSpringMatchesEnergyBalance()
        {
            // 減衰なし・つぶれ切らない：½ k x² = m g (h + x) → x_max、最大の力 k x_max
            float m = 0.5f, h = 1f, k = 20000f;
            float mg = m * G;
            float xMax = mg / k + Mathf.Sqrt((mg / k) * (mg / k) + 2f * mg * h / k);
            var r = CushionModel.Drop(m, h, 1f, k, 0f);
            Assert.AreEqual(xMax, r.MaxCompressionM, xMax * 0.01f);
            Assert.AreEqual(k * xMax / mg, r.PeakG, k * xMax / mg * 0.01f, "約 91 G");
            Assert.IsFalse(r.BottomedOut);
            Assert.AreEqual(Mathf.Sqrt(2f * G * h), r.ImpactSpeed, 1e-4f);
        }

        [Test] public void Cushion_ReducesPeakAndThinCushionBottomsOut()
        {
            var none = CushionModel.Drop(0.5f, 1f, 0f, 0f);
            var soft = CushionModel.Drop(0.5f, 1f, 0.05f, 20000f);
            var thin = CushionModel.Drop(0.5f, 1f, 0.005f, 20000f);
            Assert.Greater(none.PeakG, 5f * soft.PeakG, "緩衝材なしは硬い床に当たる");
            Assert.IsFalse(soft.BottomedOut);
            Assert.IsTrue(thin.BottomedOut, "厚み 5 mm は使い切る");
            Assert.Greater(thin.PeakG, soft.PeakG);
            Assert.Greater(soft.ContactMs, none.ContactMs, "柔らかいほど長い時間をかけて止まる");
        }

        // ---------------- つかむ ----------------
        [Test] public void Grip_HoldsUntilShakeExceedsFriction()
        {
            // 30 N × 2面 × μ0.5 = 30 N で支える。2 kg：静止は 19.6 N、上向き 6 m/s² の揺れで 31.6 N 必要 → 落とす
            var still = GripModel.Evaluate(30f, 0.2f, 2f, 0.1f);
            Assert.IsTrue(still.CanGrasp); Assert.IsFalse(still.Dropped);
            Assert.AreEqual(30f / (2f * G), still.HoldMargin, 1e-4f);
            var shake = GripModel.Evaluate(30f, 0.2f, 2f, 0.1f, new List<float> { 0f, 2f, 6f, 1f });
            Assert.IsTrue(shake.Dropped);
            Assert.AreEqual(2, shake.DropIndex);
            Assert.AreEqual(2f * (G + 6f) / (2f * 0.5f), shake.RequiredForceN, 1e-3f);
            var gentle = GripModel.Evaluate(30f, 0.2f, 2f, 0.1f, new List<float> { 0f, 2f, 1f });
            Assert.IsFalse(gentle.Dropped); Assert.AreEqual(-1, gentle.DropIndex);
            var big = GripModel.Evaluate(200f, 0.1f, 0.5f, 0.3f);
            Assert.IsFalse(big.CanGrasp); Assert.IsTrue(big.Dropped, "開き幅より大きい物はつかめない");
        }

        // ---------------- 持ち上げ ----------------
        [Test] public void Lift_TimePowerEnergy()
        {
            var r = LiftModel.Evaluate(5f, 0.8f, 0.2f, 3f, 0.8f);
            Assert.IsTrue(r.CanLift);
            Assert.AreEqual(4f, r.TimeSec, 1e-4f);
            Assert.AreEqual(3f * G * 0.2f / 0.6f, r.PowerW, 1e-3f);
            Assert.AreEqual(3f * G * 0.8f / 0.6f / 3600f, r.EnergyWh, 1e-6f);
            var heavy = LiftModel.Evaluate(5f, 0.8f, 0.2f, 8f, 0.8f);
            Assert.IsFalse(heavy.CanLift); StringAssert.Contains("超える", heavy.Reason);
            Assert.IsFalse(LiftModel.Evaluate(5f, 0.5f, 0.2f, 3f, 0.8f).CanLift, "高さが足りない");
        }

        // ---------------- 検知 ----------------
        [Test] public void Detect_RangeFieldOfViewAndOcclusion()
        {
            var s = Vector2.zero;
            Assert.IsTrue(DetectModel.CanSee(s, 0f, 5f, 90f, new Vector2(0, 4)));
            Assert.IsFalse(DetectModel.CanSee(s, 0f, 5f, 90f, new Vector2(0, 6)), "距離の外");
            Assert.IsFalse(DetectModel.CanSee(s, 0f, 5f, 90f, new Vector2(4, 1)), "視野の外（76°）");
            Assert.IsTrue(DetectModel.CanSee(s, 0f, 5f, 90f, new Vector2(1, 3)), "視野の中（18°）");
            Assert.IsTrue(DetectModel.CanSee(s, 90f, 5f, 90f, new Vector2(4, 1)), "右を向けば見える");
            var wall = new List<Vector3> { new Vector3(0, 2, 0.3f) };
            Assert.IsFalse(DetectModel.CanSee(s, 0f, 5f, 90f, new Vector2(0, 4), wall), "障害物の陰");
            Assert.IsTrue(DetectModel.CanSee(s, 0f, 5f, 360f, new Vector2(0, -3)), "全周");
            var r = DetectModel.Evaluate(s, 0f, 5f, 90f, new[] { new Vector2(0, 4), new Vector2(0, 6), new Vector2(1, 3), new Vector2(4, 1) }, wall);
            Assert.AreEqual(1, r.Detected); Assert.AreEqual(0.25f, r.Rate, 1e-6f);
        }

        // ---------------- 通知 ----------------
        [Test] public void Alert_SoundAndLightRanges()
        {
            // 80 dB（1 m）・騒音 55 dB ＋ 10 dB → 10^(15/20) = 5.62 m
            Assert.AreEqual(Mathf.Pow(10f, 0.75f), AlertModel.SoundRangeM(80f), 1e-3f);
            Assert.AreEqual(0f, AlertModel.SoundRangeM(0f));
            // 50 lm 全方向 → 3.98 cd。夜（1e-5 lx）は √(3.98/1e-5) = 631 m、昼（1e-3 lx）は 63 m
            float i = 50f / (4f * Mathf.PI);
            Assert.AreEqual(Mathf.Sqrt(i / 1e-5f), AlertModel.LightRangeM(50f, 0.1f), 0.5f);
            Assert.AreEqual(Mathf.Sqrt(i / 1e-3f), AlertModel.LightRangeM(50f, 10000f), 0.05f);
            Assert.AreEqual(1e-4f, AlertModel.NoticeLux(Mathf.Pow(10f, 1.5f)), 1e-6f, "31.6 lx は夜と昼の中間（対数）");
            var r = AlertModel.Evaluate(80f, 50f, 2f, 0.1f);
            Assert.AreEqual(r.LightRangeM, r.RangeM);
            Assert.AreEqual(4f * Mathf.PI * 1e-4f / BlockCoefficients.V("speakerEfficiency") + 50f / BlockCoefficients.V("ledEfficacy") * 0.5f, r.PowerW, 1e-4f, "音（音響出力÷スピーカー効率）＋ 光 0.25 W（点滅で半分）");
        }

        // ---------------- 全モデル：範囲の端でも NaN・無限大が出ない ----------------
        [Test] public void AllModels_FiniteAcrossParameterRanges()
        {
            foreach (var type in NewBlocks)
            {
                var info = LabCatalog.Behavior(type);
                foreach (var pick in new System.Func<ParameterInfo, float>[] { p => p.Min, p => p.Default, p => p.Max })
                {
                    var v = info.Parameters.ToDictionary(p => p.Key, pick);
                    var values = new List<float>();
                    switch (type)
                    {
                        case "illuminate":
                            foreach (var mount in LabCatalog.Mounts)
                            {
                                var r = IlluminateModel.Evaluate(v["lumens"], v["beamAngleDeg"], v["tiltDeg"], BodyGeometry.MountHeight(mount.Id));
                                values.AddRange(new[] { r.IntensityCd, r.AverageLx, r.MinLx, r.MaxLx, r.CoveredRatio, r.PowerW });
                            }
                            break;
                        case "heat": case "cool":
                            foreach (float amb in new[] { -10f, 20f, 40f })
                            {
                                var r = ThermalModel.Evaluate(type == "cool", v["powerW"], amb, amb, 600f, 5f);
                                values.AddRange(new[] { r.FinalC, r.DeltaC, r.SteadyC, r.TimeToTargetSec, r.HeatFlowW });
                            }
                            break;
                        case "sun_shade":
                            foreach (float e in new[] { 5f, 45f, 90f })
                                values.Add(SunShadeModel.Evaluate(v["radius"], v["opacity"], new Vector3(0, 2.1f, 0), e, 200f).ShadeRatio);
                            break;
                        case "impact_cushion":
                            foreach (float kg in new[] { 0.1f, 2f })
                            {
                                var r = CushionModel.Drop(kg, 1.5f, v["thicknessM"], v["stiffness"]);
                                values.AddRange(new[] { r.PeakG, r.MaxCompressionM, r.ContactMs });
                            }
                            break;
                        case "grip":
                            values.Add(GripModel.Evaluate(v["gripForceN"], v["openingM"], 3f, 0.1f, new List<float> { 0f, 5f }).HoldMargin);
                            break;
                        case "lift_load":
                            var lr = LiftModel.Evaluate(v["maxLoadKg"], v["liftHeightM"], v["speed"], 0.5f, 0.1f);
                            values.AddRange(new[] { lr.TimeSec, lr.PowerW, lr.EnergyWh });
                            break;
                        case "detect":
                            values.Add(DetectModel.Evaluate(Vector2.zero, 0f, v["rangeM"], v["fovDeg"], new[] { new Vector2(0, 1), new Vector2(3, 3) }).Rate);
                            break;
                        case "alert":
                            var ar = AlertModel.Evaluate(v["loudnessDb"], v["lightLumens"], v["flashHz"], 50f);
                            values.AddRange(new[] { ar.SoundRangeM, ar.LightRangeM, ar.RangeM, ar.PowerW });
                            break;
                    }
                    Assert.IsNotEmpty(values, type);
                    foreach (var x in values) Assert.IsFalse(float.IsNaN(x) || float.IsInfinity(x), $"{type}: {x}");
                }
            }
        }

        // ---------------- カタログと係数 ----------------
        [Test] public void Catalog_NewBlocksAreRegisteredAndExposedToAi()
        {
            foreach (var type in NewBlocks)
            {
                var info = LabCatalog.Behavior(type);
                Assert.IsNotNull(info, type);
                Assert.IsTrue(info.ExposeToAi, type + "：5週目から AI に渡す");
                Assert.IsNotEmpty(info.ModelJa, type);
                Assert.IsNotEmpty(info.AllowedTargetTypes, type);
                foreach (var t in info.AllowedTargetTypes) Assert.IsNotNull(LabCatalog.Part(t), $"{type} → {t}");
                foreach (var p in info.Parameters)
                {
                    Assert.Less(p.Min, p.Max, $"{type}.{p.Key}");
                    Assert.That(p.Default, Is.InRange(p.Min, p.Max), $"{type}.{p.Key}");
                }
            }
            var proxy = Newtonsoft.Json.Linq.JObject.Parse(LabCatalog.ToProxyJson());
            var exported = proxy["behaviors"].Select(b => (string)b["type"]).ToList();
            foreach (var type in NewBlocks) CollectionAssert.Contains(exported, type);
            Assert.AreEqual(LabCatalog.Behaviors.Length, exported.Count);
            // 5週目：実験カタログ・タグ・取り付け位置も渡す
            Assert.AreEqual(ExperimentCatalog.Metrics.Length, proxy["experiments"]["metrics"].Count());
            Assert.AreEqual(ExperimentCatalog.Scenarios.Length, proxy["experiments"]["scenarios"].Count());
            Assert.AreEqual(LabCatalog.Mounts.Length, proxy["mounts"].Count());
            Assert.AreEqual(LabCatalog.Tags.Length, proxy["tags"].Count());
        }

        [Test] public void Coefficients_HaveRangeStatusAndSource()
        {
            var statuses = new[] { "standard", "literature", "design", "estimate", "tuning" };
            Assert.AreEqual(BlockCoefficients.All.Length, BlockCoefficients.All.Select(c => c.Id).Distinct().Count(), "id の重複なし");
            foreach (var c in BlockCoefficients.All)
            {
                Assert.That(c.Value, Is.InRange(c.Min, c.Max), c.Id);
                CollectionAssert.Contains(statuses, c.Status, c.Id);
                Assert.IsNotEmpty(c.Source, c.Id);
                Assert.IsNotEmpty(c.LabelJa, c.Id);
            }
        }

        [Test] public void DnaV2_WithNewBlocks_IsValid()
        {
            const string json = @"{
  ""schemaVersion"": 2, ""id"": ""night-light-bag"", ""name"": ""夜道を照らすリュック"", ""purpose"": ""暗い帰り道で足元を照らし、車に気づいてもらう"",
  ""tags"": [""lighting"", ""safety""], ""mount"": ""back"", ""appearance"": ""night_navy"",
  ""parts"": [
    { ""id"": ""pack"", ""type"": ""body"", ""shape"": ""box"", ""material"": ""fabric"", ""position"": [0, 0, 0], ""scale"": [0.3, 0.42, 0.14] },
    { ""id"": ""lamp"", ""type"": ""light"", ""position"": [0, -0.18, 0.08], ""scale"": [0.05, 0.03, 0.05] },
    { ""id"": ""cell"", ""type"": ""battery"", ""shape"": ""box"", ""position"": [0, 0.1, -0.04], ""scale"": [0.07, 0.1, 0.03] }
  ],
  ""behaviors"": [
    { ""type"": ""illuminate"", ""targetPart"": ""lamp"", ""parameters"": { ""lumens"": 150, ""beamAngleDeg"": 60, ""tiltDeg"": 50 } },
    { ""type"": ""alert"", ""targetPart"": ""lamp"", ""parameters"": { ""loudnessDb"": 0, ""lightLumens"": 30, ""flashHz"": 2 } }
  ],
  ""functionMap"": [
    { ""need"": ""足元を照らす"", ""blocks"": [""illuminate""], ""status"": ""simulated"" },
    { ""need"": ""車に気づいてもらう"", ""blocks"": [""alert""], ""status"": ""simplified"", ""note"": ""見える距離を光の強さから概算"" },
    { ""need"": ""雨でも使える"", ""blocks"": [], ""status"": ""unverifiable"", ""note"": ""防水性能は検証できない"" }
  ],
  ""revision"": 1, ""parentRevision"": null
}";
            var r = DnaJson.Parse(json);
            Assert.IsTrue(r.IsValid, r.Summary);
            Assert.IsEmpty(r.Warnings, string.Join(" / ", r.Warnings));
            var b = r.Dna.FindBehavior("illuminate");
            var ill = IlluminateModel.Evaluate(b.Parameters["lumens"], b.Parameters["beamAngleDeg"], b.Parameters["tiltDeg"], BodyGeometry.MountHeight(r.Dna.Mount));
            Assert.Greater(ill.AverageLx, 1f, "背中（1.3 m）から 150 lm で足元〜2.5 m 先が照らされる");
            // 範囲外の値は補正される（既存の機能と同じ扱い）
            var bad = DnaJson.Parse(json.Replace(@"""lumens"": 150", @"""lumens"": 99999"));
            Assert.IsTrue(bad.IsValid);
            Assert.AreEqual(2000f, bad.Dna.FindBehavior("illuminate").Parameters["lumens"]);
        }
    }
}
