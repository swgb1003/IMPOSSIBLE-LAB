using System.IO;
using System.Linq;
using ImpossibleLab.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>5週目：AI v2（DNA v2・実験計画の提案・機能の追加/削除の改良）のアプリ側の受け取りと検証。API は呼ばない。</summary>
    public class AiV2Tests
    {
        const string NightDna = @"{ ""schemaVersion"": 2, ""id"": ""ai-night"", ""name"": ""夜道ライトリュック"", ""purpose"": ""足元を照らす"",
  ""tags"": [""lighting"", ""safety""], ""mount"": ""back"", ""appearance"": ""night_navy"", ""mode"": ""dream"",
  ""parts"": [
    { ""id"": ""pack"", ""type"": ""body"", ""shape"": ""box"", ""material"": ""fabric"", ""position"": [0, 0, 0], ""scale"": [0.3, 0.42, 0.14] },
    { ""id"": ""lamp"", ""type"": ""light"", ""shape"": ""cylinder"", ""material"": ""plastic"", ""position"": [0, -0.18, 0.08], ""scale"": [0.05, 0.03, 0.05] } ],
  ""behaviors"": [
    { ""type"": ""illuminate"", ""targetPart"": ""lamp"", ""parameters"": { ""lumens"": 150, ""tiltDeg"": 50 } },
    { ""type"": ""alert"", ""targetPart"": ""lamp"", ""parameters"": { ""lightLumens"": 30 } } ],
  ""functionMap"": [
    { ""need"": ""足元を照らす"", ""blocks"": [""illuminate""], ""status"": ""simulated"" },
    { ""need"": ""車に気づいてもらう"", ""blocks"": [""alert""], ""status"": ""simplified"", ""note"": ""見える距離を概算"" },
    { ""need"": ""雨でも使える"", ""blocks"": [], ""status"": ""unverifiable"", ""note"": ""防水は検証できない"" } ],
  ""constraints"": { ""maxParts"": 24, ""maxSpeed"": 3.0 }, ""revision"": 1, ""parentRevision"": null }";

        static InventionDna Night()
        {
            var r = DnaJson.Parse(NightDna);
            Assert.IsTrue(r.IsValid, r.Summary);
            return r.Dna;
        }

        static RevisionProposal Proposal(string opsJson)
        {
            Assert.IsTrue(RevisionEngine.TryParseProposal(@"{ ""summary"": ""テスト"", ""operations"": " + opsJson + " }", out var p, out var err), err);
            return p;
        }

        // ---------------- 改良：機能の追加・削除 ----------------
        [Test] public void Revision_AddsBehaviorWithPartAndClampsParameters()
        {
            var p = Proposal(@"[
  { ""op"": ""add_part"", ""part"": { ""id"": ""eye"", ""type"": ""sensor"", ""shape"": ""sphere"", ""position"": [0, 0.2, 0.08], ""scale"": [0.04, 0.04, 0.04] } },
  { ""op"": ""add_behavior"", ""behavior"": ""detect"", ""targetPart"": ""eye"", ""parameters"": { ""rangeM"": 99, ""fovDeg"": 120 } } ]");
            var r = RevisionEngine.Apply(Night(), p);
            Assert.IsTrue(r.Success, string.Join(" / ", r.Errors));
            var b = r.NewDna.FindBehavior("detect");
            Assert.AreEqual("eye", b.TargetPart);
            Assert.AreEqual(30f, b.Parameters["rangeM"], "範囲の上限に制限");
            Assert.AreEqual(120f, b.Parameters["fovDeg"]);
            Assert.IsTrue(r.Warnings.Any(w => w.Contains("検知距離")));
            Assert.IsTrue(r.Changes.Any(c => c.Label == "機能追加"));
            Assert.AreEqual(2, r.NewDna.Revision);
        }

        [Test] public void Revision_AddBehaviorAutoSelectsTargetAndRejectsBadRequests()
        {
            var auto = RevisionEngine.Apply(Night(), Proposal(@"[ { ""op"": ""add_behavior"", ""behavior"": ""sun_shade"" } ]"));
            Assert.IsFalse(auto.Success, "キャノピー・板がないと日よけは付けられない");
            StringAssert.Contains("先に部品を追加", string.Join(" ", auto.Errors));
            var dup = RevisionEngine.Apply(Night(), Proposal(@"[ { ""op"": ""add_behavior"", ""behavior"": ""illuminate"" } ]"));
            StringAssert.Contains("すでにあります", string.Join(" ", dup.Errors));
            var wrongPart = RevisionEngine.Apply(Night(), Proposal(@"[ { ""op"": ""add_behavior"", ""behavior"": ""heat"", ""targetPart"": ""lamp"" } ]"));
            StringAssert.Contains("付けられません", string.Join(" ", wrongPart.Errors));
            var unknownParam = RevisionEngine.Apply(Night(), Proposal(@"[ { ""op"": ""add_part"", ""part"": { ""id"": ""pad"", ""type"": ""heater"", ""position"": [0, 0, 0], ""scale"": [0.1, 0.1, 0.02] } },
  { ""op"": ""add_behavior"", ""behavior"": ""heat"", ""parameters"": { ""watts"": 10 } } ]"));
            StringAssert.Contains("未対応のパラメータ", string.Join(" ", unknownParam.Errors));
            var ok = RevisionEngine.Apply(Night(), Proposal(@"[ { ""op"": ""add_part"", ""part"": { ""id"": ""pad"", ""type"": ""heater"", ""position"": [0, 0, 0], ""scale"": [0.1, 0.1, 0.02] } },
  { ""op"": ""add_behavior"", ""behavior"": ""heat"" } ]"));
            Assert.IsTrue(ok.Success, string.Join(" / ", ok.Errors));
            Assert.AreEqual("pad", ok.NewDna.FindBehavior("heat").TargetPart, "対象部品を自動で選ぶ");
            Assert.AreEqual(LabCatalog.Behavior("heat").Param("powerW").Default, ok.NewDna.FindBehavior("heat").Parameters["powerW"]);
        }

        [Test] public void Revision_RemoveBehaviorMarksNeedUnverifiable()
        {
            var r = RevisionEngine.Apply(Night(), Proposal(@"[ { ""op"": ""remove_behavior"", ""behavior"": ""alert"" } ]"));
            Assert.IsTrue(r.Success, string.Join(" / ", r.Errors));
            Assert.IsNull(r.NewDna.FindBehavior("alert"));
            var need = r.NewDna.FunctionMap.First(n => n.Need == "車に気づいてもらう");
            Assert.AreEqual("unverifiable", need.Status);
            StringAssert.Contains("通知", need.Note);
            Assert.IsEmpty(need.Blocks);
            // そのあと部品（lamp）は照明が使っているので消せない
            var rm = RevisionEngine.Apply(r.NewDna, Proposal(@"[ { ""op"": ""remove_part"", ""partId"": ""lamp"" } ]"));
            Assert.IsFalse(rm.Success);
            Assert.IsFalse(RevisionEngine.TryParseProposal(@"{ ""operations"": [ { ""op"": ""add_behavior"", ""behavior"": ""heat"", ""parameters"": { ""powerW"": ""hot"" } } ] }", out _, out var err));
            StringAssert.Contains("数値", err);
            Assert.IsFalse(RevisionEngine.TryParseProposal(@"{ ""operations"": [ { ""op"": ""remove_behavior"" } ] }", out _, out _), "behavior がない");
        }

        // ---------------- 設計の応答：DNA v2 と実験計画 ----------------
        [Test] public void ParseDesign_AcceptsV2DnaAndValidatesEachPlan()
        {
            var body = new JObject
            {
                ["status"] = "ok", ["message"] = "夜道を照らすリュックです", ["designPoints"] = new JArray("背中の下から足元へ"),
                ["dna"] = JObject.Parse(NightDna),
                ["plans"] = new JArray
                {
                    JObject.Parse(@"{ ""title"": ""夜道の足元"", ""aim"": ""足元が見えるか"", ""plan"": { ""scenario"": ""stand"", ""mode"": ""dream"", ""durationSec"": 10, ""seed"": 1234,
                        ""modules"": [ { ""type"": ""darkness"", ""lux"": 2 } ], ""metrics"": [ ""footIlluminance"", ""alertRange"" ],
                        ""criteria"": [ { ""metric"": ""footIlluminance"", ""op"": "">="", ""value"": 10, ""reason"": ""段差が見える（目安）"" } ] } }"),
                    JObject.Parse(@"{ ""title"": ""寒さ"", ""aim"": ""温まるか"", ""plan"": { ""scenario"": ""sit_still"", ""modules"": [ { ""type"": ""temperature"", ""ambientC"": 0 } ], ""metrics"": [ ""temperatureGain"" ] } }"),
                    JObject.Parse(@"{ ""title"": ""壊れた案"" }"),
                },
                ["fixes"] = new JArray(), ["usage"] = new JObject { ["costUsd"] = 0.05 },
            };
            var o = ProxyProtocol.ParseDesign(200, body.ToString(), null);
            Assert.AreEqual(DesignStatus.Success, o.Status, o.Message);
            Assert.AreEqual(InventionDna.CurrentSchemaVersion, o.Dna.SchemaVersion); // v2 で受け取り、v3 に変換して持つ
            Assert.AreEqual("back", o.Dna.Mount);
            Assert.AreEqual(3, o.Dna.FunctionMap.Count);
            Assert.AreEqual(1, o.Plans.Count);
            Assert.AreEqual("夜道の足元", o.Plans[0].Title);
            var run = ScenarioRunner.RunPlan(o.Dna, o.Plans[0].Plan);
            Assert.IsTrue(run.Result.Passed, "提案された計画をそのまま実行できる");
            Assert.AreEqual(2, o.PlanErrors.Count);
            StringAssert.Contains("heat か cool", o.PlanErrors[0], "加温・冷却がない発明に温度の計画は使えない");
            StringAssert.Contains("形式", o.PlanErrors[1]);
        }

        [Test] public void ReviseRequest_SendsScenarioResults()
        {
            var dna = Night();
            var plan = new ExperimentPlan { Scenario = ExperimentCatalog.Stand };
            plan.Modules.Add(new PlanModule { Type = "darkness", Params = { ["lux"] = 2f } });
            var run = ScenarioRunner.RunPlan(dna, plan);
            var o = JObject.Parse(ProxyProtocol.ReviseRequestJson(dna, run.Result, "もっと明るく"));
            var lr = (JObject)o["lastResult"];
            Assert.AreEqual(LabEnvironments.ScenarioLab, (string)lr["environment"]);
            Assert.AreEqual("stand", (string)lr["scenario"]);
            Assert.AreEqual(true, (bool)lr["passed"]);
            Assert.IsNotNull(lr["footIlluminance"]);
            Assert.IsNull(lr["targetDistance"], "飛行の条件は送らない");
            var pr = JObject.Parse(ProxyProtocol.PlanRequestJson(dna, "車からの見え方"));
            Assert.AreEqual("車からの見え方", (string)pr["wish"]);
        }

        // ---------------- 評価セットの集計（API は呼ばない。合成した応答で確かめる） ----------------
        [Test] public void EvalAnalyzer_CountsOutcomesPlansAndNeeds()
        {
            var okBody = new JObject
            {
                ["status"] = "ok", ["model"] = "claude-opus-5-5", ["dna"] = JObject.Parse(NightDna), ["fixes"] = new JArray("x"),
                ["plans"] = new JArray(JObject.Parse(@"{ ""title"": ""足元"", ""plan"": { ""scenario"": ""stand"", ""modules"": [ { ""type"": ""darkness"", ""lux"": 2 } ], ""metrics"": [ ""footIlluminance"" ] } }"),
                                       JObject.Parse(@"{ ""title"": ""雨"", ""plan"": { ""scenario"": ""stand"", ""modules"": [ { ""type"": ""rain"" }, { ""type"": ""darkness"" } ] } }")),
            };
            JObject Line(string id, string kind, long http, string body, string err = "") => new JObject
            {
                ["id"] = id, ["group"] = "g", ["request"] = "依頼" + id, ["expect"] = new JObject { ["kind"] = kind, ["blocks"] = new JArray("illuminate", "heat") },
                ["httpStatus"] = http, ["ms"] = 20000, ["transportError"] = err, ["body"] = body, ["cost"] = 0.1,
            };
            var records = new[]
            {
                Line("a", "design", 200, okBody.ToString()),
                Line("b", "unsupported", 200, @"{ ""status"": ""unsupported"", ""message"": ""危険な用途のため設計しません"" }"),
                Line("c", "design", 200, @"{ ""status"": ""ok"", ""dna"": { ""schemaVersion"": 2 } }"),
                Line("d", "design", 0, "", "connect ECONNREFUSED"),
            }.Select(j => AiEvalRecord.FromJsonLine(j.ToString(Newtonsoft.Json.Formatting.None))).ToList();
            var rows = AiEvalAnalyzer.Analyze(records);
            Assert.AreEqual("設計OK", rows[0].Status);
            Assert.AreEqual(2, rows[0].PlansProposed);
            Assert.AreEqual(1, rows[0].PlansValid, "雨と暗さの組み合わせは検証で止まる");
            Assert.AreEqual(1, rows[0].PlansRan);
            Assert.AreEqual(1, rows[0].Fixes);
            Assert.AreEqual(1, rows[0].NeedsUnverifiable);
            Assert.AreEqual(0.5f, rows[0].BlockCoverage, 1e-6f, "使ってほしい機能 2 つのうち照明だけ");
            Assert.IsTrue(rows[1].Correct, "断るべき依頼を断った");
            Assert.AreEqual("検証NG", rows[2].Status);
            Assert.AreEqual("通信エラー", rows[3].Status);
            string md = AiEvalAnalyzer.Markdown(rows, "test.jsonl", "claude-opus-5-5");
            StringAssert.Contains("① 設計が検証を通った（設計してほしい 3 件のうち） | 1 件（33%）", md);
            StringAssert.Contains("② 実験計画が検証を通った（提案 2 案のうち） | 1 案（50%）", md);
            StringAssert.Contains("$0.40", md);
            string csv = AiEvalAnalyzer.RatingCsv(rows);
            Assert.AreEqual(5, csv.Split('\n').Count(l => l.Trim().Length > 0), "見出し＋4件");
            StringAssert.Contains("合致度(1-5)", csv);
        }

        // ---------------- カタログ：プロキシの catalog.json が最新 ----------------
        [Test] public void ProxyCatalogFile_IsUpToDate()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "proxy", "catalog.json"));
            Assert.IsTrue(File.Exists(path), path);
            string onDisk = File.ReadAllText(path).Replace("\r\n", "\n").Trim();
            string now = LabCatalog.ToProxyJson().Replace("\r\n", "\n").Trim();
            Assert.AreEqual(now, onDisk, "proxy/catalog.json が古い：Unity で ProjectSetup.ExportProxyCatalog を実行してください");
        }
    }
}
