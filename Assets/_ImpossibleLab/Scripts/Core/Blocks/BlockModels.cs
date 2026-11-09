using System.Collections.Generic;
using UnityEngine;

namespace ImpossibleLab.Core
{
    // 機能ブロック 第1弾（3週目）の簡易モデル。式と仮定は docs/blocks.md、係数は BlockCoefficients。
    // どれも乱数を使わない純粋な計算で、同じ入力なら同じ結果になる。実験場（シナリオ・環境モジュール）への
    // 組み込みは4週目（暗さ・温度・日射・落下・持ち上げ作業など）。
    // ゲーム・教育向けの簡易モデルで、現実の性能や安全性の予測ではない。

    /// <summary>人型モデルの寸法と、取り付け位置ごとの高さ（立った状態）。体の寸法は雨の実験と同じ。</summary>
    public static class BodyGeometry
    {
        public const float Radius = RainSimulator.BodyRadius, Height = RainSimulator.BodyHeight;

        /// <summary>取り付け位置の床からの高さ（m）。値は人型モデル（身長 1.75 m）に合わせた目安。</summary>
        public static float MountHeight(string mount)
        {
            switch (mount)
            {
                case "head": return Height;
                case "back": return 1.3f;
                case "waist": return 1.0f;
                case "wrist": return 0.9f;
                case "hand": return 0.8f;
                case "foot": return 0.05f;
                case "ground": return 0.3f;
                case "air": return 2.2f;
                case "fixed": return 2.4f;
                default: return 1.0f;
            }
        }
    }

    // ================= 照明 illuminate =================

    public struct IlluminationResult
    {
        /// <summary>光度（cd）。</summary>
        public float IntensityCd;
        /// <summary>足元〜前方の床の平均照度・最小・最大（lx、周りの明るさを含む）。</summary>
        public float AverageLx, MinLx, MaxLx;
        /// <summary>計測点のうち光の円錐に入った割合。</summary>
        public float CoveredRatio;
        public float PowerW;
    }

    public static class IlluminateModel
    {
        /// <summary>照度を測る床の範囲（人の前方、m）。</summary>
        public const float NearM = 0.5f, FarM = 2.5f, StepM = 0.25f;

        /// <summary>光の広がり（全角 beamDeg）の中で一様に光るとした光度 I = lm ÷ (2π(1−cos(θ/2)))。</summary>
        public static float Intensity(float lumens, float beamDeg)
        {
            float half = Mathf.Clamp(beamDeg, 1f, 359f) * 0.5f * Mathf.Deg2Rad;
            return Mathf.Max(0f, lumens) / (2f * Mathf.PI * (1f - Mathf.Cos(half)));
        }

        /// <summary>床（y = 0、上向き）の点の照度 E = I cosα ÷ d²。光の円錐の外は 0。</summary>
        public static float IlluminanceAt(Vector3 lamp, Vector3 axis, float intensityCd, float beamDeg, Vector3 point)
        {
            var d = point - lamp;
            float r = d.magnitude;
            if (r < 1e-4f) return 0f;
            float cosHalf = Mathf.Cos(Mathf.Clamp(beamDeg, 1f, 359f) * 0.5f * Mathf.Deg2Rad);
            if (Vector3.Dot(d / r, axis.normalized) < cosHalf - 1e-6f) return 0f;
            float cosInc = Mathf.Max(0f, -d.y / r);
            return intensityCd * cosInc / (r * r);
        }

        /// <summary>光軸：前方（+z）から下へ tiltDeg 傾けた向き。</summary>
        public static Vector3 Axis(float tiltDeg)
        {
            float t = Mathf.Clamp(tiltDeg, 0f, 90f) * Mathf.Deg2Rad;
            return new Vector3(0f, -Mathf.Sin(t), Mathf.Cos(t));
        }

        public static IlluminationResult Evaluate(float lumens, float beamDeg, float tiltDeg, float mountHeightM, float ambientLx = 0f)
        {
            var r = new IlluminationResult { IntensityCd = Intensity(lumens, beamDeg), MinLx = float.MaxValue };
            var lamp = new Vector3(0f, Mathf.Max(0.01f, mountHeightM), 0f);
            var axis = Axis(tiltDeg);
            int n = 0, covered = 0;
            float sum = 0f;
            for (float z = NearM; z <= FarM + 1e-4f; z += StepM)
            {
                float e = IlluminanceAt(lamp, axis, r.IntensityCd, beamDeg, new Vector3(0f, 0f, z));
                if (e > 0f) covered++;
                e += Mathf.Max(0f, ambientLx);
                sum += e; n++;
                r.MinLx = Mathf.Min(r.MinLx, e); r.MaxLx = Mathf.Max(r.MaxLx, e);
            }
            r.AverageLx = sum / n;
            r.CoveredRatio = (float)covered / n;
            r.PowerW = Mathf.Max(0f, lumens) / BlockCoefficients.V("ledEfficacy");
            return r;
        }
    }

    // ================= 加温 heat / 冷却 cool =================

    public struct ThermalResult
    {
        public float FinalC, DeltaC, SteadyC;
        /// <summary>目標の温度変化に届くまでの秒数（届かなければ −1）。</summary>
        public float TimeToTargetSec;
        /// <summary>加温（＋）・冷却（−）の熱の流れ（W）。</summary>
        public float HeatFlowW;
        public float PowerW;
    }

    /// <summary>
    /// 集中定数の熱モデル C dT/dt = Q − G (T − 外気温)。解析解 T(t) = T∞ + (T0 − T∞) e^(−t/τ)、T∞ = 外気温 + Q/G、τ = C/G。
    /// 加温：Q = 電力 × 加熱部の効率。冷却：Q = −電力 × 成績係数（排熱側は扱わない）。
    /// </summary>
    public static class ThermalModel
    {
        public static ThermalResult Evaluate(bool cooling, float powerW, float startC, float ambientC, float durationSec, float targetDeltaC,
            float? capacityJPerK = null, float? lossWPerK = null)
        {
            float cap = capacityJPerK ?? BlockCoefficients.V("thermalCapacity");
            float g = lossWPerK ?? BlockCoefficients.V("thermalLoss");
            float p = Mathf.Max(0f, powerW);
            float q = cooling ? -p * BlockCoefficients.V("peltierCop") : p * BlockCoefficients.V("heaterEfficiency");
            float tau = cap / g;
            float steady = ambientC + q / g;
            float final = steady + (startC - steady) * Mathf.Exp(-Mathf.Max(0f, durationSec) / tau);
            var r = new ThermalResult { FinalC = final, DeltaC = final - startC, SteadyC = steady, HeatFlowW = q, PowerW = p, TimeToTargetSec = -1f };
            float target = startC + (cooling ? -1f : 1f) * Mathf.Abs(targetDeltaC);
            float denom = startC - steady;
            if (Mathf.Abs(targetDeltaC) < 1e-6f) r.TimeToTargetSec = 0f;
            else if (Mathf.Abs(denom) > 1e-6f)
            {
                float ratio = (target - steady) / denom;
                if (ratio > 0f && ratio < 1f) r.TimeToTargetSec = -tau * Mathf.Log(ratio);
            }
            return r;
        }

        /// <summary>T(t)（画面の再生・グラフ用）。</summary>
        public static float TemperatureAt(float t, float startC, float steadyC, float tauSec) =>
            steadyC + (startC - steadyC) * Mathf.Exp(-Mathf.Max(0f, t) / tauSec);
    }

    // ================= 日よけ sun_shade =================

    public struct ShadeResult
    {
        /// <summary>直射日光を受ける体の表面（面積 × cos）のうち、日よけで遮った割合（0〜1）。</summary>
        public float ShadeRatio;
        /// <summary>日よけがないときに直射を受ける量（m²、面積 × cos の合計）。</summary>
        public float SunlitArea;
    }

    /// <summary>
    /// 平行光線と人型モデル（円柱）の影の判定。体の表面の点から太陽の方向へ光線を伸ばし、水平な円板（日よけ）に当たるかを見る（雨の実験と同じ方式）。
    /// 不透明度 opacity の分だけ遮る。
    /// </summary>
    public static class SunShadeModel
    {
        const int Rings = 12, Around = 24;

        /// <summary>太陽の方向（太陽へ向かう単位ベクトル）。方位 0° = 前方（+z）、90° = 右（+x）。</summary>
        public static Vector3 SunDirection(float elevationDeg, float azimuthDeg)
        {
            float e = elevationDeg * Mathf.Deg2Rad, a = azimuthDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(e) * Mathf.Sin(a), Mathf.Sin(e), Mathf.Cos(e) * Mathf.Cos(a));
        }

        public static ShadeResult Evaluate(float radius, float opacity, Vector3 shadeCenter, float sunElevationDeg, float sunAzimuthDeg)
        {
            var r = new ShadeResult();
            if (sunElevationDeg <= 0f) return r;
            var sun = SunDirection(sunElevationDeg, sunAzimuthDeg);
            float R = BodyGeometry.Radius, H = BodyGeometry.Height, o = Mathf.Clamp01(opacity);
            float lit = 0f, shaded = 0f;
            // 側面：高さ方向 Rings 段 × 周方向 Around 点（1点あたりの面積は同じ）
            float sideArea = 2f * Mathf.PI * R * H / (Rings * Around);
            for (int i = 0; i < Rings; i++)
            {
                float y = H * (i + 0.5f) / Rings;
                for (int j = 0; j < Around; j++)
                {
                    float phi = 2f * Mathf.PI * j / Around;
                    var n = new Vector3(Mathf.Sin(phi), 0f, Mathf.Cos(phi));
                    float w = Vector3.Dot(n, sun);
                    if (w <= 0f) continue;
                    Accumulate(new Vector3(R * n.x, y, R * n.z), w * sideArea, sun, shadeCenter, radius, o, ref lit, ref shaded);
                }
            }
            // 頭頂（上面の円）：同心円で面積に比例した点
            const int capRings = 4;
            for (int i = 0; i < capRings; i++)
            {
                float r0 = R * i / capRings, r1 = R * (i + 1) / capRings, rm = 0.5f * (r0 + r1);
                int count = Mathf.Max(1, 6 * (i + 1));
                float area = Mathf.PI * (r1 * r1 - r0 * r0) / count;
                for (int j = 0; j < count; j++)
                {
                    float phi = 2f * Mathf.PI * (j + 0.5f) / count;
                    Accumulate(new Vector3(rm * Mathf.Sin(phi), H, rm * Mathf.Cos(phi)), sun.y * area, sun, shadeCenter, radius, o, ref lit, ref shaded);
                }
            }
            r.SunlitArea = lit;
            r.ShadeRatio = lit > 0f ? shaded / lit : 0f;
            return r;
        }

        static void Accumulate(Vector3 p, float weight, Vector3 sun, Vector3 c, float radius, float opacity, ref float lit, ref float shaded)
        {
            lit += weight;
            if (p.y >= c.y || sun.y <= 0f) return;
            float t = (c.y - p.y) / sun.y;
            var q = p + sun * t;
            float dx = q.x - c.x, dz = q.z - c.z;
            if (dx * dx + dz * dz <= radius * radius) shaded += weight * opacity;
        }
    }

    // ================= 衝撃吸収 impact_cushion =================

    public struct ImpactResult
    {
        /// <summary>最大の衝撃（床から受ける力 ÷ 重さ、G）。</summary>
        public float PeakG;
        public float ImpactSpeed, MaxCompressionM, ContactMs;
        /// <summary>緩衝材の厚みを使い切った（つぶれ切った）。</summary>
        public bool BottomedOut;
    }

    /// <summary>
    /// 落下：高さ h から落ち、ばね（硬さ k）＋ダンパー（減衰比 ζ）の緩衝材で止まる。厚みを使い切ると、硬さが倍率分だけ上がる。
    /// 緩衝材がないときは、硬い床に当たる（硬さ hardSurfaceStiffness・減衰比 0.1）。0.02 秒より細かい刻みで数値積分する。
    /// </summary>
    public static class CushionModel
    {
        public static ImpactResult Drop(float massKg, float heightM, float thicknessM, float stiffnessNPerM, float? dampingRatio = null)
        {
            float g = RealityModel.Gravity, m = Mathf.Max(0.01f, massKg);
            float v0 = Mathf.Sqrt(2f * g * Mathf.Max(0f, heightM));
            bool none = thicknessM <= 0f || stiffnessNPerM <= 0f;
            float k = none ? BlockCoefficients.V("hardSurfaceStiffness") : stiffnessNPerM;
            float zeta = none ? 0.1f : dampingRatio ?? BlockCoefficients.V("cushionDamping");
            float thick = none ? float.PositiveInfinity : thicknessM;
            float kb = none ? 0f : k * BlockCoefficients.V("bottomOutFactor");
            float c = 2f * zeta * Mathf.Sqrt(k * m);
            float kMax = k + kb;
            double dt = 2.0 * System.Math.PI * System.Math.Sqrt(m / kMax) / 4000.0;
            double x = 0, v = v0, t = 0, peakF = 0;
            var r = new ImpactResult { ImpactSpeed = v0 };
            while (t < 2.0)
            {
                double f = k * x + c * v + (x > thick ? kb * (x - thick) : 0.0);
                if (f < 0) f = 0;
                if (f > peakF) peakF = f;
                v += (g - f / m) * dt;
                x += v * dt;
                t += dt;
                if (x > r.MaxCompressionM) r.MaxCompressionM = (float)x;
                if (v <= 0 && t > dt) break; // 止まった（跳ね返り始め）
            }
            r.PeakG = (float)(peakF / (m * g));
            r.ContactMs = (float)(t * 1000.0);
            r.BottomedOut = !none && r.MaxCompressionM > thick;
            return r;
        }
    }

    // ================= つかむ grip =================

    public struct GripResult
    {
        public bool CanGrasp, Dropped;
        /// <summary>落とした時刻の番号（落とさなければ −1）。</summary>
        public int DropIndex;
        /// <summary>保持の余裕（摩擦で支えられる力 ÷ 必要な力、1 未満で落とす）。</summary>
        public float HoldMargin;
        public float RequiredForceN;
    }

    /// <summary>2面ではさむ：2 μ F ≥ m (g + a)（a は上向きの揺れの加速度）なら落とさない。開き幅より大きい物はつかめない。</summary>
    public static class GripModel
    {
        public static GripResult Evaluate(float gripForceN, float openingM, float objectKg, float objectSizeM, IList<float> verticalAccel = null)
        {
            float mu = BlockCoefficients.V("gripFriction"), g = RealityModel.Gravity, m = Mathf.Max(0f, objectKg);
            float hold = 2f * mu * Mathf.Max(0f, gripForceN);
            float aMax = 0f;
            var r = new GripResult { CanGrasp = objectSizeM <= openingM, DropIndex = -1 };
            if (verticalAccel != null)
                for (int i = 0; i < verticalAccel.Count; i++)
                {
                    aMax = Mathf.Max(aMax, verticalAccel[i]);
                    if (r.DropIndex < 0 && hold < m * (g + verticalAccel[i])) r.DropIndex = i;
                }
            float need = m * (g + aMax);
            r.RequiredForceN = need / (2f * mu);
            r.HoldMargin = need > 0f ? hold / need : float.PositiveInfinity;
            if (!r.CanGrasp) r.Dropped = true;
            else r.Dropped = verticalAccel != null ? r.DropIndex >= 0 : hold < m * g;
            return r;
        }
    }

    // ================= 持ち上げ lift_load =================

    public struct LiftResult
    {
        public bool CanLift;
        public string Reason;
        public float TimeSec, PowerW, EnergyWh;
    }

    /// <summary>一定の速さで持ち上げる：時間 = 高さ ÷ 速さ、電力 = m g v ÷ 効率、エネルギー = m g h ÷ 効率。</summary>
    public static class LiftModel
    {
        public static LiftResult Evaluate(float maxLoadKg, float liftHeightM, float speedMps, float loadKg, float targetHeightM)
        {
            var r = new LiftResult { CanLift = true, Reason = "" };
            if (loadKg > maxLoadKg) { r.CanLift = false; r.Reason = $"荷物 {loadKg:0.0} kg が持ち上げられる重さ {maxLoadKg:0.0} kg を超える"; }
            else if (targetHeightM > liftHeightM + 1e-4f) { r.CanLift = false; r.Reason = $"目標の高さ {targetHeightM:0.00} m が持ち上げられる高さ {liftHeightM:0.00} m を超える"; }
            if (!r.CanLift || speedMps <= 0f) { r.CanLift = false; return r; }
            float g = RealityModel.Gravity, eta = BlockCoefficients.V("liftEfficiency");
            r.TimeSec = targetHeightM / speedMps;
            r.PowerW = loadKg * g * speedMps / eta;
            r.EnergyWh = loadKg * g * targetHeightM / eta / 3600f;
            return r;
        }
    }

    // ================= 検知 detect =================

    public struct DetectResult
    {
        public int Detected, Total;
        public float Rate => Total > 0 ? (float)Detected / Total : 0f;
        public float PowerW;
    }

    /// <summary>床の上の2次元の幾何判定：距離 ≤ 検知距離、向きとの角度 ≤ 視野角/2、間に障害物（円）がない。</summary>
    public static class DetectModel
    {
        /// <summary>向き 0° = 前方（+z）、90° = 右（+x）。obstacles は (x, z, 半径)。</summary>
        public static bool CanSee(Vector2 sensor, float headingDeg, float rangeM, float fovDeg, Vector2 target, IList<Vector3> obstacles = null)
        {
            var d = target - sensor;
            float dist = d.magnitude;
            if (dist > rangeM) return false;
            if (dist > 1e-4f && fovDeg < 360f)
            {
                float h = headingDeg * Mathf.Deg2Rad;
                var fwd = new Vector2(Mathf.Sin(h), Mathf.Cos(h));
                float ang = Mathf.Acos(Mathf.Clamp(Vector2.Dot(fwd, d / dist), -1f, 1f)) * Mathf.Rad2Deg;
                if (ang > fovDeg * 0.5f + 1e-4f) return false;
            }
            if (obstacles != null)
                foreach (var o in obstacles)
                    if (SegmentHitsCircle(sensor, target, new Vector2(o.x, o.y), o.z)) return false;
            return true;
        }

        public static DetectResult Evaluate(Vector2 sensor, float headingDeg, float rangeM, float fovDeg, IList<Vector2> targets, IList<Vector3> obstacles = null)
        {
            var r = new DetectResult { Total = targets.Count, PowerW = BlockCoefficients.V("detectPowerW") };
            foreach (var t in targets) if (CanSee(sensor, headingDeg, rangeM, fovDeg, t, obstacles)) r.Detected++;
            return r;
        }

        /// <summary>線分 a→b が円（中心 c・半径 rad）を通るか。端点が円の中にある場合も通るとみなす。</summary>
        public static bool SegmentHitsCircle(Vector2 a, Vector2 b, Vector2 c, float rad)
        {
            var ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 0f ? Mathf.Clamp01(Vector2.Dot(c - a, ab) / len2) : 0f;
            return (a + ab * t - c).sqrMagnitude <= rad * rad;
        }
    }

    // ================= 通知 alert =================

    public struct AlertResult
    {
        /// <summary>音・光それぞれで気づいてもらえる距離と、その大きい方（m）。</summary>
        public float SoundRangeM, LightRangeM, RangeM;
        public float PowerW;
    }

    /// <summary>
    /// 音：1 m で L dB の音が距離の逆二乗で弱まる（L − 20 log10 r）。周りの騒音＋差 以上なら気づく。
    /// 光：全方向に光る点の光（光度 = lm ÷ 4π）。照度 I ÷ r² が「気づく照度」以上なら気づく。気づく照度は周りの明るさで夜〜昼の値の間をとる。
    /// 点滅（flashHz）は消費電力だけに効く（点灯は半分の時間）。点滅による気づきやすさの違いは扱わない。
    /// </summary>
    public static class AlertModel
    {
        public const float MaxRangeM = 5000f;

        public static float SoundRangeM(float loudnessDbAt1m, float? backgroundDb = null)
        {
            if (loudnessDbAt1m <= 0f) return 0f;
            float need = (backgroundDb ?? BlockCoefficients.V("backgroundNoiseDb")) + BlockCoefficients.V("audibleMarginDb");
            return Mathf.Min(MaxRangeM, Mathf.Pow(10f, (loudnessDbAt1m - need) / 20f));
        }

        /// <summary>気づく照度：周りが 1 lx 以下なら夜の値、1000 lx 以上なら昼の値、その間は対数で補間。</summary>
        public static float NoticeLux(float ambientLx)
        {
            float t = Mathf.Clamp01(Mathf.Log10(Mathf.Max(1f, ambientLx)) / 3f);
            float a = Mathf.Log10(BlockCoefficients.V("lightNoticeNightLx")), b = Mathf.Log10(BlockCoefficients.V("lightNoticeDayLx"));
            return Mathf.Pow(10f, Mathf.Lerp(a, b, t));
        }

        public static float LightRangeM(float lumens, float ambientLx)
        {
            if (lumens <= 0f) return 0f;
            float intensity = lumens / (4f * Mathf.PI);
            return Mathf.Min(MaxRangeM, Mathf.Sqrt(intensity / NoticeLux(ambientLx)));
        }

        public static AlertResult Evaluate(float loudnessDbAt1m, float lightLumens, float flashHz, float ambientLx, float? backgroundDb = null)
        {
            var r = new AlertResult { SoundRangeM = SoundRangeM(loudnessDbAt1m, backgroundDb), LightRangeM = LightRangeM(lightLumens, ambientLx) };
            r.RangeM = Mathf.Max(r.SoundRangeM, r.LightRangeM);
            // 音の電力：音響出力 = 4π × 10^-12 × 10^(L/10)（1 m の球面）÷ 変換効率
            float acoustic = loudnessDbAt1m > 0f ? 4f * Mathf.PI * 1e-12f * Mathf.Pow(10f, loudnessDbAt1m / 10f) : 0f;
            float light = Mathf.Max(0f, lightLumens) / BlockCoefficients.V("ledEfficacy") * (flashHz > 0f ? 0.5f : 1f);
            r.PowerW = acoustic / BlockCoefficients.V("speakerEfficiency") + light;
            return r;
        }
    }
}
