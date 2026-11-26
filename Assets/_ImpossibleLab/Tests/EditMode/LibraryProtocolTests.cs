using System;
using System.IO;
using System.Linq;
using ImpossibleLab.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>発明ライブラリとの約束（Quest 側）：端末の記録・公開できる条件・送る形・返ってくる形（proxy/src/library/routes.ts と同じ）。</summary>
    public class LibraryProtocolTests
    {
        string _dir;
        [SetUp] public void SetUp() { _dir = Path.Combine(Path.GetTempPath(), "ilab-lib-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_dir); }
        [TearDown] public void TearDown() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

        static SavedInvention Tested()
        {
            var r = DnaJson.Parse(Resources.Load<TextAsset>("ImpossibleLabSamples/handsfree_rain").text);
            var inv = new SavedInvention { Current = r.Dna, RequestText = "手を使わず雨を防ぐ装置を作って" };
            inv.Experiments.Add(new RainSimulator(r.Dna, new RainExperimentConfig { Mode = LabModes.Dream, Seed = 2026, DurationSec = 5 }).RunToEnd());
            return inv;
        }

        [Test] public void Local_MakesAKeyOnce_AndKeepsItWithTheNameAndPublications()
        {
            var a = LibraryLocal.Load(_dir);
            StringAssert.IsMatch("^[A-Za-z0-9_-]{16,64}$", a.CreatorKey);
            a.DisplayName = "あおい";
            a.Published["rain-1"] = "p-0001";
            a.SeenAdoptions.Add("a-0001");
            a.Save(_dir);
            var b = LibraryLocal.Load(_dir);
            Assert.AreEqual(a.CreatorKey, b.CreatorKey, "同じ端末は同じ鍵");
            Assert.AreEqual("あおい", b.DisplayName);
            Assert.AreEqual("p-0001", b.Published["rain-1"]);
            Assert.IsTrue(b.SeenAdoptions.Contains("a-0001"));
        }

        [Test] public void Local_ABrokenFile_StartsOverWithANewKey()
        {
            File.WriteAllText(Path.Combine(_dir, LibraryLocal.FileName), "{ 壊れた");
            var l = LibraryLocal.Load(_dir);
            StringAssert.IsMatch("^[A-Za-z0-9_-]{16,64}$", l.CreatorKey);
            File.WriteAllText(Path.Combine(_dir, LibraryLocal.FileName), "{\"creatorKey\":\"short\",\"published\":{\"x\":\"Bad Id!\"}}");
            l = LibraryLocal.Load(_dir);
            Assert.AreNotEqual("short", l.CreatorKey, "形の合わない鍵は使わない");
            Assert.AreEqual(0, l.Published.Count, "形の合わない公開 ID は読まない");
        }

        [Test] public void Problem_NeedsAnExperimentAndThePc()
        {
            var inv = Tested();
            Assert.IsNull(LibraryProtocol.PublishProblem(inv, true));
            Assert.IsNull(LibraryProtocol.PublishProblem(inv, null), "まだ確かめていないときは押せる（送って失敗すれば理由を出す）");
            StringAssert.Contains("PC", LibraryProtocol.PublishProblem(inv, false));
            inv.Experiments.Clear();
            StringAssert.Contains("実験", LibraryProtocol.PublishProblem(inv, true));
            Assert.IsNotNull(LibraryProtocol.PublishProblem(null, true));
        }

        [Test] public void Body_CarriesThePublication_TheModel_AndOnlyPhotosThatFit()
        {
            var pub = PublicationBuilder.Build(Tested(), new PublishOptions { Anonymous = true, AgreedAt = "2026-11-26T10:00:00" });
            var glb = new byte[] { 0x67, 0x6c, 0x54, 0x46, 2, 0, 0, 0 };
            var small = new byte[] { 0xff, 0xd8, 0xff, 0xe0, 1, 2, 3 };
            var huge = new byte[LibraryProtocol.MaxJpegBytes + 1];
            var o = JObject.Parse(LibraryProtocol.PublishBody(pub, glb, new[] { ("stage", "IMPOSSIBLE LAB で組み立てた Rev.1 の試作品", 1, small), ("stage", "大きすぎる", 1, huge) }));
            Assert.AreEqual(1, (int)o["publication"]["format"]);
            CollectionAssert.AreEqual(glb, Convert.FromBase64String((string)o["glbBase64"]));
            var images = (JArray)o["images"];
            Assert.AreEqual(1, images.Count, "上限を超える写真は送らない");
            Assert.AreEqual("stage", (string)images[0]["kind"]);
            CollectionAssert.AreEqual(small, Convert.FromBase64String((string)images[0]["jpgBase64"]));
        }

        [Test] public void Publish_ReadsTheIdOrTheReason()
        {
            Assert.AreEqual(("p-3897b44f0f", (string)null), LibraryProtocol.ParsePublish(201, "{\"status\":\"ok\",\"id\":\"p-3897b44f0f\",\"created\":true}", null));
            var (id, err) = LibraryProtocol.ParsePublish(400, "{\"status\":\"error\",\"code\":\"bad_glb\",\"message\":\"3D のファイルが短すぎます\"}", null);
            Assert.IsNull(id);
            Assert.AreEqual("3D のファイルが短すぎます", err);
            Assert.AreEqual("PC とつながっていません", LibraryProtocol.ParsePublish(0, null, "PC とつながっていません").error);
            StringAssert.Contains("古く", LibraryProtocol.ParsePublish(404, "<html>", null).error, "古い中継プログラムには入口が無い");
            Assert.IsNull(LibraryProtocol.ParsePublish(200, "{\"status\":\"ok\",\"id\":\"../x\"}", null).id, "形の合わない ID は使わない");
        }

        [Test] public void Mine_ReadsStatusCountsAndAdoptions()
        {
            const string body = "{\"items\":[{\"id\":\"p-1\",\"inventionId\":\"rain-1\",\"revision\":3,\"name\":\"ハンズフリー雨よけ装置\",\"status\":\"adopted\",\"withdrawnBy\":null," +
                "\"wants\":{\"vr\":2,\"site\":5},\"tried\":4,\"considering\":1,\"adoptions\":[{\"id\":\"a-1\",\"at\":\"2026-11-26T01:00:00Z\",\"reason\":\"研究に使える\"," +
                "\"adopter\":{\"id\":\"u-1\",\"name\":\"高瀬 真央\",\"affiliation\":\"北辰工科大学\",\"role\":\"researcher\",\"field\":\"\",\"verified\":true},\"threadId\":\"t-1\",\"messageCount\":2,\"lastFrom\":\"adopter\",\"lastAt\":null}]}," +
                "{\"id\":\"p-2\",\"inventionId\":\"x\",\"status\":\"withdrawn\",\"wants\":{\"vr\":0,\"site\":0},\"adoptions\":[]},{\"name\":\"ID の無い項目は飛ばす\"}]}";
            var mine = LibraryProtocol.ParseMine(body);
            Assert.AreEqual(2, mine.Count);
            var m = mine[0];
            Assert.AreEqual("採用", m.StatusJa);
            Assert.AreEqual(7, m.WantsVr + m.WantsSite);
            Assert.AreEqual(4, m.Tried);
            Assert.AreEqual("高瀬 真央", m.Adoptions[0].AdopterName);
            Assert.AreEqual("北辰工科大学", m.Adoptions[0].AdopterAffiliation);
            Assert.IsTrue(m.Adoptions[0].AdopterVerified);
            Assert.IsFalse(mine[1].Live, "取り下げたものは公開中として扱わない");
            Assert.AreEqual(0, LibraryProtocol.ParseMine("壊れた").Count);
        }

        [Test] public void Pair_ReadsTheCodeAndItsLimit_OrTheReason()
        {
            var (code, at, err) = LibraryProtocol.ParsePair(200, "{\"code\":\"042917\",\"expiresAt\":\"2026-11-26T05:10:00.000Z\"}", null);
            Assert.AreEqual("042917", code);
            Assert.IsNull(err);
            Assert.AreEqual(new DateTime(2026, 11, 26, 5, 10, 0, DateTimeKind.Utc), at.ToUniversalTime());
            Assert.AreEqual("042 917", LibraryProtocol.SpacedPair(code));
            Assert.AreEqual("端末の発案者キーがありません", LibraryProtocol.ParsePair(401, "{\"status\":\"error\",\"message\":\"端末の発案者キーがありません\"}", null).error);
            Assert.AreEqual("つながりません", LibraryProtocol.ParsePair(0, null, "つながりません").error);
            Assert.IsNull(LibraryProtocol.ParsePair(200, "{\"code\":\"12ab\"}", null).code, "6 桁の数字でなければ使わない");
        }
    }
}
