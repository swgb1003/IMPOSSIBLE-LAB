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
