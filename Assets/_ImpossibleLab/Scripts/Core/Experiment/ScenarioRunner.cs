using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ImpossibleLab.Core
{
    /// <summary>シナリオ実行中の状態。シナリオと機能ブロックのモデルがここを読み書きする。</summary>
    public sealed class SimContext
    {
        public InventionDna Dna;
        public ExperimentPlan Plan;
        public RainExperimentResult Result;
        public CourseRun Run;
        public bool Reality;
        public float T;
        /// <summary>実験を終える（シナリオやモデルが決める）。</summary>
        public bool Stop;

        // 人型モデル（案内役・利用者）
        public Vector2 Leader;
        public bool LeaderFinished;
        public float LeaderFinishTime = float.PositiveInfinity;

        // 発明品（実験場ローカル座標、y は高さ）
        public Vector3 Device;
        public float DeviceYaw;
        public bool CollidedNow;

        /// <summary>実験計画にない計測を除くため、結果に入れる前に通す。</summary>
        public void AddMetric(string key, float value)
        {
            if (Plan.Metrics.Count > 0 && !Plan.Metrics.Contains(key)) return;
            var info = ExperimentCatalog.Metric(key);
            if (info == null) throw new ArgumentException("未登録の計測項目: " + key);
            Result.Metrics.Add(info.Make(value));
        }
    }

    /// <summary>シナリオ：人型モデルの行動と、実験場の配置。</summary>
    public interface IScenario
    {
        /// <summary>この時間で打ち切る（0 なら打ち切らず、モデルが Stop を決める）。</summary>
        float MaxTime { get; }
        void Begin(SimContext ctx);
        /// <summary>時間を進める前に、人型モデルを動かす。</summary>
        void StepAvatar(SimContext ctx);
    }

    /// <summary>
    /// 機能ブロックのモデル（docs/GENERAL_INVENTION_DESIGN.md 4章）。決まったコードのみで、AI が書いたコードは実行しない。
    /// 1ステップの順番：シナリオ.StepAvatar → 各モデル.Step → 時刻を進める → 各モデル.AfterTick → 再生用の記録 → 各モデル.AfterFrame。
    /// </summary>
    public interface IBlockModel
    {
        void Begin(SimContext ctx);
        void Step(SimContext ctx);
        void AfterTick(SimContext ctx);
        void AfterFrame(SimContext ctx);
        void End(SimContext ctx);
    }

    /// <summary>
    /// 実験計画を実行する。固定時間刻み（0.02秒）・シード固定で、同じ計画と設計なら同じ結果になる。
    /// 計算は開始時に一括で行い、画面は記録した軌跡を再生する（見た目と計測が一致する）。
    /// </summary>
    public static class ScenarioRunner
    {
        public const float Dt = 0.02f, FrameInterval = 0.05f, HardTimeLimit = 300f;

        public static CourseRun Run(InventionDna dna, ExperimentPlan plan, RainExperimentResult result)
        {
            var errors = new List<string>();
            ExperimentPlanJson.Validate(plan, null, errors); // 機能が足りない場合は失敗にせず、結果の「制限」に理由を出す
            if (errors.Count > 0) throw new ArgumentException("実験計画が不正です: " + string.Join(" / ", errors.Take(3)));
            var run = new CourseRun { Result = result };
            var ctx = new SimContext { Dna = dna, Plan = plan, Result = result, Run = run, Reality = plan.Mode == LabModes.Reality };
            var scenario = CreateScenario(plan.Scenario);
            var models = CreateModels(plan.Scenario, dna);
            scenario.Begin(ctx);
            foreach (var m in models) m.Begin(ctx);
            float nextFrame = 0f;
            while (!ctx.Stop && (scenario.MaxTime <= 0f || ctx.T < scenario.MaxTime) && ctx.T < HardTimeLimit)
            {
                ctx.CollidedNow = false;
                scenario.StepAvatar(ctx);
                foreach (var m in models) m.Step(ctx);
                ctx.T += Dt;
                foreach (var m in models) m.AfterTick(ctx);
                if (ctx.T >= nextFrame || ctx.CollidedNow)
                {
                    run.Frames.Add(new CourseFrame { Time = ctx.T, Device = ctx.Device, YawDeg = ctx.DeviceYaw, Leader = new Vector3(ctx.Leader.x, 0f, ctx.Leader.y), Collision = ctx.CollidedNow });
                    if (ctx.T >= nextFrame) nextFrame += FrameInterval;
                }
                foreach (var m in models) m.AfterFrame(ctx);
            }
            foreach (var m in models) m.End(ctx);
            return run;
        }

        static IScenario CreateScenario(string id)
        {
            switch (id)
            {
                case ExperimentCatalog.WalkRoute: return new WalkRouteScenario();
                case ExperimentCatalog.FlyToTarget: return new FlyToTargetScenario();
                default: throw new ArgumentException("このシナリオは ScenarioRunner ではまだ実行できません: " + id + "（stand は RainSimulator）");
            }
        }

        /// <summary>
        /// 発明品の動きを決めるモデル。2週目の時点ではシナリオごとに1つ（4週目に取り付け位置と機能ブロックから組み立てる形にする）。
        /// </summary>
        static List<IBlockModel> CreateModels(string scenario, InventionDna dna)
        {
            var list = new List<IBlockModel>();
            if (scenario == ExperimentCatalog.WalkRoute) list.Add(new GroundFollowerModel());
            else if (scenario == ExperimentCatalog.FlyToTarget) list.Add(new FlightModel());
            return list;
        }
    }

    // ================= シナリオ =================

    /// <summary>walk_route：案内役が障害物を迂回しながらルートを歩く。</summary>
    public sealed class WalkRouteScenario : IScenario
    {
        CourseLayout _L;
        float _s, _len;
        public float MaxTime => 0f;

        public void Begin(SimContext ctx)
        {
            _L = CourseSimulator.CarrierLayout(ctx.Plan.Seed, Mathf.RoundToInt(ctx.Plan.Get("obstacles", "count")));
            ctx.Run.Layout = _L;
            _len = CourseSimulator.PathLength(_L.Route);
            ctx.LeaderFinishTime = _len / CourseSimulator.LeaderSpeed;
        }

        public void StepAvatar(SimContext ctx)
        {
            _s = Mathf.Min(_len, _s + CourseSimulator.LeaderSpeed * ScenarioRunner.Dt);
            CourseSimulator.PointAt(_L.Route, _s, out var leader, out _);
            ctx.Leader = leader;
        }
    }

    /// <summary>fly_to_target：人型モデルが目標地点まで飛ぶ（観察モード。装置の位置＝人型モデルの足元）。</summary>
    public sealed class FlyToTargetScenario : IScenario
    {
        public float MaxTime => CourseSimulator.FlightTimeout;

        public void Begin(SimContext ctx)
        {
            ctx.Run.Layout = CourseSimulator.FlightLayout(ctx.Plan.Get("target", "distanceM"), ctx.Dna);
        }

        public void StepAvatar(SimContext ctx) { }
    }

    // ================= 発明品の動き（機能ブロックのモデル） =================

    /// <summary>
    /// 床を走って人についていく（follow_user・avoid_obstacle・carry_load）。計算式は docs/experiments.md 1章。
    /// </summary>
    public sealed class GroundFollowerModel : IBlockModel
    {
        const float Dt = ScenarioRunner.Dt;
        CourseLayout L;
        float speed, followDist, range, payload, accel, capacity;
        bool avoid, hasCarry, canFollow, moving;
        Vector2 p, v, heading;
        float speedNow, turnRate, stall, sumGap, sumErr, maxGap, energyJ, yaw;
        int samples, collisions;
        float? arrival;
        bool[] contact;

        public void Begin(SimContext ctx)
        {
            var dna = ctx.Dna; var r = ctx.Result; bool reality = ctx.Reality;
            L = ctx.Run.Layout;
            canFollow = dna.FindBehavior("follow_user") != null;
            speed = Mathf.Min(dna.GetParameter("follow_user", "speed", 0f), dna.Constraints.MaxSpeed);
            followDist = dna.GetParameter("follow_user", "followDistance", 1.2f);
            avoid = dna.FindBehavior("avoid_obstacle") != null;
            range = dna.GetParameter("avoid_obstacle", "detectionRange", 0f);
            hasCarry = dna.FindBehavior("carry_load") != null;
            capacity = dna.GetParameter("carry_load", "capacity", 0f);
            payload = ctx.Plan.Get("payload", "kg");
            accel = 3f;

            r.Assumptions.Add($"案内役は決まったルート（{CourseSimulator.PathLength(L.Route):0.0} m）を {CourseSimulator.LeaderSpeed:0.0} m/s で歩く。ロボットは案内役へ直進し、追従距離まで近づくと止まる");
            r.Assumptions.Add($"ロボットは半径 {CourseSimulator.RobotRadius:0.0} m の円、障害物は半径 {CourseSimulator.ObstacleRadius:0.00} m の円柱。接触1回で衝突1回と数え、0.4秒止まる。旋回は毎秒 {(reality ? 90 : 150)}° まで。センサーの反応遅れ {CourseSimulator.SensorLatency} 秒の間に進む分だけ検知距離が短くなる（仮定）");
            if (reality)
            {
                float cap = CourseSimulator.RealityCarrierSpeedCap * (1f - 0.25f * payload / RainExperimentConfig.MaxPayload);
                if (speed > cap) r.Limits.Add($"追従速度 {speed:0.00} m/s → {cap:0.00} m/s に制限（歩道走行ロボットの速度上限 {CourseSimulator.RealityCarrierSpeedCap} m/s と積荷による低下を仮定）");
                speed = Mathf.Min(speed, cap);
                accel = 1.2f * (1f - 0.5f * payload / RainExperimentConfig.MaxPayload);
                r.Assumptions.Add($"REALITY：加速度 {accel:0.00} m/s²（積荷で低下）、車体 {CourseSimulator.RobotMassKg:0} kg、転がり抵抗係数 {CourseSimulator.RollingResistance}、待機電力 {CourseSimulator.CarrierBasePowerW:0} W、駆動効率 {CourseSimulator.CarrierDriveEfficiency}（いずれも仮定）");
            }
            else r.Assumptions.Add("DREAM：加速度 3 m/s²、電力・重量の制約なし");
            r.Assumptions.Add("移動ロボットの実際の制御・センサー性能の予測ではない");

            bool overload = !hasCarry || capacity < payload;
            if (!canFollow) r.Limits.Add("「ユーザー追従」の機能がないため、ロボットはついて来られない");
            if (overload) r.Limits.Add(hasCarry ? $"積載量 {capacity:0.0} kg < 積荷 {payload:0.0} kg：運べない" : "「積載」の機能がないため、積荷を運べない");
            if (!avoid) r.Assumptions.Add("「障害物回避」の機能がないため、障害物を避けずに進む");

            var ahead = L.Route[Mathf.Min(5, L.Route.Count - 1)] - L.Start;
            p = L.Start - ahead.normalized * followDist;
            v = Vector2.zero;
            heading = ahead.normalized;
            speedNow = 0f; turnRate = reality ? 90f : 150f; // 度/秒
            contact = new bool[L.Obstacles.Count];
            moving = canFollow && !overload;
            ctx.Device = new Vector3(p.x, 0f, p.y);
        }

        public void Step(SimContext ctx)
        {
            if (!moving) return;
            var leader = ctx.Leader;
            var to = leader - p; float d = to.magnitude;
            var dir = d > 1e-4f ? to / d : Vector2.zero;
            float want = d > followDist ? speed * Mathf.Clamp01((d - followDist) / 0.5f) : 0f;
            // センサーの反応遅れの間に進む距離だけ、実際に使える検知距離は短くなる
            float effRange = range - speedNow * CourseSimulator.SensorLatency;
            if (avoid && want > 0f && effRange > 0.01f)
            {
                var steer = dir;
                foreach (var o in L.Obstacles)
                {
                    var away = p - new Vector2(o.x, o.y); float dist = away.magnitude;
                    float clear = dist - o.z - CourseSimulator.RobotRadius;
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
                if (dist > o.z + CourseSimulator.RobotRadius + 0.01f || dist < 1e-4f) continue;
                var n = away / dist; float into = Vector2.Dot(v, n);
                if (into < 0f) v -= into * n;
            }
            p += v * Dt;
            for (int i = 0; i < L.Obstacles.Count; i++)
            {
                var o = L.Obstacles[i]; var c = new Vector2(o.x, o.y);
                var away = p - c; float dist = away.magnitude, min = o.z + CourseSimulator.RobotRadius;
                if (dist < min)
                {
                    if (!contact[i]) { collisions++; ctx.CollidedNow = true; stall = 0.4f; speedNow *= 0.3f; }
                    contact[i] = true;
                    p = c + (dist > 1e-4f ? away / dist : Vector2.right) * min;
                }
                else if (dist > min + 0.05f) contact[i] = false;
            }
            if (ctx.Reality)
            {
                float mass = CourseSimulator.RobotMassKg + payload;
                energyJ += (mass * 9.80665f * CourseSimulator.RollingResistance * v.magnitude / CourseSimulator.CarrierDriveEfficiency + CourseSimulator.CarrierBasePowerW) * Dt;
            }
            if (v.sqrMagnitude > 1e-4f) yaw = Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg;
            ctx.Device = new Vector3(p.x, 0f, p.y);
            ctx.DeviceYaw = yaw;
        }

        public void AfterTick(SimContext ctx)
        {
            float gap = (ctx.Leader - p).magnitude;
            if (ctx.T <= ctx.LeaderFinishTime) { sumGap += gap; sumErr += Mathf.Abs(gap - followDist); samples++; maxGap = Mathf.Max(maxGap, gap); }
        }

        public void AfterFrame(SimContext ctx)
        {
            float t = ctx.T, done = ctx.LeaderFinishTime;
            float gap = (ctx.Leader - p).magnitude;
            if (t >= done && arrival == null && gap <= followDist + 0.3f) arrival = t - done;
            if (t >= done && (arrival != null || t - done >= CourseSimulator.CarrierTimeout)) ctx.Stop = true;
        }

        public void End(SimContext ctx)
        {
            var r = ctx.Result;
            float delay = arrival ?? CourseSimulator.CarrierTimeout;
            float avgGap = samples > 0 ? sumGap / samples : 0f, avgErr = samples > 0 ? sumErr / samples : 0f;
            ctx.AddMetric("collisionCount", collisions);
            ctx.AddMetric("arrivalDelay", delay);
            ctx.AddMetric("followError", avgErr);
            ctx.AddMetric("averageFollowDistance", avgGap);
            ctx.AddMetric("maxGap", maxGap);
            if (ctx.Reality)
            {
                r.EnergyWh = energyJ / 3600f;
                ctx.AddMetric("energyWh", r.EnergyWh.Value);
            }
            if (arrival == null && moving) r.Limits.Add($"案内役の到着から {CourseSimulator.CarrierTimeout:0} 秒以内に追いつけなかった");
            r.Passed = moving && collisions == 0 && delay <= CourseSimulator.ArrivalLimitSec;
            r.Criteria = $"衝突0回・到着の遅れ{CourseSimulator.ArrivalLimitSec:0}秒以内・積荷を運べること";
        }

        static Vector2 Rotate(Vector2 v, float deg)
        {
            float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), sn = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * sn, v.x * sn + v.y * c);
        }
    }

    /// <summary>
    /// 浮いて目標まで飛ぶ（hover・move_forward・stabilize）。計算式は docs/experiments.md 2章。
    /// </summary>
    public sealed class FlightModel : IBlockModel
    {
        const float Dt = ScenarioRunner.Dt;
        const float kWind = 0.15f, kHold = 1.0f, damp = 1.2f, accel = 1.5f, descend = 0.6f;
        CourseLayout L;
        XorShift rng;
        float stab, vmax, climb, available, windAlong, windCross, phase, gustState;
        bool canHover, canMove, batteryOut, landed, airborne;
        float x, y, z, vx, vz, sumSq, maxDev, maxAlt;
        int samples;

        public void Begin(SimContext ctx)
        {
            var dna = ctx.Dna; var r = ctx.Result; bool reality = ctx.Reality;
            L = ctx.Run.Layout;
            float lift = dna.GetParameter("hover", "lift", 0f);
            float thrust = dna.GetParameter("move_forward", "thrust", 0f);
            float speedLimit = Mathf.Min(dna.GetParameter("move_forward", "speedLimit", 0f), dna.Constraints.MaxSpeed);
            stab = dna.GetParameter("stabilize", "stabilizationStrength", 0f);
            canHover = dna.FindBehavior("hover") != null && lift >= 0.15f && L.CruiseHeight >= 0.2f;
            canMove = dna.FindBehavior("move_forward") != null && thrust > 0.01f;
            vmax = Mathf.Min(speedLimit, 0.5f + 2.5f * thrust);
            climb = 0.3f + 1.2f * lift;
            available = float.PositiveInfinity;

            r.Assumptions.Add("観察モード：人型モデルが靴で飛ぶのを外から見る（酔い対策のため視点は動かさない）");
            r.Assumptions.Add($"離陸 → 高度 {L.CruiseHeight:0.0} m へ上昇（{climb:0.0} m/s）→ 最大 {vmax:0.0} m/s で目標へ → 着地。風は横方向の揺れとして加える");
            r.Assumptions.Add($"揺れ：横風と突風（シード固定）に対し、姿勢安定の強さ s で戻す力が (1 + 4s) 倍になると仮定（調整用の仮定）");
            if (!canHover) r.Limits.Add(dna.FindBehavior("hover") == null ? "「浮遊」の機能がないため浮上できない" : "揚力・最大高度が小さく浮上できない（揚力0.15以上・最大高度0.2 m以上が必要）");
            if (canHover && !canMove) r.Limits.Add("「推進」の機能がない（または推力0）ため前へ進めない");
            if (reality)
            {
                var fp = CourseSimulator.EstimateFlightPower(dna);
                if (fp == null) r.Limits.Add(RealityModel.DefaultError + "。必要電力を計算できません");
                else
                {
                    var f = fp.Value;
                    available = f.AvailableSec;
                    r.Assumptions.Add($"REALITY：体重 {CourseSimulator.PilotMassKg:0} kg＋靴 {CourseSimulator.ShoesMassKg:0} kg＋電池 {CourseSimulator.ShoeBatteryKg:0} kg を推進ユニットの円板（合計 {f.DiscAreaM2:0.000} m²）で浮かせる");
                    r.Assumptions.Add($"必要電力 約 {f.RequiredKW:0} kW（運動量理論 P = T^1.5/√(2ρA) を効率で割る）。電池で浮いていられる時間 約 {f.AvailableSec:0.0} 秒");
                    ctx.AddMetric("requiredPowerKW", f.RequiredKW);
                    ctx.AddMetric("availableFlightSec", f.AvailableSec);
                }
            }
            else r.Assumptions.Add("DREAM：電力・重量の制約なし（空想シミュレーション）");
            r.Assumptions.Add("ゲーム的な飛行モデル。現実の飛行性能・安全性の予測ではない");

            rng = new XorShift(ctx.Plan.Seed);
            var wind = ctx.Plan.WindDir() * ctx.Plan.Get("wind", "speed");
            windAlong = wind.x; windCross = wind.y;
            phase = rng.NextFloat() * 6.283f; gustState = 0f;
            x = L.Start.x;
            ctx.Device = new Vector3(x, 0f, L.Start.y);
        }

        public void Step(SimContext ctx)
        {
            float t = ctx.T;
            gustState = Mathf.Lerp(gustState, rng.NextFloat() * 2f - 1f, 0.05f);
            float gust = 0.7f + 0.3f * Mathf.Sin(6.283f * t / 3.1f + phase) + 0.4f * gustState;
            bool powered = canHover && !batteryOut;
            if (ctx.Reality && airborne && t > available && !batteryOut) { batteryOut = true; powered = false; ctx.Result.Limits.Add($"{available:0.0} 秒で電池切れ、途中で着地（推定）"); }
            float dx = L.Goal.x - x;
            if (powered)
            {
                // 進行方向の風で流される分（drift）。推進があれば打ち消して、対地速度で目標へ向かい・止まる
                float drift = y > 0.02f ? 0.15f * windAlong * gust : 0f;
                float comp = canMove ? drift : 0f;
                bool nearGoal = Mathf.Abs(dx) < 0.15f && Mathf.Abs(vx + comp) < 0.2f;
                if (!nearGoal) { y = Mathf.MoveTowards(y, L.CruiseHeight, climb * Dt); airborne |= y > 0.05f; }
                else y = Mathf.MoveTowards(y, 0f, descend * Dt);
                float targetVx = canMove && y > 0.5f * L.CruiseHeight && !nearGoal
                    ? Mathf.Sign(dx) * Mathf.Min(vmax, Mathf.Sqrt(2f * accel * Mathf.Abs(dx))) : 0f;
                float command = canMove ? Mathf.Clamp(targetVx - comp, -vmax, vmax) : targetVx;
                vx = Mathf.MoveTowards(vx, command, accel * Dt);
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
            ctx.Device = new Vector3(x, y, L.Start.y + z);
            ctx.DeviceYaw = Mathf.Clamp(vz * 25f, -25f, 25f); // 再生時は横揺れの傾き（ロール）として使う
        }

        public void AfterTick(SimContext ctx)
        {
            if (airborne) { sumSq += z * z; samples++; maxDev = Mathf.Max(maxDev, Mathf.Abs(z)); maxAlt = Mathf.Max(maxAlt, y); }
        }

        public void AfterFrame(SimContext ctx)
        {
            if (landed || (!canHover && ctx.T > 2f)) ctx.Stop = true;
        }

        public void End(SimContext ctx)
        {
            var r = ctx.Result;
            float t = ctx.T;
            float rms = samples > 0 ? Mathf.Sqrt(sumSq / samples) : 0f;
            float score = airborne ? 100f * Mathf.Clamp01(1f - rms / 0.4f) : 0f;
            float landingError = new Vector2(L.Goal.x - x, z).magnitude;
            bool reached = landed && landingError <= CourseSimulator.LandingTolerance;
            ctx.AddMetric("completionTime", reached ? t : CourseSimulator.FlightTimeout);
            ctx.AddMetric("stabilityScore", score);
            ctx.AddMetric("maxDeviation", maxDev);
            ctx.AddMetric("landingError", landingError);
            ctx.AddMetric("maxAltitude", maxAlt);
            if (airborne && !landed) r.Limits.Add($"{CourseSimulator.FlightTimeout:0} 秒以内に着地できなかった");
            r.Passed = reached && !batteryOut && score >= CourseSimulator.StabilityPassScore;
            r.Criteria = $"目標地点から{CourseSimulator.LandingTolerance} m以内に着地・安定度{CourseSimulator.StabilityPassScore:0}点以上";
            r.DriftM = maxDev;
        }
    }
}
