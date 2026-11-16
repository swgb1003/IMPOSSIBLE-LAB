using System;
using System.Collections.Generic;
using System.Linq;

namespace ImpossibleLab.Core
{
    /// <summary>センサー値の取得口（実験・試用で別の実装を渡す）。真偽は 1 / 0。取れない値は false。</summary>
    public interface IProgramSensors
    {
        bool TryGet(string sensor, out float value);
    }

    /// <summary>発火の記録（時刻・ルール・動作・結果）。画面のタイムラインと実験の計測に使う。</summary>
    public sealed class ProgramEvent
    {
        public float Time;
        public string RuleId;
        /// <summary>start / end / move_joint / set_behavior / set_parameter / set_flag / wait / preempted / skipped / stopped</summary>
        public string Kind;
        public string Detail;
        public override string ToString() => $"{Time:0.00} {RuleId} {Kind} {Detail}";
    }

    /// <summary>
    /// 動作プログラムの固定のインタプリタ（仕様 28.4・28.6）。Unity に依存しない純 C# の状態機械。
    /// ・固定刻み（0.02 秒）ごとに、上のルールから順に条件を評価する。
    /// ・rise：条件が偽→真になったときに起動（実行中は再起動しない、cooldown あり）。while：真の間ずっと（wait・move_joint なし）。
    /// ・move_joint は目標を決めるだけで待たない（関節は決まった速さで目標へ動く）。順番の調整は wait で行う。
    /// ・同じ関節・機能を複数のルールが動かしたら、後から始まったルールを優先し、前のルールの動作は skipped として記録する。
    /// ・ループ・ジャンプ・式の評価はない。1刻みに実行する動作の数に上限があり、必ず終わる。例外が起きたらプログラムだけを止める。
    /// </summary>
    public sealed class MotionInterpreter
    {
        public const float Step = 0.02f;
        /// <summary>1刻みに実行する動作の上限（ルール8 × 動作8 に余裕をもたせた値）。</summary>
        public const int MaxActionsPerStep = 80;

        sealed class RuleState
        {
            public bool Running;
            public int Next;
            public float WaitUntil;
            public float StartedAt = float.NegativeInfinity;
            public float CooldownUntil;
            public bool LastCondition;
        }

        readonly MotionProgram _program;
        readonly Dictionary<string, JointSpec> _joints = new Dictionary<string, JointSpec>();
        readonly RuleState[] _state;
        readonly Dictionary<string, int> _jointOwner = new Dictionary<string, int>();
        readonly Dictionary<string, int> _behaviorOwner = new Dictionary<string, int>();

        public float Time { get; private set; }
        public readonly Dictionary<string, float> JointValues = new Dictionary<string, float>();
        public readonly Dictionary<string, float> JointTargets = new Dictionary<string, float>();
        readonly Dictionary<string, float> _jointSpeeds = new Dictionary<string, float>();
        public readonly Dictionary<string, bool> Flags = new Dictionary<string, bool>();
        /// <summary>機能のオン・オフ（set_behavior）。書かれていない機能はオンのまま。</summary>
        public readonly Dictionary<string, bool> BehaviorOn = new Dictionary<string, bool>();
        /// <summary>機能の値の上書き（set_parameter）。キーは "behavior.parameter"。</summary>
        public readonly Dictionary<string, float> ParameterOverrides = new Dictionary<string, float>();
        public readonly List<ProgramEvent> Events = new List<ProgramEvent>();
        public bool Stopped { get; private set; }
        public string StopReason { get; private set; }

        public MotionInterpreter(InventionDna dna)
        {
            _program = dna?.Program ?? new MotionProgram();
            foreach (var p in dna?.Parts ?? new List<PartSpec>())
                if (p.Joint != null)
                {
                    _joints[p.Id] = p.Joint;
                    JointValues[p.Id] = p.Joint.Home;
                    JointTargets[p.Id] = p.Joint.Home;
                    _jointSpeeds[p.Id] = p.Joint.Speed;
                }
            foreach (var f in _program.Flags) Flags[f] = false;
            _state = _program.Rules.Select(_ => new RuleState()).ToArray();
        }

        public bool IsRunning(string ruleId)
        {
            int i = _program.Rules.FindIndex(r => r.Id == ruleId);
            return i >= 0 && _state[i].Running;
        }

        /// <summary>1刻み進める。</summary>
        public void Tick(IProgramSensors sensors)
        {
            if (Stopped) return;
            try
            {
                Time += Step;
                int budget = MaxActionsPerStep;
                // 1. 条件の評価と起動（上のルールから順に）
                for (int i = 0; i < _program.Rules.Count; i++)
                {
                    var rule = _program.Rules[i];
                    var st = _state[i];
                    if (!rule.Enabled) { st.LastCondition = false; continue; }
                    bool cond = Evaluate(rule, st, sensors);
                    if (rule.Trigger == ProgramRule.While)
                    {
                        if (cond)
                        {
                            if (!st.LastCondition) { st.StartedAt = Time; Log(rule, "start", "while"); }
                            foreach (var a in rule.Do) { if (--budget < 0) { Stop("1刻みの動作の上限を超えました"); return; } Execute(i, rule, a); }
                        }
                        else if (st.LastCondition) Log(rule, "end", "while");
                    }
                    else if (cond && !st.LastCondition && !st.Running && Time >= st.CooldownUntil)
                    {
                        st.Running = true; st.Next = 0; st.WaitUntil = 0f; st.StartedAt = Time;
                        Log(rule, "start", null);
                    }
                    st.LastCondition = cond;
                }
                // 2. 実行中のルールを進める（wait まで、または終わりまで）
                for (int i = 0; i < _program.Rules.Count; i++)
                {
                    var rule = _program.Rules[i];
                    var st = _state[i];
                    if (!st.Running || Time < st.WaitUntil - 1e-5f) continue;
                    while (st.Next < rule.Do.Count)
                    {
                        if (--budget < 0) { Stop("1刻みの動作の上限を超えました"); return; }
                        var a = rule.Do[st.Next++];
                        if (a.Action == ProgramAction.Wait)
                        {
                            st.WaitUntil = Time + a.Seconds;
                            Log(rule, "wait", $"{a.Seconds:0.##}秒");
                            break;
                        }
                        Execute(i, rule, a);
                    }
                    if (st.Next >= rule.Do.Count && Time >= st.WaitUntil - 1e-5f)
                    {
                        st.Running = false;
                        st.CooldownUntil = Time + rule.Cooldown;
                        Log(rule, "end", null);
                    }
                }
                // 3. 関節を目標へ動かす（決まった速さ・範囲内）
                foreach (var id in _joints.Keys.ToList())
                {
                    float v = JointValues[id], t = JointTargets[id], step = _jointSpeeds[id] * Step;
                    JointValues[id] = Math.Abs(t - v) <= step ? t : v + Math.Sign(t - v) * step;
                }
            }
            catch (Exception e)
            {
                Stop("内部エラー: " + e.GetType().Name);
            }
        }

        public bool JointAtTarget(string joint, float tolerance = 0.5f) =>
            JointValues.TryGetValue(joint, out var v) && JointTargets.TryGetValue(joint, out var t) && Math.Abs(v - t) <= tolerance;

        bool Evaluate(ProgramRule rule, RuleState st, IProgramSensors sensors)
        {
            if (rule.When.Count == 0) return false;
            bool any = false, all = true;
            foreach (var c in rule.When)
            {
                bool ok = Check(c, st, sensors);
                any |= ok; all &= ok;
            }
            return rule.Mode == "any" ? any : all;
        }

        bool Check(ProgramCondition c, RuleState st, IProgramSensors sensors)
        {
            if (c.IsFlag) return Flags.TryGetValue(c.Flag, out var f) && f == c.Is;
            float v;
            switch (c.Sensor)
            {
                case "timer": v = st.Running || st.StartedAt > float.NegativeInfinity ? Time - st.StartedAt : Time; break;
                case "elapsed": v = Time; break;
                case "joint_position": if (!JointValues.TryGetValue(c.Joint ?? "", out v)) return false; break;
                default: if (!sensors.TryGet(c.Sensor, out v)) return false; break;
            }
            if (ProgramCatalog.Sensor(c.Sensor)?.IsBool == true) return (v >= 0.5f) == c.Is;
            switch (c.Op)
            {
                case "<": return v < c.Value;
                case "<=": return v <= c.Value;
                case ">": return v > c.Value;
                case ">=": return v >= c.Value;
                default: return false;
            }
        }

        void Execute(int ruleIndex, ProgramRule rule, ProgramAction a)
        {
            switch (a.Action)
            {
                case ProgramAction.MoveJoint:
                    if (!_joints.TryGetValue(a.Joint ?? "", out var j)) { Log(rule, "skipped", $"関節 {a.Joint} がありません"); return; }
                    if (!Claim(_jointOwner, a.Joint, ruleIndex, rule)) return;
                    JointTargets[a.Joint] = Math.Max(j.Min, Math.Min(j.Max, a.Target));
                    _jointSpeeds[a.Joint] = a.Speed ?? j.Speed;
                    Log(rule, ProgramAction.MoveJoint, $"{a.Joint} → {a.Target:0.##}");
                    break;
                case ProgramAction.SetBehavior:
                    if (!Claim(_behaviorOwner, a.Behavior, ruleIndex, rule)) return;
                    if (BehaviorOn.TryGetValue(a.Behavior, out var was) && was == a.On && rule.Trigger == ProgramRule.While) return; // while で毎刻み同じ値は記録しない
                    BehaviorOn[a.Behavior] = a.On;
                    Log(rule, ProgramAction.SetBehavior, $"{a.Behavior} {(a.On ? "オン" : "オフ")}");
                    break;
                case ProgramAction.SetParameter:
                {
                    if (!Claim(_behaviorOwner, a.Behavior, ruleIndex, rule)) return;
                    string key = a.Behavior + "." + a.Parameter;
                    if (ParameterOverrides.TryGetValue(key, out var old) && Math.Abs(old - a.Value) < 1e-6f && rule.Trigger == ProgramRule.While) return;
                    ParameterOverrides[key] = a.Value;
                    Log(rule, ProgramAction.SetParameter, $"{key} = {a.Value:0.###}");
                    break;
                }
                case ProgramAction.SetFlag:
                    if (Flags.TryGetValue(a.Name, out var fv) && fv == a.FlagValue && rule.Trigger == ProgramRule.While) return;
                    Flags[a.Name] = a.FlagValue;
                    Log(rule, ProgramAction.SetFlag, $"{a.Name} = {(a.FlagValue ? "真" : "偽")}");
                    break;
            }
        }

        /// <summary>同じ対象を別のルールが動かしていたら、後から始まったルールを優先する。</summary>
        bool Claim(Dictionary<string, int> owners, string key, int ruleIndex, ProgramRule rule)
        {
            if (key != null && owners.TryGetValue(key, out var other) && other != ruleIndex && _state[other].Running)
            {
                if (_state[other].StartedAt > _state[ruleIndex].StartedAt)
                {
                    Log(rule, "skipped", $"{key}：後から始まったルール「{_program.Rules[other].Id}」を優先");
                    return false;
                }
                Log(rule, "preempted", $"{key}：ルール「{_program.Rules[other].Id}」の動作を打ち切り");
            }
            if (key != null) owners[key] = ruleIndex;
            return true;
        }

        void Stop(string reason)
        {
            Stopped = true;
            StopReason = reason;
            Events.Add(new ProgramEvent { Time = Time, RuleId = "", Kind = "stopped", Detail = reason });
        }

        void Log(ProgramRule rule, string kind, string detail) =>
            Events.Add(new ProgramEvent { Time = Time, RuleId = rule.Id, Kind = kind, Detail = detail });
    }
}
