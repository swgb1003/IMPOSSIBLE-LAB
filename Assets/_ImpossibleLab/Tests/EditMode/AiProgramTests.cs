using System.Linq;
using ImpossibleLab.Core;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>
    /// 動作プログラムの AI 対応（仕様 28.8、ゲート G-A3）：プロキシが返す形をアプリが検証して受け付けること、
    /// 改良のルール・関節の操作（add_rule / remove_rule / update_rule / add_joint / update_joint）。API は呼ばない。
    /// </summary>
    public class AiProgramTests
    {
        // proxy の toDna・toPlans が、テストの見本（自動で開くゴミ箱）から作った出力（proxy/test/convert.test.ts の autoBin）
        const string ProxyDna = @"{""schemaVersion"":3,""id"":""ai-bin"",""name"":""自動で開くゴミ箱"",""purpose"":""近づくとフタが開く"",""tags"":[""daily""],""mount"":""fixed"",""appearance"":""lab_white"",""mode"":""dream"",
""parts"":[{""id"":""bin"",""type"":""container"",""shape"":""box"",""material"":""plastic"",""position"":[0,0.3,0],""scale"":[0.3,0.6,0.38]},
{""id"":""lid"",""type"":""panel"",""shape"":""box"",""material"":""plastic"",""position"":[0,0.61,0],""scale"":[0.3,0.02,0.38],""joint"":{""type"":""hinge"",""parent"":""bin"",""axis"":""x"",""pivot"":[0,0,-0.19],""min"":0,""max"":110,""speed"":360,""home"":0}},
{""id"":""eye"",""type"":""sensor"",""shape"":""sphere"",""material"":""plastic"",""position"":[0,0.5,0.2],""scale"":[0.03,0.03,0.03]},
{""id"":""hinge-motor"",""type"":""motor"",""shape"":""cylinder"",""material"":""plastic"",""position"":[0,0.6,-0.18],""scale"":[0.04,0.06,0.04]}],
""behaviors"":[{""type"":""detect"",""targetPart"":""eye"",""parameters"":{""rangeM"":3}}],""functionMap"":[{""need"":""近づくと開く"",""blocks"":[""detect""],""status"":""simulated""}],
""program"":{""version"":1,""flags"":[""opened""],""rules"":[{""id"":""open-lid"",""label"":""人が近づいたらフタを開ける"",""trigger"":""rise"",""cooldown"":1,
""when"":{""all"":[{""sensor"":""target_distance"",""op"":""<"",""value"":1},{""sensor"":""target_detected"",""is"":true},{""flag"":""opened"",""is"":false}]},
""do"":[{""action"":""move_joint"",""joint"":""lid"",""target"":100,""speed"":120},{""action"":""set_flag"",""name"":""opened"",""value"":true},{""action"":""wait"",""seconds"":3},{""action"":""move_joint"",""joint"":""lid"",""target"":0},{""action"":""set_flag"",""name"":""opened"",""value"":false}]}]},
""constraints"":{""maxParts"":24,""maxSpeed"":3},""revision"":1,""parentRevision"":null}";

        const string ProxyPlan = @"{""scenario"":""approach_device"",""mode"":""dream"",""durationSec"":60,""seed"":1234,""modules"":[{""type"":""approach"",""count"":3}],
""metrics"":[""ruleFiredCount"",""reactionTime"",""expectationRate""],""criteria"":[{""metric"":""expectationRate"",""op"":"">="",""value"":0.7,""reason"":""近くにいる間は開いている（目安）""}],
""expect"":{""when"":{""sensor"":""target_distance"",""op"":""<"",""value"":1},""minRatio"":0.7,""joint"":""lid"",""atLeast"":80}}";

        static InventionDna Bin()
        {
            var r = DnaJson.Parse(Resources.Load<TextAsset>("ImpossibleLabSamples/auto_bin").text);
            Assert.IsTrue(r.IsValid, r.Summary);
            return r.Dna;
        }

        static RevisionProposal Parse(string ops)
        {
            Assert.IsTrue(RevisionEngine.TryParseProposal("{\"summary\":\"t\",\"operations\":[" + ops + "]}", out var p, out var err), err);
            return p;
        }

        [Test] public void ProxyDesignWithProgram_IsAcceptedAndRuns()
        {
            var r = DnaJson.Parse(ProxyDna);
            Assert.IsTrue(r.IsValid, r.Summary);
            Assert.AreEqual(1, r.Dna.Program.Rules.Count);
            var plan = ExperimentPlanJson.Parse(ProxyPlan, r.Dna);
            Assert.IsTrue(plan.IsValid, plan.Summary);
            Assert.AreEqual("lid", plan.Plan.Expect.Joint);
            var res = ScenarioRunner.RunPlan(r.Dna, plan.Plan).Result;
            Assert.AreEqual(3f, res.Metric("ruleFiredCount").Value, "3回近づいて3回開く");
            Assert.IsNotNull(res.Metric("expectationRate"));
        }

        [Test] public void ProxyPlan_WithBehaviorExpectation_IsAccepted()
        {
            var light = DnaJson.Parse(Resources.Load<TextAsset>("ImpossibleLabSamples/motion_light").text).Dna;
            string json = ProxyPlan.Replace(@"""joint"":""lid"",""atLeast"":80", @"""joint"":"""",""behavior"":""illuminate""");
            var plan = ExperimentPlanJson.Parse(json, light);
            Assert.IsTrue(plan.IsValid, plan.Summary);
            Assert.AreEqual("illuminate", plan.Plan.Expect.Behavior);
            var bad = ExperimentPlanJson.Parse(json.Replace("illuminate", "teleport"), light);
            Assert.IsFalse(bad.IsValid, "発明に無い機能の期待は拒否");
        }

        [Test] public void AiRevision_UpdateRuleAndJoint()
        {
            var dna = Bin();
            var src = dna.Program.Rules.First(x => x.Do.Any(a => a.Action == ProgramAction.MoveJoint) && x.Do.Any(a => a.Action == ProgramAction.Wait));
            string rule = src.Id;
            int wait = src.Do.FindIndex(a => a.Action == ProgramAction.Wait);
            int cond = src.When.FindIndex(c => c.Sensor == "target_distance");
            var p = Parse($@"{{""op"":""update_joint"",""partId"":""lid"",""field"":""speed"",""value"":200}},
                {{""op"":""update_rule"",""ruleId"":""{rule}"",""field"":""when"",""index"":{cond},""value"":1.5}},
                {{""op"":""update_rule"",""ruleId"":""{rule}"",""field"":""seconds"",""index"":{wait},""value"":99}}");
            Assert.AreEqual("ai", p.Source);
            var r = RevisionEngine.Apply(dna, p);
            Assert.IsTrue(r.Success, string.Join(",", r.Errors));
            Assert.AreEqual(200f, r.NewDna.FindPart("lid").Joint.Speed);
            var nr = r.NewDna.Program.Rules.First(x => x.Id == rule);
            Assert.AreEqual(1.5f, nr.When[cond].Value, 1e-4f);
            Assert.AreEqual(ProgramCatalog.MaxWait, nr.Do[wait].Seconds, "待つ時間は範囲内に補正");
            Assert.IsTrue(r.Warnings.Any(w => w.Contains("補正")), "補正したことを知らせる");
            Assert.IsTrue(r.Changes.All(c => c.Label.Contains("ルール") || c.Label.Contains("関節")), "差分はルール・関節の単位で表示");
        }

        [Test] public void AiRevision_AddAndRemoveRule()
        {
            var dna = Bin();
            var add = Parse(@"{""op"":""add_rule"",""rule"":{""id"":""beep"",""label"":""開いたら知らせる"",""trigger"":""rise"",""cooldown"":0,
                ""when"":{""all"":[{""sensor"":""joint_position"",""op"":"">"",""value"":90,""joint"":""lid""}]},
                ""do"":[{""action"":""set_behavior"",""behavior"":""detect"",""on"":true}]}}");
            var r = RevisionEngine.Apply(dna, add);
            Assert.IsTrue(r.Success, string.Join(",", r.Errors));
            Assert.AreEqual(dna.Program.Rules.Count + 1, r.NewDna.Program.Rules.Count);
            StringAssert.Contains("ルールを追加", r.Changes[0].Label);

            var dup = RevisionEngine.Apply(r.NewDna, add);
            Assert.IsFalse(dup.Success, "同じ id のルールは追加しない");

            var rm = RevisionEngine.Apply(r.NewDna, Parse(@"{""op"":""remove_rule"",""ruleId"":""beep""}"));
            Assert.IsTrue(rm.Success, string.Join(",", rm.Errors));
            Assert.AreEqual(dna.Program.Rules.Count, rm.NewDna.Program.Rules.Count);
        }

        [Test] public void AiRevision_RejectsInvalidRuleAndJoint()
        {
            var dna = Bin();
            // 未登録の命令・存在しない関節・使えないセンサーのルールは、DNA の検証器で拒否する（アプリは止まらない）
            foreach (var bad in new[]
            {
                @"{""id"":""x"",""trigger"":""rise"",""when"":{""all"":[{""sensor"":""elapsed"",""op"":"">"",""value"":0}]},""do"":[{""action"":""run_code"",""code"":""x""}]}",
                @"{""id"":""x"",""trigger"":""rise"",""when"":{""all"":[{""sensor"":""elapsed"",""op"":"">"",""value"":0}]},""do"":[{""action"":""move_joint"",""joint"":""ghost"",""target"":1}]}",
                @"{""id"":""x"",""trigger"":""rise"",""when"":{""all"":[{""sensor"":""holding"",""is"":true}]},""do"":[{""action"":""wait"",""seconds"":1}]}",
            })
            {
                var r = RevisionEngine.Apply(dna, Parse(@"{""op"":""add_rule"",""rule"":" + bad + "}"));
                Assert.IsFalse(r.Success, bad);
                Assert.IsNull(r.NewDna);
            }
            // 関節：部品がない・すでにある・駆動部がない
            Assert.IsFalse(RevisionEngine.Apply(dna, Parse(@"{""op"":""add_joint"",""partId"":""ghost"",""joint"":{""type"":""hinge"",""axis"":""x"",""min"":0,""max"":10,""speed"":90}}")).Success);
            Assert.IsFalse(RevisionEngine.Apply(dna, Parse(@"{""op"":""add_joint"",""partId"":""lid"",""joint"":{""type"":""hinge"",""axis"":""x"",""min"":0,""max"":10,""speed"":90}}")).Success);
            var light = DnaJson.Parse(Resources.Load<TextAsset>("ImpossibleLabSamples/motion_light").text).Dna;
            var noMotor = RevisionEngine.Apply(light, Parse(@"{""op"":""add_joint"",""partId"":""lamp"",""joint"":{""type"":""hinge"",""axis"":""x"",""min"":0,""max"":45,""speed"":90}}"));
            Assert.IsFalse(noMotor.Success, "関節には駆動部が必要");
            // 改良提案の形：手動専用の update_part・未知の項目は受け付けない
            Assert.IsFalse(RevisionEngine.TryParseProposal(@"{""operations"":[{""op"":""update_part"",""partId"":""lid"",""shape"":""sphere""}]}", out _, out _));
            Assert.IsFalse(RevisionEngine.TryParseProposal(@"{""operations"":[{""op"":""update_rule"",""ruleId"":""a"",""field"":""code"",""index"":0,""value"":1}]}", out _, out _));
            Assert.IsFalse(RevisionEngine.TryParseProposal(@"{""operations"":[{""op"":""update_joint"",""partId"":""lid"",""field"":""axis"",""value"":1}]}", out _, out _));
        }

        [Test] public void AiRevision_AddJointToPartWithMotor()
        {
            var dna = Bin();
            var r = RevisionEngine.Apply(dna, Parse(@"{""op"":""add_joint"",""partId"":""eye"",""joint"":{""type"":""hinge"",""parent"":""bin"",""axis"":""y"",""pivot"":[0,0,0],""min"":-45,""max"":45,""speed"":90,""home"":0}}"));
            Assert.IsTrue(r.Success, string.Join(",", r.Errors));
            Assert.IsNotNull(r.NewDna.FindPart("eye").Joint);
            StringAssert.Contains("関節を追加", r.Changes[0].Label);
            var narrow = RevisionEngine.Apply(dna, Parse(@"{""op"":""update_joint"",""partId"":""lid"",""field"":""max"",""value"":60}"));
            Assert.IsTrue(narrow.Success, string.Join(",", narrow.Errors));
            Assert.IsTrue(narrow.NewDna.Program.Rules.SelectMany(x => x.Do).Where(a => a.Action == ProgramAction.MoveJoint).All(a => a.Target <= 60f), "範囲の外の目標は補正");
        }

        [Test] public void ProxyCatalog_HasProgramSection()
        {
            var o = Newtonsoft.Json.Linq.JObject.Parse(LabCatalog.ToProxyJson());
            var sensors = (Newtonsoft.Json.Linq.JArray)o["program"]["sensors"];
            Assert.AreEqual(ProgramCatalog.Sensors.Length, sensors.Count);
            CollectionAssert.AreEquivalent(ProgramCatalog.Actions, o["program"]["actions"].Values<string>().ToArray());
            Assert.AreEqual(ProgramCatalog.MaxRules, o["program"]["limits"].Value<int>("maxRules"));
        }
    }
}
