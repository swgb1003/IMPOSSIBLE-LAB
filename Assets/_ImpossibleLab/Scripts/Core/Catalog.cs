using System.Collections.Generic;
using System.Linq;

namespace ImpossibleLab.Core
{
    /// <summary>部品タイプの許可リスト（仕様7.1）。</summary>
    public sealed class PartTypeInfo
    {
        public string Type;
        public string LabelJa;
        public float MinScale;
        public float MaxScale;
        /// <summary>形の指定がないときの形（DNA v1 の見た目と同じになるよう決めてある）。</summary>
        public string DefaultShape = "cylinder";
        public string DefaultMaterial = "plastic";
    }

    /// <summary>名前つきの選択肢（タグ・取り付け位置・形）。</summary>
    public sealed class NamedOption
    {
        public string Id, LabelJa;
        public NamedOption(string id, string label) { Id = id; LabelJa = label; }
    }

    /// <summary>素材（REALITY の質量計算に使う密度）。値は代表値で、出典は要確認（docs/GENERAL_INVENTION_DESIGN.md 5.5）。</summary>
    public sealed class MaterialInfo
    {
        public string Id, LabelJa;
        public float DensityKgM3;
    }

    public sealed class ParameterInfo
    {
        public string Key;
        public string LabelJa;
        public string Unit;
        public float Min;
        public float Max;
        public float Default;
        /// <summary>REALITY MODE で適用する上限（null なら制限なし）。</summary>
        public float? RealityMax;
        public string RealityReason;
        public string Format = "0.00";
    }

    public sealed class BehaviorInfo
    {
        public string Type;
        public string LabelJa;
        public bool RequiresTargetPart;
        public string[] AllowedTargetTypes = new string[0];
        public ParameterInfo[] Parameters;
        public ParameterInfo Param(string key) => Parameters.FirstOrDefault(p => p.Key == key);
    }

    public sealed class CategoryInfo
    {
        public string Id;
        public string LabelJa;
        /// <summary>このビルドで実験場が用意されているか。</summary>
        public bool ExperimentAvailable;
    }

    /// <summary>
    /// 外観プリセット（色の組み合わせ）。性能には影響しない。3D外観の生成AIの代わりに、AIまたは利用者が
    /// この一覧から選ぶ（任意の形・テクスチャは生成しない）。色は sRGB の 0xRRGGBB。
    /// </summary>
    public sealed class AppearancePreset
    {
        public string Id, LabelJa;
        public int BodyRgb, AccentRgb;
    }

    /// <summary>LLMにもこの一覧を渡し、出力はこの範囲に制限する。</summary>
    public static class LabCatalog
    {
        public const float PositionLimitXZ = 2f;
        public const float PositionMinY = -1f; // 発明品ローカル座標（本体中心が原点）
        public const float PositionMaxY = 3f;
        public const int MaxBehaviors = 8;
        public const int MaxIdLength = 48;
        public const int MaxTags = 3, MaxFunctionNeeds = 8, MaxNeedLength = 60, MaxNoteLength = 120;
        public const int MaxNameLength = 40;
        public const int MaxPurposeLength = 200;

        public static readonly PartTypeInfo[] PartTypes =
        {
            new PartTypeInfo { Type = "body", LabelJa = "本体", MinScale = 0.03f, MaxScale = 1.0f },
            new PartTypeInfo { Type = "wheel", LabelJa = "車輪", MinScale = 0.03f, MaxScale = 0.6f, DefaultMaterial = "rubber" },
            new PartTypeInfo { Type = "container", LabelJa = "格納部", MinScale = 0.05f, MaxScale = 1.0f, DefaultShape = "box" },
            new PartTypeInfo { Type = "sensor", LabelJa = "センサー", MinScale = 0.02f, MaxScale = 0.3f, DefaultShape = "sphere" },
            new PartTypeInfo { Type = "arm", LabelJa = "アーム", MinScale = 0.02f, MaxScale = 1.0f, DefaultShape = "capsule", DefaultMaterial = "aluminum" },
            new PartTypeInfo { Type = "canopy", LabelJa = "キャノピー（覆い）", MinScale = 0.02f, MaxScale = 3.0f, DefaultMaterial = "fabric" },
            new PartTypeInfo { Type = "thruster", LabelJa = "推進ユニット", MinScale = 0.03f, MaxScale = 0.5f, DefaultMaterial = "aluminum" },
            new PartTypeInfo { Type = "stabilizer", LabelJa = "姿勢安定ユニット", MinScale = 0.02f, MaxScale = 0.5f, DefaultShape = "box" },
            new PartTypeInfo { Type = "connector", LabelJa = "接続部", MinScale = 0.01f, MaxScale = 1.0f, DefaultMaterial = "aluminum" },
            // DNA v2 で追加（docs/GENERAL_INVENTION_DESIGN.md 3章）
            new PartTypeInfo { Type = "frame", LabelJa = "骨組み", MinScale = 0.01f, MaxScale = 1.5f, DefaultShape = "box", DefaultMaterial = "aluminum" },
            new PartTypeInfo { Type = "panel", LabelJa = "板", MinScale = 0.005f, MaxScale = 1.5f, DefaultShape = "box" },
            new PartTypeInfo { Type = "light", LabelJa = "発光部", MinScale = 0.01f, MaxScale = 0.3f },
            new PartTypeInfo { Type = "heater", LabelJa = "加熱部", MinScale = 0.02f, MaxScale = 0.5f, DefaultShape = "box", DefaultMaterial = "aluminum" },
            new PartTypeInfo { Type = "cooler", LabelJa = "冷却部", MinScale = 0.02f, MaxScale = 0.5f, DefaultShape = "box", DefaultMaterial = "aluminum" },
            new PartTypeInfo { Type = "motor", LabelJa = "駆動部（関節）", MinScale = 0.02f, MaxScale = 0.3f, DefaultMaterial = "steel" },
            new PartTypeInfo { Type = "gripper", LabelJa = "つかみ具", MinScale = 0.02f, MaxScale = 0.4f, DefaultShape = "box" },
            new PartTypeInfo { Type = "cushion", LabelJa = "緩衝材", MinScale = 0.01f, MaxScale = 1.0f, DefaultShape = "box", DefaultMaterial = "foam" },
            new PartTypeInfo { Type = "battery", LabelJa = "電池", MinScale = 0.02f, MaxScale = 0.4f, DefaultShape = "box" },
            new PartTypeInfo { Type = "solar", LabelJa = "太陽電池", MinScale = 0.005f, MaxScale = 1.0f, DefaultShape = "box", DefaultMaterial = "glass" },
            new PartTypeInfo { Type = "speaker", LabelJa = "音を出す部品", MinScale = 0.02f, MaxScale = 0.3f },
            new PartTypeInfo { Type = "strap", LabelJa = "ベルト・取り付け具", MinScale = 0.005f, MaxScale = 1.0f, DefaultShape = "box", DefaultMaterial = "fabric" },
        };

        /// <summary>部品の形。Unity の標準プリミティブのみ（円錐・輪は専用メッシュが必要なため初版では扱わない）。</summary>
        public static readonly NamedOption[] Shapes =
        {
            new NamedOption("box", "箱"), new NamedOption("cylinder", "円柱"), new NamedOption("sphere", "球"), new NamedOption("capsule", "カプセル"),
        };

        public static readonly MaterialInfo[] Materials =
        {
            new MaterialInfo { Id = "plastic", LabelJa = "樹脂", DensityKgM3 = 1050f },
            new MaterialInfo { Id = "aluminum", LabelJa = "アルミ", DensityKgM3 = 2700f },
            new MaterialInfo { Id = "steel", LabelJa = "鉄", DensityKgM3 = 7850f },
            new MaterialInfo { Id = "fabric", LabelJa = "布", DensityKgM3 = 400f },
            new MaterialInfo { Id = "foam", LabelJa = "発泡材", DensityKgM3 = 30f },
            new MaterialInfo { Id = "rubber", LabelJa = "ゴム", DensityKgM3 = 1100f },
            new MaterialInfo { Id = "glass", LabelJa = "ガラス", DensityKgM3 = 2500f },
        };

        /// <summary>分類用のタグ（実験場とは結び付けない）。最初の3つは DNA v1 のカテゴリと同じ。</summary>
        public static readonly NamedOption[] Tags =
        {
            new NamedOption("protection", "防護"), new NamedOption("carrier", "運搬"), new NamedOption("mobility", "移動"),
            new NamedOption("lighting", "照明"), new NamedOption("thermal", "温度"), new NamedOption("safety", "安全"),
            new NamedOption("assist", "身体補助"), new NamedOption("sensing", "検知・通知"), new NamedOption("daily", "生活"),
            new NamedOption("outdoor", "屋外"),
        };

        /// <summary>取り付け位置（人型モデルのどこに付くか、または自走・飛行・設置）。</summary>
        public static readonly NamedOption[] Mounts =
        {
            new NamedOption("head", "頭"), new NamedOption("back", "背中"), new NamedOption("waist", "腰"),
            new NamedOption("wrist", "手首"), new NamedOption("hand", "手"), new NamedOption("foot", "足"),
            new NamedOption("ground", "床（自走）"), new NamedOption("air", "空中"), new NamedOption("fixed", "設置"),
        };

        /// <summary>タグを日本語で「防護・安全」のようにつなぐ。</summary>
        public static string TagsJa(IEnumerable<string> tags) => string.Join("・", tags.Select(t => Tag(t)?.LabelJa ?? t));

        public static NamedOption Shape(string id) => Shapes.FirstOrDefault(x => x.Id == id);
        public static MaterialInfo Material(string id) => Materials.FirstOrDefault(x => x.Id == id);
        public static NamedOption Tag(string id) => Tags.FirstOrDefault(x => x.Id == id);
        public static NamedOption Mount(string id) => Mounts.FirstOrDefault(x => x.Id == id);
        public static readonly string[] FunctionStatuses = { "simulated", "simplified", "unverifiable" };
        public static string FunctionStatusJa(string s) => s == "simulated" ? "検証する" : s == "simplified" ? "単純化して検証" : "検証できない";

        public static readonly BehaviorInfo[] Behaviors =
        {
            new BehaviorInfo { Type = "follow_user", LabelJa = "ユーザー追従", Parameters = new[] {
                P("speed", "追従速度", "m/s", 0.1f, 3f, 1f),
                P("followDistance", "追従距離", "m", 0.5f, 3f, 1.2f) } },
            new BehaviorInfo { Type = "avoid_obstacle", LabelJa = "障害物回避", Parameters = new[] {
                P("detectionRange", "検知距離", "m", 0.2f, 5f, 1.5f) } },
            new BehaviorInfo { Type = "carry_load", LabelJa = "積載", Parameters = new[] {
                P("capacity", "積載量", "kg", 0f, 30f, 5f, "0.0") } },
            new BehaviorInfo { Type = "hover", LabelJa = "浮遊", Parameters = new[] {
                P("lift", "揚力", "", 0f, 1f, 0.5f),
                P("maxHeight", "最大高度", "m", 0.2f, 3f, 2f) } },
            new BehaviorInfo { Type = "move_forward", LabelJa = "推進", Parameters = new[] {
                P("thrust", "推力", "", 0f, 1f, 0.5f),
                P("speedLimit", "速度上限", "m/s", 0.1f, 3f, 1.5f) } },
            new BehaviorInfo { Type = "stabilize", LabelJa = "姿勢安定", RequiresTargetPart = true,
                AllowedTargetTypes = new[] { "stabilizer", "body" }, Parameters = new[] {
                P("stabilizationStrength", "安定化の強さ", "", 0f, 1f, 0.3f) } },
            new BehaviorInfo { Type = "follow_head", LabelJa = "頭上追従", RequiresTargetPart = true,
                AllowedTargetTypes = new[] { "body", "canopy" }, Parameters = new[] {
                P("heightOffset", "頭上からの高さ", "m", 0.15f, 0.8f, 0.35f) } },
            new BehaviorInfo { Type = "rain_shield", LabelJa = "雨よけ判定", RequiresTargetPart = true,
                AllowedTargetTypes = new[] { "canopy" }, Parameters = new[] {
                P("radius", "防護半径", "m", 0.2f, 1.5f, 0.6f, "0.00", 0.9f, "大型化で重量と風荷重が増すため（仮定）"),
                P("effectiveness", "遮水効率", "", 0.1f, 1f, 0.9f, "0.00", 0.95f, "縁からの跳ね返り・漏れを仮定"),
                P("skirtLength", "側面カバーの長さ", "m", 0f, 0.6f, 0f) } },
        };

        public static readonly CategoryInfo[] Categories =
        {
            new CategoryInfo { Id = "protection", LabelJa = "防護", ExperimentAvailable = true },
            new CategoryInfo { Id = "carrier", LabelJa = "運搬", ExperimentAvailable = true },
            new CategoryInfo { Id = "mobility", LabelJa = "移動", ExperimentAvailable = true },
        };

        public const string DefaultAppearance = "lab_white";
        public static readonly AppearancePreset[] Appearances =
        {
            new AppearancePreset { Id = "lab_white", LabelJa = "研究所ホワイト", BodyRgb = 0xE8EDF1, AccentRgb = 0x0EA5E9 },
            new AppearancePreset { Id = "safety_orange", LabelJa = "セーフティオレンジ", BodyRgb = 0xE8862A, AccentRgb = 0x1E293B },
            new AppearancePreset { Id = "night_navy", LabelJa = "ナイトネイビー", BodyRgb = 0x2B3A55, AccentRgb = 0x38BDF8 },
            new AppearancePreset { Id = "field_green", LabelJa = "フィールドグリーン", BodyRgb = 0x4E7A5A, AccentRgb = 0xF8FAFC },
            new AppearancePreset { Id = "graphite", LabelJa = "グラファイト", BodyRgb = 0x4A4F57, AccentRgb = 0xF59E0B },
        };
        public static AppearancePreset Appearance(string id) => Appearances.FirstOrDefault(a => a.Id == id);

        static ParameterInfo P(string key, string label, string unit, float min, float max, float def,
            string format = "0.00", float? realityMax = null, string realityReason = null) =>
            new ParameterInfo { Key = key, LabelJa = label, Unit = unit, Min = min, Max = max, Default = def,
                Format = format, RealityMax = realityMax, RealityReason = realityReason };

        public static PartTypeInfo Part(string type) => PartTypes.FirstOrDefault(p => p.Type == type);
        public static BehaviorInfo Behavior(string type) => Behaviors.FirstOrDefault(b => b.Type == type);
        public static CategoryInfo Category(string id) => Categories.FirstOrDefault(c => c.Id == id);

        public static IEnumerable<string> PartTypeIds => PartTypes.Select(p => p.Type);
        public static IEnumerable<string> BehaviorTypeIds => Behaviors.Select(b => b.Type);

        /// <summary>AI設計プロキシ用に書き出すカタログ（proxy/catalog.json）。部品・機能の唯一の出典はこのクラス。</summary>
        public static string ToProxyJson()
        {
            var parts = new Newtonsoft.Json.Linq.JArray(PartTypes.Select(p => new Newtonsoft.Json.Linq.JObject
            {
                ["type"] = p.Type, ["labelJa"] = p.LabelJa, ["minScale"] = p.MinScale, ["maxScale"] = p.MaxScale,
            }));
            var behaviors = new Newtonsoft.Json.Linq.JArray(Behaviors.Select(b => new Newtonsoft.Json.Linq.JObject
            {
                ["type"] = b.Type, ["labelJa"] = b.LabelJa, ["requiresTargetPart"] = b.RequiresTargetPart,
                ["allowedTargetTypes"] = new Newtonsoft.Json.Linq.JArray(b.AllowedTargetTypes),
                ["parameters"] = new Newtonsoft.Json.Linq.JArray(b.Parameters.Select(p => new Newtonsoft.Json.Linq.JObject
                {
                    ["key"] = p.Key, ["labelJa"] = p.LabelJa, ["unit"] = p.Unit ?? "", ["min"] = p.Min, ["max"] = p.Max, ["default"] = p.Default,
                })),
            }));
            var cats = new Newtonsoft.Json.Linq.JArray(Categories.Select(c => new Newtonsoft.Json.Linq.JObject
            {
                ["id"] = c.Id, ["labelJa"] = c.LabelJa, ["experimentAvailable"] = c.ExperimentAvailable,
            }));
            var root = new Newtonsoft.Json.Linq.JObject
            {
                ["generatedBy"] = "ImpossibleLab.Core.LabCatalog.ToProxyJson (Unity で再生成すること。手で編集しない)",
                ["partTypes"] = parts, ["behaviors"] = behaviors, ["categories"] = cats,
                ["appearances"] = new Newtonsoft.Json.Linq.JArray(Appearances.Select(a => new Newtonsoft.Json.Linq.JObject { ["id"] = a.Id, ["labelJa"] = a.LabelJa })),
                ["limits"] = new Newtonsoft.Json.Linq.JObject
                {
                    ["maxParts"] = InventionConstraints.HardMaxParts, ["maxBehaviors"] = MaxBehaviors,
                    ["positionLimitXZ"] = PositionLimitXZ, ["positionMinY"] = PositionMinY, ["positionMaxY"] = PositionMaxY,
                    ["maxNameLength"] = MaxNameLength, ["maxPurposeLength"] = MaxPurposeLength, ["maxRequestLength"] = RequestRules.MaxRequestLength,
                },
            };
            return root.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        public static string FormatValue(ParameterInfo info, float value) =>
            value.ToString(info.Format, System.Globalization.CultureInfo.InvariantCulture) + (string.IsNullOrEmpty(info.Unit) ? "" : " " + info.Unit);
    }
}
