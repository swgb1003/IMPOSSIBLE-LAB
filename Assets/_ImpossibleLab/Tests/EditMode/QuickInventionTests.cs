using System.Collections.Generic;
using System.Linq;
using ImpossibleLab.Core;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>S01 のクイック発明（9件）が、AI 未接続でもオフラインの設計図で設計でき、実験の候補があること。</summary>
    public class QuickInventionTests
    {
        static readonly Dictionary<string, string> Files = new Dictionary<string, string>
        {
            ["protection"] = "handsfree_rain", ["mobility"] = "flying_shoes", ["carrier"] = "carrier_robot",
            ["sun_shade"] = "sun_canopy", ["lighting"] = "foot_light", ["heating"] = "back_heater",
            ["lifting"] = "box_lifter", ["sensing"] = "rear_watch_pack", ["impact"] = "phone_bumper",
        };

        static OfflineDesignService Offline() =>
            new OfflineDesignService(Files.ToDictionary(kv => kv.Key, kv => Resources.Load<TextAsset>("ImpossibleLabSamples/" + kv.Value).text));

        [Test] public void NineExamples_EachMapsToItsOwnSample()
        {
            Assert.That(QuickInventions.All.Length, Is.InRange(6, 9), "仕様7章：6〜9件");
            var kinds = QuickInventions.All.Select(q => OfflineDesignService.KindOf(q.Request)).ToList();
            CollectionAssert.AllItemsAreNotNull(kinds);
            CollectionAssert.AllItemsAreUnique(kinds, "例ごとに別の設計図になる");
            foreach (var k in kinds) Assert.IsTrue(Files.ContainsKey(k), k);
        }

        [Test] public void EveryExample_DesignsOffline_AndHasAnExperiment()
        {
            var svc = Offline();
            foreach (var q in QuickInventions.All)
            {
                var outcome = svc.GenerateAsync(q.Request, null, default).Result;
                Assert.AreEqual(DesignStatus.Success, outcome.Status, q.Title + ": " + outcome.Message);
                Assert.IsTrue(outcome.Report.IsValid, q.Title);
                Assert.IsNotEmpty(PlanCandidates.Build(outcome.Dna, null), q.Title + "：S04 で選べる実験がない");
            }
        }

        [Test] public void FreeText_StillUnsupportedOffline()
        {
            var outcome = Offline().GenerateAsync("宿題を自動でやってくれるペン", null, default).Result;
            Assert.AreEqual(DesignStatus.Unsupported, outcome.Status);
            Assert.IsNotEmpty(outcome.Suggestions);
        }

        [Test] public void LiftingIsCheckedBeforeCarrier()
        {
            Assert.AreEqual("lifting", OfflineDesignService.KindOf("重い荷物を棚に持ち上げる道具"));
            Assert.AreEqual("carrier", OfflineDesignService.KindOf("荷物を運んでついてくるロボット"));
        }
    }
}
