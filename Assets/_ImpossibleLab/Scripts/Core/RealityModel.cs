using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ImpossibleLab.Core
{
    /// <summary>REALITY の係数1件。値・幅・確度・出典を持つ（docs/reality_model.md）。</summary>
    public sealed class RealityCoefficient
    {
        public string Id, LabelJa, Unit, Status, Source;
        public float Value, Min, Max;
        /// <summary>"high"：大きいほど稼働時間が延びる側 / "low"：小さいほど延びる側。</summary>
        public string Better;

        public float Pick(RealityCase c)
        {
            if (c == RealityCase.Nominal) return Value;
            bool optimistic = c == RealityCase.Optimistic;
            return (Better == "high") == optimistic ? Max : Min;
        }

        public string FormatValue(float v)
        {
            string s = v.ToString(Math.Abs(v) >= 10f ? "0.#" : "0.###", CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(Unit) ? s : s + " " + Unit;
        }
    }

    public enum RealityCase { Nominal, Optimistic, Pessimistic }

    /// <summary>1ケース分の見積もり結果。</summary>
    public struct RealityEstimate
    {
        public float MassKg, WeightN, WindForceN, ThrustN, TiltDeg;
        public float IdealPowerW, PowerW, BatteryWh, RuntimeMinutes, DriftM;
        public bool CanHoldPosition;
    }

    /// <summary>
    /// 頭上追従キャノピーの簡易物理モデル。
    /// ホバー電力：運動量理論 P_ideal = T^1.5 / √(2ρA)、電気入力 = P_ideal ÷ (FM × 駆動効率) + 制御電力。
    /// 風荷重：F = ½ ρ Cd A_side v²。推力 T = √(W² + F²)、傾き = atan(F/W)。
    /// 位置ずれ：F ÷ 位置保持の強さ（調整用仮定）。
    /// </summary>
    public sealed class RealityModel
    {
        public const string ResourcePath = "ImpossibleLabData/reality_model";
        public const float Gravity = 9.80665f; // 標準重力加速度

        public static readonly string[] RequiredIds =
        {
            "airDensity", "figureOfMerit", "driveEfficiency", "rotorDiscArea", "baseMassKg", "canopyArealDensity",
            "skirtArealDensity", "batteryMassKg", "batteryEnergyDensity", "usableFraction", "avionicsPowerW",
            "dragCoefficient", "canopyProfileHeightRatio", "maxTiltDeg", "holdStiffnessNPerM", "stiffnessGainPerStab", "maxDriftM",
        };

        public string Model = "", Summary = "";
        public readonly List<RealityCoefficient> Coefficients = new List<RealityCoefficient>();
        public readonly Dictionary<string, string> StatusLegend = new Dictionary<string, string>();
        readonly Dictionary<string, RealityCoefficient> _byId = new Dictionary<string, RealityCoefficient>();

        public RealityCoefficient Get(string id) => _byId.TryGetValue(id, out var c) ? c : null;
        float V(string id, RealityCase c) => _byId[id].Pick(c);

        public string StatusLabel(string status) => StatusLegend.TryGetValue(status ?? "", out var s) ? s : "要調査";

        public int CountStatus(string status)
        {
            int n = 0;
            foreach (var c in Coefficients) if (c.Status == status) n++;
            return n;
        }

        // ---------- 読み込み ----------
        static RealityModel _default;
        static string _defaultError;
        static bool _loaded;

        /// <summary>Resources から読み込む。失敗時は null（REALITY の電力計算は行わず、その旨を表示する）。</summary>
        public static RealityModel Default
        {
            get
            {
                if (!_loaded)
                {
                    _loaded = true;
                    var ta = Resources.Load<TextAsset>(ResourcePath);
                    if (ta == null) _defaultError = "REALITY モデルのデータ（" + ResourcePath + "）が見つかりません";
                    else _default = Parse(ta.text, out _defaultError);
                    if (_default == null) Debug.LogError("[ImpossibleLab] " + _defaultError);
                }
                return _default;
            }
        }

        public static string DefaultError { get { var _ = Default; return _defaultError; } }

        public static RealityModel Parse(string json, out string error)
        {
            error = null;
            try
            {
                var root = JObject.Parse(json);
                var m = new RealityModel { Model = root.Value<string>("model") ?? "", Summary = root.Value<string>("summary") ?? "" };
                if (root["statusLegend"] is JObject legend)
                    foreach (var p in legend.Properties()) m.StatusLegend[p.Name] = (string)p.Value;
                if (!(root["coefficients"] is JArray arr)) { error = "coefficients がありません"; return null; }
                foreach (var t in arr)
                {
                    if (!(t is JObject o)) continue;
                    var c = new RealityCoefficient
                    {
                        Id = o.Value<string>("id"), LabelJa = o.Value<string>("labelJa") ?? "", Unit = o.Value<string>("unit") ?? "",
                        Status = o.Value<string>("status") ?? "", Source = o.Value<string>("source") ?? "",
                        Better = o.Value<string>("better") ?? "high",
                        Value = o.Value<float>("value"), Min = o.Value<float>("min"), Max = o.Value<float>("max"),
                    };
                    if (string.IsNullOrEmpty(c.Id)) { error = "id のない係数があります"; return null; }
                    if (m._byId.ContainsKey(c.Id)) { error = "係数 " + c.Id + " が重複しています"; return null; }
                    if (!Finite(c.Value) || !Finite(c.Min) || !Finite(c.Max) || c.Min > c.Value || c.Value > c.Max || c.Min <= 0f)
                    { error = "係数 " + c.Id + " の値・範囲が不正です"; return null; }
                    if (c.Better != "high" && c.Better != "low") { error = "係数 " + c.Id + " の better が不正です"; return null; }
                    m.Coefficients.Add(c);
                    m._byId[c.Id] = c;
                }
                foreach (var id in RequiredIds)
                    if (!m._byId.ContainsKey(id)) { error = "必須の係数 " + id + " がありません"; return null; }
                if (m.V("figureOfMerit", RealityCase.Optimistic) > 1f || m.V("driveEfficiency", RealityCase.Optimistic) > 1f ||
                    m.V("usableFraction", RealityCase.Optimistic) > 1f)
                { error = "効率・割合は1以下である必要があります"; return null; }
                if (m.V("maxTiltDeg", RealityCase.Optimistic) >= 89f) { error = "maxTiltDeg が大きすぎます"; return null; }
                return m;
            }
            catch (Exception ex) { error = "REALITY モデルの読み込みに失敗：" + ex.Message; return null; }
        }

        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        // ---------- 計算 ----------
        public RealityEstimate Estimate(float radius, float skirtLength, float stabilization, float windSpeed, RealityCase c)
        {
            radius = Mathf.Max(0f, radius);
            skirtLength = Mathf.Max(0f, skirtLength);
            stabilization = Mathf.Clamp01(stabilization);
            windSpeed = Mathf.Max(0f, windSpeed);

            float rho = V("airDensity", c);
            float canopyArea = Mathf.PI * radius * radius;
            float skirtArea = 2f * Mathf.PI * radius * skirtLength;
            float batteryKg = V("batteryMassKg", c);
            var e = new RealityEstimate();
            e.MassKg = V("baseMassKg", c) + canopyArea * V("canopyArealDensity", c) + skirtArea * V("skirtArealDensity", c) + batteryKg;
            e.WeightN = e.MassKg * Gravity;

            // 横から見た投影面積：直径 ×（キャノピーの側面高さ + 側面カバー長）
            float sideArea = 2f * radius * (V("canopyProfileHeightRatio", c) * radius + skirtLength);
            e.WindForceN = 0.5f * rho * V("dragCoefficient", c) * sideArea * windSpeed * windSpeed;
            e.ThrustN = Mathf.Sqrt(e.WeightN * e.WeightN + e.WindForceN * e.WindForceN);
            e.TiltDeg = Mathf.Atan2(e.WindForceN, e.WeightN) * Mathf.Rad2Deg;
            e.CanHoldPosition = e.TiltDeg <= V("maxTiltDeg", c);

            // 運動量理論の理想ホバー電力（風中の前進飛行による誘導電力の低下は無視＝保守側）
            e.IdealPowerW = Mathf.Pow(e.ThrustN, 1.5f) / Mathf.Sqrt(2f * rho * V("rotorDiscArea", c));
            e.PowerW = e.IdealPowerW / (V("figureOfMerit", c) * V("driveEfficiency", c)) + V("avionicsPowerW", c);
            e.BatteryWh = batteryKg * V("batteryEnergyDensity", c) * V("usableFraction", c);
            e.RuntimeMinutes = e.PowerW > 0f ? e.BatteryWh / e.PowerW * 60f : 0f;

            float stiffness = V("holdStiffnessNPerM", c) * (1f + V("stiffnessGainPerStab", c) * stabilization);
            float maxDrift = V("maxDriftM", c);
            e.DriftM = e.CanHoldPosition ? Mathf.Min(e.WindForceN / stiffness, maxDrift) : maxDrift;
            return e;
        }
    }
}
