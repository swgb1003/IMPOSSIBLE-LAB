using System.Linq;
using ImpossibleLab.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>製品の説明（概要・部品の役割・しくみ）。表示だけの文なので、長すぎても設計は拒否せず、書式の記号は無害にする。</summary>
    public class ProductExplainTests
    {
        static JObject Bin() => JObject.Parse(Resources.Load<TextAsset>("ImpossibleLabSamples/auto_bin").text);

        [Test] public void Fields_RoundTrip()
        {
            var r = DnaJson.Parse(Bin().ToString());
            Assert.IsTrue(r.IsValid, r.Summary);
            Assert.IsNotEmpty(r.Dna.Overview);
            Assert.IsNotEmpty(r.Dna.FindPart("eye").Role);
            Assert.IsNotEmpty(r.Dna.FunctionMap[0].How);
            var again = DnaJson.Parse(DnaJson.ToJson(r.Dna)).Dna;
            Assert.AreEqual(r.Dna.Overview, again.Overview);
            Assert.AreEqual(r.Dna.FindPart("eye").Role, again.FindPart("eye").Role);
            Assert.AreEqual(r.Dna.FunctionMap[0].How, again.FunctionMap[0].How);
            Assert.AreEqual(r.Dna.FunctionMap[0].How, r.Dna.Clone().FunctionMap[0].How);
        }

        [Test] public void LongOrMarkupText_IsCleaned_NotRejected()
        {
            var o = Bin();
            o["overview"] = new string('あ', LabCatalog.MaxOverviewLength + 40);
            o["parts"][0]["role"] = "<color=red>赤</color>\n改行";
            o["functionMap"][0]["how"] = new string('い', 300);
            var r = DnaJson.Parse(o.ToString());
            Assert.IsTrue(r.IsValid, r.Summary);
            Assert.AreEqual(LabCatalog.MaxOverviewLength, r.Dna.Overview.Length);
            Assert.AreEqual("＜color=red＞赤＜/color＞ 改行", r.Dna.Parts[0].Role);
            Assert.AreEqual(LabCatalog.MaxHowLength, r.Dna.FunctionMap[0].How.Length);
            o["parts"][0]["role"] = 3;
            Assert.IsFalse(DnaJson.Parse(o.ToString()).IsValid, "文字列でない説明は不正");
        }

        [Test] public void Derived_WhenDesignHasNoText()
        {
            var o = Bin();
            o.Remove("overview");
            foreach (JObject p in o["parts"]) p.Remove("role");
            foreach (JObject n in o["functionMap"]) n.Remove("how");
            var dna = DnaJson.Parse(o.ToString()).Dna;
            var role = ProductExplain.Role(dna, dna.FindPart("eye"));
            Assert.IsTrue(role.Derived);
            StringAssert.Contains("検知", role.Value, "受け持つ機能の名前を入れる");
            var how = ProductExplain.How(dna, dna.FunctionMap[0]);
            Assert.IsTrue(how.Derived);
            StringAssert.Contains("eye", how.Value);
            Assert.IsTrue(ProductExplain.How(dna, dna.FunctionMap[1]).Derived);
            Assert.AreEqual(dna.Purpose, ProductExplain.Overview(dna).Value, "概要が無ければ目的");
        }

        [Test] public void Samples_HaveTextForEveryPart()
        {
            foreach (var t in Resources.LoadAll<TextAsset>("ImpossibleLabSamples"))
            {
                var r = DnaJson.Parse(t.text);
                Assert.IsTrue(r.IsValid, t.name + "：" + r.Summary);
                Assert.IsNotEmpty(r.Dna.Overview, t.name);
                foreach (var p in r.Dna.Parts) Assert.IsNotEmpty(p.Role, $"{t.name}：{p.Id}");
                foreach (var n in r.Dna.FunctionMap) Assert.IsNotEmpty(n.How, $"{t.name}：{n.Need}");
            }
        }

        [Test] public void Revision_AddPart_KeepsRole()
        {
            var json = "{\"status\":\"ok\",\"proposal\":{\"summary\":\"灯りを足す\",\"notes\":[],\"operations\":[{\"op\":\"add_part\",\"part\":{\"id\":\"lamp2\",\"type\":\"light\",\"shape\":\"sphere\",\"material\":\"plastic\",\"position\":[0,0.4,0],\"scale\":[0.04,0.04,0.04],\"role\":\"ゴミ箱の中を照らす\"}}]}}";
            var o = ProxyProtocol.ParseRevision(200, json, null);
            Assert.AreEqual(DesignStatus.Success, o.Status, o.Message);
            Assert.AreEqual("ゴミ箱の中を照らす", o.Proposal.Operations.Single().Part.Role);
        }
    }
}
