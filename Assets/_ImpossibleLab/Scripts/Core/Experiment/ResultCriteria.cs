using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ImpossibleLab.Core
{
    /// <summary>合格条件1件の判定（S05 の表示用）。</summary>
    public sealed class CriterionCheck
    {
        public string Metric = "", Target = "", Reason = "";
        /// <summary>実測値の表示（測れなかったら「—」）。</summary>
        public string Actual = "—";
        public bool Measured, Passed;
    }

    /// <summary>計器（弧のゲージ）に出す主な計測値1つ。割合（0〜1）は % に直してある。</summary>
    public sealed class GaugeReading
    {
        public string Key = "", LabelJa = "", Unit = "", ValueText = "";
        /// <summary>表示用の値と、ゲージの端の値。</summary>
        public float Value, Max;
        /// <summary>合格の線（合格条件がなければ null）。</summary>
        public float? Pass;
        public bool HasCriterion, Passed, LowerIsBetter;
    }

    /// <summary>
    /// 結果の合格条件ごとの判定（6週目 S05）。判定に使う条件は実行時と同じ選び方で決める：
    /// 組み立て式の実験＝実験計画の条件（なければ目安のうち測れたもの、ScenarioRunner.Judge と同じ）、
    /// 既存の実験場＝ExperimentPlan.FromConfig の条件（各モデルの合否と同じ値）。
    /// </summary>
    public static class ResultCriteria
    {
        public static List<CriterionCheck> Evaluate(RainExperimentResult r)
        {
            var list = new List<CriterionCheck>();
            if (r == null) return list;
            foreach (var c in CriteriaOf(r))
            {
                var info = ExperimentCatalog.Metric(c.Metric);
                if (info == null) continue;
                float? v = Value(r, c.Metric);
                list.Add(new CriterionCheck
                {
                    Metric = c.Metric, Target = PlanText.Criterion(c), Reason = c.Reason ?? "",
                    Measured = v.HasValue, Passed = v.HasValue && c.Check(v.Value),
                    Actual = v.HasValue ? v.Value.ToString(info.Format, CultureInfo.InvariantCulture) + info.Unit : "—",
                });
            }
            return list;
        }

        /// <summary>
        /// 計器に出す値：測れた合格条件の先頭。なければ雨の実験は防護率、ほかは主な計測値の先頭。
        /// </summary>
        public static GaugeReading Headline(RainExperimentResult r)
        {
            if (r == null) return null;
            PlanCriterion crit = null; MetricInfo info = null; float? v = null;
            foreach (var c in CriteriaOf(r))
            {
                var i = ExperimentCatalog.Metric(c.Metric);
                var val = Value(r, c.Metric);
                if (i == null || !val.HasValue) continue;
                crit = c; info = i; v = val; break;
            }
            if (info == null)
            {
                if (r.IsRain) { info = ExperimentCatalog.Metric("coverageRatio"); v = r.CoverageRatio; }
                else
                {
                    var m = r.Metrics.FirstOrDefault(x => x.Primary) ?? r.Metrics.FirstOrDefault();
                    if (m == null) return null;
                    info = ExperimentCatalog.Metric(m.Key); v = m.Value;
                }
                if (info == null) return null;
            }
            bool ratio = string.IsNullOrEmpty(info.Unit) && info.Max <= 1f + 1e-6f;
            float k = ratio ? 100f : 1f;
            var g = new GaugeReading
            {
                Key = info.Key, LabelJa = info.LabelJa, Unit = ratio ? "%" : (info.Unit ?? "").Trim(),
                Value = v.Value * k, LowerIsBetter = info.LowerIsBetter, HasCriterion = crit != null,
            };
            if (crit != null) { g.Pass = crit.Value * k; g.Passed = crit.Check(v.Value); }
            g.Max = ratio ? 100f : NiceMax(System.Math.Max(g.Value, g.Pass ?? 0f), info.Max);
            g.ValueText = ratio ? g.Value.ToString("0.0", CultureInfo.InvariantCulture) : v.Value.ToString(info.Format, CultureInfo.InvariantCulture);
            return g;
        }

        /// <summary>値が収まる、切りのよい端の値（1・2・5 の並び）。cap を超えない。</summary>
        public static float NiceMax(float need, float cap)
        {
            if (need <= 0f) return cap > 0f ? System.Math.Min(1f, cap) : 1f;
            double target = need * 1.25, pow = System.Math.Pow(10, System.Math.Floor(System.Math.Log10(target)));
            double nice = 10 * pow;
            foreach (double m in new[] { 1.0, 2.0, 5.0, 10.0 }) if (m * pow >= target - 1e-9) { nice = m * pow; break; }
            if (cap > 0f && nice > cap) nice = System.Math.Max(cap, need);
            return (float)nice;
        }

        /// <summary>結果の判定に使った実験計画（雨の実験は計画を持たないため設定から作る）。</summary>
        public static ExperimentPlan PlanOf(RainExperimentResult r)
        {
            if (r == null) return null;
            if (r.Plan != null) return r.Plan;
            if (r.Config == null) return null;
            var cfg = r.Config.Copy();
            if (LabEnvironments.IsValid(r.Environment)) cfg.Environment = r.Environment;
            return ExperimentPlan.FromConfig(cfg);
        }

        static IEnumerable<PlanCriterion> CriteriaOf(RainExperimentResult r)
        {
            var plan = PlanOf(r);
            if (plan == null) return Enumerable.Empty<PlanCriterion>();
            if (r.IsScenarioLab && plan.Criteria.Count == 0)
                return ExperimentCatalog.DefaultCriteria.Where(c => r.Metric(c.Metric) != null);
            return plan.Criteria;
        }

        /// <summary>計測値（雨の実験の防護率・被濡れは専用の項目から）。</summary>
        static float? Value(RainExperimentResult r, string key)
        {
            var m = r.Metric(key);
            if (m != null) return m.Value;
            if (r.IsRain && key == "coverageRatio") return r.CoverageRatio;
            if (r.IsRain && key == "wetnessScore") return r.WetnessScore;
            return null;
        }
    }
}
