using System.IO;
using System.Linq;
using ImpossibleLab.Core;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>発明DNA v2（タグ・取り付け位置・形・素材・機能対応表）と、v1 からの変換。</summary>
    public class DnaV2Tests
    {
        static string Sample(string file) => Resources.Load<TextAsset>("ImpossibleLabSamples/" + file).text;

        const string V2 = @"{
  ""schemaVersion"": 2, ""id"": ""night-light-bag"", ""name"": ""夜道を照らすリュック"", ""purpose"": ""足元を照らす"",
  ""tags"": [""lighting"", ""safety""], ""mount"": ""back"", ""appearance"": ""night_navy"",
  ""parts"": [
    { ""id"": ""pack"", ""type"": ""body"", ""shape"": ""box"", ""material"": ""fabric"", ""position"": [0, 0, 0], ""scale"": [0.3, 0.42, 0.14] },
    { ""id"": ""lamp"", ""type"": ""light"", ""position"": [0, -0.18, 0.08], ""scale"": [0.05, 0.03, 0.05] },
    { ""id"": ""cell"", ""type"": ""battery"", ""shape"": ""box"", ""position"": [0, 0.1, -0.04], ""scale"": [0.07, 0.1, 0.03] }
  ],
  ""behaviors"": [ { ""type"": ""stabilize"", ""targetPart"": ""pack"", ""parameters"": { ""stabilizationStrength"": 0.2 } } ],
  ""functionMap"": [
    { ""need"": ""揺れを抑える"", ""blocks"": [""stabilize""], ""status"": ""simplified"", ""note"": ""見た目のみ"" },
    { ""need"": ""雨でも使える"", ""blocks"": [], ""status"": ""unverifiable"", ""note"": ""防水性能は検証できない"" }
  ],
  ""revision"": 1, ""parentRevision"": null
}";

        [Test] public void V1Samples_AreMigratedToV2()
        {
            foreach (var (file, cat, mount) in new[] { ("handsfree_rain", "protection", "head"), ("carrier_robot", "carrier", "ground"), ("flying_shoes", "mobility", "foot") })
            {
                var r = DnaJson.Parse(Sample(file));
                Assert.IsTrue(r.IsValid, file + ": " + r.Summary);
                Assert.AreEqual(2, r.Dna.SchemaVersion);
                Assert.AreEqual(cat, r.Dna.Category);
                CollectionAssert.AreEqual(new[] { cat }, r.Dna.Tags);
                Assert.AreEqual(mount, r.Dna.Mount, file);
                Assert.IsEmpty(r.Warnings, "v1 の変換は補正扱いにしない");
                // v1 の見た目を保つ：形の既定値が以前と同じ
                Assert.IsTrue(r.Dna.Parts.All(p => p.Shape == "" && LabCatalog.Shape(p.ShapeOrDefault) != null));
            }
            var rain = DnaJson.Parse(Sample("handsfree_rain")).Dna;
            Assert.AreEqual("cylinder", rain.FindPart("shield").ShapeOrDefault);
            Assert.AreEqual("sphere", rain.FindPart("eye").ShapeOrDefault);
            Assert.AreEqual("box", rain.FindPart("gyro").ShapeOrDefault);
            Assert.AreEqual("fabric", rain.FindPart("shield").MaterialOrDefault);
        }

        [Test] public void V1_SavesAsV2_AndRoundTrips()
        {
            var dna = DnaJson.Parse(Sample("carrier_robot")).Dna;
            string json = DnaJson.ToJson(dna);
            StringAssert.Contains("\"schemaVersion\": 2", json);
            StringAssert.DoesNotContain("\"category\"", json);
            var back = DnaJson.Parse(json);
            Assert.IsTrue(back.IsValid, back.Summary);
            Assert.AreEqual("carrier", back.Dna.Category);
            Assert.AreEqual("ground", back.Dna.Mount);
            Assert.AreEqual("rubber", back.Dna.FindPart("wheel-fl").Material, "保存時に既定の素材を明示する");
            Assert.AreEqual(DnaJson.ToJson(back.Dna), json, "v2 → v2 は変化しない");
        }

        [Test] public void V2_ParsesTagsMountShapesAndFunctionMap()
        {
            var r = DnaJson.Parse(V2);
            Assert.IsTrue(r.IsValid, r.Summary);
            var d = r.Dna;
            CollectionAssert.AreEqual(new[] { "lighting", "safety" }, d.Tags);
            Assert.AreEqual("", d.Category, "旧カテゴリのタグがない発明は実験場が未定");
            Assert.AreEqual("back", d.Mount);
            Assert.AreEqual("fabric", d.FindPart("pack").Material);
            Assert.AreEqual("cylinder", d.FindPart("lamp").ShapeOrDefault, "light の既定の形");
            Assert.AreEqual(2, d.FunctionMap.Count);
            Assert.AreEqual("unverifiable", d.FunctionMap[1].Status);
            var clone = d.Clone();
            clone.Tags.Add("daily"); clone.FunctionMap[0].Blocks.Clear();
            Assert.AreEqual(2, d.Tags.Count, "Clone はタグを共有しない");
            Assert.AreEqual(1, d.FunctionMap[0].Blocks.Count, "Clone は機能対応表を共有しない");
            var back = DnaJson.Parse(DnaJson.ToJson(d));
            Assert.IsTrue(back.IsValid, back.Summary);
            Assert.AreEqual(d.FunctionMap[1].Note, back.Dna.FunctionMap[1].Note);
        }

        [TestCase("\"tags\": [\"lighting\", \"safety\"]", "\"tags\": [\"laser\"]", "未対応のタグ")]
        [TestCase("\"tags\": [\"lighting\", \"safety\"]", "\"tags\": []", "tags は1〜3")]
        [TestCase("\"tags\": [\"lighting\", \"safety\"]", "\"tags\": [\"lighting\", \"lighting\"]", "重複")]
        [TestCase("\"tags\": [\"lighting\", \"safety\"]", "\"tags\": [\"lighting\", \"safety\", \"daily\", \"outdoor\"]", "tags は1〜3")]
        [TestCase("\"mount\": \"back\"", "\"mount\": \"tail\"", "取り付け位置")]
        [TestCase("\"shape\": \"box\", \"material\": \"fabric\"", "\"shape\": \"cone\", \"material\": \"fabric\"", "shape")]
        [TestCase("\"shape\": \"box\", \"material\": \"fabric\"", "\"shape\": \"box\", \"material\": \"vibranium\"", "material")]
        [TestCase("\"blocks\": [\"stabilize\"], \"status\": \"simplified\"", "\"blocks\": [\"teleport\"], \"status\": \"simplified\"", "無い機能")]
        [TestCase("\"blocks\": [\"stabilize\"], \"status\": \"simplified\"", "\"blocks\": [], \"status\": \"simplified\"", "blocks")]
        [TestCase("\"status\": \"unverifiable\", \"note\": \"防水性能は検証できない\"", "\"status\": \"unverifiable\"", "理由")]
        [TestCase("\"status\": \"simplified\"", "\"status\": \"guaranteed\"", "status")]
        [TestCase("\"mount\": \"back\",", "\"mount\": \"back\", \"category\": \"protection\",", "未知のフィールド")]
        public void V2_RejectsInvalidFields(string from, string to, string expect)
        {
            Assert.IsTrue(V2.Contains(from), "テストの置換元がない: " + from);
            var r = DnaJson.Parse(V2.Replace(from, to));
            Assert.IsFalse(r.IsValid);
            Assert.IsTrue(r.Errors.Any(e => e.Contains(expect)), string.Join(" / ", r.Errors));
        }

        [Test] public void V1_DoesNotAcceptV2OnlyFields()
        {
            var json = Sample("handsfree_rain").Replace("\"mode\": \"dream\",", "\"mode\": \"dream\", \"mount\": \"head\",");
            Assert.IsFalse(DnaJson.Parse(json).IsValid);
        }

        [Test] public void PartLimit_Is24()
        {
            string Parts(int n) => string.Join(",", Enumerable.Range(0, n).Select(i => $"{{\"id\":\"p{i}\",\"type\":\"frame\",\"position\":[0,0,0],\"scale\":[0.1,0.1,0.1]}}"));
            string Json(int n) => "{\"schemaVersion\":2,\"id\":\"x\",\"name\":\"x\",\"purpose\":\"x\",\"tags\":[\"daily\"],\"mount\":\"fixed\",\"parts\":[" + Parts(n) + "],\"behaviors\":[]}";
            Assert.IsTrue(DnaJson.Parse(Json(24)).IsValid);
            Assert.IsFalse(DnaJson.Parse(Json(25)).IsValid);
        }

        [Test] public void OldSaveFile_LoadsAndResavesAsV2()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ilab_v2_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(dir, "inventions"));
                // v1 形式の保存ファイル（2026-11-08 以前のアプリが書いた形）
                string v1 = Sample("handsfree_rain");
                File.WriteAllText(Path.Combine(dir, "inventions", "inv-handsfree-rain.json"),
                    "{\"format\":1,\"updatedAt\":\"2026-11-08 12:00\",\"current\":" + v1 + ",\"history\":[" + v1 + "],\"experiments\":[]}");
                var repo = new InventionRepository(dir);
                var inv = repo.Load("inv-handsfree-rain", out var err);
                Assert.IsNotNull(inv, err);
                Assert.AreEqual("head", inv.Current.Mount);
                Assert.AreEqual(1, inv.History.Count);
                repo.Save(inv);
                StringAssert.Contains("\"schemaVersion\": 2", File.ReadAllText(Path.Combine(dir, "inventions", "inv-handsfree-rain.json")));
                Assert.IsNotNull(repo.Load("inv-handsfree-rain", out _));
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test] public void Revision_AddPart_AcceptsV2Fields()
        {
            var dna = DnaJson.Parse(Sample("carrier_robot")).Dna;
            string json = "{\"summary\":\"ライトを追加\",\"notes\":[],\"operations\":[{\"op\":\"add_part\",\"part\":{\"id\":\"lamp\",\"type\":\"light\",\"shape\":\"sphere\",\"material\":\"plastic\",\"position\":[0,0.4,0.3],\"scale\":[0.06,0.06,0.06]}}]}";
            Assert.IsTrue(RevisionEngine.TryParseProposal(json, out var proposal, out var error), error);
            var res = RevisionEngine.Apply(dna, proposal);
            Assert.IsTrue(res.Success, string.Join(" / ", res.Errors));
            Assert.AreEqual("sphere", res.NewDna.FindPart("lamp").Shape);
            Assert.AreEqual("carrier", res.NewDna.Category, "改良でタグは変わらない");
        }
    }
}
