using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ImpossibleLab.Core
{
    /// <summary>
    /// 発明ライブラリの端末側の記録（persistentDataPath/library.json）。docs/PUBLISH_LIBRARY.md 4章。
    /// 発案者の鍵は端末ごとに1つ作って残す（PC はハッシュだけを持つ）。公開した発明の公開 ID と、見た採用の知らせを覚える。
    /// </summary>
    public sealed class LibraryLocal
    {
        public const string FileName = "library.json";
        public const int MaxDisplayName = 40;
        static readonly Regex KeyRule = new Regex("^[A-Za-z0-9_-]{16,64}$");

        public string CreatorKey = "";
        /// <summary>前に公開したときの表示名（次の公開で最初から入れておく）。</summary>
        public string DisplayName = "";
        /// <summary>発明 ID → 公開 ID。</summary>
        public Dictionary<string, string> Published = new Dictionary<string, string>();
        /// <summary>知らせを出し終えた採用の ID。</summary>
        public HashSet<string> SeenAdoptions = new HashSet<string>();

        public static string NewKey() => "q-" + Guid.NewGuid().ToString("N");

        /// <summary>読む。無い・壊れているときは新しい鍵で作り直す（壊れた記録で公開できなくならないように）。</summary>
        public static LibraryLocal Load(string dir)
        {
            var l = new LibraryLocal();
            try
            {
                string path = Path.Combine(dir, FileName);
                if (File.Exists(path))
                {
                    var o = JObject.Parse(File.ReadAllText(path));
                    l.CreatorKey = o.Value<string>("creatorKey") ?? "";
                    l.DisplayName = Clip(o.Value<string>("displayName") ?? "", MaxDisplayName);
                    if (o["published"] is JObject pub)
                        foreach (var p in pub.Properties())
                            if (p.Value.Type == JTokenType.String && Regex.IsMatch((string)p.Value, "^[a-z0-9-]{3,40}$")) l.Published[p.Name] = (string)p.Value;
                    if (o["seenAdoptions"] is JArray seen)
                        foreach (var s in seen) if (s.Type == JTokenType.String) l.SeenAdoptions.Add((string)s);
                }
            }
            catch (Exception) { l = new LibraryLocal(); }
            if (!KeyRule.IsMatch(l.CreatorKey ?? "")) l.CreatorKey = NewKey();
            return l;
        }

        public void Save(string dir)
        {
            var o = new JObject
            {
                ["creatorKey"] = CreatorKey,
                ["displayName"] = DisplayName ?? "",
                ["published"] = JObject.FromObject(Published),
                ["seenAdoptions"] = new JArray(SeenAdoptions.OrderBy(x => x, StringComparer.Ordinal)),
            };
            string path = Path.Combine(dir, FileName), tmp = path + ".tmp";
            File.WriteAllText(tmp, o.ToString(Formatting.Indented));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        static string Clip(string s, int n) => s.Length <= n ? s : s.Substring(0, n);
    }

    /// <summary>Quest の自分の公開（GET /library/mine の1件）。</summary>
    public sealed class MinePublication
    {
        public string Id, InventionId, Name, Status, WithdrawnBy;
        public int Revision, WantsVr, WantsSite, Tried, Considering;
        public List<MineAdoption> Adoptions = new List<MineAdoption>();
        public bool Live => Status != "withdrawn";
        public string StatusJa => LibraryProtocol.StatusJa(Status);
    }

    public sealed class MineAdoption
    {
        public string Id, At, Reason, AdopterName, AdopterAffiliation, AdopterRole, ThreadId, LastFrom;
        public bool AdopterVerified;
        public int MessageCount;
        public string RoleJa => AdopterRole == "developer" ? "開発者" : "研究者";
    }

    /// <summary>発明ライブラリ（PC の proxy/src/library）との約束：送る形と、返ってくる形の読み方。</summary>
    public static class LibraryProtocol
    {
        /// <summary>写真1枚の上限（PC 側の上限 300KB に合わせる）。</summary>
        public const int MaxJpegBytes = 300 * 1024;

        public static string StatusJa(string s) =>
            s == "open" ? "公開中" : s == "considering" ? "検討中" : s == "adopted" ? "採用" : s == "withdrawn" ? "取り下げ" : s ?? "";

        /// <summary>公開できない理由（無ければ null）。保存していて、実験を1回以上した発明だけ公開できる。</summary>
        public static string PublishProblem(SavedInvention inv, bool? pcOnline)
        {
            if (inv?.Current == null) return "保存した発明がありません";
            if (inv.Experiments == null || inv.Experiments.Count == 0) return "実験を1回してから公開できます";
            if (pcOnline == false) return "PC とつながっていないので公開できません";
            return null;
        }

        /// <summary>POST /library の本文。写真は JPEG（大きすぎるものは入れない）。</summary>
        public static string PublishBody(JObject publication, byte[] glb, IList<(string kind, string caption, int revision, byte[] jpg)> images)
        {
            var arr = new JArray();
            foreach (var im in images ?? Array.Empty<(string, string, int, byte[])>())
            {
                if (im.jpg == null || im.jpg.Length == 0 || im.jpg.Length > MaxJpegBytes) continue;
                arr.Add(new JObject
                {
                    ["kind"] = im.kind, ["caption"] = PublicationBuilder.Line(im.caption ?? "", 80),
                    ["revision"] = Math.Max(1, im.revision), ["jpgBase64"] = Convert.ToBase64String(im.jpg),
                });
                if (arr.Count >= 4) break;
            }
            return new JObject { ["publication"] = publication, ["glbBase64"] = Convert.ToBase64String(glb ?? Array.Empty<byte>()), ["images"] = arr }
                .ToString(Formatting.None);
        }

        /// <summary>公開の結果を読む。成功なら公開 ID、失敗なら画面に出す理由。</summary>
        public static (string id, string error) ParsePublish(long code, string body, string transportError)
        {
            if (transportError != null) return (null, transportError);
            var o = TryParse(body);
            if ((code == 200 || code == 201) && o?.Value<string>("status") == "ok")
            {
                string id = o.Value<string>("id");
                if (!string.IsNullOrEmpty(id) && Regex.IsMatch(id, "^[a-z0-9-]{3,40}$")) return (id, null);
                return (null, "PC から公開 ID が返ってきませんでした");
            }
            return (null, ErrorMessage(code, o));
        }

        /// <summary>自分の公開の一覧（GET /library/mine）を読む。形が合わない項目は飛ばす。</summary>
        public static List<MinePublication> ParseMine(string body)
        {
            var list = new List<MinePublication>();
            if (!(TryParse(body)?["items"] is JArray items)) return list;
            foreach (var t in items.OfType<JObject>())
            {
                string id = t.Value<string>("id"), inv = t.Value<string>("inventionId");
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(inv)) continue;
                var m = new MinePublication
                {
                    Id = id, InventionId = inv, Name = t.Value<string>("name") ?? "", Status = t.Value<string>("status") ?? "open",
                    WithdrawnBy = t.Value<string>("withdrawnBy"), Revision = t.Value<int?>("revision") ?? 0,
                    WantsVr = t["wants"]?.Value<int?>("vr") ?? 0, WantsSite = t["wants"]?.Value<int?>("site") ?? 0,
                    Tried = t.Value<int?>("tried") ?? 0, Considering = t.Value<int?>("considering") ?? 0,
                };
                if (t["adoptions"] is JArray ads)
                    foreach (var a in ads.OfType<JObject>())
                    {
                        string aid = a.Value<string>("id");
                        if (string.IsNullOrEmpty(aid)) continue;
                        var who = a["adopter"] as JObject;
                        m.Adoptions.Add(new MineAdoption
                        {
                            Id = aid, At = a.Value<string>("at") ?? "", Reason = a.Value<string>("reason") ?? "", ThreadId = a.Value<string>("threadId"),
                            AdopterName = who?.Value<string>("name") ?? "", AdopterAffiliation = who?.Value<string>("affiliation") ?? "",
                            AdopterRole = who?.Value<string>("role") ?? "researcher", AdopterVerified = who?.Value<bool?>("verified") ?? false,
                            MessageCount = a.Value<int?>("messageCount") ?? 0, LastFrom = a.Value<string>("lastFrom"),
                        });
                    }
                list.Add(m);
            }
            return list;
        }

        /// <summary>PC が返した失敗の理由（{status:"error", message}）。読めなければ状態の番号で。</summary>
        public static string ErrorMessage(long code, JObject o)
        {
            string msg = o?.Value<string>("message");
            if (!string.IsNullOrWhiteSpace(msg)) return msg;
            if (code == 404) return "PC のプログラムが古く、発明ライブラリに対応していません";
            return $"PC が受け付けませんでした（{code}）";
        }

        static JObject TryParse(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try { return JObject.Parse(body); } catch (JsonException) { return null; }
        }
    }
}
