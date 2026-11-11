using System.IO;
using System.Linq;
using ImpossibleLab.Core;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>6週目：機能ブロックのオフライン改良ルールと、代表6件の記録した AI 設計。</summary>
    public class RevisionRulesV2Tests
    {
        static readonly string[] Representative = { "r03", "r15", "r18", "r22", "r09", "r05" };

        static DesignOutcome Fixture(string id)
        {
            string path = Path.Combine(Application.dataPath, $"_ImpossibleLab/Tests/EditMode/Fixtures/representative/ai_{id}.json");
            return ProxyProtocol.ParseDesign(200, File.ReadAllText(path), null);
        }

        [Test]
        public void Representative_FixturesDesignAndHaveAiPlans()
        {
            foreach (var id in Representative)
            {
                var o = Fixture(id);
                Assert.AreEqual(DesignStatus.Success, o.Status, id + " " + o.Message);
                Assert.IsNotEmpty(o.Plans, id + "：検証を通った AI の計画");
                Assert.IsTrue(PlanCandidates.Build(o.Dna, o.Plans)[0].FromAi, id);
            }
        }

        [Test]
        public void EveryRepresentative_HasARequestThatChangesTheJudgedMetric()
        {
            foreach (var id in Representative)
            {
                var o = Fixture(id);
                var plan = PlanEditor.Effective(PlanCandidates.Build(o.Dna, o.Plans)[0].Plan, LabModes.Dream, 2026, o.Dna);
                var r1 = ScenarioRunner.RunPlan(o.Dna, plan).Result;
                var checks = ResultCriteria.Evaluate(r1);
                // 依頼文がある計測項目のうち、未達のものを優先
                var target = checks.Where(c => c.Measured).OrderBy(c => c.Passed ? 1 : 0).FirstOrDefault(c => OfflineRevisionRules.RequestForMetric(o.Dna, c.Metric) != null);
                Assert.IsNotNull(target, id + "：判定に使う計測項目");
                string req = OfflineRevisionRules.RequestForMetric(o.Dna, target.Metric);
                Assert.IsNotNull(req, $"{id}：{target.Metric} を良くする依頼文");
                var p = OfflineRevisionRules.Propose(o.Dna, req);
                Assert.IsNotEmpty(p.Operations, $"{id}：「{req}」で改良案ができる（{string.Join(" / ", p.Notes)}）");
                var res = RevisionEngine.Apply(o.Dna, p);
                Assert.IsTrue(res.Success, id + " " + string.Join(" / ", res.Errors));
                var r2 = ScenarioRunner.RunPlan(res.NewDna, plan).Result;
                var info = ExperimentCatalog.Metric(target.Metric);
                float a = r1.Metric(target.Metric).Value, b = r2.Metric(target.Metric).Value;
                if (target.Passed) Assert.IsFalse(info.LowerIsBetter ? b > a + 1e-4f : b < a - 1e-4f, $"{id}：{target.Metric} {a} → {b} が悪くならない");
                else Assert.IsTrue(info.LowerIsBetter ? b < a : b > a, $"{id}：{target.Metric} {a} → {b} が良くなる");
            }
        }

        [Test]
        public void Suggestions_ForBlockInventions_UseTheirBlocks()
        {
            var o = Fixture("r15");
            var s = OfflineRevisionRules.SuggestedRequestsFor(o.Dna).ToList();
            Assert.Contains("もっと明るくして", s);
            Assert.IsTrue(s.All(x => OfflineRevisionRules.Propose(o.Dna, x).Operations.Count > 0), string.Join(" / ", s));
            // 既存3カテゴリはこれまでの例
            var rain = DnaJson.Parse(Resources.Load<TextAsset>("ImpossibleLabSamples/handsfree_rain").text).Dna;
            CollectionAssert.AreEqual(OfflineRevisionRules.SuggestedRequestsFor("protection"), OfflineRevisionRules.SuggestedRequestsFor(rain));
        }

        [Test]
        public void Hints_GiveTheRequestText()
        {
            var o = Fixture("r15");
            var plan = PlanEditor.Clone(PlanCandidates.Build(o.Dna, o.Plans)[0].Plan);
            plan.Criteria.Clear();
            plan.Criteria.Add(new PlanCriterion { Metric = "footIlluminance", Op = ">=", Value = 100000f });
            var r = ScenarioRunner.RunPlan(o.Dna, plan).Result;
            var h = CourseSimulator.Hints(r, o.Dna);
            Assert.IsTrue(h[0].Contains("「もっと明るくして」"), string.Join(" / ", h));
        }
    }
}
