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
        /// <summary>まとまり（LabCatalog.PartGroups）。AI への部品の説明と画面の並びに使う。</summary>
        public string Group = "structure";
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
        /// <summary>AI設計プロキシのカタログ（proxy/catalog.json）に載せるか。実験場がまだないブロックは載せない（5週目に AI 側を対応）。</summary>
        public bool ExposeToAi = true;
        /// <summary>簡易モデルの説明（式の要約。詳しくは docs/blocks.md）。</summary>
        public string ModelJa = "";
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
        /// <summary>説明の文の長さ（製品の概要・部品の役割・しくみ）。超えた分は切り詰める（設計は拒否しない）。</summary>
        public const int MaxOverviewLength = 160, MaxRoleLength = 50, MaxHowLength = 100;

        public static readonly PartTypeInfo[] PartTypes =
        {
            new PartTypeInfo { Type = "body", LabelJa = "本体", MinScale = 0.03f, MaxScale = 1.0f },
            new PartTypeInfo { Type = "wheel", LabelJa = "車輪", MinScale = 0.015f, MaxScale = 0.6f, DefaultMaterial = "rubber" },
            new PartTypeInfo { Type = "container", LabelJa = "格納部", MinScale = 0.05f, MaxScale = 1.0f, DefaultShape = "box" },
            new PartTypeInfo { Type = "sensor", LabelJa = "センサー", MinScale = 0.001f, MaxScale = 0.3f, DefaultShape = "sphere" },
            new PartTypeInfo { Type = "arm", LabelJa = "アーム", MinScale = 0.02f, MaxScale = 1.0f, DefaultShape = "capsule", DefaultMaterial = "aluminum" },
            new PartTypeInfo { Type = "canopy", LabelJa = "キャノピー（覆い）", MinScale = 0.02f, MaxScale = 3.0f, DefaultMaterial = "fabric" },
            new PartTypeInfo { Type = "thruster", LabelJa = "推進ユニット", MinScale = 0.03f, MaxScale = 0.5f, DefaultMaterial = "aluminum" },
            new PartTypeInfo { Type = "stabilizer", LabelJa = "姿勢安定ユニット", MinScale = 0.02f, MaxScale = 0.5f, DefaultShape = "box" },
            new PartTypeInfo { Type = "connector", LabelJa = "接続部", MinScale = 0.01f, MaxScale = 1.0f, DefaultMaterial = "aluminum" },
            // DNA v2 で追加（docs/GENERAL_INVENTION_DESIGN.md 3章）。薄い部品・背の高い骨組みの範囲は AI 評価1回目（2026-11-09）の結果で広げた
            new PartTypeInfo { Type = "frame", LabelJa = "骨組み", MinScale = 0.005f, MaxScale = 2.2f, DefaultShape = "box", DefaultMaterial = "aluminum" },
            new PartTypeInfo { Type = "panel", LabelJa = "板", MinScale = 0.003f, MaxScale = 2.0f, DefaultShape = "box" },
            new PartTypeInfo { Type = "light", LabelJa = "発光部", MinScale = 0.002f, MaxScale = 1.0f },
            new PartTypeInfo { Type = "heater", LabelJa = "加熱部", MinScale = 0.001f, MaxScale = 0.5f, DefaultShape = "box", DefaultMaterial = "aluminum" },
            new PartTypeInfo { Type = "cooler", LabelJa = "冷却部", MinScale = 0.003f, MaxScale = 0.5f, DefaultShape = "box", DefaultMaterial = "aluminum" },
            new PartTypeInfo { Type = "motor", LabelJa = "駆動部（モーター）", MinScale = 0.008f, MaxScale = 0.8f, DefaultMaterial = "steel" },
            new PartTypeInfo { Type = "gripper", LabelJa = "つかみ具", MinScale = 0.02f, MaxScale = 0.4f, DefaultShape = "box" },
            new PartTypeInfo { Type = "cushion", LabelJa = "緩衝材", MinScale = 0.005f, MaxScale = 1.0f, DefaultShape = "box", DefaultMaterial = "foam" },
            new PartTypeInfo { Type = "battery", LabelJa = "電池", MinScale = 0.002f, MaxScale = 0.5f, DefaultShape = "box" },
            new PartTypeInfo { Type = "solar", LabelJa = "太陽電池", MinScale = 0.002f, MaxScale = 1.0f, DefaultShape = "box", DefaultMaterial = "glass" },
            new PartTypeInfo { Type = "speaker", LabelJa = "音を出す部品", MinScale = 0.005f, MaxScale = 0.3f },
            new PartTypeInfo { Type = "strap", LabelJa = "ベルト・取り付け具", MinScale = 0.005f, MaxScale = 1.0f, DefaultShape = "box", DefaultMaterial = "fabric" },

            // 部品の拡充（2026-11-27、docs/DECISIONS.md D-41）。品番は Data/Catalog/Resources/ImpossibleLabCatalog/part_models.json（PartModels）
            // 動き
            new PartTypeInfo { Type = "hinge", LabelJa = "ちょうつがい", MinScale = 0.002f, MaxScale = 0.6f, DefaultShape = "box", DefaultMaterial = "steel", Group = "motion" },
            new PartTypeInfo { Type = "slide_rail", LabelJa = "スライドレール", MinScale = 0.005f, MaxScale = 1.5f, DefaultShape = "box", DefaultMaterial = "steel", Group = "motion" },
            new PartTypeInfo { Type = "telescopic", LabelJa = "伸縮ポール", MinScale = 0.01f, MaxScale = 2.0f, DefaultShape = "cylinder", DefaultMaterial = "aluminum", Group = "motion" },
            new PartTypeInfo { Type = "spring", LabelJa = "ばね", MinScale = 0.005f, MaxScale = 0.5f, DefaultShape = "cylinder", DefaultMaterial = "steel", Group = "motion" },
            new PartTypeInfo { Type = "gear", LabelJa = "歯車・ベルト", MinScale = 0.005f, MaxScale = 0.5f, DefaultShape = "cylinder", DefaultMaterial = "steel", Group = "motion" },
            new PartTypeInfo { Type = "latch", LabelJa = "ロック・留め金", MinScale = 0.005f, MaxScale = 0.2f, DefaultShape = "box", DefaultMaterial = "steel", Group = "motion" },
            new PartTypeInfo { Type = "crawler", LabelJa = "クローラー", MinScale = 0.03f, MaxScale = 1.2f, DefaultShape = "box", DefaultMaterial = "rubber", Group = "motion" },
            new PartTypeInfo { Type = "caster", LabelJa = "キャスター", MinScale = 0.015f, MaxScale = 0.2f, DefaultShape = "cylinder", DefaultMaterial = "rubber", Group = "motion" },
            new PartTypeInfo { Type = "leg", LabelJa = "脚", MinScale = 0.01f, MaxScale = 1.2f, DefaultShape = "cylinder", DefaultMaterial = "aluminum", Group = "motion" },
            // 流体・風
            new PartTypeInfo { Type = "pump", LabelJa = "ポンプ", MinScale = 0.01f, MaxScale = 0.4f, DefaultShape = "cylinder", Group = "fluid" },
            new PartTypeInfo { Type = "tank", LabelJa = "タンク", MinScale = 0.02f, MaxScale = 1.0f, DefaultShape = "cylinder", Group = "fluid" },
            new PartTypeInfo { Type = "nozzle", LabelJa = "ノズル", MinScale = 0.005f, MaxScale = 0.2f, DefaultShape = "cone", Group = "fluid" },
            new PartTypeInfo { Type = "valve", LabelJa = "弁", MinScale = 0.008f, MaxScale = 0.2f, DefaultShape = "box", DefaultMaterial = "steel", Group = "fluid" },
            new PartTypeInfo { Type = "hose", LabelJa = "ホース・チューブ", MinScale = 0.002f, MaxScale = 2.0f, DefaultShape = "cylinder", DefaultMaterial = "rubber", Group = "fluid" },
            new PartTypeInfo { Type = "fan", LabelJa = "ファン", MinScale = 0.005f, MaxScale = 0.5f, DefaultShape = "box", Group = "fluid" },
            // 光・音・知らせる
            new PartTypeInfo { Type = "display", LabelJa = "画面", MinScale = 0.002f, MaxScale = 0.6f, DefaultShape = "box", Group = "output" },
            new PartTypeInfo { Type = "vibrator", LabelJa = "振動モーター", MinScale = 0.002f, MaxScale = 0.06f, DefaultShape = "cylinder", DefaultMaterial = "steel", Group = "output" },
            new PartTypeInfo { Type = "reflector", LabelJa = "反射材", MinScale = 0.001f, MaxScale = 1.0f, DefaultShape = "box", Group = "output" },
            // 電気
            new PartTypeInfo { Type = "switch", LabelJa = "スイッチ・ボタン", MinScale = 0.003f, MaxScale = 0.1f, DefaultShape = "box", Group = "power" },
            new PartTypeInfo { Type = "radio", LabelJa = "無線モジュール", MinScale = 0.002f, MaxScale = 0.15f, DefaultShape = "box", Group = "power" },
            new PartTypeInfo { Type = "charger", LabelJa = "ワイヤレス充電", MinScale = 0.002f, MaxScale = 0.3f, DefaultShape = "cylinder", Group = "power" },
            new PartTypeInfo { Type = "capacitor", LabelJa = "コンデンサ（キャパシタ）", MinScale = 0.005f, MaxScale = 0.15f, DefaultShape = "cylinder", DefaultMaterial = "aluminum", Group = "power" },
            // 熱
            new PartTypeInfo { Type = "heat_sink", LabelJa = "放熱板", MinScale = 0.005f, MaxScale = 0.4f, DefaultShape = "box", DefaultMaterial = "aluminum", Group = "thermal" },
            new PartTypeInfo { Type = "insulation", LabelJa = "断熱材", MinScale = 0.002f, MaxScale = 1.5f, DefaultShape = "box", DefaultMaterial = "foam", Group = "thermal" },
            // 取り付け
            new PartTypeInfo { Type = "clip", LabelJa = "クリップ", MinScale = 0.005f, MaxScale = 0.15f, DefaultShape = "box", Group = "mounting" },
            new PartTypeInfo { Type = "suction_cup", LabelJa = "吸盤", MinScale = 0.003f, MaxScale = 0.15f, DefaultShape = "cylinder", DefaultMaterial = "rubber", Group = "mounting" },
            new PartTypeInfo { Type = "magnet", LabelJa = "磁石", MinScale = 0.003f, MaxScale = 0.1f, DefaultShape = "cylinder", DefaultMaterial = "steel", Group = "mounting" },
            new PartTypeInfo { Type = "hook", LabelJa = "フック", MinScale = 0.005f, MaxScale = 0.2f, DefaultShape = "box", DefaultMaterial = "steel", Group = "mounting" },
            new PartTypeInfo { Type = "velcro", LabelJa = "面ファスナー", MinScale = 0.003f, MaxScale = 0.6f, DefaultShape = "box", DefaultMaterial = "fabric", Group = "mounting" },
            new PartTypeInfo { Type = "handle", LabelJa = "取っ手", MinScale = 0.01f, MaxScale = 0.6f, DefaultShape = "box", Group = "mounting" },
            // 守る
            new PartTypeInfo { Type = "gasket", LabelJa = "防水パッキン", MinScale = 0.001f, MaxScale = 1.0f, DefaultShape = "ring", DefaultMaterial = "rubber", Group = "protection" },
            new PartTypeInfo { Type = "mesh_cover", LabelJa = "メッシュの覆い", MinScale = 0.001f, MaxScale = 1.0f, DefaultShape = "box", DefaultMaterial = "steel", Group = "protection" },
            new PartTypeInfo { Type = "window", LabelJa = "透明な窓", MinScale = 0.001f, MaxScale = 1.0f, DefaultShape = "box", DefaultMaterial = "glass", Group = "protection" },
        };

        /// <summary>部品のまとまり（AI への説明と画面の並び）。</summary>
        public static readonly NamedOption[] PartGroups =
        {
            new NamedOption("structure", "本体・骨組み"), new NamedOption("motion", "動き"), new NamedOption("power", "電気"),
            new NamedOption("sensing", "センサー"), new NamedOption("output", "光・音・知らせる"), new NamedOption("thermal", "熱"),
            new NamedOption("fluid", "流体・風"), new NamedOption("mounting", "取り付け"), new NamedOption("protection", "守る"),
        };

        /// <summary>前からある部品のまとまり（表に Group を書かずに済むよう、ここでまとめて決める）。</summary>
        static readonly Dictionary<string, string> LegacyGroups = new Dictionary<string, string>
        {
            ["wheel"] = "motion", ["arm"] = "motion", ["thruster"] = "motion", ["motor"] = "motion", ["gripper"] = "motion", ["stabilizer"] = "motion",
            ["sensor"] = "sensing", ["light"] = "output", ["speaker"] = "output", ["heater"] = "thermal", ["cooler"] = "thermal",
            ["battery"] = "power", ["solar"] = "power", ["strap"] = "mounting", ["cushion"] = "protection",
        };

        static LabCatalog()
        {
            foreach (var p in PartTypes) if (LegacyGroups.TryGetValue(p.Type, out var g)) p.Group = g;
        }

        /// <summary>
        /// 部品の形。箱・円柱・球・カプセルは Unity の標準の形（と部品キット）、ドーム・円すい・輪は組み立てるときに作る形（Core/FormMesher）。
        /// 大きさ：ドームは [直径, 盛り上がりの高さ, 直径]（下が開いた曲面の板）、円すいは [下の直径, 高さ, 下の直径]（上は下の 30%）、輪は [外の直径, 輪の太さ, 外の直径]（寝かせた輪）。
        /// </summary>
        public static readonly NamedOption[] Shapes =
        {
            new NamedOption("box", "箱"), new NamedOption("cylinder", "円柱"), new NamedOption("sphere", "球"), new NamedOption("capsule", "カプセル"),
            new NamedOption("dome", "ドーム"), new NamedOption("cone", "円すい"), new NamedOption("ring", "輪"),
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
        /// <summary>demonstrated：動きを再現して見せるが、数値では検証しない（動作プログラム、仕様 28.7）。</summary>
        public static readonly string[] FunctionStatuses = { "simulated", "simplified", "demonstrated", "unverifiable" };
        public static string FunctionStatusJa(string s) => s == "simulated" ? "検証する" : s == "simplified" ? "単純化して検証" : s == "demonstrated" ? "動きで見せる" : "検証できない";

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

            // ---- 機能ブロック 第1弾（3週目）。計算式は Core/Blocks/BlockModels.cs・docs/blocks.md。実験場への組み込みは4週目 ----
            new BehaviorInfo { Type = "illuminate", LabelJa = "照明", RequiresTargetPart = true,
                AllowedTargetTypes = new[] { "light" }, ModelJa = "光度 I = lm ÷ (2π(1−cos(θ/2)))、床の照度 E = I cosα ÷ d²", Parameters = new[] {
                P("lumens", "明るさ（光束）", "lm", 10f, 2000f, 150f, "0"),
                P("beamAngleDeg", "光の広がり", "°", 10f, 160f, 60f, "0"),
                P("tiltDeg", "下向きの角度", "°", 0f, 90f, 45f, "0") } },
            new BehaviorInfo { Type = "heat", LabelJa = "加温", RequiresTargetPart = true,
                AllowedTargetTypes = new[] { "heater" }, ModelJa = "集中定数の熱モデル C dT/dt = 電力×効率 − G(T − 外気温)", Parameters = new[] {
                P("powerW", "加熱の電力", "W", 1f, 100f, 10f, "0") } },
            new BehaviorInfo { Type = "cool", LabelJa = "冷却", RequiresTargetPart = true,
                AllowedTargetTypes = new[] { "cooler", "fan" }, ModelJa = "集中定数の熱モデル C dT/dt = −電力×成績係数 − G(T − 外気温)", Parameters = new[] {
                P("powerW", "冷却の電力", "W", 1f, 100f, 10f, "0") } },
            new BehaviorInfo { Type = "sun_shade", LabelJa = "日よけ", RequiresTargetPart = true,
                AllowedTargetTypes = new[] { "canopy", "panel" }, ModelJa = "平行光線と人型モデル（円柱）の影の判定（雨の実験と同じ方式）", Parameters = new[] {
                P("radius", "日よけの半径", "m", 0.2f, 1.5f, 0.6f),
                P("opacity", "遮光率", "", 0.1f, 1f, 0.9f) } },
            new BehaviorInfo { Type = "impact_cushion", LabelJa = "衝撃吸収", RequiresTargetPart = true,
                AllowedTargetTypes = new[] { "cushion" }, ModelJa = "ばね・ダンパーの落下モデル（厚みを使い切ると硬くなる）", Parameters = new[] {
                P("thicknessM", "緩衝材の厚み", "m", 0.005f, 0.2f, 0.03f, "0.000"),
                P("stiffness", "緩衝材の硬さ", "N/m", 500f, 200000f, 20000f, "0") } },
            new BehaviorInfo { Type = "grip", LabelJa = "つかむ", RequiresTargetPart = true,
                AllowedTargetTypes = new[] { "gripper", "arm" }, ModelJa = "2面ではさむ：2μF ≥ m(g + 揺れの加速度)", Parameters = new[] {
                P("gripForceN", "つかむ力", "N", 1f, 200f, 30f, "0"),
                P("openingM", "開き幅", "m", 0.02f, 0.6f, 0.2f) } },
            new BehaviorInfo { Type = "lift_load", LabelJa = "持ち上げ", RequiresTargetPart = true,
                AllowedTargetTypes = new[] { "arm", "motor", "telescopic", "slide_rail" }, ModelJa = "一定の速さで持ち上げる：電力 = m g v ÷ 効率", Parameters = new[] {
                P("maxLoadKg", "持ち上げられる重さ", "kg", 0.5f, 30f, 5f, "0.0"),
                P("liftHeightM", "持ち上げられる高さ", "m", 0.1f, 2f, 0.8f),
                P("speed", "持ち上げる速さ", "m/s", 0.05f, 1f, 0.2f) } },
            // ---- 機能ブロック 第2弾（6週目）。計算式は Core/Blocks/MassAndDriveModels.cs・docs/blocks.md 8章 ----
            new BehaviorInfo { Type = "drive", LabelJa = "走行", RequiresTargetPart = true,
                AllowedTargetTypes = new[] { "wheel", "motor", "body", "crawler" }, ModelJa = "坂の角度 ≤ 上れる角度なら制限速度で上る。電力 P = m g (sinθ + 転がり抵抗係数 cosθ) v ÷ 駆動効率", Parameters = new[] {
                P("speedLimit", "走る速さ", "m/s", 0.1f, 3f, 1f, "0.0"),
                P("maxSlopeDeg", "上れる坂の角度", "°", 0f, 30f, 10f, "0") } },
            new BehaviorInfo { Type = "detect", LabelJa = "検知", RequiresTargetPart = true,
                AllowedTargetTypes = new[] { "sensor" }, ModelJa = "距離・視野角・障害物による遮りの幾何判定", Parameters = new[] {
                P("rangeM", "検知距離", "m", 0.5f, 30f, 5f, "0.0"),
                P("fovDeg", "視野角", "°", 10f, 360f, 90f, "0") } },
            new BehaviorInfo { Type = "alert", LabelJa = "通知", RequiresTargetPart = true,
                AllowedTargetTypes = new[] { "light", "speaker", "display", "vibrator" }, ModelJa = "音：距離の逆二乗で弱まる、光：点の光が気づく照度に届く距離", Parameters = new[] {
                P("loudnessDb", "音の大きさ（1 m）", "dB", 0f, 110f, 80f, "0"),
                P("lightLumens", "光の明るさ", "lm", 0f, 500f, 50f, "0"),
                P("flashHz", "点滅の速さ", "Hz", 0f, 10f, 2f, "0.0") } },
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
        /// <summary>AI設計プロキシに渡す機能（ExposeToAi）。</summary>
        public static IEnumerable<BehaviorInfo> AiBehaviors => Behaviors.Where(b => b.ExposeToAi);

        /// <summary>AI設計プロキシ用に書き出すカタログ（proxy/catalog.json）。部品・機能の唯一の出典はこのクラス。</summary>
        public static string ToProxyJson()
        {
            var parts = new Newtonsoft.Json.Linq.JArray(PartTypes.Select(p => new Newtonsoft.Json.Linq.JObject
            {
                ["type"] = p.Type, ["labelJa"] = p.LabelJa, ["minScale"] = p.MinScale, ["maxScale"] = p.MaxScale, ["group"] = p.Group,
                ["defaultShape"] = p.DefaultShape, ["defaultMaterial"] = p.DefaultMaterial,
            }));
            var behaviors = new Newtonsoft.Json.Linq.JArray(AiBehaviors.Select(b => new Newtonsoft.Json.Linq.JObject
            {
                ["type"] = b.Type, ["labelJa"] = b.LabelJa, ["requiresTargetPart"] = b.RequiresTargetPart,
                ["allowedTargetTypes"] = new Newtonsoft.Json.Linq.JArray(b.AllowedTargetTypes),
                ["parameters"] = new Newtonsoft.Json.Linq.JArray(b.Parameters.Select(p => new Newtonsoft.Json.Linq.JObject
                {
                    ["key"] = p.Key, ["labelJa"] = p.LabelJa, ["unit"] = p.Unit ?? "", ["min"] = p.Min, ["max"] = p.Max, ["default"] = p.Default,
                })),
                ["modelJa"] = b.ModelJa ?? "",
            }));
            Newtonsoft.Json.Linq.JArray Named(IEnumerable<NamedOption> xs) => new Newtonsoft.Json.Linq.JArray(xs.Select(x => new Newtonsoft.Json.Linq.JObject { ["id"] = x.Id, ["labelJa"] = x.LabelJa }));
            Newtonsoft.Json.Linq.JArray Strs(IEnumerable<string> xs) => new Newtonsoft.Json.Linq.JArray(xs);
            var experiments = new Newtonsoft.Json.Linq.JObject
            {
                ["scenarios"] = new Newtonsoft.Json.Linq.JArray(ExperimentCatalog.Scenarios.Select(x => new Newtonsoft.Json.Linq.JObject
                {
                    ["id"] = x.Id, ["labelJa"] = x.LabelJa, ["description"] = x.Description, ["allowedModules"] = Strs(x.AllowedModules),
                })),
                ["modules"] = new Newtonsoft.Json.Linq.JArray(ExperimentCatalog.Modules.Select(m => new Newtonsoft.Json.Linq.JObject
                {
                    ["id"] = m.Id, ["labelJa"] = m.LabelJa,
                    ["params"] = new Newtonsoft.Json.Linq.JArray(m.Params.Select(p => new Newtonsoft.Json.Linq.JObject
                    { ["key"] = p.Key, ["labelJa"] = p.LabelJa, ["unit"] = p.Unit ?? "", ["min"] = p.Min, ["max"] = p.Max, ["default"] = p.Default })),
                })),
                ["metrics"] = new Newtonsoft.Json.Linq.JArray(ExperimentCatalog.Metrics.Select(m => new Newtonsoft.Json.Linq.JObject
                {
                    ["key"] = m.Key, ["labelJa"] = m.LabelJa, ["unit"] = (m.Unit ?? "").Trim(), ["lowerIsBetter"] = m.LowerIsBetter, ["realityOnly"] = m.RealityOnly,
                    ["scenarios"] = Strs(m.Scenarios), ["anyBlocks"] = Strs(m.AnyBlocks), ["modules"] = Strs(m.Modules), ["standModules"] = Strs(m.StandModules), ["min"] = m.Min, ["max"] = m.Max,
                    ["needsPower"] = m.NeedsPower,
                })),
            };
            var cats = new Newtonsoft.Json.Linq.JArray(Categories.Select(c => new Newtonsoft.Json.Linq.JObject
            {
                ["id"] = c.Id, ["labelJa"] = c.LabelJa, ["experimentAvailable"] = c.ExperimentAvailable,
            }));
            var program = new Newtonsoft.Json.Linq.JObject
            {
                ["sensors"] = new Newtonsoft.Json.Linq.JArray(ProgramCatalog.Sensors.Select(s => new Newtonsoft.Json.Linq.JObject
                {
                    ["id"] = s.Id, ["labelJa"] = s.LabelJa, ["unit"] = s.Unit ?? "", ["min"] = s.Min, ["max"] = s.Max, ["isBool"] = s.IsBool, ["requires"] = s.Requires ?? "",
                })),
                ["actions"] = Strs(ProgramCatalog.Actions),
                ["ops"] = Strs(ProgramCatalog.Ops),
                ["limits"] = new Newtonsoft.Json.Linq.JObject
                {
                    ["maxRules"] = ProgramCatalog.MaxRules, ["maxActionsPerRule"] = ProgramCatalog.MaxActionsPerRule, ["maxFlags"] = ProgramCatalog.MaxFlags,
                    ["maxConditions"] = ProgramCatalog.MaxConditions, ["maxLabelLength"] = ProgramCatalog.MaxLabelLength,
                    ["maxJoints"] = ProgramCatalog.MaxJoints, ["maxJointDepth"] = ProgramCatalog.MaxJointDepth,
                    ["maxCooldown"] = ProgramCatalog.MaxCooldown, ["minWait"] = ProgramCatalog.MinWait, ["maxWait"] = ProgramCatalog.MaxWait,
                    ["hingeLimit"] = ProgramCatalog.HingeLimit, ["hingeSpeedMin"] = ProgramCatalog.HingeSpeedMin, ["hingeSpeedMax"] = ProgramCatalog.HingeSpeedMax,
                    ["sliderLimit"] = ProgramCatalog.SliderLimit, ["sliderSpeedMin"] = ProgramCatalog.SliderSpeedMin, ["sliderSpeedMax"] = ProgramCatalog.SliderSpeedMax,
                },
            };
            var root = new Newtonsoft.Json.Linq.JObject
            {
                ["generatedBy"] = "ImpossibleLab.Core.LabCatalog.ToProxyJson (Unity で再生成すること。手で編集しない)",
                ["schemaVersion"] = InventionDna.CurrentSchemaVersion,
                ["partTypes"] = parts, ["behaviors"] = behaviors, ["categories"] = cats,
                ["tags"] = Named(Tags), ["mounts"] = Named(Mounts), ["shapes"] = Named(Shapes), ["partGroups"] = Named(PartGroups),
                ["partModels"] = PartModels.ToProxyJson(),
                ["materials"] = new Newtonsoft.Json.Linq.JArray(Materials.Select(m => new Newtonsoft.Json.Linq.JObject { ["id"] = m.Id, ["labelJa"] = m.LabelJa, ["densityKgM3"] = m.DensityKgM3 })),
                ["functionStatuses"] = Strs(FunctionStatuses),
                ["experiments"] = experiments,
                ["program"] = program,
                ["appearances"] = new Newtonsoft.Json.Linq.JArray(Appearances.Select(a => new Newtonsoft.Json.Linq.JObject { ["id"] = a.Id, ["labelJa"] = a.LabelJa })),
                ["limits"] = new Newtonsoft.Json.Linq.JObject
                {
                    ["maxParts"] = InventionConstraints.HardMaxParts, ["maxBehaviors"] = MaxBehaviors,
                    ["positionLimitXZ"] = PositionLimitXZ, ["positionMinY"] = PositionMinY, ["positionMaxY"] = PositionMaxY,
                    ["maxNameLength"] = MaxNameLength, ["maxPurposeLength"] = MaxPurposeLength, ["maxRequestLength"] = RequestRules.MaxRequestLength,
                    ["maxTags"] = MaxTags, ["maxFunctionNeeds"] = MaxFunctionNeeds, ["maxNeedLength"] = MaxNeedLength, ["maxNoteLength"] = MaxNoteLength,
                    ["maxOverviewLength"] = MaxOverviewLength, ["maxRoleLength"] = MaxRoleLength, ["maxHowLength"] = MaxHowLength,
                    ["maxPlans"] = ProxyProtocol.MaxPlans, ["maxModules"] = ExperimentCatalog.MaxModules, ["maxMetrics"] = ExperimentCatalog.MaxMetrics,
                    ["maxCriteria"] = ExperimentCatalog.MaxCriteria, ["maxReasonLength"] = ExperimentCatalog.MaxReasonLength,
                    ["minDurationSec"] = RainExperimentConfig.MinDuration, ["maxDurationSec"] = RainExperimentConfig.MaxDuration,
                },
            };
            return root.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        public static string FormatValue(ParameterInfo info, float value) =>
            value.ToString(info.Format, System.Globalization.CultureInfo.InvariantCulture) + (string.IsNullOrEmpty(info.Unit) ? "" : " " + info.Unit);
    }
}
