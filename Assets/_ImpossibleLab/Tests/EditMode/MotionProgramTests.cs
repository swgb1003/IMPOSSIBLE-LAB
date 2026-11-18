using System.Collections.Generic;
using System.Linq;
using ImpossibleLab.Core;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>動作プログラム（仕様 28、ゲート G-A1）：データ・検証・インタプリタ。</summary>
    public class MotionProgramTests
    {
        // 仕様 28.4 の例「自動で開くゴミ箱」
        const string Program = @"{ ""version"": 1, ""flags"": [""opened""], ""rules"": [
          { ""id"": ""open_lid"", ""label"": ""人が近づいたらフタを開ける"", ""trigger"": ""rise"", ""cooldown"": 1.0,
            ""when"": { ""all"": [ { ""sensor"": ""target_distance"", ""op"": ""<"", ""value"": 1.0 }, { ""flag"": ""opened"", ""is"": false } ] },
            ""do"": [ { ""action"": ""move_joint"", ""joint"": ""lid"", ""target"": 100 },
                      { ""action"": ""set_flag"", ""name"": ""opened"", ""value"": true },
                      { ""action"": ""wait"", ""seconds"": 3 },
                      { ""action"": ""move_joint"", ""joint"": ""lid"", ""target"": 0 },
                      { ""action"": ""set_flag"", ""name"": ""opened"", ""value"": false } ] } ] }";

        const string LidJoint = @"{ ""type"": ""hinge"", ""parent"": ""bin"", ""axis"": ""x"", ""pivot"": [0, 0, -0.15], ""min"": 0, ""max"": 110, ""speed"": 90, ""home"": 0 }";

        static string Dna(string program = Program, string lidJoint = LidJoint, string extraParts = "", int version = 3, bool motor = true, bool detect = true)
        {
            string motorPart = motor ? @",{ ""id"": ""hinge-motor"", ""type"": ""motor"", ""position"": [0, 0.3, -0.15], ""scale"": [0.04, 0.04, 0.04] }" : "";
            string behaviors = detect ? @"{ ""type"": ""detect"", ""targetPart"": ""eye"", ""parameters"": { ""rangeM"": 3, ""fovDeg"": 120 } }" : "";
            return $@"{{ ""schemaVersion"": {version}, ""id"": ""auto-bin"", ""name"": ""自動で開くゴミ箱"", ""purpose"": ""人が近づくとフタが開く"",
              ""tags"": [""daily""], ""mount"": ""fixed"",
              ""parts"": [ {{ ""id"": ""bin"", ""type"": ""container"", ""position"": [0, 0, 0], ""scale"": [0.3, 0.5, 0.3] }},
                          {{ ""id"": ""lid"", ""type"": ""panel"", ""position"": [0, 0.26, 0], ""scale"": [0.3, 0.02, 0.3]{(lidJoint != null ? ", \"joint\": " + lidJoint : "")} }},
                          {{ ""id"": ""eye"", ""type"": ""sensor"", ""position"": [0, 0.2, 0.16], ""scale"": [0.03, 0.03, 0.03] }}{motorPart}{extraParts} ],
              ""behaviors"": [ {behaviors} ]{(program != null ? ", \"program\": " + program : "")} }}";
        }

        static ValidationReport Parse(string json) => DnaJson.Parse(json);

        /// <summary>対象までの距離を時刻で決めるセンサー（実験の人型モデルの代わり）。</summary>
        sealed class ScriptedSensors : IProgramSensors
        {
            public System.Func<float, float> Distance;
            public float Now;
            public bool TryGet(string sensor, out float value)
            {
                if (sensor == "target_distance") { value = Distance(Now); return true; }
                if (sensor == "target_detected") { value = Distance(Now) < 3f ? 1f : 0f; return true; }
                value = 0; return false;
            }
        }

        static (MotionInterpreter it, List<float> lid) Run(InventionDna dna, System.Func<float, float> distance, float seconds)
        {
            var it = new MotionInterpreter(dna);
            var s = new ScriptedSensors { Distance = distance };
            var lid = new List<float>();
            int steps = Mathf.RoundToInt(seconds / MotionInterpreter.Step);
            for (int i = 0; i < steps; i++) { s.Now = it.Time; it.Tick(s); lid.Add(it.JointValues["lid"]); }
            return (it, lid);
        }

        // ---------------- データ・検証 ----------------
        [Test] public void SpecExample_ParsesAndRoundTrips()
        {
            var r = Parse(Dna());
            Assert.IsTrue(r.IsValid, r.Summary);
            Assert.AreEqual(3, r.Dna.SchemaVersion);
            Assert.AreEqual(1, r.Dna.Program.Rules.Count);
            Assert.AreEqual(5, r.Dna.Program.Rules[0].Do.Count);
            Assert.AreEqual(110f, r.Dna.FindPart("lid").Joint.Max);
            var again = Parse(DnaJson.ToJson(r.Dna));
            Assert.IsTrue(again.IsValid, again.Summary);
            Assert.AreEqual(DnaJson.ToJson(r.Dna), DnaJson.ToJson(again.Dna));
        }

        [Test] public void OldVersions_LoadAsV3_WithoutProgram()
        {
            var v2 = Parse(Dna(program: null, lidJoint: null, version: 2));
            Assert.IsTrue(v2.IsValid, v2.Summary);
            Assert.AreEqual(3, v2.Dna.SchemaVersion);
            Assert.IsNull(v2.Dna.Program);
            Assert.IsTrue(v2.Dna.Parts.All(p => p.Joint == null));
            StringAssert.Contains("\"schemaVersion\": 3", DnaJson.ToJson(v2.Dna));
            // v2 では program・joint は未知のフィールド
            Assert.IsFalse(Parse(Dna(version: 2)).IsValid);
            // 既存の見本（v1・v2）も読み込める
            foreach (var f in new[] { "handsfree_rain", "back_heater", "sun_canopy" })
                Assert.IsTrue(DnaJson.Parse(Resources.Load<TextAsset>("ImpossibleLabSamples/" + f).text).IsValid, f);
        }

        // AT-14：無効な動作プログラムは安全に拒否し、理由を返す
        [TestCase(@"""action"": ""move_joint"", ""joint"": ""lid"", ""target"": 100 }", @"""action"": ""self_destruct"" }", "未知の動作")]
        [TestCase(@"""joint"": ""lid"", ""target"": 100", @"""joint"": ""arm"", ""target"": 100", "関節 arm がありません")]
        [TestCase(@"""joint"": ""lid"", ""target"": 100", @"""joint"": ""lid"", ""target"": 170", "範囲")]
        [TestCase(@"""sensor"": ""target_distance""", @"""sensor"": ""x_ray""", "未知のセンサー")]
        [TestCase(@"""seconds"": 3", @"""seconds"": 600", "範囲")]
        [TestCase(@"""cooldown"": 1.0,", @"""cooldown"": 1.0, ""script"": ""rm -rf"",", "未知のフィールド")]
        [TestCase(@"{ ""flag"": ""opened"", ""is"": false }", @"{ ""flag"": ""closed"", ""is"": false }", "宣言されていません")]
        [TestCase(@"{ ""flag"": ""opened"", ""is"": false }", @"{ ""any"": [ { ""flag"": ""opened"", ""is"": false } ] }", "入れ子")]
        [TestCase(@"""value"": 1.0 }", @"""value"": 50 }", "範囲")]
        public void InvalidProgram_IsRejectedWithReason(string from, string to, string expected)
        {
            string json = Dna(program: Program.Replace(from, to));
            Assert.AreNotEqual(Dna(), json, "置き換えが効いていない");
            var r = Parse(json);
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(r.Errors.Any(e => e.Contains(expected)), string.Join(" / ", r.Errors));
        }

        [Test] public void SensorWithoutRequiredFunction_IsRejected()
        {
            var r = Parse(Dna(detect: false));
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(r.Errors.Any(e => e.Contains("target_distance") && e.Contains("検知")), string.Join(" / ", r.Errors));
        }

        [Test] public void Joints_NeedMotor_NoCycles_Limits()
        {
            Assert.IsTrue(Parse(Dna(motor: false)).Errors.Any(e => e.Contains("駆動部")));
            // 循環：bin の親を lid に
            string cyc = Dna().Replace(@"""id"": ""bin"", ""type"": ""container"", ""position"": [0, 0, 0], ""scale"": [0.3, 0.5, 0.3]",
                @"""id"": ""bin"", ""type"": ""container"", ""position"": [0, 0, 0], ""scale"": [0.3, 0.5, 0.3], ""joint"": { ""type"": ""slider"", ""parent"": ""lid"", ""axis"": ""y"", ""min"": 0, ""max"": 0.2, ""speed"": 0.1 }");
            Assert.IsTrue(Parse(cyc).Errors.Any(e => e.Contains("循環")), string.Join(" / ", Parse(cyc).Errors));
            // 関節 7 個（上限 6）
            string extra = string.Concat(Enumerable.Range(0, 6).Select(i =>
                $@",{{ ""id"": ""flap{i}"", ""type"": ""panel"", ""position"": [0, 0.1, 0], ""scale"": [0.1, 0.01, 0.1], ""joint"": {{ ""type"": ""hinge"", ""axis"": ""x"", ""min"": 0, ""max"": 90, ""speed"": 90 }} }}"));
            Assert.IsTrue(Parse(Dna(extraParts: extra)).Errors.Any(e => e.Contains("関節は 6 個まで")));
            // hinge の速さの上限
            Assert.IsFalse(Parse(Dna(lidJoint: LidJoint.Replace(@"""speed"": 90", @"""speed"": 999"))).IsValid);
        }

        [Test] public void RuleLimits_AndWhileRestrictions()
        {
            string rule = @"{ ""id"": ""rX"", ""trigger"": ""rise"", ""when"": { ""all"": [ { ""sensor"": ""elapsed"", ""op"": "">"", ""value"": 1 } ] }, ""do"": [ { ""action"": ""set_flag"", ""name"": ""opened"", ""value"": true } ] }";
            string nine = @"{ ""version"": 1, ""flags"": [""opened""], ""rules"": [" + string.Join(",", Enumerable.Range(0, 9).Select(i => rule.Replace("rX", "r" + i))) + "] }";
            Assert.IsTrue(Parse(Dna(program: nine)).Errors.Any(e => e.Contains("ルールは 8 個まで")));
            string whileWait = @"{ ""version"": 1, ""flags"": [""opened""], ""rules"": [ { ""id"": ""w"", ""trigger"": ""while"", ""when"": { ""all"": [ { ""sensor"": ""elapsed"", ""op"": "">"", ""value"": 1 } ] }, ""do"": [ { ""action"": ""wait"", ""seconds"": 1 } ] } ] }";
            Assert.IsTrue(Parse(Dna(program: whileWait)).Errors.Any(e => e.Contains("while")));
        }

        [Test] public void Warnings_NeverFiresAndUnusedFlag()
        {
            string p = @"{ ""version"": 1, ""flags"": [""unused""], ""rules"": [ { ""id"": ""never"", ""when"": { ""all"": [ { ""sensor"": ""target_distance"", ""op"": ""<"", ""value"": 0 } ] }, ""do"": [ { ""action"": ""move_joint"", ""joint"": ""lid"", ""target"": 90 } ] } ] }";
            var r = Parse(Dna(program: p));
            Assert.IsTrue(r.IsValid, r.Summary);
            Assert.IsTrue(r.Warnings.Any(w => w.Contains("一度も成り立ちません")));
            Assert.IsTrue(r.Warnings.Any(w => w.Contains("unused")));
        }

        // ---------------- インタプリタ ----------------
        // 人型モデル：0〜2秒は 2.5m、2秒で 0.6m まで近づき 6秒まで留まり、その後 2.5m に離れる
        static float Approach(float t) => t < 2f ? 2.5f : t < 6f ? 0.6f : 2.5f;

        [Test] public void Lid_OpensWhenApproached_ThenClosesAfterWait()
        {
            var dna = Parse(Dna()).Dna;
            var (it, lid) = Run(dna, Approach, 10f);
            Assert.IsFalse(it.Stopped, it.StopReason);
            Assert.AreEqual(0f, lid[50], 0.01f, "近づく前は閉じている");              // 1.0 秒
            float max = lid.Max();
            Assert.AreEqual(100f, max, 0.01f, "100° まで開く");
            int opened = lid.FindIndex(v => v >= 99.9f);
            Assert.AreEqual(2f + 100f / 90f, opened * MotionInterpreter.Step, 0.06f, "90°/秒 で開く");
            Assert.AreEqual(0f, lid.Last(), 0.01f, "3秒後に閉じる");
            Assert.AreEqual(1, it.Events.Count(e => e.Kind == "start"), "近くにいる間は再起動しない（rise と opened フラグ）");
            Assert.IsFalse(it.Flags["opened"]);
        }

        [Test] public void Interpreter_IsDeterministic()
        {
            var dna = Parse(Dna()).Dna;
            var a = Run(dna, t => 1.5f + Mathf.Sin(t * 1.3f), 20f);
            var b = Run(dna, t => 1.5f + Mathf.Sin(t * 1.3f), 20f);
            CollectionAssert.AreEqual(a.lid, b.lid);
            CollectionAssert.AreEqual(a.it.Events.Select(e => e.ToString()).ToList(), b.it.Events.Select(e => e.ToString()).ToList());
            Assert.Greater(a.it.Events.Count(e => e.Kind == "start"), 1, "近づくたびに起動する");
        }

        [Test] public void Cooldown_DelaysRestart()
        {
            string p = Program.Replace(@"""cooldown"": 1.0", @"""cooldown"": 10.0");
            var dna = Parse(Dna(program: p)).Dna;
            // 近づく→離れる を2秒ごとにくり返す
            var (it, _) = Run(dna, t => (int)(t / 2f) % 2 == 1 ? 0.5f : 2.5f, 20f);
            var starts = it.Events.Where(e => e.Kind == "start").Select(e => e.Time).ToList();
            Assert.GreaterOrEqual(starts.Count, 2);
            Assert.GreaterOrEqual(starts[1] - starts[0], 10f + 3f, "終わってから cooldown の間は起動しない");
        }

        [Test] public void LaterRule_WinsTheSameJoint()
        {
            string p = @"{ ""version"": 1, ""flags"": [], ""rules"": [
              { ""id"": ""slow"", ""when"": { ""all"": [ { ""sensor"": ""elapsed"", ""op"": "">"", ""value"": 0.5 } ] },
                ""do"": [ { ""action"": ""move_joint"", ""joint"": ""lid"", ""target"": 30 }, { ""action"": ""wait"", ""seconds"": 2 }, { ""action"": ""move_joint"", ""joint"": ""lid"", ""target"": 60 } ] },
              { ""id"": ""fast"", ""when"": { ""all"": [ { ""sensor"": ""elapsed"", ""op"": "">"", ""value"": 1.0 } ] },
                ""do"": [ { ""action"": ""move_joint"", ""joint"": ""lid"", ""target"": 100 }, { ""action"": ""wait"", ""seconds"": 3 } ] } ] }";
            var r = Parse(Dna(program: p));
            Assert.IsTrue(r.IsValid, r.Summary);
            var (it, lid) = Run(r.Dna, _ => 2.5f, 5f);
            Assert.IsTrue(it.Events.Any(e => e.Kind == "preempted" && e.RuleId == "fast"));
            Assert.IsTrue(it.Events.Any(e => e.Kind == "skipped" && e.RuleId == "slow"), "前のルールの動作は打ち切り、記録に残す");
            Assert.AreEqual(100f, lid.Last(), 0.01f);
        }

        [Test] public void WhileRule_SetsBehaviorWhileTrue()
        {
            string p = @"{ ""version"": 1, ""flags"": [], ""rules"": [ { ""id"": ""watch"", ""trigger"": ""while"",
                ""when"": { ""all"": [ { ""sensor"": ""target_detected"", ""is"": false } ] },
                ""do"": [ { ""action"": ""set_parameter"", ""behavior"": ""detect"", ""parameter"": ""rangeM"", ""value"": 1 } ] } ] }";
            var r = Parse(Dna(program: p));
            Assert.IsTrue(r.IsValid, r.Summary);
            var (it, _) = Run(r.Dna, t => t < 1f ? 5f : 1f, 2f);
            Assert.AreEqual(1f, it.ParameterOverrides["detect.rangeM"]);
            Assert.AreEqual(1, it.Events.Count(e => e.Kind == ProgramAction.SetParameter), "同じ値は毎刻み記録しない");
        }

        [Test] public void DisabledRule_DoesNotRun_AndFeatureFlagExists()
        {
            var dna = Parse(Dna()).Dna;
            dna.Program.Rules[0].Enabled = false;
            var (it, lid) = Run(dna, Approach, 10f);
            Assert.AreEqual(0f, lid.Max());
            Assert.AreEqual(0, it.Events.Count);
            Assert.IsTrue(Features.MotionProgram);
        }

        [Test] public void ProgramText_DescribesRuleFromData()
        {
            var dna = Parse(Dna()).Dna;
            string t = ProgramText.Rule(dna.Program.Rules[0], 0, dna);
            StringAssert.StartsWith("①", t);
            StringAssert.Contains("対象までの距離が 1m より小さい", t);
            StringAssert.Contains("状態「opened」が偽", t);
            StringAssert.Contains("板（lid）を 100°へ", t);
            StringAssert.Contains("3秒待つ", t);
            StringAssert.Contains("▶ 人が近づいたらフタを開ける", ProgramText.TimelineLine("2.02 open_lid start", dna));
        }

        static RevisionResult UpdateRule(InventionDna dna, string field, int index, float value)
        {
            var p = new RevisionProposal { Source = "manual" };
            p.Operations.Add(new RevisionOperation { Op = RevisionOps.UpdateRule, RuleId = "open_lid", RuleField = field, Index = index, Value = value });
            return RevisionEngine.Apply(dna, p);
        }

        [Test] public void UpdateRule_ChangesValuesWithClampAndHistory()
        {
            var dna = Parse(Dna()).Dna;
            var off = UpdateRule(dna, "enabled", 0, 0f);
            Assert.IsTrue(off.Success, string.Join(",", off.Errors));
            Assert.IsFalse(off.NewDna.Program.Rules[0].Enabled);
            Assert.IsTrue(dna.Program.Rules[0].Enabled, "元の版は変えない");
            var wait = UpdateRule(dna, "seconds", 2, 999f);
            Assert.IsTrue(wait.Success);
            Assert.AreEqual(ProgramCatalog.MaxWait, wait.NewDna.Program.Rules[0].Do[2].Seconds, "範囲内に補正");
            Assert.IsTrue(wait.Warnings.Any(w => w.Contains("補正")));
            var target = UpdateRule(dna, "target", 0, 80f);
            Assert.AreEqual(80f, target.NewDna.Program.Rules[0].Do[0].Target);
            StringAssert.Contains("100°", target.Changes[0].Before);
            var dist = UpdateRule(dna, "when", 0, 1.5f);
            Assert.AreEqual(1.5f, dist.NewDna.Program.Rules[0].When[0].Value);
            Assert.IsFalse(UpdateRule(dna, "when", 1, 1f).Success, "フラグの条件は数値ではない");
            Assert.IsFalse(UpdateRule(dna, "target", 2, 10f).Success, "wait は関節の動きではない");
            Assert.IsTrue(RevisionOps.IsValid(RevisionOps.UpdateRule), "G-A3 から AI の改良提案でも使える");
            Assert.IsFalse(RevisionOps.IsValid(RevisionOps.UpdatePart), "形・素材の切り替えは手動だけ");
        }

        [Test] public void Revision_KeepsProgramAndJoints()
        {
            var dna = Parse(Dna()).Dna;
            var p = new RevisionProposal { Source = "manual" };
            p.Operations.Add(new RevisionOperation { Op = RevisionOps.UpdatePart, PartId = "bin", Material = "steel" });
            var res = RevisionEngine.Apply(dna, p);
            Assert.IsTrue(res.Success, string.Join(",", res.Errors));
            Assert.IsNotNull(res.NewDna.Program);
            Assert.IsNotNull(res.NewDna.FindPart("lid").Joint);
            Assert.AreNotSame(dna.Program, res.NewDna.Program, "版ごとに別のデータ");
        }
    }
}
