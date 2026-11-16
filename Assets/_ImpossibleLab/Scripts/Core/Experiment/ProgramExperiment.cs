using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ImpossibleLab.Core
{
    /// <summary>
    /// 期待（仕様 28.7）：「いつ・何が起きるべきか」。例：対象が 1m 以内にいる時間のうち 90% 以上でフタが 80° 以上開いている。
    /// </summary>
    public sealed class ProgramExpectation
    {
        public string Sensor = "target_distance", Op = "<";
        public float Value = 1f;
        public string Joint = "";
        public float AtLeast;
        public float MinRatio = 0.7f;
        /// <summary>関節の代わりに「この機能がオンになっている」ことを期待する（足元灯など）。Joint が空のときに使う。</summary>
        public string Behavior;

        public ProgramExpectation Clone() => (ProgramExpectation)MemberwiseClone();

        /// <summary>動作プログラムから既定の期待を作る：対象までの距離の条件で関節を動かす最初のルール（目標の 8 割以上開いている）。</summary>
        public static ProgramExpectation FromProgram(InventionDna dna)
        {
            if (dna?.Program == null) return null;
            foreach (var rule in dna.Program.Rules.Where(r => r.Enabled))
            {
                var c = rule.When.FirstOrDefault(w => w.Sensor == "target_distance" && (w.Op == "<" || w.Op == "<="));
                var mv = rule.Do.FirstOrDefault(a => a.Action == ProgramAction.MoveJoint);
                var j = mv != null ? dna.FindPart(mv.Joint)?.Joint : null;
                if (c == null || j == null) continue;
                float home = j.Home;
                return new ProgramExpectation { Sensor = c.Sensor, Op = c.Op, Value = c.Value, Joint = mv.Joint, AtLeast = home + (mv.Target - home) * 0.8f, MinRatio = 0.7f };
            }
            // 関節がなければ、距離の条件で機能をオンにするルール
            foreach (var rule in dna.Program.Rules.Where(r => r.Enabled))
            {
                var c = rule.When.FirstOrDefault(w => w.Sensor == "target_distance" && (w.Op == "<" || w.Op == "<="));
                var on = rule.Do.FirstOrDefault(a => a.Action == ProgramAction.SetBehavior && a.On);
                if (c != null && on != null)
                    return new ProgramExpectation { Sensor = c.Sensor, Op = c.Op, Value = c.Value, Joint = "", Behavior = on.Behavior, MinRatio = 0.7f };
            }
            return null;
        }

        public string Describe(InventionDna dna)
        {
            var s = ProgramCatalog.Sensor(Sensor);
            if (string.IsNullOrEmpty(Joint) && !string.IsNullOrEmpty(Behavior))
                return $"{s?.LabelJa ?? Sensor} {Op} {Value:0.##}{s?.Unit} の間、{LabCatalog.Behavior(Behavior)?.LabelJa ?? Behavior}がオン（{MinRatio * 100f:0}% 以上の時間）";
            var j = dna?.FindPart(Joint)?.Joint;
            string unit = j != null && !j.IsHinge ? " m" : "°";
            return $"{s?.LabelJa ?? Sensor} {Op} {Value:0.##}{s?.Unit} の間、{Joint} が {AtLeast:0.##}{unit} 以上（{MinRatio * 100f:0}% 以上の時間）";
        }
    }

    /// <summary>
    /// approach_device（仕様 28.7）：設置した発明に、人型モデルが近づいて留まり、離れる。設定した回数・速さでくり返す。
    /// 発明は原点、人型モデルは +Z 方向の直線上を動く（距離＝Leader の z）。
    /// </summary>
    public sealed class ApproachDeviceScenario : IScenario
    {
        public float MaxTime => 0f;
        float _far, _near, _speed, _dwell;
        int _count, _done;
        int _phase; // 0 近づく 1 留まる 2 離れる 3 間をあける
        float _phaseT;

        public void Begin(SimContext ctx)
        {
            var p = ctx.Plan;
            _far = p.Get("approach", "farM");
            _near = Mathf.Min(p.Get("approach", "nearM"), _far - 0.2f);
            _speed = p.Get("approach", "speed");
            _dwell = p.Get("approach", "dwellSec");
            _count = Mathf.Max(1, Mathf.RoundToInt(p.Get("approach", "count")));
            ctx.Device = Vector3.zero;
            ctx.Leader = new Vector2(0f, _far);
            _phase = 0; _phaseT = 0f; _done = 0;
        }

        public void StepAvatar(SimContext ctx)
        {
            float dt = ScenarioRunner.Dt;
            float z = ctx.Leader.y;
            _phaseT += dt;
            switch (_phase)
            {
                case 0: z = Mathf.Max(_near, z - _speed * dt); if (z <= _near + 1e-4f) Next(); break;
                case 1: if (_phaseT >= _dwell) Next(); break;
                case 2: z = Mathf.Min(_far, z + _speed * dt); if (z >= _far - 1e-4f) Next(); break;
                case 3:
                    if (_phaseT >= 2f)
                    {
                        _done++;
                        if (_done >= _count) { ctx.Stop = true; ctx.LeaderFinished = true; ctx.LeaderFinishTime = ctx.T; }
                        else Next();
                    }
                    break;
            }
            ctx.Leader = new Vector2(0f, z);
        }

        void Next() { _phase = (_phase + 1) % 4; _phaseT = 0f; }
    }

    /// <summary>
    /// 動作プログラムの実験のモデル（仕様 28.6・28.7）。試用と同じ MotionInterpreter を固定刻みで動かし、
    /// 動作の回数・反応時間・期待の達成率・関節の電力量（REALITY）を計る。発火の記録は結果のタイムラインに入れる。
    /// </summary>
    public sealed class ProgramModel : IBlockModel
    {
        MotionInterpreter _it;
        ExpSensors _sensors;
        ProgramExpectation _expect;
        string[] _jointIds;
        int _seenEvents;
        readonly List<(string rule, float t0, string joint, float target)> _pending = new List<(string, float, string, float)>();
        readonly List<float> _reactions = new List<float>();
        int _expectTicks, _expectOk;
        float _movingSec;

        sealed class ExpSensors : IProgramSensors
        {
            public SimContext Ctx;
            public bool TryGet(string sensor, out float value)
            {
                float d = new Vector2(Ctx.Leader.x - Ctx.Device.x, Ctx.Leader.y - Ctx.Device.z).magnitude;
                switch (sensor)
                {
                    case "target_distance": value = d; return true;
                    case "target_detected": value = d <= Ctx.Dna.GetParameter("detect", "rangeM", 0f) ? 1f : 0f; return true;
                    case "ambient_light": value = 500f; return true;
                    case "temperature": value = 22f; return true;
                    case "rain": value = 0f; return true;
                    case "user_moving": value = 0f; return true;
                    case "holding": value = 0f; return true;
                    case "battery_percent": value = 100f; return true;
                    default: value = 0f; return false;
                }
            }
        }

        public void Begin(SimContext ctx)
        {
            _it = new MotionInterpreter(ctx.Dna);
            _sensors = new ExpSensors { Ctx = ctx };
            _expect = ctx.Plan.Expect ?? ProgramExpectation.FromProgram(ctx.Dna);
            _jointIds = ctx.Dna.Parts.Where(p => p.Joint != null).Select(p => p.Id).ToArray();
            ctx.Joints = _jointIds.Select(id => _it.JointValues[id]).ToArray();
            ctx.Result.JointIds = _jointIds.ToList();
        }

        public void Step(SimContext ctx) => _it.Tick(_sensors);

        public void AfterTick(SimContext ctx)
        {
            for (int i = 0; i < _jointIds.Length; i++) ctx.Joints[i] = _it.JointValues[_jointIds[i]];
            // 起動したルールの、最初に動かす関節が目標に着くまでの時間
            for (; _seenEvents < _it.Events.Count; _seenEvents++)
            {
                var e = _it.Events[_seenEvents];
                if (e.Kind != "start") continue;
                var rule = ctx.Dna.Program.Rules.Find(r => r.Id == e.RuleId);
                var first = rule?.Do.FirstOrDefault(a => a.Action == ProgramAction.MoveJoint || (a.Action == ProgramAction.SetBehavior && a.On));
                if (first?.Action == ProgramAction.MoveJoint) _pending.Add((rule.Id, e.Time, first.Joint, first.Target));
                else if (first != null) _pending.Add((rule.Id, e.Time, "behavior:" + first.Behavior, 1f));
            }
            for (int k = _pending.Count - 1; k >= 0; k--)
            {
                var p = _pending[k];
                bool reached = p.joint.StartsWith("behavior:")
                    ? _it.BehaviorOn.TryGetValue(p.joint.Substring(9), out var on) && on
                    : System.Math.Abs(_it.JointValues[p.joint] - p.target) <= 0.5f;
                if (reached) { _reactions.Add(_it.Time - p.t0); _pending.RemoveAt(k); }
            }
            // 期待：条件が成り立っている時間のうち、関節が基準以上の割合
            if (_expect != null && ExpectCondition(ctx))
            {
                if (!string.IsNullOrEmpty(_expect.Joint) && _it.JointValues.TryGetValue(_expect.Joint, out var jv))
                {
                    _expectTicks++;
                    if (jv >= _expect.AtLeast - 1e-4f) _expectOk++;
                }
                else if (!string.IsNullOrEmpty(_expect.Behavior))
                {
                    _expectTicks++;
                    // 書かれていない機能はオンのまま（MotionInterpreter の決まり）
                    if (!_it.BehaviorOn.TryGetValue(_expect.Behavior, out var bon) || bon) _expectOk++;
                }
            }
            foreach (var id in _jointIds) if (!_it.JointAtTarget(id, 1e-3f)) _movingSec += ScenarioRunner.Dt;
        }

        bool ExpectCondition(SimContext ctx)
        {
            if (!_sensors.TryGet(_expect.Sensor, out var v)) return false;
            switch (_expect.Op)
            {
                case "<": return v < _expect.Value;
                case "<=": return v <= _expect.Value;
                case ">": return v > _expect.Value;
                case ">=": return v >= _expect.Value;
                default: return false;
            }
        }

        public void AfterFrame(SimContext ctx) { }

        public void End(SimContext ctx)
        {
            var r = ctx.Result;
            r.Timeline = _it.Events.Where(e => e.Kind != "wait").Select(e => e.ToString()).ToList();
            ctx.AddMetric("ruleFiredCount", _it.Events.Count(e => e.Kind == "start"));
            if (_reactions.Count > 0) ctx.AddMetric("reactionTime", _reactions.Average());
            else if (_pending.Count > 0) { ctx.AddMetric("reactionTime", ExperimentCatalog.ReactionTimeout); r.Limits.Add("関節が目標に着く前に実験が終わった"); }
            else r.Limits.Add("関節を動かすルールが一度も起動しなかった");
            if (_expect != null && _expectTicks > 0) ctx.AddMetric("expectationRate", (float)_expectOk / _expectTicks);
            else if (_expect != null) r.Limits.Add("期待の条件が一度も成り立たなかった（人型モデルが近づく距離を見直す）");
            else r.Limits.Add("期待を決められなかった（距離の条件で関節を動かすルールがない）");
            if (ctx.Reality) ctx.AddMetric("jointEnergyWh", _movingSec * BlockCoefficients.V("jointMotorPowerW") / 3600f);
            if (_it.Stopped) r.Limits.Add("動作プログラムを止めました（" + _it.StopReason + "）");
        }
    }
}
