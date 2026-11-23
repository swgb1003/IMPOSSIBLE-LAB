using System.Linq;
using ImpossibleLab.Core;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>コンソールの弧ゲージに出す値（ResultCriteria.Headline）：どの計測値を選ぶか、単位と端の値。</summary>
    public class ConsoleReadingTests
    {
        [Test]
        public void NiceMax_PicksRoundScale_AndRespectsCap()
        {
            Assert.AreEqual(2f, ResultCriteria.NiceMax(0.9f, 60f), 1e-4f);
            Assert.AreEqual(10f, ResultCriteria.NiceMax(4.2f, 100f), 1e-4f);
            Assert.AreEqual(50f, ResultCriteria.NiceMax(37f, 100f), 1e-4f);
            Assert.AreEqual(100f, ResultCriteria.NiceMax(95f, 100f), 1e-4f, "上限を超えない");
            Assert.AreEqual(1f, ResultCriteria.NiceMax(0f, 60f), 1e-4f, "値が 0 でも端は 0 にしない");
        }

        [Test]
        public void Headline_Null_WhenNoResult() => Assert.IsNull(ResultCriteria.Headline(null));

        [Test]
        public void Headline_Rain_ShowsCoverageAsPercent_WithPassLine()
        {
            var r = new RainExperimentResult { Environment = LabEnvironments.RainLab, Config = new RainExperimentConfig(), CoverageRatio = 0.623f };
            var g = ResultCriteria.Headline(r);
            Assert.IsNotNull(g);
            Assert.AreEqual("coverageRatio", g.Key);
            Assert.AreEqual("%", g.Unit);
            Assert.AreEqual(62.3f, g.Value, 0.01f);
            Assert.AreEqual(100f, g.Max, 1e-4f);
            Assert.AreEqual("62.3", g.ValueText);
            Assert.IsTrue(g.HasCriterion, "雨の実験は防護率 80% が合格条件");
            Assert.AreEqual(RainExperimentResult.SuccessCoverage * 100f, g.Pass.Value, 0.01f);
            Assert.IsFalse(g.Passed);
        }

        [Test]
        public void Headline_Scenario_ValueAndPassFitTheScale()
        {
            var dna = DnaJson.Parse(Resources.Load<TextAsset>("ImpossibleLabSamples/auto_bin").text).Dna;
            var plan = PlanCandidates.Standard(dna).First().Plan;
            var r = ScenarioRunner.RunPlan(dna, plan).Result;
            var g = ResultCriteria.Headline(r);
            Assert.IsNotNull(g, "計測値のある結果からは必ず1つ選ぶ");
            Assert.IsNotNull(ExperimentCatalog.Metric(g.Key));
            Assert.GreaterOrEqual(g.Max, g.Value - 1e-4f, "値はゲージの端を超えない");
            if (g.Pass.HasValue) Assert.GreaterOrEqual(g.Max, g.Pass.Value - 1e-4f, "合格の線もゲージの中に入る");
            Assert.IsNotEmpty(g.ValueText);
        }
    }
}
