using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ImpossibleLab.Core
{
    /// <summary>
    /// 動作プログラムを画面の文にする（仕様 28.9）。文は AI ではなく、プログラムからアプリが定型文で作る
    /// （AI の説明と実際の動きのずれを防ぐ）。例：「① 対象までの距離が 1.0m より近く、状態「opened」が偽のとき → 板（lid）を 100° → … 」
    /// </summary>
    public static class ProgramText
    {
        static readonly string[] Circled = { "①", "②", "③", "④", "⑤", "⑥", "⑦", "⑧" };

        public static string Number(int i) => i >= 0 && i < Circled.Length ? Circled[i] : (i + 1) + ".";

        static string F(float v) => v.ToString(v == (int)v ? "0" : "0.##", CultureInfo.InvariantCulture);

        public static string PartName(InventionDna dna, string id)
        {
            var p = dna?.FindPart(id ?? "");
            return p == null ? id : $"{LabCatalog.Part(p.Type)?.LabelJa ?? p.Type}（{p.Id}）";
        }

        public static string JointValue(InventionDna dna, string joint, float v)
        {
            var j = dna?.FindPart(joint ?? "")?.Joint;
            return j != null && !j.IsHinge ? F(v) + " m" : F(v) + "°";
        }

        public static string Condition(ProgramCondition c, InventionDna dna)
        {
            if (c.IsFlag) return $"状態「{c.Flag}」が{(c.Is ? "真" : "偽")}";
            var s = ProgramCatalog.Sensor(c.Sensor);
            string label = s?.LabelJa ?? c.Sensor;
            if (s != null && s.IsBool) return c.Is ? label : $"{label}ではない";
            string value = c.Sensor == "joint_position" ? JointValue(dna, c.Joint, c.Value) : F(c.Value) + (s?.Unit ?? "");
            if (c.Sensor == "joint_position") label = PartName(dna, c.Joint) + "の角度";
            switch (c.Op)
            {
                case "<": return $"{label}が {value} より小さい";
                case "<=": return $"{label}が {value} 以下";
                case ">": return $"{label}が {value} より大きい";
                case ">=": return $"{label}が {value} 以上";
                default: return $"{label} {c.Op} {value}";
            }
        }

        public static string Action(ProgramAction a, InventionDna dna)
        {
            switch (a.Action)
            {
                case ProgramAction.MoveJoint:
                {
                    // 速さを指定したときだけ添える（改良で「動かす速さ」を変えたことが手順の文で分かるように）
                    var j = dna?.FindPart(a.Joint ?? "")?.Joint;
                    string speed = a.Speed.HasValue && j != null ? $"（{a.Speed.Value:0.##}{(j.IsHinge ? "°" : " m")}/秒）" : "";
                    return $"{PartName(dna, a.Joint)}を {JointValue(dna, a.Joint, a.Target)}へ{speed}";
                }
                case ProgramAction.SetBehavior: return $"{LabCatalog.Behavior(a.Behavior)?.LabelJa ?? a.Behavior}を{(a.On ? "オン" : "オフ")}";
                case ProgramAction.SetParameter:
                {
                    var p = LabCatalog.Behavior(a.Behavior)?.Param(a.Parameter);
                    return p != null ? $"{p.LabelJa}を {LabCatalog.FormatValue(p, a.Value)} に" : $"{a.Behavior}.{a.Parameter} を {F(a.Value)} に";
                }
                case ProgramAction.SetFlag: return $"状態「{a.Name}」を{(a.FlagValue ? "真" : "偽")}に";
                case ProgramAction.Wait: return $"{F(a.Seconds)}秒待つ";
                default: return a.Action;
            }
        }

        /// <summary>1つのルールの手順（条件 → 動作 → …）。</summary>
        public static string Rule(ProgramRule r, int index, InventionDna dna)
        {
            string join = r.Mode == "any" ? "、または" : "、";
            string when = string.Join(join, r.When.Select(c => Condition(c, dna)));
            string trig = r.Trigger == ProgramRule.While ? "間ずっと" : "とき";
            string steps = string.Join(" → ", r.Do.Select(a => Action(a, dna)));
            string off = r.Enabled ? "" : "（オフ）";
            return $"{Number(index)} {when}{trig} → {steps}{off}";
        }

        public static List<string> Rules(InventionDna dna) =>
            dna?.Program == null ? new List<string>() : dna.Program.Rules.Select((r, i) => Rule(r, i, dna)).ToList();

        /// <summary>タイムラインの1行（ProgramEvent の文字列 "時刻 ルール 種類 詳細" から作る）。</summary>
        public static string TimelineLine(string ev, InventionDna dna)
        {
            var parts = (ev ?? "").Split(new[] { ' ' }, 4);
            if (parts.Length < 3) return ev;
            string time = parts[0], ruleId = parts[1], kind = parts[2], detail = parts.Length > 3 ? parts[3] : "";
            var rule = dna?.Program?.Rules.Find(r => r.Id == ruleId);
            string name = rule != null && !string.IsNullOrEmpty(rule.Label) ? rule.Label : ruleId;
            switch (kind)
            {
                case "start": return $"{time}秒　▶ {name}";
                case "end": return $"{time}秒　■ {name} 終わり";
                case ProgramAction.MoveJoint: return $"{time}秒　　{detail}";
                case "preempted": case "skipped": return $"{time}秒　　! {detail}";
                case "stopped": return $"{time}秒　× 止めました：{detail}";
                default: return $"{time}秒　　{detail}";
            }
        }
    }
}
