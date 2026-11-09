using System.Linq;

namespace ImpossibleLab.Core
{
    /// <summary>シナリオ（人型モデルや装置の行動）。</summary>
    public sealed class ScenarioInfo
    {
        public string Id, LabelJa, Description;
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
        /// <summary>合格条件に使える値の範囲。</summary>
        public float Min, Max;

        public ExperimentMetric Make(float value) => new ExperimentMetric
        { Key = Key, LabelJa = LabelJa, Value = value, Format = Format, LowerIsBetter = LowerIsBetter, Unit = Unit, Primary = Primary };
    }

    /// <summary>
    /// 組み立て式の実験のカタログ（docs/GENERAL_INVENTION_DESIGN.md 5章）。唯一の出典はこのクラス。
    /// 2週目の時点では、既存3デモ（雨具・運搬・移動）で使うものだけを登録している。
    /// </summary>
    public static class ExperimentCatalog
    {
        public const int MaxModules = 6, MaxMetrics = 8, MaxCriteria = 4, MaxReasonLength = 80;
        public const string Stand = "stand", WalkRoute = "walk_route", FlyToTarget = "fly_to_target";

        public static readonly ScenarioInfo[] Scenarios =
        {
            new ScenarioInfo { Id = Stand, LabelJa = "その場に立つ", Description = "人型モデルが立ち、装置が頭上などで働く" },
            new ScenarioInfo { Id = WalkRoute, LabelJa = "ルートを歩く", Description = "人型モデル（案内役）が決まったルートを歩き、装置がついていく" },
            new ScenarioInfo { Id = FlyToTarget, LabelJa = "目標まで飛ぶ", Description = "人型モデルが装置で目標地点まで飛ぶ（観察モード）" },
        };

        public static readonly ModuleInfo[] Modules =
        {
            new ModuleInfo { Id = "rain", LabelJa = "雨", Params = new[] { P("intensity", "雨量", "mm/h", 5f, 80f, 20f) } },
            new ModuleInfo { Id = "wind", LabelJa = "風", Params = new[] { P("speed", "風速", "m/s", 0f, 15f, 3f), P("directionDeg", "風向", "°", 0f, 359.9f, 90f) } },
            new ModuleInfo { Id = "obstacles", LabelJa = "障害物", Params = new[] { P("count", "障害物の数", "個", 0f, 10f, 4f) } },
            new ModuleInfo { Id = "payload", LabelJa = "積荷", Params = new[] { P("kg", "積荷の重さ", "kg", 0f, 30f, 5f) } },
            new ModuleInfo { Id = "target", LabelJa = "目標地点", Params = new[] { P("distanceM", "目標までの距離", "m", 4f, 10f, 7f) } },
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
            new MetricInfo { Key = "energyWh", LabelJa = "消費エネルギー", Unit = " Wh", RealityOnly = true, Scenarios = new[] { Stand, WalkRoute }, Min = 0, Max = 10000 },
            // 移動（fly_to_target）
            new MetricInfo { Key = "requiredPowerKW", LabelJa = "必要電力", Unit = " kW", Format = "0", Primary = false, RealityOnly = true, Scenarios = new[] { FlyToTarget }, AnyBlocks = new[] { "hover" }, Min = 0, Max = 100000 },
            new MetricInfo { Key = "availableFlightSec", LabelJa = "電池で飛べる時間", Unit = " 秒", Format = "0.0", LowerIsBetter = false, Primary = false, RealityOnly = true, Scenarios = new[] { FlyToTarget }, AnyBlocks = new[] { "hover" }, Min = 0, Max = 100000 },
            new MetricInfo { Key = "completionTime", LabelJa = "所要時間", Unit = " 秒", Format = "0.0", Scenarios = new[] { FlyToTarget }, Min = 0, Max = 60 },
            new MetricInfo { Key = "stabilityScore", LabelJa = "安定度（100点満点）", Unit = " 点", Format = "0", LowerIsBetter = false, Scenarios = new[] { FlyToTarget }, Min = 0, Max = 100 },
            new MetricInfo { Key = "maxDeviation", LabelJa = "横の最大の揺れ", Unit = " m", Scenarios = new[] { FlyToTarget }, Min = 0, Max = 20 },
            new MetricInfo { Key = "landingError", LabelJa = "着地点のずれ", Unit = " m", Scenarios = new[] { FlyToTarget }, Min = 0, Max = 50 },
            new MetricInfo { Key = "maxAltitude", LabelJa = "最高高度", Unit = " m", LowerIsBetter = false, Primary = false, Scenarios = new[] { FlyToTarget }, Min = 0, Max = 10 },
        };

        static ModuleParam P(string key, string label, string unit, float min, float max, float def) =>
            new ModuleParam { Key = key, LabelJa = label, Unit = unit, Min = min, Max = max, Default = def };

        public static ScenarioInfo Scenario(string id) => Scenarios.FirstOrDefault(s => s.Id == id);
        public static ModuleInfo Module(string id) => Modules.FirstOrDefault(m => m.Id == id);
        public static MetricInfo Metric(string key) => Metrics.FirstOrDefault(m => m.Key == key);
    }
}
