using System.IO;
using System.Linq;
using ImpossibleLab.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>公開データを作る処理（docs/PUBLISH_LIBRARY.md 3・4章。形は proxy/src/library/schema.ts と同じ）。</summary>
    public class PublicationBuilderTests
    {
        static InventionDna Rain()
        {
            var r = DnaJson.Parse(Resources.Load<TextAsset>("ImpossibleLabSamples/handsfree_rain").text);
            Assert.IsTrue(r.IsValid, r.Summary);
            return r.Dna;
        }

        static RainExperimentConfig Cfg(string mode) =>
            new RainExperimentConfig { Mode = mode, RainIntensity = 30, WindSpeed = 4, WindDirectionDeg = 90, Seed = 2026, DurationSec = 10 };

        static (RevisionResult res, RevisionProposal p) Widen(InventionDna dna, string param, float v)
        {
            var p = new RevisionProposal { Source = "offline", Summary = "防ぐ範囲を広げる" };
            p.Operations.Add(new RevisionOperation { Op = RevisionOps.UpdateParameter, BehaviorType = "rain_shield", Parameter = param, Value = v });
            var res = RevisionEngine.Apply(dna, p);
            Assert.IsTrue(res.Success, string.Join(" / ", res.Errors));
            return (res, p);
        }

        /// <summary>雨よけ：Rev.1 を実験 → 側面カバー（記録あり）→ 半径（記録あり）→ Rev.3 を DREAM と REALITY で実験。</summary>
        static SavedInvention RainStory()
        {
            var dna = Rain();
            var inv = new SavedInvention { Current = dna, RequestText = "手を使わず雨を防ぐ装置を作って", DesignSource = "offline" };
            inv.Experiments.Add(new RainSimulator(dna, Cfg(LabModes.Dream)).RunToEnd());
            var (r2, p2) = Widen(dna, "skirtLength", 0.25f);
            inv.History.Add(inv.Current); inv.Current = r2.NewDna; inv.RecordRevision(r2, p2, "横からの雨も防いで", "2026-11-25T18:01:47");
            var (r3, p3) = Widen(r2.NewDna, "radius", 0.8f);
            inv.History.Add(inv.Current); inv.Current = r3.NewDna; inv.RecordRevision(r3, p3, "防ぐ範囲を広げて", "2026-11-25T18:01:48");
            inv.Experiments.Add(new RainSimulator(r3.NewDna, Cfg(LabModes.Dream)).RunToEnd());
            inv.Experiments.Add(new RainSimulator(r3.NewDna, Cfg(LabModes.Reality)).RunToEnd());
            return inv;
        }

        [Test] public void Rain_HasTheRequest_TheRevisionsAndTheExperiments()
        {
            var o = PublicationBuilder.Build(RainStory(), new PublishOptions { DisplayName = "あおい", Anonymous = false, AgreedAt = "2026-11-25T20:00:00" });
            Assert.AreEqual(1, (int)o["format"]);
            Assert.AreEqual("2026-11-26", (string)o["consent"]["version"]);
            Assert.AreEqual(true, (bool)o["consent"]["agreed"]);
            Assert.AreEqual("あおい", (string)o["creator"]["displayName"]);
            Assert.AreEqual(false, (bool)o["creator"]["anonymous"]);
            var inv = (JObject)o["invention"];
            Assert.AreEqual(3, (int)inv["revision"]);
            Assert.AreEqual("手を使わず雨を防ぐ装置を作って", (string)inv["request"]);
            Assert.AreEqual("offline", (string)inv["designSource"]);
            Assert.AreEqual("頭", (string)inv["mount"]["label"]);
            Assert.AreEqual("防護", (string)inv["tags"][0]["label"]);

            // 改良の履歴：記録した依頼文と変えた値。Rev.3 は実験の結果が変わった
            var revs = (JArray)inv["revisions"];
            Assert.AreEqual(2, revs.Count);
            Assert.AreEqual("横からの雨も防いで", (string)revs[0]["request"]);
            Assert.AreEqual("offline", (string)revs[0]["source"]);
            Assert.AreEqual(false, (bool)revs[0]["resultChanged"], "Rev.2 は実験していない");
            Assert.AreEqual("防ぐ範囲を広げて", (string)revs[1]["request"]);
            Assert.AreEqual(true, (bool)revs[1]["resultChanged"]);
            StringAssert.Contains("0.80", (string)revs[1]["changes"][0]["after"]);

            // 実験：雨の実験場は防いだ割合を % で作り、合格の目安 80 % で判定する。条件は項目と値に分ける
            var exps = (JArray)inv["experiments"];
            Assert.AreEqual(3, exps.Count);
            var first = exps[0];
            Assert.AreEqual("雨の実験場", (string)first["title"]);
            Assert.AreEqual("dream", (string)first["mode"]);
            var cov = first["metrics"].First(m => (string)m["key"] == "coverageRatio");
            Assert.AreEqual("%", (string)cov["unit"]);
            Assert.AreEqual(true, (bool)cov["primary"]);
            Assert.AreEqual(((float)cov["value"] * 100f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), (string)cov["display"]);
            Assert.AreEqual(false, (bool)first["passed"]);
            StringAssert.Contains("80", (string)first["criteria"]);
            var cond = first["conditions"].ToDictionary(c => (string)c["label"], c => (string)c["value"]);
            Assert.AreEqual("30 mm/h", cond["雨量"]);
            Assert.AreEqual("4.0 m/s", cond["風速"]);
            Assert.IsFalse(cond.Keys.Any(k => k.StartsWith("シード") || k.Contains("MODE")), "モードとシードは別の欄");
            Assert.AreEqual(2026, (int)first["seed"]);
            var reality = exps[2];
            Assert.AreEqual("reality", (string)reality["mode"]);
            Assert.IsTrue(reality["metrics"].Any(m => (string)m["key"] == "powerW"), "REALITY は電力も出す");

            // 開発者向け：部品の重さの合計、REALITY の電力と時間
            var dev = inv["developer"];
            Assert.Greater((float)dev["totalMassKg"], 0f);
            Assert.AreEqual(JTokenType.Float, dev["powerW"].Type);
            Assert.AreEqual(JTokenType.Null, dev["tipAngleDeg"].Type, "頭に付ける製品は転倒を計算しない");
            // 研究者向け：実験の前提と制限（REALITY の電力の求め方など）が計算の前提に入る
            Assert.IsTrue(inv["research"]["assumptions"].Any(a => ((string)a).Contains("電力")), string.Join(" / ", inv["research"]["assumptions"]));
            // 部品：種類の名前・役割・寸法
            var shield = inv["parts"].First(p => (string)p["id"] == "shield");
            Assert.AreEqual("キャノピー（覆い）", (string)shield["label"]);
            Assert.AreEqual(3, ((JArray)shield["sizeM"]).Count);
            Assert.IsNotEmpty((string)shield["role"]);
            // 機能対応表が無い設計でも、機能ごとの要望の行を作る
            Assert.IsNotEmpty((JArray)inv["needs"]);
        }

        [Test] public void SavedAndLoaded_KeepsTheAssumptions_ForTheResearchSection()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ilab-test-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var inv = RainStory();
                var before = inv.Experiments[2].Assumptions.Concat(inv.Experiments[2].Limits).Take(10).Count();
                Assert.Greater(before, 0, "REALITY の実験には前提がある");
                new InventionRepository(dir).Save(inv);
                var loaded = new InventionRepository(dir).Load(inv.Current.Id, out var err);
                Assert.IsNull(err);
                Assert.AreEqual(before, loaded.Experiments[2].Assumptions.Count + loaded.Experiments[2].Limits.Count, "保存して読み直しても前提が残る");
                var o = PublicationBuilder.Build(loaded, new PublishOptions());
                Assert.IsNotEmpty((JArray)o["invention"]["research"]["assumptions"]);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test] public void RatioCriteria_AreShownInPercentLikeTheMetrics()
        {
            Assert.AreEqual("日陰率 ≥ 70 %・体が受ける日射≤150W", PublicationBuilder.RatioCriteria("日陰率≥0.70・体が受ける日射≤150W"));
            Assert.AreEqual("検知率 ≥ 80 %", PublicationBuilder.RatioCriteria("検知率 ≥ 0.8"));
            Assert.AreEqual("最大の衝撃≤100G", PublicationBuilder.RatioCriteria("最大の衝撃≤100G"), "割合でない計測はそのまま");
        }

        [Test] public void Anonymous_WhenNoDisplayName()
        {
            var o = PublicationBuilder.Build(RainStory(), new PublishOptions { DisplayName = "  ", Anonymous = false });
            Assert.AreEqual(true, (bool)o["creator"]["anonymous"]);
            Assert.AreEqual("", (string)o["creator"]["displayName"]);
        }

        [Test] public void OldSave_WithoutRecords_DerivesChangesFromTheVersions()
        {
            var inv = RainStory();
            inv.RevisionNotes.Clear();
            inv.RequestText = "";
            var o = PublicationBuilder.Build(inv, new PublishOptions());
            Assert.AreEqual("", (string)o["invention"]["request"], "依頼文の記録なし");
            var rev3 = o["invention"]["revisions"][1];
            Assert.AreEqual("unknown", (string)rev3["source"]);
            Assert.AreEqual("", (string)rev3["request"]);
            var c = rev3["changes"][0];
            StringAssert.Contains("防護半径", (string)c["label"]);
            StringAssert.StartsWith("0.60", (string)c["before"]);
            StringAssert.StartsWith("0.80", (string)c["after"]);
        }

        [Test] public void LongTexts_AreCutToTheLibraryLimits()
        {
            var inv = RainStory();
            inv.RequestText = new string('あ', 1000);
            inv.Current.Name = "名前\n改行" + new string('い', 100);
            var o = PublicationBuilder.Build(inv, new PublishOptions());
            Assert.AreEqual(400, ((string)o["invention"]["request"]).Length);
            var name = (string)o["invention"]["name"];
            Assert.AreEqual(60, name.Length);
            Assert.IsFalse(name.Contains("\n"), "名前は1行");
            // サロゲートペアは途中で切らない
            Assert.AreEqual("あい", PublicationBuilder.Text("あい𠀋う", 3));
            Assert.AreEqual("10 秒", PublicationBuilder.Build(RainStory(), new PublishOptions())["invention"]["experiments"][0]["conditions"]
                .First(c => (string)c["label"] == "時間")["value"].ToString(), "数字で始まる条件は時間");
        }

        [Test] public void Representative_ScenarioExperiment_ShowsRatiosAsPercent()
        {
            var path = Path.Combine(Application.dataPath, "_ImpossibleLab/Tests/EditMode/Fixtures/representative/ai_r03.json");
            var outcome = ProxyProtocol.ParseDesign(200, File.ReadAllText(path), null);
            Assert.AreEqual(DesignStatus.Success, outcome.Status, outcome.Message);
            var dna = outcome.Dna;
            var plan = PlanEditor.Effective(PlanCandidates.Build(dna, outcome.Plans)[0].Plan, LabModes.Dream, 2026, dna);
            var run = ScenarioRunner.RunPlan(dna, plan);
            var inv = new SavedInvention { Current = dna, RequestText = "夏の通学路で日差しがきついので、頭の上で日陰を作ってくれる道具", DesignSource = "ai" };
            inv.Experiments.Add(run.Result);
            var o = PublicationBuilder.Build(inv, new PublishOptions());
            var exp = o["invention"]["experiments"][0];
            StringAssert.Contains("（", (string)exp["title"], "場面（主な条件）");
            var shade = exp["metrics"].First(m => (string)m["key"] == "shadeRatio");
            Assert.AreEqual("%", (string)shade["unit"]);
            Assert.AreEqual(((float)shade["value"] * 100f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), (string)shade["display"]);
            Assert.IsTrue(exp["conditions"].Any(), "条件の項目がある");
            Assert.AreEqual(run.Result.Passed, (bool?)exp["passed"]);
            // AI の機能対応表をそのまま使う
            Assert.AreEqual(dna.FunctionMap.Count, ((JArray)o["invention"]["needs"]).Count);
            Assert.AreEqual("ai", (string)o["invention"]["designSource"]);
        }
    }
}
