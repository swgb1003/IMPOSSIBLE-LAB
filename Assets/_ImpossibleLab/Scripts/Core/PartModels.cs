using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ImpossibleLab.Core
{
    /// <summary>
    /// 部品の品番（同じ種類の中の仕様違い。docs/DECISIONS.md D-41）。値は市販の汎用部品のよくある値に近い目安で、メーカー名・型番は使わない。
    /// 出典は Data/Catalog/Resources/ImpossibleLabCatalog/part_models.json（ArtSource/data/part_models.py が作る）。
    /// 3D は種類ごとの形を使い回し、variant で見た目を変える（部品キット KIT_&lt;type&gt;_&lt;shape&gt;-&lt;variant&gt;）。
    /// </summary>
    public sealed class PartModelInfo
    {
        public string Id = "", Type = "", LabelJa = "", Shape = "", Variant = "";
        /// <summary>DNA の scale と同じ決まりの大きさ（m）。</summary>
        public Vector3 Size;
        public float MassKg, PriceJpy;
        /// <summary>防水の等級（IPX の数字）。不明は -1。</summary>
        public int Ip = -1;
        public string[] Tags = new string[0];
        public readonly Dictionary<string, float> Specs = new Dictionary<string, float>();

        public float? Spec(string key) => Specs.TryGetValue(key, out var v) ? v : (float?)null;
    }

    public static class PartModels
    {
        public const string ResourcePath = "ImpossibleLabCatalog/part_models";
        public const int MaxIdLength = 40;

        static List<PartModelInfo> _all;
        static Dictionary<string, PartModelInfo> _byId;

        /// <summary>すべての品番（読めなければ空）。</summary>
        public static IReadOnlyList<PartModelInfo> All { get { Load(); return _all; } }

        public static PartModelInfo Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            Load();
            return _byId.TryGetValue(id, out var m) ? m : null;
        }

        public static IEnumerable<PartModelInfo> OfType(string type) => All.Where(m => m.Type == type);

        static void Load()
        {
            if (_all != null) return;
            _all = new List<PartModelInfo>();
            _byId = new Dictionary<string, PartModelInfo>();
            var ta = Resources.Load<TextAsset>(ResourcePath);
            if (ta == null) { Debug.LogError("[ImpossibleLab] 品番のデータ（" + ResourcePath + "）が見つかりません"); return; }
            foreach (var m in Parse(ta.text)) { _all.Add(m); _byId[m.Id] = m; }
        }

        public static List<PartModelInfo> Parse(string json)
        {
            var list = new List<PartModelInfo>();
            var root = JObject.Parse(json);
            foreach (var t in root["models"] as JArray ?? new JArray())
            {
                if (!(t is JObject o)) continue;
                var size = o["size"] as JArray;
                var m = new PartModelInfo
                {
                    Id = (string)o["id"] ?? "", Type = (string)o["type"] ?? "", LabelJa = (string)o["labelJa"] ?? "",
                    Shape = (string)o["shape"] ?? "", Variant = (string)o["variant"] ?? "",
                    Size = size != null && size.Count == 3 ? new Vector3((float)size[0], (float)size[1], (float)size[2]) : Vector3.one * 0.05f,
                    MassKg = (float?)o["massKg"] ?? 0f, PriceJpy = (float?)o["priceJpy"] ?? 0f, Ip = (int?)o["ip"] ?? -1,
                    Tags = (o["tags"] as JArray)?.Select(x => (string)x).ToArray() ?? new string[0],
                };
                if (o["specs"] is JObject sp) foreach (var kv in sp) m.Specs[kv.Key] = (float)kv.Value;
                if (m.Id.Length == 0 || LabCatalog.Part(m.Type) == null || LabCatalog.Shape(m.Shape) == null) continue;
                list.Add(m);
            }
            return list;
        }

        /// <summary>品番の体積（m³）。品番と大きさの違う部品の重さ・仕様を、体積の比で見積もるときに使う。</summary>
        public static float Volume(string shape, Vector3 size)
        {
            float x = Mathf.Abs(size.x), y = Mathf.Abs(size.y), z = Mathf.Abs(size.z);
            switch (shape)
            {
                case "box": return x * y * z;
                case "sphere": return Mathf.PI / 6f * x * y * z;
                case "dome": case "cone": case "ring":
                    FormMesher.Measure(shape, new Vector3(x, y, z), out float solid, out _); return solid;
                default: return Mathf.PI / 4f * x * z * y;
            }
        }

        /// <summary>部品の大きさと品番の大きさの比（体積。0.25〜4 倍に丸める）。</summary>
        public static float SizeRatio(PartSpec p, PartModelInfo m)
        {
            float vm = Volume(m.Shape, m.Size);
            if (vm <= 0f) return 1f;
            return Mathf.Clamp(Volume(p.ShapeOrDefault, p.Scale) / vm, 0.25f, 4f);
        }

        /// <summary>
        /// 品番の仕様で、機能の値の上限を決める（例：光の明るさ ≤ 品番の光束）。対象の部品に品番があるときだけ。
        /// キーは（機能, パラメータ）、値は品番の仕様のキー。
        /// </summary>
        public static readonly (string behavior, string parameter, string spec)[] SpecLimits =
        {
            ("illuminate", "lumens", "lumens"),
            ("alert", "lightLumens", "lumens"),
            ("alert", "loudnessDb", "db"),
            ("detect", "rangeM", "rangeM"),
            ("detect", "fovDeg", "fovDeg"),
            ("heat", "powerW", "powerW"),
            ("cool", "powerW", "powerW"),
        };

        /// <summary>機能の値を、対象の部品の品番の仕様までに丸める。丸めたら警告を残す。</summary>
        public static void ApplySpecLimits(InventionDna dna, ValidationReport report)
        {
            foreach (var b in dna.Behaviors)
            {
                if (string.IsNullOrEmpty(b.TargetPart)) continue;
                var part = dna.FindPart(b.TargetPart);
                var model = Find(part?.Model);
                if (model == null) continue;
                foreach (var (behavior, parameter, spec) in SpecLimits)
                {
                    if (b.Type != behavior || !b.Parameters.TryGetValue(parameter, out float v)) continue;
                    var cap = model.Spec(spec);
                    // 仕様が 0（その働きを持たない品番。例：距離を測らない温度センサー）は丸めない
                    if (!cap.HasValue || cap.Value <= 0f || v <= cap.Value + 1e-4f) continue;
                    var info = LabCatalog.Behavior(behavior)?.Parameters.FirstOrDefault(x => x.Key == parameter);
                    float floor = info?.Min ?? 0f;
                    float to = Mathf.Max(floor, cap.Value);
                    b.Parameters[parameter] = to;
                    report?.Warnings.Add($"{LabCatalog.Behavior(behavior)?.LabelJa ?? behavior}の{info?.LabelJa ?? parameter}を、品番「{model.LabelJa}」の仕様 {to:0.##} までにしました");
                }
            }
        }

        /// <summary>プロキシ用の書き出し（AI に渡す候補の選び出しと、品番の確認に使う）。</summary>
        public static JArray ToProxyJson() => new JArray(All.Select(m =>
        {
            var o = new JObject
            {
                ["id"] = m.Id, ["type"] = m.Type, ["labelJa"] = m.LabelJa, ["shape"] = m.Shape,
                ["size"] = new JArray(m.Size.x, m.Size.y, m.Size.z), ["massKg"] = m.MassKg, ["priceJpy"] = m.PriceJpy, ["ip"] = m.Ip,
                ["tags"] = new JArray(m.Tags),
            };
            var sp = new JObject();
            foreach (var kv in m.Specs) sp[kv.Key] = kv.Value;
            o["specs"] = sp;
            return o;
        }));
    }
}
