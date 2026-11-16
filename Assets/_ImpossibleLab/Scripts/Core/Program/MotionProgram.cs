using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ImpossibleLab.Core
{
    /// <summary>拡張機能の切り替え（仕様 30.2）。切ると v2.0 と同じ動作に戻る（データは読み書きするが実行しない）。</summary>
    public static class Features
    {
        public static bool MotionProgram = true;
    }

    /// <summary>部品の簡易な関節（仕様 28.2）。物理の関節は使わず、決まった速さで目標値へ動く。</summary>
    public sealed class JointSpec
    {
        public const string Hinge = "hinge", Slider = "slider";
        /// <summary>hinge（角度・度）／slider（移動量・m）。</summary>
        public string Type = Hinge;
        /// <summary>親の部品 id（空なら発明の本体に付く）。</summary>
        public string Parent;
        /// <summary>x / y / z。</summary>
        public string Axis = "y";
        /// <summary>回転・移動の中心（部品の座標系、m）。</summary>
        public Vector3 Pivot;
        public float Min, Max, Speed, Home;

        public JointSpec Clone() => (JointSpec)MemberwiseClone();
        public bool IsHinge => Type == Hinge;
    }

    /// <summary>動作プログラム（仕様 28.4）。AI の出力はこのデータとしてだけ扱い、固定のインタプリタで解釈する。</summary>
    public sealed class MotionProgram
    {
        public int Version = 1;
        public List<string> Flags = new List<string>();
        public List<ProgramRule> Rules = new List<ProgramRule>();

        public MotionProgram Clone() => new MotionProgram
        {
            Version = Version,
            Flags = new List<string>(Flags),
            Rules = Rules.ConvertAll(r => r.Clone()),
        };
    }

    public sealed class ProgramRule
    {
        public const string Rise = "rise", While = "while";
        public string Id = "";
        public string Label = "";
        public string Trigger = Rise;
        public float Cooldown;
        /// <summary>all（すべて成り立つ）／any（どれかが成り立つ）。入れ子にしない。</summary>
        public string Mode = "all";
        public List<ProgramCondition> When = new List<ProgramCondition>();
        public List<ProgramAction> Do = new List<ProgramAction>();
        /// <summary>S03 のオン・オフ（JSON では "enabled"、既定 true）。</summary>
        public bool Enabled = true;

        public ProgramRule Clone()
        {
            var c = (ProgramRule)MemberwiseClone();
            c.When = When.ConvertAll(w => w.Clone());
            c.Do = Do.ConvertAll(d => d.Clone());
            return c;
        }
    }

    /// <summary>条件の1項目。センサーの比較（sensor・op・value）、真偽のセンサー（sensor・is）、フラグの真偽（flag・is）。</summary>
    public sealed class ProgramCondition
    {
        public string Sensor;
        public string Op;
        public float Value;
        /// <summary>joint_position のときの関節（部品 id）。</summary>
        public string Joint;
        public string Flag;
        public bool Is;

        public bool IsFlag => Flag != null;
        public ProgramCondition Clone() => (ProgramCondition)MemberwiseClone();
    }

    public sealed class ProgramAction
    {
        public const string MoveJoint = "move_joint", SetBehavior = "set_behavior", SetParameter = "set_parameter", SetFlag = "set_flag", Wait = "wait";
        public string Action = "";
        public string Joint;
        public float Target;
        public float? Speed;
        public string Behavior;
        public bool On;
        public string Parameter;
        public float Value;
        public string Name;
        public bool FlagValue;
        public float Seconds;

        public ProgramAction Clone() => (ProgramAction)MemberwiseClone();
    }

    /// <summary>センサーの種類（仕様 28.3）。</summary>
    public sealed class SensorInfo
    {
        public string Id, LabelJa, Unit;
        public float Min, Max;
        public bool IsBool;
        /// <summary>必要なもの：null（なし）、"behavior:xxx"、"part:xxx"、"mount:body"、"joint"、"flag"。</summary>
        public string Requires;
    }

    /// <summary>動作プログラムの命令・上限（仕様 28.3・28.4）。足すときはカタログ・proxy/catalog.json・単体テストに同時に登録する。</summary>
    public static class ProgramCatalog
    {
        public const int MaxRules = 8, MaxActionsPerRule = 8, MaxFlags = 4, MaxConditions = 4, MaxLabelLength = 40;
        public const int MaxJoints = 6, MaxJointDepth = 3;
        public const float MaxCooldown = 60f, MinWait = 0.1f, MaxWait = 60f;
        public const float HingeLimit = 180f, HingeSpeedMin = 10f, HingeSpeedMax = 360f;
        public const float SliderLimit = 1.0f, SliderSpeedMin = 0.05f, SliderSpeedMax = 1.0f;

        public static readonly SensorInfo[] Sensors =
        {
            new SensorInfo { Id = "timer", LabelJa = "ルールが始まってからの時間", Unit = "秒", Min = 0, Max = 600 },
            new SensorInfo { Id = "elapsed", LabelJa = "始めてからの時間", Unit = "秒", Min = 0, Max = 600 },
            // 上限は発明の detect.rangeM（検証時に置き換える）
            new SensorInfo { Id = "target_distance", LabelJa = "対象までの距離", Unit = "m", Min = 0, Max = 30, Requires = "behavior:detect" },
            new SensorInfo { Id = "target_detected", LabelJa = "対象を検知している", IsBool = true, Requires = "behavior:detect" },
            new SensorInfo { Id = "ambient_light", LabelJa = "周りの明るさ", Unit = "lx", Min = 0, Max = 100000, Requires = "part:sensor" },
            new SensorInfo { Id = "rain", LabelJa = "雨が当たっている", IsBool = true, Requires = "part:sensor" },
            new SensorInfo { Id = "temperature", LabelJa = "気温", Unit = "°C", Min = -20, Max = 45, Requires = "part:sensor" },
            new SensorInfo { Id = "user_moving", LabelJa = "利用者が歩いている", IsBool = true, Requires = "mount:body" },
            new SensorInfo { Id = "holding", LabelJa = "つかんでいる", IsBool = true, Requires = "behavior:grip" },
            new SensorInfo { Id = "joint_position", LabelJa = "関節の今の値", Unit = "", Min = -180, Max = 180, Requires = "joint" },
            // 仕様の power_supply は機能ブロックに無いため、電池の部品で代える（DECISIONS D-28）
            new SensorInfo { Id = "battery_percent", LabelJa = "電池の残量", Unit = "%", Min = 0, Max = 100, Requires = "part:battery" },
        };

        public static readonly string[] Actions = { ProgramAction.MoveJoint, ProgramAction.SetBehavior, ProgramAction.SetParameter, ProgramAction.SetFlag, ProgramAction.Wait };
        public static readonly string[] Ops = { "<", "<=", ">", ">=" };

        public static SensorInfo Sensor(string id) => Sensors.FirstOrDefault(s => s.Id == id);
        public static bool IsAction(string id) => Actions.Contains(id);

        /// <summary>体に付ける取り付け位置か（重さの目安がある位置）。user_moving に使う。</summary>
        public static bool IsBodyMount(string mount) => MassModel.MountLimitKg(mount).HasValue;
    }
}
