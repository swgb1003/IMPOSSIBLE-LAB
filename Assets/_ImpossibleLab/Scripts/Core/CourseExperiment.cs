using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ImpossibleLab.Core
{
    /// <summary>再生用の1コマ（実験場ローカル座標、y は高さ）。</summary>
    public struct CourseFrame
    {
        public float Time;
        public Vector3 Device;
        public float YawDeg;
        public Vector3 Leader;
        public bool Collision;
    }

    /// <summary>実験場の配置（可視化と計算で同じものを使う）。</summary>
    public sealed class CourseLayout
    {
        public readonly List<Vector2> Route = new List<Vector2>();
        public readonly List<Vector3> Obstacles = new List<Vector3>(); // x, z, 半径
        public Vector2 Start, Goal;
        public float CruiseHeight;
    }

    /// <summary>計算済みの実験（結果＋再生用の軌跡）。</summary>
    public sealed class CourseRun
    {
        public RainExperimentResult Result;
        public CourseLayout Layout;
        public readonly List<CourseFrame> Frames = new List<CourseFrame>();
        public float Duration => Frames.Count == 0 ? 0f : Frames[Frames.Count - 1].Time;
    }

    /// <summary>
    /// 運搬（ObstacleCourse）と移動（FlightDeck）の簡易シミュレーションの入口と定数（仕様7.3, 9.1, 9.3）。
    /// 計算本体は ScenarioRunner（WalkRouteScenario + GroundFollowerModel、FlyToTargetScenario + FlightModel）。
    /// 固定時間刻み・シード固定で再現可能。ゲーム的な運動モデルであり、現実の走行・飛行性能の予測ではない。
    /// 計算式と仮定は docs/experiments.md に記載。
    /// </summary>
    public static class CourseSimulator
    {
        public const float Dt = 0.02f, FrameInterval = 0.05f;

        public static CourseRun Run(InventionDna dna, RainExperimentConfig config, string experimentId = null, string startTime = null)
        {
            var cfg = config.Clamped();
            cfg.Environment = LabEnvironments.ForCategory(dna.Category);
            if (cfg.Environment == LabEnvironments.RainLab) throw new ArgumentException("RainLab は RainSimulator で実行してください");
            var result = new RainExperimentResult
            {
                ExperimentId = experimentId ?? "exp-" + cfg.Seed, InventionId = dna.Id, Revision = dna.Revision,
                Environment = cfg.Environment, Config = cfg, StartTime = startTime ?? "",
            };
            // 実験は「実験計画」に変換して ScenarioRunner で実行する（2週目〜）
            return ScenarioRunner.Run(dna, ExperimentPlan.FromConfig(cfg), result);
        }

        // ================= 運搬：追従運搬ロボット =================
        public const float LeaderSpeed = 1.0f, RobotRadius = 0.3f, ObstacleRadius = 0.18f;
        public const float CarrierTimeout = 15f, ArrivalLimitSec = 3f, SensorLatency = 0.3f;
        // REALITY の仮定（docs/experiments.md）
        public const float RealityCarrierSpeedCap = 1.6f, RobotMassKg = 15f, RollingResistance = 0.02f, CarrierBasePowerW = 20f, CarrierDriveEfficiency = 0.7f;

        /// <summary>
        /// 障害物は基本ルートの上に置き、案内役（人）はそれを迂回して歩く。後ろを追うロボットは近道しようとして
        /// 障害物に寄るため、早く検知して曲がらないと当たる。
        /// </summary>
        public static CourseLayout CarrierLayout(RainExperimentConfig cfg) => CarrierLayout(cfg.Seed, cfg.ObstacleCount);

        public static CourseLayout CarrierLayout(int seed, int obstacleCount)
        {
            var L = new CourseLayout();
            // パネル越しに見えるよう、プレイヤー位置（z = -4.5）から 6〜9.5 m 先に置く
            var basePath = new List<Vector2> { new Vector2(-4.0f, 2.0f), new Vector2(-2.6f, 4.8f), new Vector2(-0.6f, 2.5f), new Vector2(1.4f, 5.0f), new Vector2(3.0f, 2.7f), new Vector2(4.2f, 4.4f) };
            var rng = new XorShift(seed);
            float len = PathLength(basePath);
            int n = obstacleCount;
            var along = new List<float>(); var side = new List<float>();
            for (int i = 0; i < n; i++)
            {
                float s = Mathf.Clamp(len * (i + 0.5f) / n + (rng.NextFloat() - 0.5f) * 0.6f, 1.2f, len - 1.2f);
                PointAt(basePath, s, out var p, out var tangent);
                var normal = new Vector2(-tangent.y, tangent.x);
                float off = (rng.NextFloat() - 0.5f) * 0.2f;
                var c = p + normal * off;
                L.Obstacles.Add(new Vector3(c.x, c.y, ObstacleRadius));
                along.Add(s); side.Add(off >= 0f ? -1f : 1f); // 障害物の反対側へ迂回
            }
            // 案内役のルート：0.1 m ごとに、障害物の前後 ±1 m で横へ最大 0.55 m 迂回する
            for (float s = 0f; s <= len + 1e-3f; s += 0.1f)
            {
                PointAt(basePath, Mathf.Min(s, len), out var p, out var tangent);
                var normal = new Vector2(-tangent.y, tangent.x);
                float detour = 0f;
                for (int i = 0; i < n; i++)
                {
                    float k = Mathf.Abs(s - along[i]);
                    if (k < 1f) { float c = Mathf.Cos(0.5f * Mathf.PI * k); detour += side[i] * 0.55f * c * c; }
                }
                L.Route.Add(p + normal * detour);
            }
            L.Start = L.Route[0]; L.Goal = L.Route[L.Route.Count - 1];
            return L;
        }

        // ================= 移動：空飛ぶ靴 =================
        public const float FlightLane = 3.6f, FlightTimeout = 25f, StabilityPassScore = 60f, LandingTolerance = 0.5f;
        // REALITY の仮定（docs/experiments.md）
        public const float PilotMassKg = 60f, ShoesMassKg = 2f, ShoeBatteryKg = 1f;

        public static CourseLayout FlightLayout(RainExperimentConfig cfg, InventionDna dna) => FlightLayout(cfg.TargetDistance, dna);

        public static CourseLayout FlightLayout(float targetDistance, InventionDna dna)
        {
            var L = new CourseLayout { Start = new Vector2(-targetDistance * 0.5f, FlightLane), Goal = new Vector2(targetDistance * 0.5f, FlightLane) };
            L.CruiseHeight = Mathf.Min(dna.GetParameter("hover", "maxHeight", 0f), 1.2f);
            L.Route.Add(L.Start); L.Route.Add(L.Goal);
            return L;
        }

        /// <summary>REALITY：人を浮かせるのに必要な電力（運動量理論）と、靴の電池で浮いていられる時間。</summary>
        public struct FlightPower { public float RequiredKW, AvailableSec, DiscAreaM2, LiftN; }

        public static FlightPower? EstimateFlightPower(InventionDna dna)
        {
            var m = RealityModel.Default;
            if (m == null) return null;
            float area = 0f;
            foreach (var part in dna.Parts)
                if (part.Type == "thruster") { float rad = 0.5f * Mathf.Max(part.Scale.x, part.Scale.z); area += Mathf.PI * rad * rad; }
            area = Mathf.Max(area, 1e-4f);
            float rho = m.Get("airDensity").Value;
            float lift = (PilotMassKg + ShoesMassKg + ShoeBatteryKg) * RealityModel.Gravity;
            float ideal = Mathf.Pow(lift, 1.5f) / Mathf.Sqrt(2f * rho * area);
            float power = ideal / (m.Get("figureOfMerit").Value * m.Get("driveEfficiency").Value);
            float wh = ShoeBatteryKg * m.Get("batteryEnergyDensity").Value * m.Get("usableFraction").Value;
            return new FlightPower { RequiredKW = power / 1000f, AvailableSec = wh / power * 3600f, DiscAreaM2 = area, LiftN = lift };
        }

        // ================= 共通 =================
        public static float PathLength(List<Vector2> pts)
        {
            float l = 0f;
            for (int i = 1; i < pts.Count; i++) l += (pts[i] - pts[i - 1]).magnitude;
            return l;
        }

        public static void PointAt(List<Vector2> pts, float s, out Vector2 point, out Vector2 tangent)
        {
            for (int i = 1; i < pts.Count; i++)
            {
                var seg = pts[i] - pts[i - 1]; float l = seg.magnitude;
                if (s <= l || i == pts.Count - 1)
                {
                    tangent = l > 1e-5f ? seg / l : Vector2.right;
                    point = pts[i - 1] + tangent * Mathf.Clamp(s, 0f, l);
                    return;
                }
                s -= l;
            }
            point = pts.Count > 0 ? pts[0] : Vector2.zero; tangent = Vector2.right;
        }

        /// <summary>計測値に基づく改善案（単純な規則。AIではない）。</summary>
        public static List<string> Hints(RainExperimentResult r)
        {
            var h = new List<string>();
            float V(string k) => r.Metric(k)?.Value ?? 0f;
            if (r.Environment == LabEnvironments.ObstacleCourse)
            {
                if (r.Limits.Exists(s => s.Contains("運べない"))) h.Add("積荷が重すぎます →「重い荷物も運んで」");
                if (V("collisionCount") > 0) h.Add($"障害物に {V("collisionCount"):0} 回ぶつかりました →「障害物をよけて」");
                if (V("arrivalDelay") > ArrivalLimitSec) h.Add($"到着が {V("arrivalDelay"):0.0} 秒遅れました →「もっと速くついてきて」");
                if (V("followError") > 0.6f) h.Add("追従距離のぶれが大きい →「もっと近くでついてきて」");
            }
            else
            {
                if (r.Limits.Exists(s => s.Contains("浮上できない"))) h.Add("浮上できません →「もっと高く飛んで」");
                if (r.Limits.Exists(s => s.Contains("電池切れ"))) h.Add("REALITY では人を浮かせる電力が足りません（現実化の最大の課題）");
                if (V("stabilityScore") < StabilityPassScore && V("maxDeviation") > 0.1f) h.Add($"横に {V("maxDeviation"):0.00} m 揺れました →「風で揺れないようにして」");
                if (V("completionTime") > 12f) h.Add("目標まで時間がかかります →「もっと速く飛んで」");
            }
            if (h.Count == 0) h.Add(r.Environment == LabEnvironments.ObstacleCourse ? "大きな問題は見つかりませんでした。障害物や積荷を増やして試してください" : "大きな問題は見つかりませんでした。風速や距離を上げて試してください");
            return h;
        }
    }
}
