using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace ImpossibleLab.Core
{
    /// <summary>S04 に並べる実験計画の候補1件（AI の提案、または発明の機能から決まる標準の計画）。</summary>
    public sealed class PlanCandidate
    {
        public string Title = "", Aim = "";
        public bool FromAi;
        public ExperimentPlan Plan;
        /// <summary>既存の実験場（雨・障害物コース・飛行デッキ）で動く計画か。条件は RainExperimentConfig で持つ。</summary>
        public bool Legacy => PlanCandidates.IsLegacy(Plan);
    }

    /// <summary>
    /// 実験計画の候補づくり（6週目 S04）。AI がなくても、発明にある機能ブロックから標準の計画を作る。
    /// どの候補も ExperimentPlanJson.Validate を発明つきで通ったものだけ。
    /// </summary>
    public static class PlanCandidates
    {
        public const int MaxCandidates = 6;

        /// <summary>雨・運搬・移動の既存の実験場で実行する計画か（画面の条件は RainExperimentConfig と同期する）。</summary>
        public static bool IsLegacy(ExperimentPlan p) => p != null &&
            (p.Scenario == ExperimentCatalog.WalkRoute || p.Scenario == ExperimentCatalog.FlyToTarget || (p.Scenario == ExperimentCatalog.Stand && p.Has("rain")));

        public static string LegacyEnvironment(ExperimentPlan p) =>
            p.Scenario == ExperimentCatalog.WalkRoute ? LabEnvironments.ObstacleCourse
            : p.Scenario == ExperimentCatalog.FlyToTarget ? LabEnvironments.FlightDeck : LabEnvironments.RainLab;

        /// <summary>
        /// 候補を作る。AI の提案（今の設計で検証し直す）→ 機能から決まる標準の計画（AI 案と同じ組み合わせは省く）の順。
        /// 通らなかった AI 案の理由は rejected に入れる（黙って捨てない）。
        /// </summary>
        public static List<PlanCandidate> Build(InventionDna dna, IEnumerable<ProposedPlan> aiPlans, List<string> rejected = null)
        {
            var list = new List<PlanCandidate>();
            if (dna == null) return list;
            foreach (var ai in aiPlans ?? Enumerable.Empty<ProposedPlan>())
            {
                if (ai?.Plan == null) continue;
                var errors = new List<string>();
                ExperimentPlanJson.Validate(ai.Plan, dna, errors);
                if (errors.Count > 0) { rejected?.Add($"AI案「{Short(ai.Title, 20)}」：{errors[0]}"); continue; }
                var plan = PlanEditor.Clone(ai.Plan);
                FitPayload(plan, dna);
                list.Add(new PlanCandidate { Title = string.IsNullOrWhiteSpace(ai.Title) ? ScenarioLabel(ai.Plan.Scenario) : Short(ai.Title, 24), Aim = Short(ai.Aim, 80), FromAi = true, Plan = plan });
            }
            foreach (var c in Standard(dna))
            {
                if (list.Any(x => SameShape(x.Plan, c.Plan))) continue;
                var errors = new List<string>();
                ExperimentPlanJson.Validate(c.Plan, dna, errors);
                if (errors.Count == 0) list.Add(c);
            }
            return list.Take(MaxCandidates).ToList();
        }

        /// <summary>坂の実験の荷物を、格納部に入る大きさまで小さくする（AI 案・条件の追加）。</summary>
        public static void FitPayload(ExperimentPlan plan, InventionDna dna)
        {
            if (plan?.Scenario != ExperimentCatalog.ClimbSlope) return;
            var m = plan.Modules.FirstOrDefault(x => x.Type == "payload");
            if (m == null || !m.Params.TryGetValue("sizeM", out var size)) return;
            m.Params["sizeM"] = StabilityModel.FitPayloadSize(dna, size);
        }

        /// <summary>機能ブロックから決まる標準の計画。既存3カテゴリの実験場は、機能が欠けていても（結果に理由を出すため）用意する。</summary>
        public static IEnumerable<PlanCandidate> Standard(InventionDna dna)
        {
            bool Has(string b) => dna.FindBehavior(b) != null;
            string cat = dna.Category;
            // 動作プログラム（仕様 28.7）：近づいて離れる。動きの手順がある発明では最初の候補にする
            if (Features.MotionProgram && dna.Program != null)
                yield return C("近づいて離れる", "人が近づいたとき、期待どおりに動くか（反応時間・期待の達成率）", ExperimentCatalog.ApproachDevice,
                    M("approach", ("count", 3f), ("speed", 1f), ("nearM", 0.5f), ("farM", 3f), ("dwellSec", 2f)));
            if (Has("rain_shield") || cat == "protection")
                yield return C("雨と風の中に立つ", "頭上の雨をどれだけ防げるか（防護率）", ExperimentCatalog.Stand,
                    M("rain", ("intensity", 20f)), M("wind", ("speed", 3f), ("directionDeg", 90f)));
            if (Has("follow_user") || cat == "carrier")
                yield return C("障害物コースを歩く", "ぶつからずに、遅れずについて来られるか", ExperimentCatalog.WalkRoute,
                    M("obstacles", ("count", 4f)), M("payload", ("kg", 5f)));
            if (Has("hover") || cat == "mobility")
                yield return C("目標まで飛ぶ", "横風の中で、揺れずに目標へ着地できるか", ExperimentCatalog.FlyToTarget,
                    M("target", ("distanceM", 7f)), M("wind", ("speed", 4f), ("directionDeg", 0f)));
            if (Has("illuminate"))
                yield return C("暗い道で足元を照らす", "夜の屋外（約 2 lx）で足元が見える明るさになるか", ExperimentCatalog.Stand, M("darkness", ("lux", 2f)));
            else if (Has("alert"))
                yield return C("夜道で知らせる", "暗い中で、周りの人に気づいてもらえる距離", ExperimentCatalog.Stand, M("darkness", ("lux", 2f)));
            if (Has("sun_shade"))
                yield return C("日差しの下に立つ", "体のどれだけを日陰にできるか", ExperimentCatalog.Stand,
                    M("sunlight", ("elevationDeg", 60f), ("azimuthDeg", 180f), ("irradiance", 800f)));
            if (Has("detect"))
                yield return C("周りの物に気づく", "周りに置いた対象のうち、いくつに気づけるか", ExperimentCatalog.Stand,
                    M("targets", ("count", 6f)), M("obstacles", ("count", 2f)));
            if (Has("heat"))
                yield return C("寒い場所で座って過ごす", "気温 5 °C で、15 分以内に 5 °C 温められるか", ExperimentCatalog.SitStill,
                    M("temperature", ("ambientC", 5f), ("targetDeltaC", 5f), ("minutes", 15f)));
            else if (Has("cool"))
                yield return C("暑い場所で座って過ごす", "気温 35 °C で、15 分以内に 5 °C 冷やせるか", ExperimentCatalog.SitStill,
                    M("temperature", ("ambientC", 35f), ("targetDeltaC", 5f), ("minutes", 15f)));
            if (Has("impact_cushion"))
                yield return C("落としたときの衝撃", "1 m から落としても、中身が壊れにくい衝撃に収まるか", ExperimentCatalog.DropTest,
                    M("drop", ("heightM", 1f), ("massKg", 0.5f)));
            if (Has("drive"))
            {
                // 車いす用スロープの最大勾配（ADA 1:12、約 4.76°）。運ぶ機能があれば荷物を載せる
                float ramp = BlockCoefficients.V("rampMaxSlopeDeg");
                var mods = new List<PlanModule> { M("slope", ("angleDeg", ramp), ("lengthM", 5f)) };
                // 荷物の大きさは格納部に入る大きさにする（入らない箱を上に載せて上るのは、運ぶ道具の使い方と違う）
                if (Has("carry_load")) mods.Add(M("payload", ("kg", 5f), ("sizeM", StabilityModel.FitPayloadSize(dna, 0.3f))));
                yield return C("スロープを上る", Has("carry_load") ? "車いす用スロープの上限の坂（約 4.8°）を、5 kg の荷物を載せて上れるか" : "車いす用スロープの上限の坂（約 4.8°）を上れるか",
                    ExperimentCatalog.ClimbSlope, mods.ToArray());
            }
            if (Has("grip") || Has("lift_load"))
                yield return C("荷物を棚へ上げる", "5 kg の荷物を落とさずに棚（0.8 m）へ上げられるか", ExperimentCatalog.LiftTask,
                    M("payload", ("kg", 5f), ("sizeM", 0.3f)), M("shelf", ("heightM", 0.8f)));
            // 電気を使う発明は、1日の使い方で電池がもつかを見る
            if (EnergyModel.UsesPower(dna))
                yield return C("1日使う", "1日 4 時間使って、毎日の充電でもつか", ExperimentCatalog.DailyUse,
                    M("usage", ("hoursPerDay", 4f), ("activePercent", 20f), ("sunHours", 2f)));
        }

        static PlanCandidate C(string title, string aim, string scenario, params PlanModule[] modules)
        {
            var p = new ExperimentPlan { Scenario = scenario };
            p.Modules.AddRange(modules);
            return new PlanCandidate { Title = title, Aim = aim, Plan = p };
        }

        static PlanModule M(string type, params (string key, float v)[] values)
        {
            var m = new PlanModule { Type = type };
            foreach (var (k, v) in values) m.Params[k] = v;
            return m;
        }

        /// <summary>シナリオと環境モジュールの種類が同じか（値は問わない）。</summary>
        public static bool SameShape(ExperimentPlan a, ExperimentPlan b) =>
            a != null && b != null && a.Scenario == b.Scenario &&
            a.Modules.Select(m => m.Type).OrderBy(t => t, StringComparer.Ordinal).SequenceEqual(b.Modules.Select(m => m.Type).OrderBy(t => t, StringComparer.Ordinal));

        /// <summary>同じ条件の実験か（モード・合格条件・計測項目は問わない）を比べるための文字列。</summary>
        public static string ConditionKey(ExperimentPlan p)
        {
            if (p == null) return "";
            var ci = CultureInfo.InvariantCulture;
            var mods = p.Modules.OrderBy(m => m.Type, StringComparer.Ordinal).Select(m =>
                m.Type + ":" + string.Join(",", m.Params.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + "=" + Math.Round(k.Value, 4).ToString(ci))));
            return $"{p.Scenario}|{p.Seed}|{p.DurationSec.ToString("0.###", ci)}|{string.Join(";", mods)}";
        }

        /// <summary>既存の実験場で、モデルが使う機能（計測項目の AnyBlocks に出てこないものを含む）。</summary>
        static readonly Dictionary<string, string[]> LegacyBlocks = new Dictionary<string, string[]>
        {
            [ExperimentCatalog.Stand] = new[] { "rain_shield", "follow_head", "stabilize" },
            [ExperimentCatalog.WalkRoute] = new[] { "follow_user", "avoid_obstacle", "carry_load" },
            [ExperimentCatalog.FlyToTarget] = new[] { "hover", "move_forward", "stabilize" },
        };

        /// <summary>
        /// この計画で、発明のどの機能が実際に動いて結果に効くか。組み立て式の実験は「測れる計測項目が必要とする機能」、
        /// 既存の実験場はそのモデルが使う機能。
        /// </summary>
        public static HashSet<string> BlocksExercised(ExperimentPlan plan, InventionDna dna)
        {
            var set = new HashSet<string>();
            if (plan == null || dna == null) return set;
            if (IsLegacy(plan) && LegacyBlocks.TryGetValue(plan.Scenario, out var used))
            {
                foreach (var b in used) if (dna.FindBehavior(b) != null) set.Add(b);
                return set;
            }
            foreach (var m in PlanEditor.ExpectedMetrics(plan, dna))
                foreach (var b in m.AnyBlocks) if (dna.FindBehavior(b) != null) set.Add(b);
            return set;
        }

        public static string ScenarioLabel(string id) => ExperimentCatalog.Scenario(id)?.LabelJa ?? id;

        static string Short(string s, int max) => string.IsNullOrEmpty(s) ? "" : s.Length > max ? s.Substring(0, max) + "…" : s;
    }

    /// <summary>S04 での実験計画の編集（値の増減・環境モジュールの追加と削除・モードに合わない計測の除外）。</summary>
    public static class PlanEditor
    {
        public static ExperimentPlan Clone(ExperimentPlan p) => p == null ? null : new ExperimentPlan
        {
            Scenario = p.Scenario, Mode = p.Mode, DurationSec = p.DurationSec, Seed = p.Seed,
            Modules = p.Modules.Select(m => new PlanModule { Type = m.Type, Params = new Dictionary<string, float>(m.Params) }).ToList(),
            Metrics = new List<string>(p.Metrics),
            Criteria = p.Criteria.Select(c => new PlanCriterion { Metric = c.Metric, Op = c.Op, Value = c.Value, Reason = c.Reason }).ToList(),
            Expect = p.Expect?.Clone(),
        };

        /// <summary>方位のように一周する値か（0〜359.9）。</summary>
        public static bool Wraps(ModuleParam p) => p.Min <= 0f && p.Max >= 359f && p.Unit == "°";

        public static float StepSize(ModuleParam p)
        {
            if (p.Step > 0f) return p.Step;
            float raw = (p.Max - p.Min) / 20f;
            float mag = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(Mathf.Max(raw, 1e-6f))));
            float n = raw / mag;
            float nice = n <= 1f ? 1f : n <= 2f ? 2f : n <= 5f ? 5f : 10f;
            float s = nice * mag;
            return p.Integer ? Mathf.Max(1f, Mathf.Round(s)) : s;
        }

        /// <summary>値を1段階変える（範囲で止める・方位は一周する）。変わったら true。</summary>
        public static bool StepParam(ExperimentPlan plan, string type, string key, int dir)
        {
            var m = plan?.Module(type);
            var info = ExperimentCatalog.Module(type)?.Param(key);
            if (m == null || info == null || dir == 0) return false;
            float now = m.Get(key), next;
            if (info.Levels != null && info.Levels.Length > 0)
            {
                int i = NearestLevel(info.Levels, now);
                // 段階の間の値なら、まずその向きの隣の段階へ
                if (dir > 0 && info.Levels[i] > now + 1e-4f) next = info.Levels[i];
                else if (dir < 0 && info.Levels[i] < now - 1e-4f) next = info.Levels[i];
                else next = info.Levels[Mathf.Clamp(i + Math.Sign(dir), 0, info.Levels.Length - 1)];
            }
            else
            {
                float step = StepSize(info);
                next = now + step * Math.Sign(dir);
                if (Wraps(info)) next = Mathf.Repeat(next, 360f);
                else next = Mathf.Clamp(next, info.Min, info.Max);
                next = info.Integer ? Mathf.Round(next) : (float)Math.Round(next, 4);
                if (Wraps(info) && next > info.Max) next = 0f;
            }
            next = Mathf.Clamp(next, info.Min, info.Max);
            if (Mathf.Approximately(next, now)) return false;
            m.Params[key] = next;
            return true;
        }

        static int NearestLevel(float[] levels, float v)
        {
            int best = 0;
            for (int i = 1; i < levels.Length; i++) if (Mathf.Abs(levels[i] - v) < Mathf.Abs(levels[best] - v)) best = i;
            return best;
        }

        /// <summary>
        /// 画面で追加できる環境モジュール。既存の実験場の計画は値の変更だけ（追加・削除なし）。
        /// 雨は既存の雨の実験だけ。発明の機能では何も変わらないモジュールは出さない。
        /// </summary>
        public static List<string> Addable(ExperimentPlan plan, InventionDna dna)
        {
            var list = new List<string>();
            var sc = ExperimentCatalog.Scenario(plan?.Scenario);
            if (sc == null || PlanCandidates.IsLegacy(plan) || plan.Modules.Count >= ExperimentCatalog.MaxModules) return list;
            foreach (var type in sc.AllowedModules)
                if (type != "rain" && !plan.Has(type) && Useful(type, plan.Scenario, dna)) list.Add(type);
            return list;
        }

        /// <summary>そのモジュールで、この発明の計測やモデルが変わるか。</summary>
        public static bool Useful(string type, string scenario, InventionDna dna)
        {
            bool Has(string b) => dna != null && dna.FindBehavior(b) != null;
            foreach (var m in ExperimentCatalog.Metrics)
            {
                if (m.Scenarios.Length > 0 && !m.Scenarios.Contains(scenario)) continue;
                bool uses = m.Modules.Contains(type) || (scenario == ExperimentCatalog.Stand && m.StandModules.Contains(type));
                if (uses && (m.AnyBlocks.Length == 0 || m.AnyBlocks.Any(Has))) return true;
            }
            if (scenario == ExperimentCatalog.Stand && type == "obstacles" && Has("detect")) return true; // 視線を遮る
            if (scenario == ExperimentCatalog.Stand && type == "darkness" && Has("alert")) return true;   // 光の通知が届く距離
            if (scenario == ExperimentCatalog.FlyToTarget && type == "wind") return true;
            if (scenario == ExperimentCatalog.ClimbSlope && type == "payload" && Has("drive")) return true; // 荷物で必要な電力が変わる
            return false;
        }

        public static bool CanRemove(ExperimentPlan plan, string type) =>
            plan != null && plan.Has(type) && !PlanCandidates.IsLegacy(plan) && !(ExperimentCatalog.Scenario(plan.Scenario)?.RequiredModules.Contains(type) ?? false);

        public static bool AddModule(ExperimentPlan plan, string type, InventionDna dna)
        {
            if (!Addable(plan, dna).Contains(type)) return false;
            var info = ExperimentCatalog.Module(type);
            var m = new PlanModule { Type = type };
            foreach (var p in info.Params) m.Params[p.Key] = p.Default;
            plan.Modules.Add(m);
            PlanCandidates.FitPayload(plan, dna);
            return true;
        }

        public static bool RemoveModule(ExperimentPlan plan, string type)
        {
            if (!CanRemove(plan, type)) return false;
            plan.Modules.RemoveAll(m => m.Type == type);
            return true;
        }

        /// <summary>
        /// 実行する計画：モード・シードを入れ、今の条件・モード・発明では測れない計測項目と合格条件を外したもの（元の計画は変えない）。
        /// 外したものは removed に「計測項目名：理由」で入れる（画面に出す）。
        /// </summary>
        public static ExperimentPlan Effective(ExperimentPlan plan, string mode, int seed, InventionDna dna, List<string> removed = null)
        {
            var p = Clone(plan);
            if (p == null) return null;
            p.Mode = LabModes.IsValid(mode) ? mode : LabModes.Dream;
            p.Seed = seed;
            foreach (var key in p.Metrics.ToList())
            {
                var mi = ExperimentCatalog.Metric(key);
                string why = mi == null ? "未対応の計測項目" : ExperimentPlanJson.WhyCannotMeasure(mi, p, dna);
                if (why == null) continue;
                p.Metrics.Remove(key);
                removed?.Add($"{mi?.LabelJa ?? key}：{why}");
            }
            foreach (var c in p.Criteria.ToList())
            {
                var mi = ExperimentCatalog.Metric(c.Metric);
                string why = mi == null ? "未対応の計測項目" : (p.Metrics.Count > 0 && !p.Metrics.Contains(c.Metric)) ? "計測項目にない" : ExperimentPlanJson.WhyCannotMeasure(mi, p, dna);
                if (why == null) continue;
                p.Criteria.Remove(c);
                if (removed != null && !removed.Any(r => r.StartsWith((mi?.LabelJa ?? c.Metric) + "：", StringComparison.Ordinal)))
                    removed.Add($"{mi?.LabelJa ?? c.Metric}：{why}");
            }
            return p;
        }

        /// <summary>
        /// 結果に出る計測項目（表示用）。計画で指定があればそれ、なければシナリオで測れるものすべて。
        /// </summary>
        public static List<MetricInfo> ExpectedMetrics(ExperimentPlan p, InventionDna dna)
        {
            var keys = p.Metrics.Count > 0 ? p.Metrics : ExperimentCatalog.Metrics.Select(m => m.Key).ToList();
            return keys.Select(ExperimentCatalog.Metric).Where(mi => mi != null && ExperimentPlanJson.WhyCannotMeasure(mi, p, dna) == null).ToList();
        }

        /// <summary>判定に使う合格条件（計画になければ目安のうち測れるもの。ScenarioRunner.Judge と同じ選び方）。</summary>
        public static List<PlanCriterion> EffectiveCriteria(ExperimentPlan p, InventionDna dna)
        {
            if (p.Criteria.Count > 0) return p.Criteria;
            var expected = new HashSet<string>(ExpectedMetrics(p, dna).Select(m => m.Key));
            return ExperimentCatalog.DefaultCriteria.Where(c => expected.Contains(c.Metric)).ToList();
        }
    }

    /// <summary>実験計画の表示用の文字列。</summary>
    public static class PlanText
    {
        public static string FormatParam(ModuleParam p, float v)
        {
            var ci = CultureInfo.InvariantCulture;
            string fmt;
            if (p.Integer) fmt = "0";
            else if (p.Levels != null) fmt = v < 1f ? "0.0#" : "0";
            else
            {
                float s = PlanEditor.StepSize(p);
                fmt = s >= 1f ? "0" : s >= 0.1f - 1e-6f ? "0.0" : "0.00";
            }
            string unit = p.Unit;
            return v.ToString(fmt, ci) + (unit == "°" || unit == "%" || unit == "" ? unit : " " + unit);
        }

        public static string Criterion(PlanCriterion c)
        {
            var info = ExperimentCatalog.Metric(c.Metric);
            if (info == null) return c.Metric;
            return $"{info.LabelJa} {(c.Op == ">=" ? "≥" : "≤")} {c.Value.ToString(info.Format, CultureInfo.InvariantCulture)}{info.Unit}";
        }

        /// <summary>例：「その場に立つ ／ 暗さ 2 lx ／ 検知対象 6 個」。</summary>
        public static string Describe(ExperimentPlan p)
        {
            if (p == null) return "";
            var parts = new List<string> { LabModes.Label(p.Mode), PlanCandidates.ScenarioLabel(p.Scenario) };
            foreach (var m in p.Modules)
            {
                var info = ExperimentCatalog.Module(m.Type);
                if (info == null) continue;
                parts.Add(info.LabelJa + " " + string.Join("・", info.Params.Where(x => m.Params.ContainsKey(x.Key)).Select(x => FormatParam(x, m.Params[x.Key]))));
            }
            parts.Add("シード " + p.Seed);
            return string.Join(" ／ ", parts);
        }
    }
}
