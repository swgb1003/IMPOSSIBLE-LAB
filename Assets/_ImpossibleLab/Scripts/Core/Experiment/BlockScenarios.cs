using System.Collections.Generic;
using UnityEngine;

namespace ImpossibleLab.Core
{
    // 4週目：機能ブロック第1弾を実験計画で動かすシナリオとモデル。式は Core/Blocks/BlockModels.cs（docs/blocks.md）。
    // 合否は実験計画の合格条件で決める（ScenarioRunner.Judge）。ゲーム・教育向けの簡易モデルで、現実の性能の予測ではない。

    /// <summary>取り付け位置から決める、ブロックの置き場所・向き。</summary>
    public static class BlockPlacement
    {
        /// <summary>日よけ（水平な円板）の中心の高さ。頭：頭上追従の高さ、背中・腰・手首・手：柄や支柱で頭上 2.0 m、空中・設置：その高さ。</summary>
        public static float ShadeCenterHeight(InventionDna dna)
        {
            switch (dna.Mount)
            {
                case "head": return BodyGeometry.Height + dna.GetParameter("follow_head", "heightOffset", 0.35f);
                case "back": case "waist": case "wrist": case "hand": return 2.0f;
                default: return BodyGeometry.MountHeight(dna.Mount);
            }
        }

        /// <summary>センサーの向き（0° = 前方）。背中に付けたものは後ろを見る。</summary>
        public static float SensorHeadingDeg(string mount) => mount == "back" ? 180f : 0f;

        /// <summary>暗さモジュールがないときの周りの明るさ（昼の屋外の目安、lx）。</summary>
        public const float DaylightLux = 10000f;
    }

    // ================= シナリオ =================

    /// <summary>stand（雨なし）：人型モデルがその場に立つ。装置は取り付け位置の高さにある。</summary>
    public sealed class StandScenario : IScenario
    {
        float _max;
        public float MaxTime => _max;
        public void Begin(SimContext ctx)
        {
            _max = ctx.Plan.DurationSec;
            ctx.Run.Layout = new CourseLayout();
            ctx.Device = new Vector3(0f, BodyGeometry.MountHeight(ctx.Dna.Mount), 0f);
            ctx.Result.Assumptions.Add($"人型モデル（身長 {BodyGeometry.Height:0.00} m）がその場に立ち、装置は{LabCatalog.Mount(ctx.Dna.Mount)?.LabelJa ?? ctx.Dna.Mount}（高さ {ctx.Device.y:0.00} m）に付く");
        }
        public void StepAvatar(SimContext ctx) { }
    }

    /// <summary>sit_still：座って過ごす。過ごす時間（分）を 10 秒に早送りして再生する。</summary>
    public sealed class SitStillScenario : IScenario
    {
        public const float PlaybackSec = 10f;
        public float MaxTime => PlaybackSec;
        public void Begin(SimContext ctx)
        {
            ctx.Run.Layout = new CourseLayout();
            ctx.Device = new Vector3(0f, 0.9f, 0f);
            ctx.Result.Assumptions.Add($"人型モデルが座って {ctx.Plan.Get("temperature", "minutes"):0} 分過ごす（{PlaybackSec:0} 秒に早送りして再生）。気温 {ctx.Plan.Get("temperature", "ambientC"):0} °C");
        }
        public void StepAvatar(SimContext ctx) { }
    }

    /// <summary>drop_test：落とす高さから自由落下させ、床に当たったあと 1 秒見せる。</summary>
    public sealed class DropTestScenario : IScenario
    {
        float _max;
        public float MaxTime => _max;
        public void Begin(SimContext ctx)
        {
            float h = ctx.Plan.Get("drop", "heightM");
            _max = Mathf.Sqrt(2f * h / RealityModel.Gravity) + 1f;
            ctx.Run.Layout = new CourseLayout();
            ctx.Device = new Vector3(0f, h, 0f);
            ctx.Result.Assumptions.Add($"{ctx.Plan.Get("drop", "massKg"):0.0} kg の物を {h:0.00} m から硬い床へまっすぐ落とす（空気抵抗・回転は扱わない）");
        }
        public void StepAvatar(SimContext ctx) { }
    }

    /// <summary>lift_task：床の荷物を棚の高さまで持ち上げる。</summary>
    public sealed class LiftTaskScenario : IScenario
    {
        public float MaxTime => 30f;
        public void Begin(SimContext ctx)
        {
            ctx.Run.Layout = new CourseLayout();
            ctx.Device = Vector3.zero;
            ctx.Result.Assumptions.Add($"{ctx.Plan.Get("payload", "kg"):0.0} kg・幅 {ctx.Plan.Get("payload", "sizeM"):0.00} m の荷物を、床から棚（{ctx.Plan.Get("shelf", "heightM"):0.00} m）まで持ち上げる");
        }
        public void StepAvatar(SimContext ctx) { }
    }

    // ================= 機能ブロックのモデル =================

    /// <summary>計算を Begin でまとめて行うブロック用の土台。</summary>
    public abstract class StaticBlockModel : IBlockModel
    {
        public abstract void Begin(SimContext ctx);
        public virtual void Step(SimContext ctx) { }
        public virtual void AfterTick(SimContext ctx) { }
        public virtual void AfterFrame(SimContext ctx) { }
        public virtual void End(SimContext ctx) { }
    }

    /// <summary>照明：前方の床の照度（暗さモジュールの明るさを足す）。</summary>
    public sealed class IlluminateBlock : StaticBlockModel
    {
        public override void Begin(SimContext ctx)
        {
            var dna = ctx.Dna;
            float lm = dna.GetParameter("illuminate", "lumens", 150f), beam = dna.GetParameter("illuminate", "beamAngleDeg", 60f), tilt = dna.GetParameter("illuminate", "tiltDeg", 45f);
            float h = BodyGeometry.MountHeight(dna.Mount), amb = ctx.Plan.Get("darkness", "lux");
            var r = IlluminateModel.Evaluate(lm, beam, tilt, h, amb);
            ctx.AddMetric("footIlluminance", r.AverageLx);
            ctx.AddMetric("illuminanceMin", r.MinLx);
            ctx.BlockPowerW += r.PowerW;
            ctx.Result.Assumptions.Add($"照明：高さ {h:0.00} m から {lm:0} lm・広がり {beam:0}°・下向き {tilt:0}° → 光度 {r.IntensityCd:0} cd。前方 {IlluminateModel.NearM}〜{IlluminateModel.FarM} m の床の平均照度（周りの明るさ {amb:0.#} lx を含む）");
            if (r.CoveredRatio < 1f) ctx.Result.Limits.Add($"光の円錐に入るのは前方の床の {r.CoveredRatio * 100f:0}% だけ（向き・広がりを見直す）");
        }
    }

    /// <summary>日よけ：体が受ける日射と日陰率（日差しモジュールがあるとき）。日よけがなくても日射は測る（比較用）。</summary>
    public sealed class SunShadeBlock : StaticBlockModel
    {
        public override void Begin(SimContext ctx)
        {
            var dna = ctx.Dna; var plan = ctx.Plan;
            float e = plan.Get("sunlight", "elevationDeg"), az = plan.Get("sunlight", "azimuthDeg"), irr = plan.Get("sunlight", "irradiance");
            bool has = dna.FindBehavior("sun_shade") != null;
            float radius = has ? dna.GetParameter("sun_shade", "radius", 0.6f) : 0f, opacity = has ? dna.GetParameter("sun_shade", "opacity", 0.9f) : 0f;
            float hc = BlockPlacement.ShadeCenterHeight(dna);
            var r = SunShadeModel.Evaluate(radius, opacity, new Vector3(0f, hc, 0f), e, az);
            if (has) ctx.AddMetric("shadeRatio", r.ShadeRatio);
            ctx.AddMetric("sunExposureW", irr * r.SunlitArea * (1f - r.ShadeRatio));
            ctx.Result.Assumptions.Add(has
                ? $"日よけ：高さ {hc:0.00} m の水平な円板（半径 {radius:0.00} m・遮光率 {opacity:0.00}）。太陽の高さ {e:0}°・方位 {az:0}°の平行光線で体（円柱）の影を判定"
                : $"日よけの機能なし：太陽の高さ {e:0}°・方位 {az:0}°で体が受ける日射だけを測る");
            ctx.Result.Assumptions.Add($"体が受ける日射 = 日射の強さ {irr:0} W/m² × 日の当たる面積（cos つき）× (1 − 日陰率)");
        }
    }

    /// <summary>検知：周りに置いた対象（シード固定）のうち、検知距離・視野角の中で障害物に遮られないものの割合。</summary>
    public sealed class DetectBlock : StaticBlockModel
    {
        public const float TargetMinR = 1f, TargetMaxR = 12f, ObstacleRadius = 0.3f;

        public override void Begin(SimContext ctx)
        {
            var dna = ctx.Dna; var plan = ctx.Plan; var L = ctx.Run.Layout ?? (ctx.Run.Layout = new CourseLayout());
            float range = dna.GetParameter("detect", "rangeM", 5f), fov = dna.GetParameter("detect", "fovDeg", 90f);
            float heading = BlockPlacement.SensorHeadingDeg(dna.Mount);
            var rng = new XorShift(plan.Seed ^ 0x5151);
            int n = Mathf.RoundToInt(plan.Get("targets", "count"));
            for (int i = 0; i < n; i++)
            {
                float r = Mathf.Lerp(TargetMinR, TargetMaxR, rng.NextFloat()), a = rng.NextFloat() * 2f * Mathf.PI;
                L.Targets.Add(new Vector2(r * Mathf.Sin(a), r * Mathf.Cos(a)));
            }
            int obs = plan.Has("obstacles") ? Mathf.RoundToInt(plan.Get("obstacles", "count")) : 0;
            for (int i = 0; i < obs; i++)
            {
                float r = Mathf.Lerp(1.5f, 6f, rng.NextFloat()), a = rng.NextFloat() * 2f * Mathf.PI;
                L.Obstacles.Add(new Vector3(r * Mathf.Sin(a), r * Mathf.Cos(a), ObstacleRadius));
            }
            var res = DetectModel.Evaluate(Vector2.zero, heading, range, fov, L.Targets, L.Obstacles);
            ctx.AddMetric("detectionRate", res.Rate);
            ctx.BlockPowerW += res.PowerW;
            ctx.Result.Assumptions.Add($"検知：{(heading == 0f ? "前" : "後ろ")}向き・検知距離 {range:0.0} m・視野角 {fov:0}°。対象 {n} 個を {TargetMinR:0}〜{TargetMaxR:0} m の周りに置く（シード固定）{(obs > 0 ? $"、障害物 {obs} 個が視線を遮る" : "")}");
            ctx.Result.Assumptions.Add("検知は距離・視野角・遮りの幾何判定のみ（誤検知・暗さ・反射は扱わない）");
        }
    }

    /// <summary>通知：音・光で気づいてもらえる距離（暗さモジュールがなければ昼の屋外）。</summary>
    public sealed class AlertBlock : StaticBlockModel
    {
        public override void Begin(SimContext ctx)
        {
            var dna = ctx.Dna;
            float db = dna.GetParameter("alert", "loudnessDb", 80f), lm = dna.GetParameter("alert", "lightLumens", 50f), hz = dna.GetParameter("alert", "flashHz", 2f);
            float amb = ctx.Plan.Has("darkness") ? ctx.Plan.Get("darkness", "lux") : BlockPlacement.DaylightLux;
            var r = AlertModel.Evaluate(db, lm, hz, amb);
            ctx.AddMetric("alertRange", r.RangeM);
            ctx.BlockPowerW += r.PowerW;
            ctx.Result.Assumptions.Add($"通知：音 {db:0} dB（1 m）→ {r.SoundRangeM:0} m、光 {lm:0} lm（周りの明るさ {amb:0.#} lx）→ {r.LightRangeM:0} m。届く距離はその大きい方");
            ctx.Result.Assumptions.Add("音は距離の逆二乗・騒音との差、光は気づく照度で判定。点滅による気づきやすさの違いは扱わない");
        }
    }

    /// <summary>加温・冷却：集中定数の熱モデル（解析解）。再生では早送りした温度の変化を Aux に入れる。</summary>
    public sealed class ThermalBlock : StaticBlockModel
    {
        float _start, _steady, _tau, _scale;

        public override void Begin(SimContext ctx)
        {
            var dna = ctx.Dna; var plan = ctx.Plan;
            bool hasHeat = dna.FindBehavior("heat") != null, hasCool = dna.FindBehavior("cool") != null;
            bool cooling = !hasHeat;
            if (hasHeat && hasCool) ctx.Result.Limits.Add("加温と冷却の両方があるため、この版では加温だけで計算する");
            float p = cooling ? dna.GetParameter("cool", "powerW", 10f) : dna.GetParameter("heat", "powerW", 10f);
            float amb = plan.Get("temperature", "ambientC"), target = plan.Get("temperature", "targetDeltaC"), minutes = plan.Get("temperature", "minutes");
            var r = ThermalModel.Evaluate(cooling, p, amb, amb, minutes * 60f, target);
            _start = amb; _steady = r.SteadyC; _tau = BlockCoefficients.V("thermalCapacity") / BlockCoefficients.V("thermalLoss");
            _scale = minutes * 60f / SitStillScenario.PlaybackSec;
            float gain = cooling ? -r.DeltaC : r.DeltaC;
            ctx.AddMetric("temperatureGain", gain);
            ctx.AddMetric("timeToTarget", r.TimeToTargetSec < 0f ? ExperimentCatalog.NotReached : Mathf.Min(ExperimentCatalog.NotReached, r.TimeToTargetSec / 60f));
            ctx.BlockPowerW += p;
            string verb = cooling ? "冷却" : "加温";
            ctx.Result.Assumptions.Add($"{verb}：電力 {p:0} W → 熱 {r.HeatFlowW:+0.0;-0.0} W。装着部の熱容量 {BlockCoefficients.V("thermalCapacity"):0} J/K・熱損失 {BlockCoefficients.V("thermalLoss"):0.0} W/K（調整用の仮定）→ 定常で {r.SteadyC - amb:+0.0;-0.0} °C");
            if (r.TimeToTargetSec < 0f) ctx.Result.Limits.Add($"目標の温度変化 {target:0.0} °C に届かない（定常でも {Mathf.Abs(r.SteadyC - amb):0.0} °C）");
            else if (r.TimeToTargetSec > minutes * 60f) ctx.Result.Limits.Add($"目標の温度変化に届くのは {r.TimeToTargetSec / 60f:0.0} 分後（過ごす時間 {minutes:0} 分より長い）");
            if (cooling) ctx.Result.Limits.Add("冷却部の排熱（反対側が熱くなる）は扱わない");
            ctx.Aux = amb;
        }

        public override void AfterTick(SimContext ctx) => ctx.Aux = ThermalModel.TemperatureAt(ctx.T * _scale, _start, _steady, _tau);
    }

    /// <summary>衝撃吸収：落下の最大の衝撃。緩衝材がなければ硬い床に当たる（比較のため、どちらの値も出す）。</summary>
    public sealed class CushionBlock : StaticBlockModel
    {
        float _h, _peak;

        public override void Begin(SimContext ctx)
        {
            var dna = ctx.Dna; var plan = ctx.Plan;
            _h = plan.Get("drop", "heightM");
            float m = plan.Get("drop", "massKg");
            bool has = dna.FindBehavior("impact_cushion") != null;
            float thick = dna.GetParameter("impact_cushion", "thicknessM", 0.03f), k = dna.GetParameter("impact_cushion", "stiffness", 20000f);
            var none = CushionModel.Drop(m, _h, 0f, 0f);
            var r = has ? CushionModel.Drop(m, _h, thick, k) : none;
            _peak = r.PeakG;
            ctx.AddMetric("peakImpactG", r.PeakG);
            if (has) ctx.AddMetric("cushionCompression", r.MaxCompressionM);
            ctx.Result.Assumptions.Add(has
                ? $"衝撃吸収：厚み {thick * 1000f:0} mm・硬さ {k:0} N/m の緩衝材（減衰比 {BlockCoefficients.V("cushionDamping"):0.0}）。緩衝材なしなら約 {none.PeakG:0} G"
                : $"衝撃吸収の機能なし：硬い床に当たる（約 {none.PeakG:0} G）");
            ctx.Result.Assumptions.Add($"床に当たる速さ {r.ImpactSpeed:0.0} m/s、止まるまで {r.ContactMs:0.0} ms（ばね・ダンパーの落下モデル）");
            if (r.BottomedOut) ctx.Result.Limits.Add("緩衝材の厚みを使い切った（つぶれ切って硬い床に近づいた）");
        }

        public override void Step(SimContext ctx)
        {
            float y = Mathf.Max(0f, _h - 0.5f * RealityModel.Gravity * ctx.T * ctx.T);
            ctx.Device = new Vector3(0f, y, 0f);
            ctx.Aux = y > 0f ? 0f : _peak;
        }
    }

    /// <summary>
    /// 持ち上げ・つかむ：持ち上げ機構があればその速さで、なければ人がつかみ具で持ち上げる（0.5 m/s と仮定）。
    /// つかみ具があれば、持ち上げ始めの加速（上向き）で落とさないかを判定する。持ち上げ機構だけなら荷台に載せる（落とさない）。
    /// </summary>
    public sealed class LiftBlock : StaticBlockModel
    {
        public const float HumanLiftSpeed = 0.5f, HumanRampSec = 0.2f, MachineRampSec = 0.25f;
        float _shelf, _speed, _done;
        bool _ok;

        public override void Begin(SimContext ctx)
        {
            var dna = ctx.Dna; var plan = ctx.Plan; var res = ctx.Result;
            float kg = plan.Get("payload", "kg"), size = plan.Get("payload", "sizeM");
            _shelf = plan.Get("shelf", "heightM");
            bool hasLift = dna.FindBehavior("lift_load") != null, hasGrip = dna.FindBehavior("grip") != null;
            float power = 0f;
            bool can = true;
            if (hasLift)
            {
                var lr = LiftModel.Evaluate(dna.GetParameter("lift_load", "maxLoadKg", 5f), dna.GetParameter("lift_load", "liftHeightM", 0.8f), dna.GetParameter("lift_load", "speed", 0.2f), kg, _shelf);
                can = lr.CanLift;
                _speed = dna.GetParameter("lift_load", "speed", 0.2f);
                if (!can) res.Limits.Add("持ち上げられない：" + lr.Reason);
                else { power = lr.PowerW; ctx.BlockPowerW += power; }
                res.Assumptions.Add($"持ち上げ機構：{_speed:0.00} m/s で持ち上げる（効率 {BlockCoefficients.V("liftEfficiency"):0.0}、加速・減速の時間は数えない）");
            }
            else
            {
                _speed = HumanLiftSpeed;
                res.Assumptions.Add($"持ち上げ機構なし：人がつかみ具で {HumanLiftSpeed} m/s で持ち上げる（仮定）");
            }
            bool dropped = false;
            if (hasGrip && can)
            {
                float ramp = hasLift ? MachineRampSec : HumanRampSec;
                float a = _speed / ramp;
                var g = GripModel.Evaluate(dna.GetParameter("grip", "gripForceN", 30f), dna.GetParameter("grip", "openingM", 0.2f), kg, size, new List<float> { a, 0f, -a });
                dropped = g.Dropped;
                res.Assumptions.Add($"つかむ：2面ではさむ（摩擦係数 {BlockCoefficients.V("gripFriction"):0.0}）。持ち上げ始めの上向きの加速 {a:0.0} m/s² でも支えられるか");
                if (!g.CanGrasp) res.Limits.Add($"荷物の幅 {size:0.00} m がつかみ具の開き幅より大きく、つかめない");
                else if (dropped) res.Limits.Add($"つかむ力が足りず落とした（必要 {g.RequiredForceN:0} N）");
            }
            else if (hasLift) res.Assumptions.Add("つかみ具なし：荷台に載せて持ち上げる（落とさない）");
            _ok = can && !dropped;
            _done = _ok ? _shelf / _speed : 0f;
            ctx.AddMetric("dropCount", dropped ? 1f : 0f);
            ctx.AddMetric("liftTime", _ok ? _done : ExperimentCatalog.LiftTimeout);
            if (hasLift && can) ctx.AddMetric("liftPowerW", power);
        }

        public override void Step(SimContext ctx)
        {
            float y = _ok ? Mathf.Min(_shelf, _speed * ctx.T) : 0f;
            ctx.Device = new Vector3(0f, y, 0f);
            ctx.Aux = _ok ? 0f : 1f;
        }

        public override void AfterFrame(SimContext ctx)
        {
            if (ctx.T > _done + 1f) ctx.Stop = true;
        }
    }
    // ================= 6週目：走行（坂） =================

    /// <summary>climb_slope：坂の下から上まで走る。坂は人型モデルの奥（z = 3 m）に置く（操作パネル越しに見えるように）。</summary>
    public sealed class ClimbSlopeScenario : IScenario
    {
        public const float LaneZ = 3.0f;
        public float MaxTime => ExperimentCatalog.ClimbTimeout + 2f;
        public void Begin(SimContext ctx)
        {
            ctx.Run.Layout = new CourseLayout();
            float a = ctx.Plan.Get("slope", "angleDeg"), L = ctx.Plan.Get("slope", "lengthM");
            ctx.Device = new Vector3(StartX(a, L), 0f, LaneZ);
            ctx.Result.Assumptions.Add($"長さ {L:0.0} m・角度 {a:0.0}° の坂を下から上まで走る" + (ctx.Plan.Has("payload") ? $"（荷物 {ctx.Plan.Get("payload", "kg"):0.0} kg を載せる）" : ""));
        }
        public void StepAvatar(SimContext ctx) { }
        /// <summary>坂の下の端の x（坂の中央を x = 0 に置く）。</summary>
        public static float StartX(float angleDeg, float lengthM) => -0.5f * lengthM * Mathf.Cos(angleDeg * Mathf.Deg2Rad);
    }

    /// <summary>
    /// 走行：坂を上れるか・上り切る時間・必要な電力（docs/blocks.md 8章）。走行の機能がなければ上れない（理由を制限に出す）。
    /// 滑り・段差・加速と減速は扱わない。
    /// </summary>
    public sealed class DriveBlock : StaticBlockModel
    {
        float _angle, _len, _speed, _reach, _done;

        public override void Begin(SimContext ctx)
        {
            var dna = ctx.Dna; var plan = ctx.Plan; var res = ctx.Result;
            _angle = plan.Get("slope", "angleDeg"); _len = plan.Get("slope", "lengthM");
            bool has = dna.FindBehavior("drive") != null;
            float payload = plan.Has("payload") ? plan.Get("payload", "kg") : 0f;
            if (!has)
            {
                res.Limits.Add("「走行」の機能がないため、坂を上れない");
                _speed = 0f; _reach = 0f; _done = 0f;
                ctx.AddMetric("climbedRatio", 0f);
                ctx.AddMetric("climbTime", ExperimentCatalog.ClimbTimeout);
                return;
            }
            float mass = MassModel.TotalKg(dna) + payload;
            var r = DriveModel.Climb(mass, dna.GetParameter("drive", "speedLimit", 1f), dna.GetParameter("drive", "maxSlopeDeg", 10f), _angle, _len);
            _speed = r.Speed;
            if (!r.CanClimb)
            {
                res.Limits.Add("坂を上れない：" + r.Reason);
                _reach = 0f; _done = 0f;
            }
            else
            {
                _reach = Mathf.Min(_len, _speed * ExperimentCatalog.ClimbTimeout);
                _done = _reach / _speed;
                if (_reach < _len) res.Limits.Add($"{ExperimentCatalog.ClimbTimeout:0} 秒以内に上り切れなかった（遅すぎる）");
            }
            bool full = r.CanClimb && _reach >= _len - 1e-4f;
            ctx.AddMetric("climbedRatio", _len > 0f ? _reach / _len : 0f);
            ctx.AddMetric("climbTime", full ? r.TimeSec : ExperimentCatalog.ClimbTimeout);
            res.Assumptions.Add($"走行：{_speed:0.0} m/s の一定の速さで上る。坂の角度 ≤ 上れる角度（{dna.GetParameter("drive", "maxSlopeDeg", 10f):0}°）なら上れる（滑り・段差・加減速は扱わない）");
            if (ctx.Reality && r.CanClimb)
            {
                ctx.AddMetric("drivePowerW", r.PowerW);
                ctx.BlockPowerW += r.PowerW;
                res.Assumptions.Add($"電力 P = m g (sinθ + 転がり抵抗 {BlockCoefficients.V("rollingResistance"):0.00} × cosθ) v ÷ 効率 {BlockCoefficients.V("driveEfficiency"):0.0} = 約 {r.PowerW:0} W（m = 総質量（推定）{mass - payload:0.00} kg ＋ 荷物 {payload:0.0} kg）");
            }
        }

        public override void Step(SimContext ctx)
        {
            float s = Mathf.Min(_reach, _speed * ctx.T);
            float th = _angle * Mathf.Deg2Rad;
            ctx.Device = new Vector3(ClimbSlopeScenario.StartX(_angle, _len) + s * Mathf.Cos(th), s * Mathf.Sin(th), ClimbSlopeScenario.LaneZ);
            ctx.Aux = s;
        }

        public override void AfterFrame(SimContext ctx)
        {
            if (ctx.T > _done + 1f) ctx.Stop = true;
        }
    }
}
