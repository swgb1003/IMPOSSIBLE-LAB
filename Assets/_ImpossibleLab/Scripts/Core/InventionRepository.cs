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
        public bool Tested => Experiments.Count > 0;
    }

    public sealed class GalleryEntry
    {
        public string Id, Name, Category, UpdatedAt;
        /// <summary>分類タグの日本語（例：防護・安全）。</summary>
        public string TagsJa = "";
        public int Revision;
        public bool Tested;
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
            };
            string path = PathFor(inv.Current.Id);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, root.ToString(Formatting.Indented));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public SavedInvention Load(string id, out string error)
        {
            error = null;
            string path;
            try { path = PathFor(id); } catch (ArgumentException e) { error = e.Message; return null; }
            if (!File.Exists(path)) { error = "保存データが見つかりません"; return null; }
            JObject root;
            try { root = JObject.Parse(File.ReadAllText(path)); }
            catch (Exception e) when (e is JsonException || e is IOException) { error = "保存データを読み込めません"; return null; }
            if (root.Value<int?>("format") != FileFormatVersion) { error = "保存データの形式が非対応です"; return null; }
            var cur = DnaJson.Parse(root["current"]?.ToString() ?? "");
            if (!cur.IsValid) { error = "保存データの検証に失敗: " + cur.Summary; return null; }
            var inv = new SavedInvention { Current = cur.Dna, UpdatedAt = root.Value<string>("updatedAt") ?? "" };
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

        public List<GalleryEntry> List()
        {
            var list = new List<GalleryEntry>();
            foreach (var file in Directory.GetFiles(_dir, "*.json"))
            {
                var id = Path.GetFileNameWithoutExtension(file);
                if (!DnaJson.IsSafeId(id)) continue;
                var inv = Load(id, out _);
                if (inv == null) continue;
                list.Add(new GalleryEntry
                {
                    Id = inv.Current.Id, Name = inv.Current.Name, Category = inv.Current.Category, TagsJa = LabCatalog.TagsJa(inv.Current.Tags),
                    Revision = inv.Current.Revision, UpdatedAt = inv.UpdatedAt, Tested = inv.Tested,
                });
            }
            return list.OrderByDescending(e => e.UpdatedAt, StringComparer.Ordinal).ToList();
        }

        public bool Delete(string id)
        {
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
                ["criteria"] = r.Criteria ?? "",
                ["notes"] = new JArray(r.Limits.Concat(r.Assumptions).Take(10)),
                ["limitCount"] = r.Limits.Count,
            };
        }

        static string Trunc(string s, int max) => string.IsNullOrEmpty(s) ? "" : s.Length > max ? s.Substring(0, max) : s;

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
                if (!LabEnvironments.IsValid(env)) return null;
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
                // notes の先頭 limitCount 件が「制限」（改善案の判定に使う）。前提は読み込み時に表示しない
                int limits = o.Value<int?>("limitCount") ?? 0;
                if (o["notes"] is JArray notes)
                    foreach (var n in notes.Take(limits)) { var t = n.Value<string>(); if (!string.IsNullOrEmpty(t)) res.Limits.Add(Trunc(t, 160)); }
                return res;
            }
            catch (Exception) { return null; }
        }
    }
}
