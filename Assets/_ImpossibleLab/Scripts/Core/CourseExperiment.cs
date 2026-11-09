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
    /// 運搬（ObstacleCourse）と移動（FlightDeck）の簡易シミュレーション（仕様7.3, 9.1, 9.3）。
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
            var result = new RainExperimentResult
            {
                ExperimentId = experimentId ?? "exp-" + cfg.Seed, InventionId = dna.Id, Revision = dna.Revision,
                Environment = cfg.Environment, Config = cfg, StartTime = startTime ?? "",
            };
            var run = new CourseRun { Result = result };
            if (cfg.Environment == LabEnvironments.ObstacleCourse) Carrier(dna, cfg, run);
            else if (cfg.Environment == LabEnvironments.FlightDeck) Flight(dna, cfg, run);
            else throw new ArgumentException("RainLab は RainSimulator で実行してください");
            return run;
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
        public static CourseLayout CarrierLayout(RainExperimentConfig cfg)
        {
            var L = new CourseLayout();
            // パネル越しに見えるよう、プレイヤー位置（z = -4.5）から 6〜9.5 m 先に置く
            var basePath = new List<Vector2> { new Vector2(-4.0f, 2.0f), new Vector2(-2.6f, 4.8f), new Vector2(-0.6f, 2.5f), new Vector2(1.4f, 5.0f), new Vector2(3.0f, 2.7f), new Vector2(4.2f, 4.4f) };
            var rng = new XorShift(cfg.Seed);
            float len = PathLength(basePath);
            int n = cfg.ObstacleCount;
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

        static void Carrier(InventionDna dna, RainExperimentConfig cfg, CourseRun run)
        {
            var r = run.Result;
            bool reality = cfg.Mode == LabModes.Reality;
            var L = CarrierLayout(cfg);
            run.Layout = L;
            bool canFollow = dna.FindBehavior("follow_user") != null;
            float speed = Mathf.Min(dna.GetParameter("follow_user", "speed", 0f), dna.Constraints.MaxSpeed);
            float followDist = dna.GetParameter("follow_user", "followDistance", 1.2f);
            bool avoid = dna.FindBehavior("avoid_obstacle") != null;
            float range = dna.GetParameter("avoid_obstacle", "detectionRange", 0f);
            bool hasCarry = dna.FindBehavior("carry_load") != null;
            float capacity = dna.GetParameter("carry_load", "capacity", 0f);
            float payload = cfg.PayloadKg;
            float accel = 3f;

            r.Assumptions.Add($"案内役は決まったルート（{PathLength(L.Route):0.0} m）を {LeaderSpeed:0.0} m/s で歩く。ロボットは案内役へ直進し、追従距離まで近づくと止まる");
            r.Assumptions.Add($"ロボットは半径 {RobotRadius:0.0} m の円、障害物は半径 {ObstacleRadius:0.00} m の円柱。接触1回で衝突1回と数え、0.4秒止まる。旋回は毎秒 {(reality ? 90 : 150)}° まで。センサーの反応遅れ {SensorLatency} 秒の間に進む分だけ検知距離が短くなる（仮定）");
            if (reality)
            {
                float cap = RealityCarrierSpeedCap * (1f - 0.25f * payload / RainExperimentConfig.MaxPayload);
                if (speed > cap) r.Limits.Add($"追従速度 {speed:0.00} m/s → {cap:0.00} m/s に制限（歩道走行ロボットの速度上限 {RealityCarrierSpeedCap} m/s と積荷による低下を仮定）");
                speed = Mathf.Min(speed, cap);
                accel = 1.2f * (1f - 0.5f * payload / RainExperimentConfig.MaxPayload);
                r.Assumptions.Add($"REALITY：加速度 {accel:0.00} m/s²（積荷で低下）、車体 {RobotMassKg:0} kg、転がり抵抗係数 {RollingResistance}、待機電力 {CarrierBasePowerW:0} W、駆動効率 {CarrierDriveEfficiency}（いずれも仮定）");
            }
            else r.Assumptions.Add("DREAM：加速度 3 m/s²、電力・重量の制約なし");
            r.Assumptions.Add("移動ロボットの実際の制御・センサー性能の予測ではない");

            bool overload = !hasCarry || capacity < payload;
            if (!canFollow) r.Limits.Add("「ユーザー追従」の機能がないため、ロボットはついて来られない");
            if (overload) r.Limits.Add(hasCarry ? $"積載量 {capacity:0.0} kg < 積荷 {payload:0.0} kg：運べない" : "「積載」の機能がないため、積荷を運べない");
            if (!avoid) r.Assumptions.Add("「障害物回避」の機能がないため、障害物を避けずに進む");

            var p = L.Start - (L.Route[Mathf.Min(5, L.Route.Count - 1)] - L.Start).normalized * followDist;
            var v = Vector2.zero;
            var heading = (L.Route[Mathf.Min(5, L.Route.Count - 1)] - L.Start).normalized;
            float speedNow = 0f, turnRate = reality ? 90f : 150f; // 度/秒
            float leaderS = 0f, len = PathLength(L.Route), t = 0f, stall = 0f, nextFrame = 0f;
            float leaderDone = len / LeaderSpeed;
            float sumGap = 0f, sumErr = 0f, maxGap = 0f, energyJ = 0f; int samples = 0, collisions = 0;
            float? arrival = null;
            var contact = new bool[L.Obstacles.Count];
            bool moving = canFollow && !overload;
            float yaw = 0f;
            while (true)
            {
                leaderS = Mathf.Min(len, leaderS + LeaderSpeed * Dt);
                PointAt(L.Route, leaderS, out var leader, out _);
                bool collidedNow = false;
                if (moving)
                {
                    var to = leader - p; float d = to.magnitude;
                    var dir = d > 1e-4f ? to / d : Vector2.zero;
                    float want = d > followDist ? speed * Mathf.Clamp01((d - followDist) / 0.5f) : 0f;
                    // センサーの反応遅れの間に進む距離だけ、実際に使える検知距離は短くなる
                    float effRange = range - speedNow * SensorLatency;
                    if (avoid && want > 0f && effRange > 0.01f)
                    {
                        var steer = dir;
                        foreach (var o in L.Obstacles)
                        {
                            var away = p - new Vector2(o.x, o.y); float dist = away.magnitude;
                            float clear = dist - o.z - RobotRadius;
                            if (clear >= effRange || dist < 1e-4f) continue;
                            away /= dist;
                            float w = 1f - Mathf.Max(0f, clear) / effRange;
                            var tangent = new Vector2(-away.y, away.x);
                            if (Vector2.Dot(tangent, dir) < 0f) tangent = -tangent;
                            steer += (away * 0.6f + tangent * 1.2f) * w;
                        }
                        if (steer.sqrMagnitude > 1e-6f) dir = steer.normalized;
                    }
                    // 向きは最大旋回速度でしか変えられない（検知が遅いと曲がりきれずに当たる）
                    float turn = Vector2.SignedAngle(heading, dir);
                    float maxTurn = turnRate * Dt;
                    heading = Rotate(heading, Mathf.Clamp(turn, -maxTurn, maxTurn));
                    float targetSpeed = stall > 0f ? 0f : want * Mathf.Clamp01(Mathf.Cos(turn * Mathf.Deg2Rad));
                    speedNow = Mathf.MoveTowards(speedNow, targetSpeed, accel * Dt);
                    if (stall > 0f) stall -= Dt;
                    v = heading * speedNow;
                    // 接触中は障害物へ押し込む向きの成分を除き、縁に沿って進む（バンパーで滑る）
                    foreach (var o in L.Obstacles)
                    {
                        var away = p - new Vector2(o.x, o.y); float dist = away.magnitude;
                        if (dist > o.z + RobotRadius + 0.01f || dist < 1e-4f) continue;
                        var n = away / dist; float into = Vector2.Dot(v, n);
                        if (into < 0f) v -= into * n;
                    }
                    p += v * Dt;
                    for (int i = 0; i < L.Obstacles.Count; i++)
                    {
                        var o = L.Obstacles[i]; var c = new Vector2(o.x, o.y);
                        var away = p - c; float dist = away.magnitude, min = o.z + RobotRadius;
                        if (dist < min)
                        {
                            if (!contact[i]) { collisions++; collidedNow = true; stall = 0.4f; speedNow *= 0.3f; }
                            contact[i] = true;
                            p = c + (dist > 1e-4f ? away / dist : Vector2.right) * min;
                        }
                        else if (dist > min + 0.05f) contact[i] = false;
                    }
                    if (reality)
                    {
                        float mass = RobotMassKg + payload;
                        energyJ += (mass * 9.80665f * RollingResistance * v.magnitude / CarrierDriveEfficiency + CarrierBasePowerW) * Dt;
                    }
                    if (v.sqrMagnitude > 1e-4f) yaw = Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg;
                }
                t += Dt;
                float gap = (leader - p).magnitude;
                if (t <= leaderDone) { sumGap += gap; sumErr += Mathf.Abs(gap - followDist); samples++; maxGap = Mathf.Max(maxGap, gap); }
                if (t >= nextFrame || collidedNow)
                {
                    run.Frames.Add(new CourseFrame { Time = t, Device = new Vector3(p.x, 0f, p.y), YawDeg = yaw, Leader = new Vector3(leader.x, 0f, leader.y), Collision = collidedNow });
                    if (t >= nextFrame) nextFrame += FrameInterval;
                }
                if (t >= leaderDone && arrival == null && gap <= followDist + 0.3f) arrival = t - leaderDone;
                if (t >= leaderDone && (arrival != null || t - leaderDone >= CarrierTimeout)) break;
            }

            float delay = arrival ?? CarrierTimeout;
            float avgGap = samples > 0 ? sumGap / samples : 0f, avgErr = samples > 0 ? sumErr / samples : 0f;
            r.Metrics.Add(M("collisionCount", "衝突回数", collisions, "0", true, " 回"));
            r.Metrics.Add(M("arrivalDelay", "到着の遅れ", delay, "0.0", true, " 秒"));
            r.Metrics.Add(M("followError", "追従距離の誤差", avgErr, "0.00", true, " m"));
            r.Metrics.Add(M("averageFollowDistance", "平均追従距離", avgGap, "0.00", true, " m", primary: false));
            r.Metrics.Add(M("maxGap", "最大の離れ", maxGap, "0.00", true, " m", primary: false));
            if (reality)
            {
                r.EnergyWh = energyJ / 3600f;
                r.Metrics.Add(M("energyWh", "消費エネルギー", r.EnergyWh.Value, "0.00", true, " Wh"));
            }
            if (arrival == null && moving) r.Limits.Add($"案内役の到着から {CarrierTimeout:0} 秒以内に追いつけなかった");
            r.Passed = moving && collisions == 0 && delay <= ArrivalLimitSec;
            r.Criteria = $"衝突0回・到着の遅れ{ArrivalLimitSec:0}秒以内・積荷を運べること";
        }

        static Vector2 Rotate(Vector2 v, float deg)
        {
            float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), sn = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * sn, v.x * sn + v.y * c);
        }

        // ================= 移動：空飛ぶ靴 =================
        public const float FlightLane = 3.6f, FlightTimeout = 25f, StabilityPassScore = 60f, LandingTolerance = 0.5f;
        // REALITY の仮定（docs/experiments.md）
        public const float PilotMassKg = 60f, ShoesMassKg = 2f, ShoeBatteryKg = 1f;

        public static CourseLayout FlightLayout(RainExperimentConfig cfg, InventionDna dna)
        {
            var L = new CourseLayout { Start = new Vector2(-cfg.TargetDistance * 0.5f, FlightLane), Goal = new Vector2(cfg.TargetDistance * 0.5f, FlightLane) };
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

        static void Flight(InventionDna dna, RainExperimentConfig cfg, CourseRun run)
        {
            var r = run.Result;
            bool reality = cfg.Mode == LabModes.Reality;
            var L = FlightLayout(cfg, dna);
            run.Layout = L;
            float lift = dna.GetParameter("hover", "lift", 0f);
            float thrust = dna.GetParameter("move_forward", "thrust", 0f);
            float speedLimit = Mathf.Min(dna.GetParameter("move_forward", "speedLimit", 0f), dna.Constraints.MaxSpeed);
            float stab = dna.GetParameter("stabilize", "stabilizationStrength", 0f);
            bool canHover = dna.FindBehavior("hover") != null && lift >= 0.15f && L.CruiseHeight >= 0.2f;
            bool canMove = dna.FindBehavior("move_forward") != null && thrust > 0.01f;
            float vmax = Mathf.Min(speedLimit, 0.5f + 2.5f * thrust);
            float climb = 0.3f + 1.2f * lift;
            float available = float.PositiveInfinity;

            r.Assumptions.Add("観察モード：人型モデルが靴で飛ぶのを外から見る（酔い対策のため視点は動かさない）");
            r.Assumptions.Add($"離陸 → 高度 {L.CruiseHeight:0.0} m へ上昇（{climb:0.0} m/s）→ 最大 {vmax:0.0} m/s で目標へ → 着地。風は横方向の揺れとして加える");
            r.Assumptions.Add($"揺れ：横風と突風（シード固定）に対し、姿勢安定の強さ s で戻す力が (1 + 4s) 倍になると仮定（調整用の仮定）");
            if (!canHover) r.Limits.Add(dna.FindBehavior("hover") == null ? "「浮遊」の機能がないため浮上できない" : "揚力・最大高度が小さく浮上できない（揚力0.15以上・最大高度0.2 m以上が必要）");
            if (canHover && !canMove) r.Limits.Add("「推進」の機能がない（または推力0）ため前へ進めない");
            if (reality)
            {
                var fp = EstimateFlightPower(dna);
                if (fp == null) r.Limits.Add(RealityModel.DefaultError + "。必要電力を計算できません");
                else
                {
                    var f = fp.Value;
                    available = f.AvailableSec;
                    r.Assumptions.Add($"REALITY：体重 {PilotMassKg:0} kg＋靴 {ShoesMassKg:0} kg＋電池 {ShoeBatteryKg:0} kg を推進ユニットの円板（合計 {f.DiscAreaM2:0.000} m²）で浮かせる");
                    r.Assumptions.Add($"必要電力 約 {f.RequiredKW:0} kW（運動量理論 P = T^1.5/√(2ρA) を効率で割る）。電池で浮いていられる時間 約 {f.AvailableSec:0.0} 秒");
                    r.Metrics.Add(M("requiredPowerKW", "必要電力", f.RequiredKW, "0", true, " kW", primary: false));
                    r.Metrics.Add(M("availableFlightSec", "電池で飛べる時間", f.AvailableSec, "0.0", false, " 秒", primary: false));
                }
            }
            else r.Assumptions.Add("DREAM：電力・重量の制約なし（空想シミュレーション）");
            r.Assumptions.Add("ゲーム的な飛行モデル。現実の飛行性能・安全性の予測ではない");

            var rng = new XorShift(cfg.Seed);
            var wind = ModeRules.WindDir(cfg) * cfg.WindSpeed;
            float windAlong = wind.x, windCross = wind.y;
            float phase = rng.NextFloat() * 6.283f, gustState = 0f;
            float x = L.Start.x, y = 0f, z = 0f, vx = 0f, vz = 0f, t = 0f, nextFrame = 0f;
            float sumSq = 0f, maxDev = 0f, maxAlt = 0f; int samples = 0;
            bool batteryOut = false, landed = false, airborne = false;
            const float kWind = 0.15f, kHold = 1.0f, damp = 1.2f, accel = 1.5f, descend = 0.6f;
            while (t < FlightTimeout)
            {
                gustState = Mathf.Lerp(gustState, rng.NextFloat() * 2f - 1f, 0.05f);
                float gust = 0.7f + 0.3f * Mathf.Sin(6.283f * t / 3.1f + phase) + 0.4f * gustState;
                bool powered = canHover && !batteryOut;
                if (reality && airborne && t > available && !batteryOut) { batteryOut = true; powered = false; r.Limits.Add($"{available:0.0} 秒で電池切れ、途中で着地（推定）"); }
                float dx = L.Goal.x - x;
                if (powered)
                {
                    bool nearGoal = Mathf.Abs(dx) < 0.15f && Mathf.Abs(vx) < 0.2f;
                    if (!nearGoal) { y = Mathf.MoveTowards(y, L.CruiseHeight, climb * Dt); airborne |= y > 0.05f; }
                    else y = Mathf.MoveTowards(y, 0f, descend * Dt);
                    float targetVx = canMove && y > 0.5f * L.CruiseHeight && !nearGoal
                        ? Mathf.Sign(dx) * Mathf.Min(vmax, Mathf.Sqrt(2f * accel * Mathf.Abs(dx))) : 0f;
                    vx = Mathf.MoveTowards(vx, targetVx, accel * Dt);
                }
                else
                {
                    y = Mathf.MoveTowards(y, 0f, 1.5f * Dt);
                    vx = Mathf.MoveTowards(vx, 0f, 2f * accel * Dt);
                }
                if (y > 0.02f)
                {
                    float az = kWind * windCross * gust - kHold * (1f + 4f * stab) * z - damp * (1f + stab) * vz;
                    vz += az * Dt; z += vz * Dt;
                    x += (vx + 0.15f * windAlong * gust) * Dt;
                }
                else { vz = 0f; if (airborne) { landed = true; } }
                t += Dt;
                if (airborne) { sumSq += z * z; samples++; maxDev = Mathf.Max(maxDev, Mathf.Abs(z)); maxAlt = Mathf.Max(maxAlt, y); }
                if (t >= nextFrame)
                {
                    float roll = Mathf.Clamp(vz * 25f, -25f, 25f);
                    run.Frames.Add(new CourseFrame { Time = t, Device = new Vector3(x, y, L.Start.y + z), YawDeg = roll });
                    nextFrame += FrameInterval;
                }
                if (landed || (!canHover && t > 2f)) break;
            }
            float rms = samples > 0 ? Mathf.Sqrt(sumSq / samples) : 0f;
            float score = airborne ? 100f * Mathf.Clamp01(1f - rms / 0.4f) : 0f;
            float landingError = new Vector2(L.Goal.x - x, z).magnitude;
            bool reached = landed && landingError <= LandingTolerance;
            r.Metrics.Add(M("completionTime", "所要時間", reached ? t : FlightTimeout, "0.0", true, " 秒"));
            r.Metrics.Add(M("stabilityScore", "安定度（100点満点）", score, "0", false, " 点"));
            r.Metrics.Add(M("maxDeviation", "横の最大の揺れ", maxDev, "0.00", true, " m"));
            r.Metrics.Add(M("landingError", "着地点のずれ", landingError, "0.00", true, " m"));
            r.Metrics.Add(M("maxAltitude", "最高高度", maxAlt, "0.00", false, " m", primary: false));
            if (airborne && !landed) r.Limits.Add($"{FlightTimeout:0} 秒以内に着地できなかった");
            r.Passed = reached && !batteryOut && score >= StabilityPassScore;
            r.Criteria = $"目標地点から{LandingTolerance} m以内に着地・安定度{StabilityPassScore:0}点以上";
            r.DriftM = maxDev;
        }

        // ================= 共通 =================
        static ExperimentMetric M(string key, string label, float v, string fmt, bool lowerBetter, string unit, bool primary = true) =>
            new ExperimentMetric { Key = key, LabelJa = label, Value = v, Format = fmt, LowerIsBetter = lowerBetter, Unit = unit, Primary = primary };

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
