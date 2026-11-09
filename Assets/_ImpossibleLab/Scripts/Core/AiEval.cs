using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace ImpossibleLab.Core
{
    /// <summary>AI 評価セットの1件（proxy/eval/run-eval.ts が保存した JSONL の1行）。</summary>
    public sealed class AiEvalRecord
    {
        public string Id = "", Group = "", Request = "", ExpectKind = "design", TransportError = "", Body = "";
        public List<string> ExpectBlocks = new List<string>();
        public long HttpStatus;
        public double Ms, CostUsd;

        public static AiEvalRecord FromJsonLine(string line)
        {
            var o = JObject.Parse(line);
            var r = new AiEvalRecord
            {
                Id = o.Value<string>("id") ?? "", Group = o.Value<string>("group") ?? "", Request = o.Value<string>("request") ?? "",
                HttpStatus = o.Value<long?>("httpStatus") ?? 0, Ms = o.Value<double?>("ms") ?? 0, CostUsd = o.Value<double?>("cost") ?? 0,
                TransportError = o.Value<string>("transportError") ?? "", Body = o.Value<string>("body") ?? "",
            };
            if (o["expect"] is JObject e)
            {
                r.ExpectKind = e.Value<string>("kind") ?? "design";
                if (e["blocks"] is JArray b) r.ExpectBlocks = b.Select(x => (string)x).ToList();
            }
            return r;
        }
    }

    /// <summary>1件の判定（アプリの検証器を通した結果）。</summary>
    public sealed class AiEvalRow
    {
        public AiEvalRecord Rec;
        public DesignOutcome Outcome;
        public string Status = "";
        public int PlansProposed, PlansValid, PlansRan, PlansPassed, Fixes, Warnings;
        public int NeedsSimulated, NeedsSimplified, NeedsUnverifiable;
        public float BlockCoverage = -1f;
        public readonly List<string> Notes = new List<string>();
        public bool ExpectedDesign => Rec.ExpectKind != "unsupported";
        public bool Correct => ExpectedDesign ? Status == "設計OK" : Status == "断った";
    }

    /// <summary>
    /// AI 評価（docs/GENERAL_INVENTION_DESIGN.md 9章）：①設計が検証を通る割合 ②実験計画が通る割合 ③機能対応表の「検証できない」割合
    /// ④所要時間・費用 ⑤依頼との合致度（人の5段階評価、CSV に記入）を、アプリと同じ検証器で数える。
    /// </summary>
    public static class AiEvalAnalyzer
    {
        public static List<AiEvalRow> Analyze(IEnumerable<AiEvalRecord> records)
        {
            var rows = new List<AiEvalRow>();
            foreach (var rec in records)
            {
                var row = new AiEvalRow { Rec = rec };
                rows.Add(row);
                var o = ProxyProtocol.ParseDesign(rec.HttpStatus, string.IsNullOrEmpty(rec.TransportError) ? rec.Body : null, rec.TransportError);
                row.Outcome = o;
                switch (o.Status)
                {
                    case DesignStatus.Success: row.Status = "設計OK"; break;
                    case DesignStatus.Unsupported: row.Status = "断った"; break;
                    case DesignStatus.Offline: row.Status = "通信エラー"; row.Notes.Add(o.Message); continue;
                    default: row.Status = "検証NG"; row.Notes.Add(o.Message); continue;
                }
                if (o.Status != DesignStatus.Success) continue;
                try
                {
                    var body = JObject.Parse(rec.Body);
                    row.Fixes = (body["fixes"] as JArray)?.Count ?? 0;
                    row.PlansProposed = Math.Min((body["plans"] as JArray)?.Count ?? 0, ProxyProtocol.MaxPlans);
                }
                catch (Newtonsoft.Json.JsonException) { }
                row.Warnings = o.Report?.Warnings.Count ?? 0;
                row.PlansValid = o.Plans.Count;
                foreach (var e in o.PlanErrors) row.Notes.Add("計画NG：" + e);
                var dna = o.Dna;
                row.NeedsSimulated = dna.FunctionMap.Count(n => n.Status == "simulated");
                row.NeedsSimplified = dna.FunctionMap.Count(n => n.Status == "simplified");
                row.NeedsUnverifiable = dna.FunctionMap.Count(n => n.Status == "unverifiable");
                if (rec.ExpectBlocks.Count > 0) row.BlockCoverage = (float)rec.ExpectBlocks.Count(b => dna.FindBehavior(b) != null) / rec.ExpectBlocks.Count;
                foreach (var pp in o.Plans)
                {
                    try
                    {
                        bool passed;
                        if (pp.Plan.Has("rain")) passed = new RainSimulator(dna, pp.Plan.ToConfig()).RunToEnd().Success;
                        else passed = ScenarioRunner.RunPlan(dna, pp.Plan).Result.Passed == true;
                        row.PlansRan++;
                        if (passed) row.PlansPassed++;
                    }
                    catch (Exception ex) { row.Notes.Add($"実行エラー（{pp.Title}）：{ex.Message}"); }
                }
            }
            return rows;
        }

        static string Pct(int a, int b) => b == 0 ? "—" : (100.0 * a / b).ToString("0", CultureInfo.InvariantCulture) + "%";
        static string F(double v, string fmt = "0.0") => v.ToString(fmt, CultureInfo.InvariantCulture);
        static string Cell(string s) => (s ?? "").Replace("|", "／").Replace("\n", " ");

        public static string Markdown(List<AiEvalRow> rows, string sourceName, string model = "")
        {
            var sb = new StringBuilder();
            var design = rows.Where(r => r.ExpectedDesign).ToList();
            var refuse = rows.Where(r => !r.ExpectedDesign).ToList();
            var ok = rows.Where(r => r.Status == "設計OK").ToList();
            int needs = ok.Sum(r => r.NeedsSimulated + r.NeedsSimplified + r.NeedsUnverifiable);
            int proposed = ok.Sum(r => r.PlansProposed), valid = ok.Sum(r => r.PlansValid), ran = ok.Sum(r => r.PlansRan), passed = ok.Sum(r => r.PlansPassed);
            var times = rows.Where(r => r.Rec.HttpStatus > 0).Select(r => r.Rec.Ms / 1000.0).OrderBy(x => x).ToList();
            double cost = rows.Sum(r => r.Rec.CostUsd);
            var cov = design.Where(r => r.BlockCoverage >= 0f && r.Status == "設計OK").Select(r => r.BlockCoverage).ToList();

            sb.AppendLine("# AI 評価セットの結果");
            sb.AppendLine();
            sb.AppendLine($"元データ：`{sourceName}`（{rows.Count} 件）{(model.Length > 0 ? "／モデル " + model : "")}。集計：`AiEvalAnalyzer`（アプリと同じ検証器）。");
            sb.AppendLine();
            sb.AppendLine("## 1. まとめ");
            sb.AppendLine();
            sb.AppendLine("| 指標 | 結果 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine($"| ① 設計が検証を通った（設計してほしい {design.Count} 件のうち） | {design.Count(r => r.Status == "設計OK")} 件（{Pct(design.Count(r => r.Status == "設計OK"), design.Count)}） |");
            sb.AppendLine($"| 　断るべき依頼を断った（{refuse.Count} 件のうち） | {refuse.Count(r => r.Status == "断った")} 件 |");
            sb.AppendLine($"| 　設計してほしい依頼を断った・検証NG・通信エラー | {design.Count(r => r.Status == "断った")} ・ {rows.Count(r => r.Status == "検証NG")} ・ {rows.Count(r => r.Status == "通信エラー")} 件 |");
            sb.AppendLine($"| ② 実験計画が検証を通った（提案 {proposed} 案のうち） | {valid} 案（{Pct(valid, proposed)}） |");
            sb.AppendLine($"| 　検証を通った計画が1つ以上ある設計 | {ok.Count(r => r.PlansValid > 0)} / {ok.Count} 件（{Pct(ok.Count(r => r.PlansValid > 0), ok.Count)}） |");
            sb.AppendLine($"| 　検証を通った計画を実行できた・そのうち合格 | {ran} 案・{passed} 案 |");
            sb.AppendLine($"| ③ 機能対応表の「検証できない」（要望 {needs} 件のうち） | {ok.Sum(r => r.NeedsUnverifiable)} 件（{Pct(ok.Sum(r => r.NeedsUnverifiable), needs)}）／単純化 {ok.Sum(r => r.NeedsSimplified)} 件／検証する {ok.Sum(r => r.NeedsSimulated)} 件 |");
            sb.AppendLine($"| 　使ってほしい機能が設計に入った割合（参考、平均） | {(cov.Count > 0 ? F(100.0 * cov.Average(), "0") + "%" : "—")} |");
            sb.AppendLine($"| 　プロキシで補正した食い違い・アプリで補正した範囲外の値 | {ok.Sum(r => r.Fixes)} 件・{ok.Sum(r => r.Warnings)} 件 |");
            if (times.Count > 0)
                sb.AppendLine($"| ④ 所要時間（1件） | 平均 {F(times.Average())} 秒・中央値 {F(times[times.Count / 2])} 秒・最大 {F(times.Last())} 秒 |");
            sb.AppendLine($"| 　費用（目安） | 合計 ${F(cost, "0.00")}・1件あたり ${F(rows.Count > 0 ? cost / rows.Count : 0, "0.000")} |");
            sb.AppendLine("| ⑤ 依頼との合致度（人の5段階評価） | 未実施（同じ名前の .csv に記入する） |");
            sb.AppendLine();
            sb.AppendLine("## 2. 1件ごと");
            sb.AppendLine();
            sb.AppendLine("| id | 分類 | 判定 | 発明の名前 | 機能 | 計画（通過/提案） | 実行・合格 | 要望（検証/単純化/不可） | 時間 | 費用 |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            foreach (var r in rows)
            {
                var d = r.Outcome?.Dna;
                string mark = r.Correct ? "" : " ⚠";
                sb.AppendLine($"| {r.Rec.Id} | {r.Rec.Group} | {r.Status}{mark} | {Cell(d?.Name ?? "")} | {Cell(d != null ? string.Join(", ", d.Behaviors.Select(b => b.Type)) : "")} | {r.PlansValid}/{r.PlansProposed} | {r.PlansRan}・{r.PlansPassed} | {r.NeedsSimulated}/{r.NeedsSimplified}/{r.NeedsUnverifiable} | {F(r.Rec.Ms / 1000.0)}s | ${F(r.Rec.CostUsd, "0.000")} |");
            }
            var noted = rows.Where(r => r.Notes.Count > 0 || r.Status == "断った").ToList();
            if (noted.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## 3. 失敗・断った理由");
                sb.AppendLine();
                foreach (var r in noted)
                {
                    if (r.Status == "断った") sb.AppendLine($"- {r.Rec.Id}（{r.Rec.Group}）断った：{Cell(r.Outcome?.Message)}");
                    foreach (var n in r.Notes.Take(4)) sb.AppendLine($"- {r.Rec.Id}（{r.Rec.Group}）{Cell(n)}");
                }
            }
            return sb.ToString();
        }

        /// <summary>人の評価用の表（UTF-8 BOM 付き CSV、Excel で開ける）。合致度・コメントの列を空けておく。</summary>
        public static string RatingCsv(List<AiEvalRow> rows)
        {
            string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
            var sb = new StringBuilder("﻿");
            sb.AppendLine("id,分類,依頼,判定,発明の名前,目的,機能,機能対応表,実験計画,合致度(1-5),コメント");
            foreach (var r in rows)
            {
                var d = r.Outcome?.Dna;
                string map = d == null ? "" : string.Join(" / ", d.FunctionMap.Select(n => $"{n.Need}:{LabCatalog.FunctionStatusJa(n.Status)}"));
                string plans = r.Outcome == null ? "" : string.Join(" / ", r.Outcome.Plans.Select(p => $"{p.Title}（{p.Plan.Scenario}）"));
                sb.AppendLine(string.Join(",", Q(r.Rec.Id), Q(r.Rec.Group), Q(r.Rec.Request), Q(r.Status), Q(d?.Name), Q(d?.Purpose),
                    Q(d != null ? string.Join(" ", d.Behaviors.Select(b => LabCatalog.Behavior(b.Type)?.LabelJa ?? b.Type)) : r.Outcome?.Message), Q(map), Q(plans), "", ""));
            }
            return sb.ToString();
        }
    }
}
