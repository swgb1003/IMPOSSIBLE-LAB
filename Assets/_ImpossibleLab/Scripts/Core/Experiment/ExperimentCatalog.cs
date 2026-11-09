using System.Linq;

namespace ImpossibleLab.Core
{
    /// <summary>シナリオ（人型モデルや装置の行動）。</summary>
    public sealed class ScenarioInfo
    {
        public string Id, LabelJa, Description;
        /// <summary>このシナリオで使える環境モジュール。</summary>
        public string[] AllowedModules = new string[0];
        /// <summary>合否を実験計画の合格条件で決める（4週目以降のシナリオ）。既存の3デモはモデルが決める。</summary>
        public bool JudgedByCriteria;
    }

    public sealed class ModuleParam
    {
        public string Key, LabelJa, Unit = "";
        public float Min, Max, Default;
    }

    /// <summary>環境モジュール（雨・風・障害物など）。</summary>
    public sealed class ModuleInfo
    {
        public string Id, LabelJa;
        public ModuleParam[] Params;
        public ModuleParam Param(string key) => Params.FirstOrDefault(p => p.Key == key);
    }

    /// <summary>計測項目。計算式は docs/experiments.md。必要な条件がそろわない計測は実験計画の検証で拒否する。</summary>
    public sealed class MetricInfo
    {
        public string Key, LabelJa, Unit = "", Format = "0.00";
        public bool LowerIsBetter = true, Primary = true, RealityOnly;
        /// <summary>使えるシナリオ。</summary>
        public string[] Scenarios = new string[0];
        /// <summary>このどれかの機能が発明にあること（空なら不要）。</summary>
        public string[] AnyBlocks = new string[0];
        /// <summary>すべて必要な環境モジュール。</summary>
        public string[] Modules = new string[0];
        /// <summary>stand で測るときだけ追加で必要な環境モジュール（例：消費エネルギーは雨の実験でだけ出す）。</summary>
        public string[] StandModules = new string[0];
        /// <summary>合格条件に使える値の範囲。</summary>
        public float Min, Max;

        public ExperimentMetric Make(float value) => new ExperimentMetric
        { Key = Key, LabelJa = LabelJa, Value = value, Format = Format, LowerIsBetter = LowerIsBetter, Unit = Unit, Primary = Primary };
    }

    /// <summary>
    /// 組み立て式の実験のカタログ（docs/GENERAL_INVENTION_DESIGN.md 5章）。唯一の出典はこのクラス。
    /// 2週目：既存3デモ（雨具・運搬・移動）。4週目：機能ブロック第1弾のためのシナリオ・環境モジュール・計測項目。
    /// </summary>
    public static class ExperimentCatalog
    {
        public const int MaxModules = 6, MaxMetrics = 8, MaxCriteria = 4, MaxReasonLength = 80;
        public const string Stand = "stand", WalkRoute = "walk_route", FlyToTarget = "fly_to_target";
        public const string SitStill = "sit_still", DropTest = "drop_test", LiftTask = "lift_task";

        public static readonly ScenarioInfo[] Scenarios =
        {
            new ScenarioInfo { Id = Stand, LabelJa = "その場に立つ", Description = "人型モデルが立ち、装置が頭上などで働く",
                AllowedModules = new[] { "rain", "wind", "darkness", "sunlight", "targets", "obstacles" }, JudgedByCriteria = true },
            new ScenarioInfo { Id = WalkRoute, LabelJa = "ルートを歩く", Description = "人型モデル（案内役）が決まったルートを歩き、装置がついていく",
                AllowedModules = new[] { "obstacles", "payload" } },
            new ScenarioInfo { Id = FlyToTarget, LabelJa = "目標まで飛ぶ", Description = "人型モデルが装置で目標地点まで飛ぶ（観察モード）",
                AllowedModules = new[] { "target", "wind" } },
            new ScenarioInfo { Id = SitStill, LabelJa = "座って過ごす", Description = "人型モデルが座って過ごし、装置が温める・冷やす（時間は早送りで再生）",
                AllowedModules = new[] { "temperature" }, JudgedByCriteria = true },
            new ScenarioInfo { Id = DropTest, LabelJa = "落とす", Description = "発明品（または守る物）を落とし、床に当たるときの衝撃を測る",
                AllowedModules = new[] { "drop" }, JudgedByCriteria = true },
            new ScenarioInfo { Id = LiftTask, LabelJa = "持ち上げて置く", Description = "荷物を棚の高さまで持ち上げる（装置の持ち上げ機構、またはつかみ具で人が持ち上げる）",
                AllowedModules = new[] { "payload", "shelf" }, JudgedByCriteria = true },
        };

        public static readonly ModuleInfo[] Modules =
        {
            new ModuleInfo { Id = "rain", LabelJa = "雨", Params = new[] { P("intensity", "雨量", "mm/h", 5f, 80f, 20f) } },
            new ModuleInfo { Id = "wind", LabelJa = "風", Params = new[] { P("speed", "風速", "m/s", 0f, 15f, 3f), P("directionDeg", "風向", "°", 0f, 359.9f, 90f) } },
            new ModuleInfo { Id = "obstacles", LabelJa = "障害物", Params = new[] { P("count", "障害物の数", "個", 0f, 10f, 4f) } },
            new ModuleInfo { Id = "payload", LabelJa = "積荷", Params = new[] { P("kg", "積荷の重さ", "kg", 0f, 30f, 5f), P("sizeM", "積荷の大きさ（幅）", "m", 0.05f, 0.8f, 0.3f) } },
            new ModuleInfo { Id = "target", LabelJa = "目標地点", Params = new[] { P("distanceM", "目標までの距離", "m", 4f, 10f, 7f) } },
            // 4週目
            new ModuleInfo { Id = "darkness", LabelJa = "暗さ", Params = new[] { P("lux", "周りの明るさ", "lx", 0f, 1000f, 2f) } },
            new ModuleInfo { Id = "temperature", LabelJa = "気温", Params = new[] { P("ambientC", "気温", "°C", -20f, 45f, 5f), P("targetDeltaC", "目標の温度変化", "°C", 1f, 20f, 5f), P("minutes", "過ごす時間", "分", 1f, 60f, 15f) } },
            new ModuleInfo { Id = "sunlight", LabelJa = "日差し", Params = new[] { P("elevationDeg", "太陽の高さ", "°", 5f, 90f, 60f), P("azimuthDeg", "太陽の方位", "°", 0f, 359.9f, 180f), P("irradiance", "日射の強さ", "W/m²", 100f, 1100f, 800f) } },
            new ModuleInfo { Id = "drop", LabelJa = "落下", Params = new[] { P("heightM", "落とす高さ", "m", 0.2f, 3f, 1f), P("massKg", "落とす物の重さ", "kg", 0.1f, 5f, 0.5f) } },
            new ModuleInfo { Id = "targets", LabelJa = "検知対象", Params = new[] { P("count", "対象の数", "個", 1f, 12f, 6f) } },
            new ModuleInfo { Id = "shelf", LabelJa = "棚", Params = new[] { P("heightM", "棚の高さ", "m", 0.2f, 2f, 0.8f) } },
        };

        static readonly string[] Rain = { "rain" };

        public static readonly MetricInfo[] Metrics =
        {
            // 雨具（stand + rain）。値は RainExperimentResult の専用の項目にも入る
            new MetricInfo { Key = "wetnessScore", LabelJa = "被濡れスコア", Unit = " 粒/秒", Format = "0.0", Scenarios = new[] { Stand }, AnyBlocks = new[] { "rain_shield" }, Modules = Rain, Min = 0, Max = 1000 },
            new MetricInfo { Key = "coverageRatio", LabelJa = "防護率", Format = "0.000", LowerIsBetter = false, Scenarios = new[] { Stand }, AnyBlocks = new[] { "rain_shield" }, Modules = Rain, Min = 0, Max = 1 },
            // 運搬（walk_route）
            new MetricInfo { Key = "collisionCount", LabelJa = "衝突回数", Unit = " 回", Format = "0", Scenarios = new[] { WalkRoute }, AnyBlocks = new[] { "follow_user" }, Min = 0, Max = 100 },
            new MetricInfo { Key = "arrivalDelay", LabelJa = "到着の遅れ", Unit = " 秒", Format = "0.0", Scenarios = new[] { WalkRoute }, AnyBlocks = new[] { "follow_user" }, Min = 0, Max = 60 },
            new MetricInfo { Key = "followError", LabelJa = "追従距離の誤差", Unit = " m", Scenarios = new[] { WalkRoute }, AnyBlocks = new[] { "follow_user" }, Min = 0, Max = 20 },
            new MetricInfo { Key = "averageFollowDistance", LabelJa = "平均追従距離", Unit = " m", Primary = false, Scenarios = new[] { WalkRoute }, AnyBlocks = new[] { "follow_user" }, Min = 0, Max = 50 },
            new MetricInfo { Key = "maxGap", LabelJa = "最大の離れ", Unit = " m", Primary = false, Scenarios = new[] { WalkRoute }, AnyBlocks = new[] { "follow_user" }, Min = 0, Max = 50 },
            new MetricInfo { Key = "energyWh", LabelJa = "消費エネルギー", Unit = " Wh", RealityOnly = true, Scenarios = new[] { Stand, WalkRoute }, StandModules = Rain, Min = 0, Max = 10000 },
            // 移動（fly_to_target）
            new MetricInfo { Key = "requiredPowerKW", LabelJa = "必要電力", Unit = " kW", Format = "0", Primary = false, RealityOnly = true, Scenarios = new[] { FlyToTarget }, AnyBlocks = new[] { "hover" }, Min = 0, Max = 100000 },
            new MetricInfo { Key = "availableFlightSec", LabelJa = "電池で飛べる時間", Unit = " 秒", Format = "0.0", LowerIsBetter = false, Primary = false, RealityOnly = true, Scenarios = new[] { FlyToTarget }, AnyBlocks = new[] { "hover" }, Min = 0, Max = 100000 },
            new MetricInfo { Key = "completionTime", LabelJa = "所要時間", Unit = " 秒", Format = "0.0", Scenarios = new[] { FlyToTarget }, Min = 0, Max = 60 },
            new MetricInfo { Key = "stabilityScore", LabelJa = "安定度（100点満点）", Unit = " 点", Format = "0", LowerIsBetter = false, Scenarios = new[] { FlyToTarget }, Min = 0, Max = 100 },
            new MetricInfo { Key = "maxDeviation", LabelJa = "横の最大の揺れ", Unit = " m", Scenarios = new[] { FlyToTarget }, Min = 0, Max = 20 },
            new MetricInfo { Key = "landingError", LabelJa = "着地点のずれ", Unit = " m", Scenarios = new[] { FlyToTarget }, Min = 0, Max = 50 },
            new MetricInfo { Key = "maxAltitude", LabelJa = "最高高度", Unit = " m", LowerIsBetter = false, Primary = false, Scenarios = new[] { FlyToTarget }, Min = 0, Max = 10 },
            // 4週目：機能ブロック第1弾（式は docs/blocks.md）
            new MetricInfo { Key = "footIlluminance", LabelJa = "足元の照度", Unit = " lx", Format = "0.0", LowerIsBetter = false, Scenarios = new[] { Stand }, AnyBlocks = new[] { "illuminate" }, Modules = new[] { "darkness" }, Min = 0, Max = 100000 },
            new MetricInfo { Key = "illuminanceMin", LabelJa = "照度の最小", Unit = " lx", Format = "0.0", LowerIsBetter = false, Primary = false, Scenarios = new[] { Stand }, AnyBlocks = new[] { "illuminate" }, Modules = new[] { "darkness" }, Min = 0, Max = 100000 },
            new MetricInfo { Key = "shadeRatio", LabelJa = "日陰率", Format = "0.00", LowerIsBetter = false, Scenarios = new[] { Stand }, AnyBlocks = new[] { "sun_shade" }, Modules = new[] { "sunlight" }, Min = 0, Max = 1 },
            new MetricInfo { Key = "sunExposureW", LabelJa = "体が受ける日射", Unit = " W", Format = "0", Primary = false, Scenarios = new[] { Stand }, Modules = new[] { "sunlight" }, Min = 0, Max = 10000 },
            new MetricInfo { Key = "detectionRate", LabelJa = "検知率", Format = "0.00", LowerIsBetter = false, Scenarios = new[] { Stand }, AnyBlocks = new[] { "detect" }, Modules = new[] { "targets" }, Min = 0, Max = 1 },
            new MetricInfo { Key = "alertRange", LabelJa = "通知の届く距離", Unit = " m", Format = "0", LowerIsBetter = false, Scenarios = new[] { Stand }, AnyBlocks = new[] { "alert" }, Min = 0, Max = 5000 },
            new MetricInfo { Key = "temperatureGain", LabelJa = "狙った向きの温度変化", Unit = " °C", Format = "0.0", LowerIsBetter = false, Scenarios = new[] { SitStill }, AnyBlocks = new[] { "heat", "cool" }, Modules = new[] { "temperature" }, Min = -50, Max = 50 },
            new MetricInfo { Key = "timeToTarget", LabelJa = "目標の温度に届くまで", Unit = " 分", Format = "0.0", Scenarios = new[] { SitStill }, AnyBlocks = new[] { "heat", "cool" }, Modules = new[] { "temperature" }, Min = 0, Max = NotReached },
            new MetricInfo { Key = "peakImpactG", LabelJa = "最大の衝撃", Unit = " G", Format = "0", Scenarios = new[] { DropTest }, Modules = new[] { "drop" }, Min = 0, Max = 100000 },
            new MetricInfo { Key = "cushionCompression", LabelJa = "緩衝材のつぶれ", Unit = " m", Format = "0.000", Primary = false, Scenarios = new[] { DropTest }, AnyBlocks = new[] { "impact_cushion" }, Modules = new[] { "drop" }, Min = 0, Max = 10 },
            new MetricInfo { Key = "dropCount", LabelJa = "落とした回数", Unit = " 回", Format = "0", Scenarios = new[] { LiftTask }, AnyBlocks = new[] { "grip", "lift_load" }, Min = 0, Max = 100 },
            new MetricInfo { Key = "liftTime", LabelJa = "持ち上げ時間", Unit = " 秒", Format = "0.0", Scenarios = new[] { LiftTask }, AnyBlocks = new[] { "grip", "lift_load" }, Min = 0, Max = LiftTimeout },
            new MetricInfo { Key = "liftPowerW", LabelJa = "持ち上げの電力", Unit = " W", Format = "0.0", Primary = false, Scenarios = new[] { LiftTask }, AnyBlocks = new[] { "lift_load" }, Min = 0, Max = 100000 },
            new MetricInfo { Key = "blockPowerW", LabelJa = "機能の消費電力", Unit = " W", Format = "0.0", Primary = false, RealityOnly = true, Scenarios = new[] { Stand, SitStill, LiftTask }, Min = 0, Max = 100000 },
        };

        /// <summary>届かなかったときの「目標の温度に届くまで」の値（分）。</summary>
        public const float NotReached = 999f;
        /// <summary>持ち上げられなかったときの「持ち上げ時間」の値（秒）。</summary>
        public const float LiftTimeout = 60f;

        /// <summary>
        /// 合格条件が計画にないときの目安（4週目以降のシナリオ）。値は調整用・要出典で、6週目の係数調査で見直す。
        /// 結果に出た計測項目の分だけ使う。
        /// </summary>
        public static readonly PlanCriterion[] DefaultCriteria =
        {
            new PlanCriterion { Metric = "footIlluminance", Op = ">=", Value = 10f, Reason = "足元の段差が見える目安（要出典）" },
            new PlanCriterion { Metric = "shadeRatio", Op = ">=", Value = 0.6f, Reason = "体の6割以上を日陰にする（調整用）" },
            new PlanCriterion { Metric = "detectionRate", Op = ">=", Value = 0.8f, Reason = "8割以上の対象に気づく（調整用）" },
            new PlanCriterion { Metric = "alertRange", Op = ">=", Value = 30f, Reason = "30 m 先の人・車に気づいてもらう（要出典）" },
            new PlanCriterion { Metric = "timeToTarget", Op = "<=", Value = 15f, Reason = "15分以内に目標の温度変化に届く（調整用）" },
            new PlanCriterion { Metric = "peakImpactG", Op = "<=", Value = 100f, Reason = "中身が壊れにくい衝撃の目安（要出典）" },
            new PlanCriterion { Metric = "dropCount", Op = "<=", Value = 0f, Reason = "落とさない" },
            new PlanCriterion { Metric = "liftTime", Op = "<=", Value = 10f, Reason = "10秒以内に棚へ置く（調整用）" },
        };

        static ModuleParam P(string key, string label, string unit, float min, float max, float def) =>
            new ModuleParam { Key = key, LabelJa = label, Unit = unit, Min = min, Max = max, Default = def };

        public static ScenarioInfo Scenario(string id) => Scenarios.FirstOrDefault(s => s.Id == id);
        public static ModuleInfo Module(string id) => Modules.FirstOrDefault(m => m.Id == id);
        public static MetricInfo Metric(string key) => Metrics.FirstOrDefault(m => m.Key == key);
    }
}
