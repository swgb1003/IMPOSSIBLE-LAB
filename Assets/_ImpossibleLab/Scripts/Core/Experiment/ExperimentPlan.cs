using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ImpossibleLab.Core
{
    public sealed class PlanModule
    {
        public string Type = "";
        public Dictionary<string, float> Params = new Dictionary<string, float>();
        public float Get(string key) => Params.TryGetValue(key, out var v) ? v : ExperimentCatalog.Module(Type)?.Param(key)?.Default ?? 0f;
    }

    public sealed class PlanCriterion
    {
        public string Metric = "", Op = ">=", Reason = "";
        public float Value;
        public bool Check(float v) => Op == "<=" ? v <= Value + 1e-6f : v >= Value - 1e-6f;
    }

    /// <summary>
    /// 実験計画（docs/GENERAL_INVENTION_DESIGN.md 5.1）。AI が提案するのも、画面で組み立てるのもこの形。
    /// データとしてのみ扱い、検証（<see cref="ExperimentPlanJson"/>）を通ったものだけを実行する。
    /// </summary>
    public sealed class ExperimentPlan
    {
        public string Scenario = ExperimentCatalog.Stand;
        public string Mode = LabModes.Dream;
        public float DurationSec = 10f;
        public int Seed = 1234;
        public List<PlanModule> Modules = new List<PlanModule>();
        /// <summary>出す計測項目（空ならシナリオで出せるものすべて）。</summary>
        public List<string> Metrics = new List<string>();
        public List<PlanCriterion> Criteria = new List<PlanCriterion>();

        public PlanModule Module(string type) => Modules.Find(m => m.Type == type);
        public bool Has(string type) => Module(type) != null;
        public float Get(string type, string key) => Module(type)?.Get(key) ?? ExperimentCatalog.Module(type)?.Param(key)?.Default ?? 0f;

        /// <summary>風向（吹いていく方向）の単位ベクトル。ModeRules.WindDir と同じ式。</summary>
        public Vector2 WindDir()
        {
            float rad = Get("wind", "directionDeg") * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
        }

        PlanModule Add(string type, params (string key, float v)[] values)
        {
            var m = new PlanModule { Type = type };
            foreach (var (k, v) in values) m.Params[k] = v;
            Modules.Add(m);
            return m;
        }

        /// <summary>
        /// 今の実験設定画面（環境ごとの条件）から実験計画を作る。2〜3週目の互換用。
        /// 4週目以降は画面で実験計画を直接組み立てる。
        /// </summary>
        public static ExperimentPlan FromConfig(RainExperimentConfig config)
        {
            var c = config.Clamped();
            var p = new ExperimentPlan { Mode = c.Mode, Seed = c.Seed, DurationSec = c.DurationSec };
            if (c.Environment == LabEnvironments.ObstacleCourse)
            {
                p.Scenario = ExperimentCatalog.WalkRoute;
                p.Add("obstacles", ("count", c.ObstacleCount));
                p.Add("payload", ("kg", c.PayloadKg));
                p.Criteria.Add(new PlanCriterion { Metric = "collisionCount", Op = "<=", Value = 0f, Reason = "障害物に当たらない" });
                p.Criteria.Add(new PlanCriterion { Metric = "arrivalDelay", Op = "<=", Value = CourseSimulator.ArrivalLimitSec, Reason = "案内役から大きく遅れない" });
            }
            else if (c.Environment == LabEnvironments.FlightDeck)
            {
                p.Scenario = ExperimentCatalog.FlyToTarget;
                p.Add("target", ("distanceM", c.TargetDistance));
                p.Add("wind", ("speed", c.WindSpeed), ("directionDeg", c.WindDirectionDeg));
                p.Criteria.Add(new PlanCriterion { Metric = "landingError", Op = "<=", Value = CourseSimulator.LandingTolerance, Reason = "目標地点に着地する" });
                p.Criteria.Add(new PlanCriterion { Metric = "stabilityScore", Op = ">=", Value = CourseSimulator.StabilityPassScore, Reason = "揺れが小さい" });
            }
            else
            {
                p.Scenario = ExperimentCatalog.Stand;
                p.Add("rain", ("intensity", c.RainIntensity));
                p.Add("wind", ("speed", c.WindSpeed), ("directionDeg", c.WindDirectionDeg));
                p.Criteria.Add(new PlanCriterion { Metric = "coverageRatio", Op = ">=", Value = RainExperimentResult.SuccessCoverage, Reason = "頭上の雨の8割以上を防ぐ" });
            }
            return p;
        }
    }

    public static class ExperimentPlanConvert
    {
        /// <summary>
        /// 実験計画 → 今の実験設定（雨の実験 RainSimulator と、保存データの条件欄に使う）。FromConfig の逆。
        /// </summary>
        public static RainExperimentConfig ToConfig(this ExperimentPlan p)
        {
            var c = new RainExperimentConfig { Mode = p.Mode, Seed = p.Seed, DurationSec = p.DurationSec };
            switch (p.Scenario)
            {
                case ExperimentCatalog.WalkRoute:
                    c.Environment = LabEnvironments.ObstacleCourse;
                    c.ObstacleCount = Mathf.RoundToInt(p.Get("obstacles", "count"));
                    c.PayloadKg = p.Get("payload", "kg");
                    break;
                case ExperimentCatalog.FlyToTarget:
                    c.Environment = LabEnvironments.FlightDeck;
                    c.TargetDistance = p.Get("target", "distanceM");
                    c.WindSpeed = p.Get("wind", "speed"); c.WindDirectionDeg = p.Get("wind", "directionDeg");
                    break;
                default:
                    c.Environment = LabEnvironments.RainLab;
                    c.RainIntensity = p.Get("rain", "intensity");
                    c.WindSpeed = p.Get("wind", "speed"); c.WindDirectionDeg = p.Get("wind", "directionDeg");
                    break;
            }
            return c.Clamped();
        }
    }

    public sealed class PlanReport
    {
        public ExperimentPlan Plan;
        public readonly List<string> Errors = new List<string>();
        public bool IsValid => Errors.Count == 0 && Plan != null;
        public string Summary => IsValid ? "検証OK" : string.Join(" / ", Errors.Take(3));
    }

    /// <summary>実験計画の JSON 入出力と検証（AI の提案は必ずここを通す）。</summary>
    public static class ExperimentPlanJson
    {
        public const int MaxJsonLength = 8 * 1024;
        static readonly HashSet<string> RootFields = new HashSet<string> { "scenario", "mode", "durationSec", "seed", "modules", "metrics", "criteria" };

        public static PlanReport Parse(string json, InventionDna dna = null)
        {
            var r = new PlanReport();
            if (string.IsNullOrWhiteSpace(json)) { r.Errors.Add("実験計画が空です"); return r; }
            if (json.Length > MaxJsonLength) { r.Errors.Add("実験計画が大きすぎます"); return r; }
            JObject o;
            try
            {
                using (var reader = new JsonTextReader(new System.IO.StringReader(json)) { MaxDepth = 8, DateParseHandling = DateParseHandling.None })
                    o = JToken.ReadFrom(reader) as JObject;
            }
            catch (JsonException) { r.Errors.Add("実験計画の JSON 構文エラー"); return r; }
            if (o == null) { r.Errors.Add("実験計画はオブジェクトです"); return r; }
            foreach (var p in o.Properties()) if (!RootFields.Contains(p.Name)) r.Errors.Add("実験計画に未知のフィールド: " + Short(p.Name));

            var plan = new ExperimentPlan
            {
                Scenario = o.Value<string>("scenario") ?? "",
                Mode = o.Value<string>("mode") ?? LabModes.Dream,
            };
            if (o["durationSec"] != null) plan.DurationSec = Num(o["durationSec"], "durationSec", r);
            if (o["seed"] != null)
            {
                if (o["seed"].Type != JTokenType.Integer) r.Errors.Add("seed は整数です");
                else plan.Seed = (int)Mathf.Clamp(o.Value<long>("seed"), 1, 999999);
            }
            if (o["modules"] is JArray mods)
                foreach (var mt in mods.Take(ExperimentCatalog.MaxModules + 1))
                {
                    if (!(mt is JObject mo)) { r.Errors.Add("modules の要素はオブジェクトです"); continue; }
                    var m = new PlanModule { Type = mo.Value<string>("type") ?? "" };
                    foreach (var p in mo.Properties())
                        if (p.Name != "type") m.Params[p.Name] = Num(p.Value, m.Type + "." + p.Name, r);
                    plan.Modules.Add(m);
                }
            else if (o["modules"] != null) r.Errors.Add("modules は配列です");
            if (o["metrics"] is JArray mets)
                foreach (var mt in mets.Take(ExperimentCatalog.MaxMetrics + 1)) plan.Metrics.Add(mt.Type == JTokenType.String ? mt.Value<string>() : "");
            if (o["criteria"] is JArray crit)
                foreach (var ct in crit.Take(ExperimentCatalog.MaxCriteria + 1))
                {
                    if (!(ct is JObject co)) { r.Errors.Add("criteria の要素はオブジェクトです"); continue; }
                    plan.Criteria.Add(new PlanCriterion
                    {
                        Metric = co.Value<string>("metric") ?? "", Op = co.Value<string>("op") ?? "",
                        Value = Num(co["value"], "criteria.value", r), Reason = co.Value<string>("reason") ?? "",
                    });
                }
            if (r.Errors.Count == 0) Validate(plan, dna, r.Errors);
            if (r.Errors.Count == 0) r.Plan = plan;
            return r;
        }

        /// <summary>計画の中身の検証（JSON を経ない計画にも使う）。dna を渡すと、計測に必要な機能があるかも確かめる。</summary>
        public static void Validate(ExperimentPlan p, InventionDna dna, List<string> errors)
        {
            if (ExperimentCatalog.Scenario(p.Scenario) == null)
                errors.Add($"未対応のシナリオ: {Short(p.Scenario)}（使用可能: {string.Join(", ", ExperimentCatalog.Scenarios.Select(s => s.Id))}）");
            if (!LabModes.IsValid(p.Mode)) errors.Add($"未対応のモード: {Short(p.Mode)}");
            if (p.DurationSec < RainExperimentConfig.MinDuration || p.DurationSec > RainExperimentConfig.MaxDuration) errors.Add("durationSec が範囲外です");
            if (p.Modules.Count > ExperimentCatalog.MaxModules) errors.Add($"環境モジュールは{ExperimentCatalog.MaxModules}個までです");
            if (p.Metrics.Count > ExperimentCatalog.MaxMetrics) errors.Add($"計測項目は{ExperimentCatalog.MaxMetrics}個までです");
            if (p.Criteria.Count > ExperimentCatalog.MaxCriteria) errors.Add($"合格条件は{ExperimentCatalog.MaxCriteria}個までです");
            var seen = new HashSet<string>();
            foreach (var m in p.Modules)
            {
                var info = ExperimentCatalog.Module(m.Type);
                if (info == null) { errors.Add($"未対応の環境モジュール: {Short(m.Type)}"); continue; }
                if (!seen.Add(m.Type)) errors.Add($"環境モジュールが重複しています: {m.Type}");
                foreach (var kv in m.Params)
                {
                    var pi = info.Param(kv.Key);
                    if (pi == null) errors.Add($"{m.Type} に未対応の値: {Short(kv.Key)}");
                    else if (kv.Value < pi.Min || kv.Value > pi.Max) errors.Add($"{info.LabelJa}の{pi.LabelJa}が範囲外です（{pi.Min}〜{pi.Max}）");
                }
            }
            var metricSet = new HashSet<string>();
            foreach (var key in p.Metrics)
            {
                var mi = ExperimentCatalog.Metric(key);
                if (mi == null) { errors.Add($"未対応の計測項目: {Short(key)}"); continue; }
                if (!metricSet.Add(key)) errors.Add($"計測項目が重複しています: {key}");
                CheckMetric(mi, p, dna, errors);
            }
            foreach (var c in p.Criteria)
            {
                var mi = ExperimentCatalog.Metric(c.Metric);
                if (mi == null) { errors.Add($"合格条件に未対応の計測項目: {Short(c.Metric)}"); continue; }
                if (c.Op != ">=" && c.Op != "<=") errors.Add($"合格条件の比較は >= か <= です: {Short(c.Op)}");
                if (c.Value < mi.Min || c.Value > mi.Max) errors.Add($"合格条件の値が{mi.LabelJa}の範囲外です（{mi.Min}〜{mi.Max}）");
                if ((c.Reason ?? "").Length > ExperimentCatalog.MaxReasonLength) errors.Add("合格条件の理由が長すぎます");
                if (p.Metrics.Count > 0 && !metricSet.Contains(c.Metric)) errors.Add($"合格条件の {c.Metric} が計測項目にありません");
                if (p.Metrics.Count == 0) CheckMetric(mi, p, dna, errors);
            }
        }

        static void CheckMetric(MetricInfo mi, ExperimentPlan p, InventionDna dna, List<string> errors)
        {
            if (mi.Scenarios.Length > 0 && !mi.Scenarios.Contains(p.Scenario)) errors.Add($"{mi.LabelJa}はシナリオ {p.Scenario} では測れません");
            foreach (var need in mi.Modules) if (!p.Has(need)) errors.Add($"{mi.LabelJa}を測るには環境モジュール {need} が必要です");
            if (mi.RealityOnly && p.Mode != LabModes.Reality) errors.Add($"{mi.LabelJa}は REALITY MODE でのみ測れます");
            if (dna != null && mi.AnyBlocks.Length > 0 && !mi.AnyBlocks.Any(b => dna.FindBehavior(b) != null))
                errors.Add($"{mi.LabelJa}を測るには機能 {string.Join(" か ", mi.AnyBlocks)} が必要です");
        }

        public static JObject ToJObject(ExperimentPlan p) => new JObject
        {
            ["scenario"] = p.Scenario, ["mode"] = p.Mode, ["durationSec"] = p.DurationSec, ["seed"] = p.Seed,
            ["modules"] = new JArray(p.Modules.Select(m =>
            {
                var o = new JObject { ["type"] = m.Type };
                foreach (var kv in m.Params.OrderBy(k => k.Key, StringComparer.Ordinal)) o[kv.Key] = Math.Round(kv.Value, 4);
                return o;
            })),
            ["metrics"] = new JArray(p.Metrics),
            ["criteria"] = new JArray(p.Criteria.Select(c => new JObject { ["metric"] = c.Metric, ["op"] = c.Op, ["value"] = c.Value, ["reason"] = c.Reason ?? "" })),
        };

        public static string ToJson(ExperimentPlan p) => ToJObject(p).ToString(Formatting.Indented);

        static float Num(JToken t, string label, PlanReport r)
        {
            if (t == null || (t.Type != JTokenType.Integer && t.Type != JTokenType.Float)) { r.Errors.Add(label + " は数値です"); return 0f; }
            double d = t.Value<double>();
            if (double.IsNaN(d) || double.IsInfinity(d) || Math.Abs(d) > 1e6) { r.Errors.Add(label + " が不正な数値です"); return 0f; }
            return (float)d;
        }

        static string Short(string s) => s == null ? "" : s.Length > 40 ? s.Substring(0, 40) + "…" : s;
    }
}
