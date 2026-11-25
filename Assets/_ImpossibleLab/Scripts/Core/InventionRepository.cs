using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ImpossibleLab.Core
{
    public sealed class SavedInvention
    {
        public InventionDna Current;
        public List<InventionDna> History = new List<InventionDna>();
        public List<RainExperimentResult> Experiments = new List<RainExperimentResult>();
        public string UpdatedAt = "";
        /// <summary>設計を作った依頼の文（原文のまま）。依頼文を保存する前の発明では空（公開ページでは「記録なし」）。</summary>
        public string RequestText = "";
        /// <summary>最初の設計を作ったところ（ai / offline）。空は記録なし。</summary>
        public string DesignSource = "";
        /// <summary>版ごとの改良の記録（Rev.2 以降。何を頼み、何を変えたか）。</summary>
        public List<RevisionNote> RevisionNotes = new List<RevisionNote>();
        public bool Tested => Experiments.Count > 0;

        public RevisionNote NoteFor(int revision) => RevisionNotes.Find(n => n.Revision == revision);

        /// <summary>
        /// res.NewDna の版を作った改良を記録する。request は利用者が頼んだ文（手動の調整では空）。
        /// 元に戻してから作り直した同じ番号の版は、前の記録を置き換える。
        /// </summary>
        public void RecordRevision(RevisionResult res, RevisionProposal proposal, string request, string at)
        {
            if (res?.NewDna == null) return;
            int rev = res.NewDna.Revision;
            RevisionNotes.RemoveAll(n => n.Revision >= rev);
            var note = new RevisionNote
            {
                Revision = rev,
                ParentRevision = res.NewDna.ParentRevision ?? rev - 1,
                Source = RevisionNote.NormalizeSource(proposal?.Source),
                Request = RevisionNote.Trunc((request ?? "").Trim(), RevisionNote.MaxRequestLength),
                Summary = RevisionNote.Trunc(proposal?.Summary, RevisionNote.MaxSummaryLength),
                At = at ?? "",
            };
            foreach (var c in res.Changes.Take(RevisionNote.MaxChanges))
                note.Changes.Add(new ChangeLine
                {
                    Label = RevisionNote.Trunc(c.Label, RevisionNote.MaxChangeText),
                    Before = RevisionNote.Trunc(c.Before, RevisionNote.MaxChangeText),
                    After = RevisionNote.Trunc(c.After, RevisionNote.MaxChangeText),
                });
            RevisionNotes.Add(note);
        }

        /// <summary>元に戻した版（とそれより後）の記録を消す。</summary>
        public void ForgetRevision(int revision) => RevisionNotes.RemoveAll(n => n.Revision >= revision);
    }

    /// <summary>1つの版を作った改良の記録（公開ページの改良の履歴に使う）。音声そのものは残さない。</summary>
    public sealed class RevisionNote
    {
        public const int MaxRequestLength = 400, MaxSummaryLength = 120, MaxChanges = 12, MaxChangeText = 80;
        public static readonly string[] Sources = { "ai", "offline", "manual" };

        public int Revision;
        public int ParentRevision;
        /// <summary>ai（AI の改良案）/ offline（AI につながらないときの見本の改良）/ manual（画面で値・形・素材・手順を変えた）。</summary>
        public string Source = "manual";
        /// <summary>改良を頼んだ文。手動の調整では空。</summary>
        public string Request = "";
        public string Summary = "";
        public List<ChangeLine> Changes = new List<ChangeLine>();
        public string At = "";

        public static string NormalizeSource(string s) => Array.IndexOf(Sources, s) >= 0 ? s : "manual";
        public static string Trunc(string s, int max) => string.IsNullOrEmpty(s) ? "" : s.Length > max ? s.Substring(0, max) : s;
    }

    public sealed class GalleryEntry
    {
        public string Id, Name, Category, UpdatedAt;
        /// <summary>分類タグの日本語（例：防護・安全）。</summary>
        public string TagsJa = "";
        /// <summary>分類タグの ID（S06 の絞り込み用）。</summary>
        public List<string> Tags = new List<string>();
        /// <summary>動作プログラムを持つか（S06 の印）。</summary>
        public bool HasProgram;
        public int Revision;
        public bool Tested;
    }

    /// <summary>S06 ギャラリーのタグ絞り込み。</summary>
    public static class GalleryFilter
    {
        /// <summary>tag が null なら全件。並び（新しい順）は変えない。</summary>
        public static List<GalleryEntry> Apply(IEnumerable<GalleryEntry> entries, string tag) =>
            string.IsNullOrEmpty(tag) ? entries.ToList() : entries.Where(e => e.Tags.Contains(tag)).ToList();

        /// <summary>一覧に出てくるタグと件数（カタログの順。0件のタグは出さない）。</summary>
        public static List<(NamedOption tag, int count)> Counts(IEnumerable<GalleryEntry> entries)
        {
            var list = entries.ToList();
            return LabCatalog.Tags.Select(t => (t, list.Count(e => e.Tags.Contains(t.Id)))).Where(x => x.Item2 > 0).ToList();
        }
    }

    public interface IInventionRepository
    {
        void Save(SavedInvention invention);
        SavedInvention Load(string id, out string error);
        List<GalleryEntry> List();
        bool Delete(string id);
    }

    /// <summary>
    /// ローカルJSON保存（仕様24.2）。ファイル名は検証済み id のみから作り、任意パスを受け付けない。
    /// 1発明 = 1ファイル（現在版・履歴・実験記録）。
    /// </summary>
    public sealed class InventionRepository : IInventionRepository
    {
        public const int FileFormatVersion = 1;
        public const int MaxHistory = 30;
        public const int MaxExperiments = 50;
        readonly string _dir;

        // 一覧の読み込み結果を、ファイルの更新時刻と大きさごとに覚えておく。
        // 一覧を開くたびに全ファイル（履歴・実験記録つき）を読み直すと、Quest では 0.15 秒ほど止まるため。
        sealed class Cached { public DateTime Time; public long Size; public GalleryEntry Entry; public string Error; }
        readonly Dictionary<string, Cached> _listCache = new Dictionary<string, Cached>();

        public InventionRepository(string rootDirectory)
        {
            _dir = Path.Combine(rootDirectory, "inventions");
            Directory.CreateDirectory(_dir);
        }

        public string DirectoryPath => _dir;

        string PathFor(string id)
        {
            if (!DnaJson.IsSafeId(id)) throw new ArgumentException("不正な発明IDです: " + id);
            return Path.Combine(_dir, id + ".json");
        }

        public void Save(SavedInvention inv)
        {
            if (inv?.Current == null) throw new ArgumentNullException(nameof(inv));
            inv.UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            var hist = new JArray();
            foreach (var h in inv.History.Skip(Math.Max(0, inv.History.Count - MaxHistory))) hist.Add(DnaJson.ToJObject(h));
            var exps = new JArray();
            foreach (var e in inv.Experiments.Skip(Math.Max(0, inv.Experiments.Count - MaxExperiments))) exps.Add(ResultToJson(e));
            var root = new JObject
            {
                ["format"] = FileFormatVersion,
                ["updatedAt"] = inv.UpdatedAt,
                ["current"] = DnaJson.ToJObject(inv.Current),
                ["history"] = hist,
                ["experiments"] = exps,
                ["requestText"] = RevisionNote.Trunc(inv.RequestText, RevisionNote.MaxRequestLength),
                ["designSource"] = inv.DesignSource ?? "",
                ["revisionNotes"] = new JArray(inv.RevisionNotes.Skip(Math.Max(0, inv.RevisionNotes.Count - MaxRevisionNotes)).Select(NoteToJson)),
            };
            string path = PathFor(inv.Current.Id);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, root.ToString(Formatting.Indented));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            _listCache.Remove(inv.Current.Id);
        }

        public SavedInvention Load(string id, out string error)
        {
            error = null;
            string path;
            try { path = PathFor(id); } catch (ArgumentException e) { error = e.Message; return null; }
            if (!File.Exists(path)) { error = "保存データが見つかりません"; return null; }
            JObject root;
            // 日時の文字列（実験の開始・改良の時刻）を日付に変えず、書いたままの文字列で読む
            try
            {
                using (var reader = new JsonTextReader(new StringReader(File.ReadAllText(path))) { DateParseHandling = DateParseHandling.None })
                    root = JObject.Load(reader);
            }
            catch (Exception e) when (e is JsonException || e is IOException) { error = "保存データを読み込めません"; return null; }
            if (root.Value<int?>("format") != FileFormatVersion) { error = "保存データの形式が非対応です"; return null; }
            var cur = DnaJson.Parse(root["current"]?.ToString() ?? "");
            if (!cur.IsValid) { error = "保存データの検証に失敗: " + cur.Summary; return null; }
            var inv = new SavedInvention { Current = cur.Dna, UpdatedAt = root.Value<string>("updatedAt") ?? "" };
            // 依頼文と改良の記録は後から足した欄（無い・壊れている記録は空のまま読む。発明そのものは読める）
            inv.RequestText = RevisionNote.Trunc(StringOrEmpty(root["requestText"]), RevisionNote.MaxRequestLength);
            string src = StringOrEmpty(root["designSource"]);
            inv.DesignSource = src == "ai" || src == "offline" ? src : "";
            if (root["revisionNotes"] is JArray notes)
                foreach (var n in notes.OfType<JObject>().Take(MaxRevisionNotes))
                {
                    var note = NoteFromJson(n);
                    if (note != null && note.Revision <= inv.Current.Revision && inv.NoteFor(note.Revision) == null) inv.RevisionNotes.Add(note);
                }
            if (root["history"] is JArray hist)
                foreach (var h in hist)
                {
                    var rep = DnaJson.Parse(h.ToString());
                    if (rep.IsValid) inv.History.Add(rep.Dna);
                }
            if (root["experiments"] is JArray exps)
                foreach (var e in exps.OfType<JObject>())
                {
                    var r = ResultFromJson(e);
                    if (r != null) inv.Experiments.Add(r);
                }
            return inv;
        }

        public List<GalleryEntry> List() => List(null);

        /// <summary>一覧。読み込めない記録（壊れた・検証を通らない DNA や動作プログラム）は出さず、skipped に「id：理由」を入れる。</summary>
        public List<GalleryEntry> List(List<string> skipped)
        {
            var list = new List<GalleryEntry>();
            var seen = new HashSet<string>();
            foreach (var file in Directory.GetFiles(_dir, "*.json"))
            {
                var id = Path.GetFileNameWithoutExtension(file);
                if (!DnaJson.IsSafeId(id)) continue;
                seen.Add(id);
                var info = new FileInfo(file);
                if (!_listCache.TryGetValue(id, out var c) || c.Time != info.LastWriteTimeUtc || c.Size != info.Length)
                {
                    c = new Cached { Time = info.LastWriteTimeUtc, Size = info.Length };
                    var inv = Load(id, out var err);
                    if (inv == null) c.Error = err;
                    else c.Entry = new GalleryEntry
                    {
                        Id = inv.Current.Id, Name = inv.Current.Name, Category = inv.Current.Category, TagsJa = LabCatalog.TagsJa(inv.Current.Tags), Tags = new List<string>(inv.Current.Tags), HasProgram = inv.Current.Program != null,
                        Revision = inv.Current.Revision, UpdatedAt = inv.UpdatedAt, Tested = inv.Tested,
                    };
                    _listCache[id] = c;
                }
                if (c.Entry == null) { skipped?.Add($"{id}：{c.Error}"); continue; }
                list.Add(Copy(c.Entry));
            }
            foreach (var gone in _listCache.Keys.Where(k => !seen.Contains(k)).ToList()) _listCache.Remove(gone);
            return list.OrderByDescending(e => e.UpdatedAt, StringComparer.Ordinal).ToList();
        }

        // 呼び出し側が一覧の項目を書き換えても、覚えている結果が変わらないように写しを渡す
        static GalleryEntry Copy(GalleryEntry e) => new GalleryEntry
        {
            Id = e.Id, Name = e.Name, Category = e.Category, UpdatedAt = e.UpdatedAt, TagsJa = e.TagsJa, Tags = new List<string>(e.Tags),
            HasProgram = e.HasProgram, Revision = e.Revision, Tested = e.Tested,
        };

        public bool Delete(string id)
        {
            _listCache.Remove(id ?? "");
            string path;
            try { path = PathFor(id); } catch (ArgumentException) { return false; }
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }

        /// <summary>複製用の新しい安全なIDを作る。</summary>
        public string NewCopyId(string baseId)
        {
            string stem = baseId.Length > 36 ? baseId.Substring(0, 36) : baseId;
            for (int i = 2; i < 1000; i++)
            {
                string id = $"{stem}-copy{i}";
                if (!File.Exists(Path.Combine(_dir, id + ".json"))) return id;
            }
            return stem + "-" + DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture).Substring(10);
        }

        // ---- 実験記録（テレメトリ, 仕様9.2）。発話や音声は保存しない ----
        public static JObject ResultToJson(RainExperimentResult r)
        {
            var c = r.Config;
            return new JObject
            {
                ["experimentId"] = r.ExperimentId, ["inventionId"] = r.InventionId, ["revision"] = r.Revision,
                ["mode"] = c.Mode, ["environment"] = r.Environment, ["seed"] = c.Seed, ["startTime"] = r.StartTime,
                ["durationSec"] = c.DurationSec,
                ["conditions"] = new JObject
                {
                    ["rainIntensity"] = c.RainIntensity, ["windSpeed"] = c.WindSpeed, ["windDirectionDeg"] = c.WindDirectionDeg,
                    ["obstacleCount"] = c.ObstacleCount, ["payloadKg"] = c.PayloadKg, ["targetDistance"] = c.TargetDistance,
                },
                ["metrics"] = new JObject
                {
                    ["wetnessScore"] = Math.Round(r.WetnessScore, 3), ["coverageRatio"] = Math.Round(r.CoverageRatio, 4),
                    ["dropsTotal"] = r.DropsTotal, ["baselineHits"] = r.BaselineHits, ["deviceHits"] = r.DeviceHits, ["blocked"] = r.Blocked,
                    ["headHits"] = r.HeadHits, ["upperHits"] = r.UpperHits, ["lowerHits"] = r.LowerHits,
                    ["energyWh"] = r.EnergyWh.HasValue ? new JValue(Math.Round(r.EnergyWh.Value, 4)) : JValue.CreateNull(),
                    ["driftM"] = Math.Round(r.DriftM, 3),
                    ["runtimeMin"] = Opt(r.RuntimeMinutes, 2), ["runtimeMinLow"] = Opt(r.RuntimeMinutesMin, 2), ["runtimeMinHigh"] = Opt(r.RuntimeMinutesMax, 2),
                    ["powerW"] = Opt(r.PowerW, 2), ["powerWLow"] = Opt(r.PowerWMin, 2), ["powerWHigh"] = Opt(r.PowerWMax, 2),
                    ["massKg"] = Opt(r.MassKg, 3), ["windForceN"] = Opt(r.WindForceN, 3), ["canHoldPosition"] = r.CanHoldPosition,
                },
                ["items"] = new JArray(r.Metrics.Select(x => new JObject
                {
                    ["key"] = x.Key, ["labelJa"] = x.LabelJa, ["value"] = Math.Round(x.Value, 4), ["unit"] = x.Unit,
                    ["format"] = x.Format, ["lowerIsBetter"] = x.LowerIsBetter, ["primary"] = x.Primary,
                })),
                ["passed"] = r.Passed.HasValue ? new JValue(r.Passed.Value) : JValue.CreateNull(),
                // 組み立て式の実験は条件が実験計画にしかないため、計画も残す（読み込み時に検証する）
                ["plan"] = r.IsScenarioLab && r.Plan != null ? ExperimentPlanJson.ToJObject(r.Plan) : null,
                ["criteria"] = r.Criteria ?? "",
                ["notes"] = new JArray(r.Limits.Concat(r.Assumptions).Take(10)),
                ["limitCount"] = r.Limits.Count,
                // 動作プログラムの発火の記録（S05 のタイムライン）。長くなりすぎないよう先頭 120 件
                ["timeline"] = r.Timeline.Count > 0 ? new JArray(r.Timeline.Take(120)) : null,
            };
        }

        static string Trunc(string s, int max) => string.IsNullOrEmpty(s) ? "" : s.Length > max ? s.Substring(0, max) : s;

        static string StringOrEmpty(JToken t) => t != null && t.Type == JTokenType.String ? t.Value<string>() : "";

        public const int MaxRevisionNotes = 40;

        static JObject NoteToJson(RevisionNote n) => new JObject
        {
            ["revision"] = n.Revision, ["parentRevision"] = n.ParentRevision, ["source"] = n.Source,
            ["request"] = n.Request ?? "", ["summary"] = n.Summary ?? "", ["at"] = n.At ?? "",
            ["changes"] = new JArray(n.Changes.Take(RevisionNote.MaxChanges).Select(c => new JObject { ["label"] = c.Label ?? "", ["before"] = c.Before ?? "", ["after"] = c.After ?? "" })),
        };

        /// <summary>形が合わない記録は null（その版の記録なしとして扱う）。</summary>
        static RevisionNote NoteFromJson(JObject o)
        {
            if (o["revision"]?.Type != JTokenType.Integer) return null;
            int rev = o.Value<int>("revision");
            if (rev < 2 || rev > 100000) return null;
            string src = StringOrEmpty(o["source"]);
            if (Array.IndexOf(RevisionNote.Sources, src) < 0) return null;
            int parent = o["parentRevision"]?.Type == JTokenType.Integer ? o.Value<int>("parentRevision") : rev - 1;
            var note = new RevisionNote
            {
                Revision = rev, ParentRevision = parent > 0 && parent < rev ? parent : rev - 1, Source = src,
                Request = RevisionNote.Trunc(StringOrEmpty(o["request"]), RevisionNote.MaxRequestLength),
                Summary = RevisionNote.Trunc(StringOrEmpty(o["summary"]), RevisionNote.MaxSummaryLength),
                At = Trunc(StringOrEmpty(o["at"]), 32),
            };
            if (o["changes"] is JArray changes)
                foreach (var c in changes.OfType<JObject>().Take(RevisionNote.MaxChanges))
                    note.Changes.Add(new ChangeLine
                    {
                        Label = RevisionNote.Trunc(StringOrEmpty(c["label"]), RevisionNote.MaxChangeText),
                        Before = RevisionNote.Trunc(StringOrEmpty(c["before"]), RevisionNote.MaxChangeText),
                        After = RevisionNote.Trunc(StringOrEmpty(c["after"]), RevisionNote.MaxChangeText),
                    });
            return note;
        }

        static JToken Opt(float? v, int digits) => v.HasValue ? new JValue(Math.Round(v.Value, digits)) : JValue.CreateNull();

        static RainExperimentResult ResultFromJson(JObject o)
        {
            try
            {
                var cond = (JObject)o["conditions"];
                var m = (JObject)o["metrics"];
                string env = o.Value<string>("environment") ?? LabEnvironments.RainLab;
                var cfg = new RainExperimentConfig
                {
                    Mode = o.Value<string>("mode"), Seed = o.Value<int>("seed"), DurationSec = o.Value<float>("durationSec"),
                    Environment = env,
                    RainIntensity = cond.Value<float>("rainIntensity"), WindSpeed = cond.Value<float>("windSpeed"),
                    WindDirectionDeg = cond.Value<float>("windDirectionDeg"),
                    ObstacleCount = cond.Value<int?>("obstacleCount") ?? 4, PayloadKg = cond.Value<float?>("payloadKg") ?? 5f,
                    TargetDistance = cond.Value<float?>("targetDistance") ?? 7f,
                }.Clamped();
                ExperimentPlan plan = null;
                if (env == LabEnvironments.ScenarioLab)
                {
                    var rep = ExperimentPlanJson.Parse(o["plan"]?.ToString(Formatting.None) ?? "");
                    if (!rep.IsValid) return null;
                    plan = rep.Plan;
                }
                else if (!LabEnvironments.IsValid(env)) return null;
                var res = new RainExperimentResult
                {
                    Environment = env,
                    ExperimentId = o.Value<string>("experimentId"), InventionId = o.Value<string>("inventionId"),
                    Revision = o.Value<int>("revision"), Config = cfg, StartTime = o.Value<string>("startTime"),
                    WetnessScore = m.Value<float>("wetnessScore"), CoverageRatio = m.Value<float>("coverageRatio"),
                    DropsTotal = m.Value<int>("dropsTotal"), BaselineHits = m.Value<int>("baselineHits"),
                    DeviceHits = m.Value<int>("deviceHits"), Blocked = m.Value<int>("blocked"),
                    HeadHits = m.Value<int>("headHits"), UpperHits = m.Value<int>("upperHits"), LowerHits = m.Value<int>("lowerHits"),
                    EnergyWh = m.Value<float?>("energyWh"), DriftM = m.Value<float>("driftM"),
                    RuntimeMinutes = m.Value<float?>("runtimeMin"), RuntimeMinutesMin = m.Value<float?>("runtimeMinLow"),
                    RuntimeMinutesMax = m.Value<float?>("runtimeMinHigh"), PowerW = m.Value<float?>("powerW"),
                    PowerWMin = m.Value<float?>("powerWLow"), PowerWMax = m.Value<float?>("powerWHigh"),
                    MassKg = m.Value<float?>("massKg"), WindForceN = m.Value<float?>("windForceN"),
                    CanHoldPosition = m.Value<bool?>("canHoldPosition") ?? true,
                    Passed = o.Value<bool?>("passed"), Criteria = o.Value<string>("criteria") ?? "",
                    Plan = plan, Scenario = plan?.Scenario ?? "",
                };
                if (o["items"] is JArray items)
                    foreach (var it in items.OfType<JObject>())
                    {
                        string key = it.Value<string>("key"), fmt = it.Value<string>("format") ?? "0.00";
                        if (string.IsNullOrEmpty(key) || key.Length > 40 || fmt.Length > 8 || fmt.Trim('0', '.').Length > 0) continue; // 書式は 0 と . のみ許可
                        float v = it.Value<float>("value");
                        if (float.IsNaN(v) || float.IsInfinity(v)) continue;
                        res.Metrics.Add(new ExperimentMetric
                        {
                            Key = key, LabelJa = Trunc(it.Value<string>("labelJa"), 40), Value = v, Unit = Trunc(it.Value<string>("unit"), 8),
                            Format = fmt, LowerIsBetter = it.Value<bool?>("lowerIsBetter") ?? true, Primary = it.Value<bool?>("primary") ?? true,
                        });
                    }
                // notes の先頭 limitCount 件が「制限」（改善案の判定に使う）、残りが「計算の前提」（結果の画面と公開ページに出す）
                int limits = o.Value<int?>("limitCount") ?? 0;
                if (o["notes"] is JArray notes)
                {
                    foreach (var n in notes.Take(limits)) { var t = n.Value<string>(); if (!string.IsNullOrEmpty(t)) res.Limits.Add(Trunc(t, 160)); }
                    foreach (var n in notes.Skip(limits)) { var t = n.Type == JTokenType.String ? n.Value<string>() : null; if (!string.IsNullOrEmpty(t)) res.Assumptions.Add(Trunc(t, 160)); }
                }
                if (o["timeline"] is JArray tl)
                    foreach (var n in tl.Take(120)) if (n.Type == JTokenType.String) res.Timeline.Add(Trunc(n.Value<string>(), 120));
                return res;
            }
            catch (Exception) { return null; }
        }
    }
}
