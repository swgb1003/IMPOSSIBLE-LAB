using System.IO;
using ImpossibleLab.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>依頼文と改良の記録の保存（docs/PUBLISH_LIBRARY.md 4章「新しく保存するデータ」）。</summary>
    public class RequestRecordTests
    {
        static InventionDna Sample()
        {
            var r = DnaJson.Parse(Resources.Load<TextAsset>("ImpossibleLabSamples/handsfree_rain").text);
            Assert.IsTrue(r.IsValid, r.Summary);
            return r.Dna;
        }

        static string TempDir() => Path.Combine(Path.GetTempPath(), "ilab-test-" + System.Guid.NewGuid().ToString("N"));

        static (RevisionResult res, RevisionProposal proposal) Widen(InventionDna dna, float radius, string source = "ai")
        {
            var p = new RevisionProposal { Source = source, Summary = "防ぐ範囲を広げる" };
            p.Operations.Add(new RevisionOperation { Op = RevisionOps.UpdateParameter, BehaviorType = "rain_shield", Parameter = "radius", Value = radius });
            var res = RevisionEngine.Apply(dna, p);
            Assert.IsTrue(res.Success, string.Join(" / ", res.Errors));
            return (res, p);
        }

        [Test] public void RecordRevision_KeepsRequestSourceAndChanges()
        {
            var dna = Sample();
            var inv = new SavedInvention { Current = dna, RequestText = "手を使わず雨を防ぐ装置を作って" };
            var (res, p) = Widen(dna, 0.8f);
            inv.RecordRevision(res, p, "  防ぐ範囲を広げて ", "2026-11-26T10:00:00");
            var note = inv.NoteFor(2);
            Assert.IsNotNull(note);
            Assert.AreEqual("ai", note.Source);
            Assert.AreEqual("防ぐ範囲を広げて", note.Request, "前後の空白は落とす");
            Assert.AreEqual(1, note.ParentRevision);
            Assert.AreEqual("防ぐ範囲を広げる", note.Summary);
            Assert.AreEqual(1, note.Changes.Count);
            Assert.AreEqual(res.Changes[0].After, note.Changes[0].After);
        }

        [Test] public void ManualChange_HasNoRequest_UnknownSourceBecomesManual()
        {
            var dna = Sample();
            var inv = new SavedInvention { Current = dna };
            var (res, p) = Widen(dna, 0.7f, source: "something");
            inv.RecordRevision(res, p, null, "");
            Assert.AreEqual("manual", inv.NoteFor(2).Source);
            Assert.AreEqual("", inv.NoteFor(2).Request);
        }

        [Test] public void Undo_ForgetsTheRevision_AndRedoReplacesIt()
        {
            var dna = Sample();
            var inv = new SavedInvention { Current = dna };
            var (r2, p2) = Widen(dna, 0.7f);
            inv.RecordRevision(r2, p2, "横からの雨も防いで", "");
            var (r3, p3) = Widen(r2.NewDna, 0.8f);
            inv.RecordRevision(r3, p3, "防ぐ範囲を広げて", "");
            Assert.AreEqual(2, inv.RevisionNotes.Count);
            inv.ForgetRevision(3);
            Assert.IsNull(inv.NoteFor(3));
            Assert.IsNotNull(inv.NoteFor(2));
            // 元に戻してから別の頼み方で Rev.3 を作り直す → 前の Rev.3 の記録は残らない
            var (r3b, p3b) = Widen(r2.NewDna, 0.9f);
            inv.RecordRevision(r3b, p3b, "もっと広く", "");
            inv.RecordRevision(r3b, p3b, "もっと広く（2回目）", "");
            Assert.AreEqual(2, inv.RevisionNotes.Count);
            Assert.AreEqual("もっと広く（2回目）", inv.NoteFor(3).Request);
        }

        [Test] public void Repository_SavesAndLoadsRequestAndNotes()
        {
            string dir = TempDir();
            try
            {
                var dna = Sample();
                var inv = new SavedInvention { Current = dna, RequestText = "手を使わず雨を防ぐ装置を作って", DesignSource = "offline" };
                var (res, p) = Widen(dna, 0.8f);
                inv.History.Add(dna);
                inv.Current = res.NewDna;
                inv.RecordRevision(res, p, "防ぐ範囲を広げて", "2026-11-26T10:00:00");
                new InventionRepository(dir).Save(inv);

                var loaded = new InventionRepository(dir).Load(dna.Id, out var err);
                Assert.IsNull(err);
                Assert.AreEqual("手を使わず雨を防ぐ装置を作って", loaded.RequestText);
                Assert.AreEqual("offline", loaded.DesignSource);
                Assert.AreEqual(1, loaded.RevisionNotes.Count);
                var n = loaded.NoteFor(2);
                Assert.AreEqual("防ぐ範囲を広げて", n.Request);
                Assert.AreEqual("ai", n.Source);
                Assert.AreEqual("2026-11-26T10:00:00", n.At);
                Assert.AreEqual(res.Changes[0].Label, n.Changes[0].Label);
                Assert.AreEqual(res.Changes[0].Before, n.Changes[0].Before);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test] public void OldSaveWithoutRecords_LoadsWithEmptyRecords()
        {
            string dir = TempDir();
            try
            {
                var repo = new InventionRepository(dir);
                var dna = Sample();
                repo.Save(new SavedInvention { Current = dna });
                // 依頼文を保存する前の形（欄そのものが無い）
                string path = Path.Combine(repo.DirectoryPath, dna.Id + ".json");
                var root = JObject.Parse(File.ReadAllText(path));
                root.Remove("requestText"); root.Remove("designSource"); root.Remove("revisionNotes");
                File.WriteAllText(path, root.ToString());
                var loaded = new InventionRepository(dir).Load(dna.Id, out var err);
                Assert.IsNull(err);
                Assert.AreEqual("", loaded.RequestText);
                Assert.AreEqual("", loaded.DesignSource);
                Assert.AreEqual(0, loaded.RevisionNotes.Count);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test] public void BrokenRecords_AreDroppedOrTrimmed_WithoutLosingTheInvention()
        {
            string dir = TempDir();
            try
            {
                var repo = new InventionRepository(dir);
                var dna = Sample();
                dna.Revision = 3;
                repo.Save(new SavedInvention { Current = dna });
                string path = Path.Combine(repo.DirectoryPath, dna.Id + ".json");
                var root = JObject.Parse(File.ReadAllText(path));
                root["requestText"] = new string('あ', 1000);
                root["designSource"] = "evil";
                root["revisionNotes"] = new JArray
                {
                    new JObject { ["revision"] = 2, ["source"] = "ai", ["request"] = new string('い', 1000), ["summary"] = 5,
                        ["changes"] = new JArray(new JObject { ["label"] = "半径", ["before"] = "0.60", ["after"] = "0.80" }, "not an object") },
                    new JObject { ["revision"] = 2, ["source"] = "ai", ["request"] = "同じ版の2つ目" },
                    new JObject { ["revision"] = 3, ["source"] = "script", ["request"] = "未知の出どころ" },
                    new JObject { ["revision"] = 9, ["source"] = "manual" },
                    new JObject { ["revision"] = "3", ["source"] = "manual" },
                    new JObject { ["revision"] = 1, ["source"] = "manual" },
                };
                File.WriteAllText(path, root.ToString());
                var loaded = new InventionRepository(dir).Load(dna.Id, out var err);
                Assert.IsNull(err, "記録が壊れていても発明は読める");
                Assert.AreEqual(RevisionNote.MaxRequestLength, loaded.RequestText.Length);
                Assert.AreEqual("", loaded.DesignSource);
                Assert.AreEqual(1, loaded.RevisionNotes.Count, "形の合う記録だけ（同じ版は最初の1つ・今の版より後は捨てる）");
                var n = loaded.NoteFor(2);
                Assert.AreEqual(RevisionNote.MaxRequestLength, n.Request.Length);
                Assert.AreEqual("", n.Summary);
                Assert.AreEqual(1, n.Changes.Count);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }
    }
}
