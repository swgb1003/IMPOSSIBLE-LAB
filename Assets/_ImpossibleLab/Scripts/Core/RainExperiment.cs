using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ImpossibleLab.Core
{
    /// <summary>実験場（仕様9.1）。</summary>
    public static class LabEnvironments
    {
        public const string RainLab = "RainLab";
        public const string ObstacleCourse = "ObstacleCourse";
        public const string FlightDeck = "FlightDeck";
        public static bool IsValid(string e) => e == RainLab || e == ObstacleCourse || e == FlightDeck;
        public static string ForCategory(string category) =>
            category == "carrier" ? ObstacleCourse : category == "mobility" ? FlightDeck : RainLab;
        public static string LabelJa(string e) =>
            e == ObstacleCourse ? "ObstacleCourse（障害物コース）" : e == FlightDeck ? "FlightDeck（飛行デッキ）" : "RainLab（雨・風）";
    }

    /// <summary>実験条件（設定スナップショットとして結果に保存）。環境ごとに使う項目が異なる。</summary>
    public sealed class RainExperimentConfig
    {
        public string Mode = LabModes.Dream;
        public string Environment = LabEnvironments.RainLab;
        /// <summary>雨量 mm/h（5〜80）。RainLab のみ。</summary>
        public float RainIntensity = 20f;
        /// <summary>風速 m/s（0〜15）。RainLab・FlightDeck。</summary>
        public float WindSpeed = 3f;
        /// <summary>風が吹いていく方向（度、0=+Z前方、90=+X右）。</summary>
        public float WindDirectionDeg = 90f;
        /// <summary>障害物の数（0〜10）。ObstacleCourse のみ。</summary>
        public int ObstacleCount = 4;
        /// <summary>積荷の重さ kg（0〜30）。ObstacleCourse のみ。</summary>
        public float PayloadKg = 5f;
        /// <summary>目標地点までの距離 m（4〜10）。FlightDeck のみ。</summary>
        public float TargetDistance = 7f;
        public int Seed = 1234;
        public float DurationSec = 10f;

        public const float MinIntensity = 5f, MaxIntensity = 80f, MaxWind = 15f, MinDuration = 2f, MaxDuration = 60f;
        public const int MaxObstacles = 10;
        public const float MaxPayload = 30f, MinTargetDistance = 4f, MaxTargetDistance = 10f, MaxFlightWind = 10f;

        public RainExperimentConfig Clamped() => new RainExperimentConfig
        {
            Mode = LabModes.IsValid(Mode) ? Mode : LabModes.Dream,
            Environment = LabEnvironments.IsValid(Environment) ? Environment : LabEnvironments.RainLab,
            RainIntensity = Mathf.Clamp(RainIntensity, MinIntensity, MaxIntensity),
            WindSpeed = Mathf.Clamp(WindSpeed, 0f, Environment == LabEnvironments.FlightDeck ? MaxFlightWind : MaxWind),
            WindDirectionDeg = Mathf.Repeat(WindDirectionDeg, 360f),
            ObstacleCount = Mathf.Clamp(ObstacleCount, 0, MaxObstacles),
            PayloadKg = Mathf.Clamp(PayloadKg, 0f, MaxPayload),
            TargetDistance = Mathf.Clamp(TargetDistance, MinTargetDistance, MaxTargetDistance),
            Seed = Seed,
            DurationSec = Mathf.Clamp(DurationSec, MinDuration, MaxDuration),
        };

        public RainExperimentConfig Copy() => (RainExperimentConfig)MemberwiseClone();

        /// <summary>モード以外の条件が同じか。</summary>
        public bool SameExceptMode(RainExperimentConfig o) => o != null && Environment == o.Environment && Seed == o.Seed &&
            Mathf.Approximately(DurationSec, o.DurationSec) &&
            (Environment == LabEnvironments.ObstacleCourse
                ? ObstacleCount == o.ObstacleCount && Mathf.Approximately(PayloadKg, o.PayloadKg)
                : Environment == LabEnvironments.FlightDeck
                    ? Mathf.Approximately(WindSpeed, o.WindSpeed) && Mathf.Approximately(WindDirectionDeg, o.WindDirectionDeg) && Mathf.Approximately(TargetDistance, o.TargetDistance)
                    : Mathf.Approximately(RainIntensity, o.RainIntensity) && Mathf.Approximately(WindSpeed, o.WindSpeed) && Mathf.Approximately(WindDirectionDeg, o.WindDirectionDeg));

        public bool SameConditions(RainExperimentConfig o) => o != null && Mode == o.Mode && SameExceptMode(o);

        public string Describe()
        {
            var ci = CultureInfo.InvariantCulture;
            if (Environment == LabEnvironments.ObstacleCourse)
                return string.Format(ci, "{0} / 障害物 {1} 個 / 積荷 {2:0} kg / シード {3}", LabModes.Label(Mode), ObstacleCount, PayloadKg, Seed);
            if (Environment == LabEnvironments.FlightDeck)
                return string.Format(ci, "{0} / 距離 {1:0} m / 風速 {2:0.0} m/s / 風向 {3:0}° / シード {4}", LabModes.Label(Mode), TargetDistance, WindSpeed, WindDirectionDeg, Seed);
            return string.Format(ci, "{0} / 雨量 {1:0} mm/h / 風速 {2:0.0} m/s / 風向 {3:0}° / {4:0} 秒 / シード {5}",
                LabModes.Label(Mode), RainIntensity, WindSpeed, WindDirectionDeg, DurationSec, Seed);
        }
    }

    /// <summary>モードごとに適用した実効パラメータと前提（仕様8章）。</summary>
    public sealed class EffectiveDevice
    {
        public float Radius, Effectiveness, SkirtLength, HeightOffset, Stabilization;
        public Vector2 Drift;
        /// <summary>REALITY の推定（中央値・楽観・悲観）。DREAM またはモデル読込失敗時は null。</summary>
        public RealityEstimate? Nominal, Optimistic, Pessimistic;
        public float PowerW => Nominal?.PowerW ?? 0f;
        public readonly List<string> Assumptions = new List<string>();
        public readonly List<string> Limits = new List<string>();
    }

    public static class ModeRules
    {
        public static EffectiveDevice Evaluate(InventionDna dna, RainExperimentConfig cfg)
        {
            var e = new EffectiveDevice
            {
                Radius = dna.GetParameter("rain_shield", "radius", 0f),
                Effectiveness = dna.GetParameter("rain_shield", "effectiveness", 0f),
                SkirtLength = dna.GetParameter("rain_shield", "skirtLength", 0f),
                HeightOffset = dna.GetParameter("follow_head", "heightOffset", 0.35f),
                Stabilization = dna.GetParameter("stabilize", "stabilizationStrength", 0f),
            };
            var info = LabCatalog.Behavior("rain_shield");
            if (cfg.Mode == LabModes.Reality)
            {
                foreach (var key in new[] { "radius", "effectiveness" })
                {
                    var p = info.Param(key);
                    float v = key == "radius" ? e.Radius : e.Effectiveness;
                    if (p.RealityMax.HasValue && v > p.RealityMax.Value)
                    {
                        e.Limits.Add($"{p.LabelJa}を {LabCatalog.FormatValue(p, v)} → {LabCatalog.FormatValue(p, p.RealityMax.Value)} に制限（{p.RealityReason}）");
                        if (key == "radius") e.Radius = p.RealityMax.Value; else e.Effectiveness = p.RealityMax.Value;
                    }
                }
                var model = RealityModel.Default;
                if (model == null)
                {
                    e.Drift = Vector2.zero;
                    e.Limits.Add(RealityModel.DefaultError + "。重量・電力は計算していません");
                }
                else
                {
                    var n = model.Estimate(e.Radius, e.SkirtLength, e.Stabilization, cfg.WindSpeed, RealityCase.Nominal);
                    e.Nominal = n;
                    e.Optimistic = model.Estimate(e.Radius, e.SkirtLength, e.Stabilization, cfg.WindSpeed, RealityCase.Optimistic);
                    e.Pessimistic = model.Estimate(e.Radius, e.SkirtLength, e.Stabilization, cfg.WindSpeed, RealityCase.Pessimistic);
                    e.Drift = WindDir(cfg) * n.DriftM;
                    if (!n.CanHoldPosition)
                        e.Limits.Add($"風荷重 {n.WindForceN:0.0} N に対し必要な傾き {n.TiltDeg:0}° が上限 {model.Get("maxTiltDeg").Value:0}° を超え、位置を保てない（推定）");
                    e.Assumptions.Add($"総重量 約 {n.MassKg:0.00} kg（本体・キャノピー・側面カバー・電池の面積比例モデル）");
                    e.Assumptions.Add($"ホバー電力は運動量理論 P = T^1.5 / √(2ρA) を効率で割って算出 → 約 {n.PowerW:0} W");
                    e.Assumptions.Add($"風荷重 F = ½ρ Cd A v² = {n.WindForceN:0.0} N。位置ずれ = F ÷ 位置保持の強さ（調整用の仮定）");
                    e.Assumptions.Add($"電池 {model.Get("batteryMassKg").Value:0.00} kg × エネルギー密度 × 使用可能割合 = 約 {n.BatteryWh:0} Wh");
                    e.Assumptions.Add($"係数 {model.Coefficients.Count} 件のうち「要調査」{model.CountStatus("estimate")} 件・調整用 {model.CountStatus("tuning")} 件。幅は各係数の上下限で計算");
                }
                e.Assumptions.Add("簡易モデルによる参考値。現実の性能・安全性は保証しない");
            }
            else
            {
                e.Drift = Vector2.zero;
                e.Assumptions.Add("空想シミュレーション：装置は風の影響を受けず、電力・重量の制約なし");
                e.Assumptions.Add("雨粒の衝突判定のみで評価（実際の流体解析ではない）");
            }
            return e;
        }

        public static Vector2 WindDir(RainExperimentConfig cfg)
        {
            float rad = cfg.WindDirectionDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
        }
    }

    public sealed class RainExperimentResult
    {
        public string ExperimentId;
        public string InventionId;
        public int Revision;
        public string Environment = "RainLab";
        public RainExperimentConfig Config;
        public string StartTime;
        public int DropsTotal;
        public int BaselineHits;
        public int DeviceHits;
        public int Blocked;
        public int HeadHits, UpperHits, LowerHits;
        public float WetnessScore;      // 身体に当たった雨粒数 / 秒（低いほどよい）
        public float CoverageRatio;     // 1 − 装置あり命中 / 装置なし命中（高いほどよい）
        public float? EnergyWh;         // REALITY のみ（中央値）
        public float? RuntimeMinutes;   // REALITY のみ（中央値）
        public float? RuntimeMinutesMin, RuntimeMinutesMax; // REALITY のみ（悲観〜楽観）
        public float? PowerW, PowerWMin, PowerWMax;         // REALITY のみ
        public float? MassKg, WindForceN;                   // REALITY のみ
        public bool CanHoldPosition = true;
        public float DriftM;
        public List<string> Assumptions = new List<string>();
        public List<string> Limits = new List<string>();
        /// <summary>RainLab 以外の実験の指標（表示順）。</summary>
        public List<ExperimentMetric> Metrics = new List<ExperimentMetric>();
        /// <summary>RainLab 以外の合否（RainLab は防護率で判定）。</summary>
        public bool? Passed;
        /// <summary>RainLab 以外の判定の説明（例：「衝突0回・到着の遅れ3秒以内」）。</summary>
        public string Criteria = "";
        public bool IsRain => Environment == LabEnvironments.RainLab;
        public bool Success => Passed ?? CoverageRatio >= SuccessCoverage;
        public const float SuccessCoverage = 0.8f;
        public ExperimentMetric Metric(string key) => Metrics.Find(m => m.Key == key);
    }

    /// <summary>実験の指標1件（計算式はコードと docs/experiments.md に明記）。</summary>
    public sealed class ExperimentMetric
    {
        public string Key, LabelJa, Unit = "", Format = "0.00";
        public float Value;
        public bool LowerIsBetter;
        /// <summary>比較・改善案の対象にするか（参考値は false）。</summary>
        public bool Primary = true;
        public string Display => Value.ToString(Format, CultureInfo.InvariantCulture) + Unit;
    }

    /// <summary>
    /// 雨よけの簡易被濡れ判定。雨粒を直線レイとして扱い、キャノピー円板・側面カバー円筒・人体円筒との交差を解析的に判定する。
    /// 乱数は独自のXorShiftでシード固定し、フレームレートに依存せず再現可能（仕様9.2, 24.2）。
    /// </summary>
    public sealed class RainSimulator
    {
        public const float BodyRadius = 0.22f, BodyHeight = 1.75f, HeadZone = 1.5f, UpperZone = 0.9f;
        public const float FallSpeed = 6f, SpawnHeight = 3.6f, DropsPerM2PerSecPerMmH = 3f;

        readonly RainExperimentConfig _cfg;
        readonly EffectiveDevice _dev;
        readonly XorShift _rng;
        readonly Vector3 _vel;
        readonly float _side;
        readonly Vector2 _spawnCenter;
        readonly float _canopyY;
        readonly int _total;
        int _emitted;

        public readonly RainExperimentResult Result;
        public EffectiveDevice Device => _dev;
        public int TotalDrops => _total;
        public bool Finished => _emitted >= _total;
        public float Progress => _total == 0 ? 1f : (float)_emitted / _total;
        public Vector3 DropVelocity => _vel;
        public Vector3 CanopyCenter => new Vector3(_dev.Drift.x, _canopyY, _dev.Drift.y);

        public RainSimulator(InventionDna dna, RainExperimentConfig config, string experimentId = null, string startTime = null)
        {
            _cfg = config.Clamped();
            _dev = ModeRules.Evaluate(dna, _cfg);
            _rng = new XorShift(_cfg.Seed);
            var w = ModeRules.WindDir(_cfg) * (_cfg.WindSpeed * 0.8f);
            _vel = new Vector3(w.x, -FallSpeed, w.y);
            _canopyY = BodyHeight + _dev.HeightOffset;
            float fallTime = (SpawnHeight - BodyHeight * 0.5f) / FallSpeed;
            _spawnCenter = -new Vector2(w.x, w.y) * fallTime;
            float slant = new Vector2(w.x, w.y).magnitude * (BodyHeight / FallSpeed);
            // 降雨範囲は装置に依存させない（同じシード・条件なら改良前後で同じ雨粒列になる）
            float maxRadius = LabCatalog.Behavior("rain_shield").Param("radius").Max;
            _side = 1.6f + 2f * slant + 2f * maxRadius;
            _total = Mathf.RoundToInt(DropsPerM2PerSecPerMmH * _cfg.RainIntensity * _side * _side * _cfg.DurationSec);

            Result = new RainExperimentResult
            {
                ExperimentId = experimentId ?? "exp-" + _cfg.Seed,
                InventionId = dna.Id, Revision = dna.Revision, Config = _cfg,
                StartTime = startTime ?? "", DriftM = _dev.Drift.magnitude,
            };
            Result.Assumptions.AddRange(_dev.Assumptions);
            Result.Limits.AddRange(_dev.Limits);
            if (_cfg.Mode == LabModes.Reality && _dev.Nominal.HasValue)
            {
                var n = _dev.Nominal.Value;
                var lo = _dev.Pessimistic.Value;
                var hi = _dev.Optimistic.Value;
                Result.EnergyWh = n.PowerW * _cfg.DurationSec / 3600f;
                Result.RuntimeMinutes = n.RuntimeMinutes;
                Result.RuntimeMinutesMin = Mathf.Min(lo.RuntimeMinutes, hi.RuntimeMinutes);
                Result.RuntimeMinutesMax = Mathf.Max(lo.RuntimeMinutes, hi.RuntimeMinutes);
                Result.PowerW = n.PowerW;
                Result.PowerWMin = Mathf.Min(lo.PowerW, hi.PowerW);
                Result.PowerWMax = Mathf.Max(lo.PowerW, hi.PowerW);
                Result.MassKg = n.MassKg;
                Result.WindForceN = n.WindForceN;
                Result.CanHoldPosition = n.CanHoldPosition;
            }
        }

        public enum DropOutcome { Miss, Blocked, HitBody, HitBodyUnprotected }

        public struct DropSample
        {
            public Vector3 Start;
            public float EndT;
            public DropOutcome Outcome;
        }

        /// <summary>雨粒を最大 count 個処理する。可視化用のサンプルを返す。</summary>
        public int Step(int count, List<DropSample> samples = null)
        {
            int n = Mathf.Min(count, _total - _emitted);
            for (int i = 0; i < n; i++)
            {
                var s = EmitOne(); // samples が null でも必ず評価する
                samples?.Add(s);
            }
            _emitted += n;
            if (Finished) Finish();
            return n;
        }

        public RainExperimentResult RunToEnd() { Step(int.MaxValue); return Result; }

        DropSample EmitOne()
        {
            var start = new Vector3(_spawnCenter.x + (_rng.NextFloat() - 0.5f) * _side, SpawnHeight,
                                    _spawnCenter.y + (_rng.NextFloat() - 0.5f) * _side);
            float leak = _rng.NextFloat(); // 遮水効率の判定用（常に消費して系列を固定）
            Result.DropsTotal++;

            float tGround = start.y / -_vel.y;
            float tBody = IntersectBody(start, _vel);
            if (tBody < float.PositiveInfinity) Result.BaselineHits++;

            float tShield = IntersectShield(start, _vel);
            var s = new DropSample { Start = start, EndT = tGround, Outcome = DropOutcome.Miss };
            if (tShield < tBody && tShield < tGround && leak < _dev.Effectiveness)
            {
                Result.Blocked++;
                s.EndT = tShield; s.Outcome = DropOutcome.Blocked;
                return s;
            }
            if (tBody < float.PositiveInfinity)
            {
                Result.DeviceHits++;
                float y = start.y + _vel.y * tBody;
                if (y >= HeadZone) Result.HeadHits++; else if (y >= UpperZone) Result.UpperHits++; else Result.LowerHits++;
                s.EndT = tBody; s.Outcome = DropOutcome.HitBody;
            }
            return s;
        }

        void Finish()
        {
            Result.WetnessScore = Result.DeviceHits / _cfg.DurationSec;
            Result.CoverageRatio = Result.BaselineHits == 0 ? 1f : 1f - (float)Result.DeviceHits / Result.BaselineHits;
        }

        float IntersectBody(Vector3 p, Vector3 v)
        {
            float best = float.PositiveInfinity;
            // 上面（頭頂）
            float tTop = (BodyHeight - p.y) / v.y;
            if (tTop > 0)
            {
                var q = p + v * tTop;
                if (q.x * q.x + q.z * q.z <= BodyRadius * BodyRadius) best = tTop;
            }
            // 側面
            float tSide = CylinderSide(p, v, Vector2.zero, BodyRadius, 0f, BodyHeight);
            return Mathf.Min(best, tSide);
        }

        float IntersectShield(Vector3 p, Vector3 v)
        {
            if (_dev.Radius <= 0f) return float.PositiveInfinity;
            var c = _dev.Drift;
            float best = float.PositiveInfinity;
            float tDisc = (_canopyY - p.y) / v.y;
            if (tDisc > 0)
            {
                var q = p + v * tDisc;
                float dx = q.x - c.x, dz = q.z - c.y;
                if (dx * dx + dz * dz <= _dev.Radius * _dev.Radius) best = tDisc;
            }
            if (_dev.SkirtLength > 0f)
                best = Mathf.Min(best, CylinderSide(p, v, c, _dev.Radius, _canopyY - _dev.SkirtLength, _canopyY));
            return best;
        }

        /// <summary>外側から入る、鉛直円筒側面との最初の交差。</summary>
        static float CylinderSide(Vector3 p, Vector3 v, Vector2 c, float r, float yMin, float yMax)
        {
            float ox = p.x - c.x, oz = p.z - c.y;
            float a = v.x * v.x + v.z * v.z;
            if (a < 1e-8f) return float.PositiveInfinity;
            float b = 2f * (ox * v.x + oz * v.z);
            float cc = ox * ox + oz * oz - r * r;
            float disc = b * b - 4f * a * cc;
            if (disc < 0f) return float.PositiveInfinity;
            float t = (-b - Mathf.Sqrt(disc)) / (2f * a);
            if (t <= 0f) return float.PositiveInfinity;
            float y = p.y + v.y * t;
            return y >= yMin && y <= yMax ? t : float.PositiveInfinity;
        }
    }

    /// <summary>プラットフォーム非依存の決定的乱数。</summary>
    public sealed class XorShift
    {
        uint _s;
        public XorShift(int seed) { _s = (uint)seed * 2654435761u ^ 0x9E3779B9u; if (_s == 0) _s = 1; }
        public uint NextUInt() { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return _s; }
        public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);
    }

    public sealed class MetricComparison
    {
        public string Label;
        public string Before;
        public string After;
        public string Verdict; // 改善 / 悪化 / 変化なし
    }

    public static class ExperimentComparer
    {
        public static List<MetricComparison> Compare(RainExperimentResult before, RainExperimentResult after)
        {
            var list = new List<MetricComparison>();
            if (before == null || after == null) return list;
            if (!before.IsRain || !after.IsRain)
            {
                if (before.Environment != after.Environment) return list;
                foreach (var a in after.Metrics)
                {
                    var b = before.Metric(a.Key);
                    if (b == null || !a.Primary) continue;
                    list.Add(Row(a.LabelJa, b.Value, a.Value, a.Format, a.LowerIsBetter, a.Unit));
                }
                return list;
            }
            list.Add(Row("被濡れスコア（雨粒/秒）", before.WetnessScore, after.WetnessScore, "0.0", lowerIsBetter: true));
            list.Add(Row("防護率", before.CoverageRatio * 100f, after.CoverageRatio * 100f, "0.0", lowerIsBetter: false, unit: "%"));
            list.Add(Row("頭部への命中", before.HeadHits, after.HeadHits, "0", true));
            list.Add(Row("上半身への命中", before.UpperHits, after.UpperHits, "0", true));
            list.Add(Row("下半身への命中", before.LowerHits, after.LowerHits, "0", true));
            if (before.EnergyWh.HasValue && after.EnergyWh.HasValue)
                list.Add(Row("消費エネルギー", before.EnergyWh.Value, after.EnergyWh.Value, "0.000", true, " Wh"));
            return list;
        }

        static MetricComparison Row(string label, float b, float a, string fmt, bool lowerIsBetter, string unit = "")
        {
            string bs = b.ToString(fmt, CultureInfo.InvariantCulture), as_ = a.ToString(fmt, CultureInfo.InvariantCulture);
            // 表示桁で同じなら「変化なし」（0.08→0.12 秒が「0.1→0.1 悪化」と見えないように）
            string verdict = bs == as_ || Mathf.Abs(a - b) < 1e-4f ? "変化なし" : ((a < b) == lowerIsBetter ? "改善" : "悪化");
            return new MetricComparison { Label = label, Before = bs + unit, After = as_ + unit, Verdict = verdict };
        }
    }
}
