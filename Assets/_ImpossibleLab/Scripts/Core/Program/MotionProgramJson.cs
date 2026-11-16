using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ImpossibleLab.Core
{
    /// <summary>
    /// 関節と動作プログラムの検証・書き出し（仕様 28.5）。DnaJson から呼ぶ。
    /// 未知の命令・フィールドは拒否、参照は存在を確認、値は範囲内でなければ拒否する（設計時）。
    /// 一度も起動しないルール・使われないフラグなどは拒否せず注意（Warnings）に出す。
    /// </summary>
    public static class MotionProgramJson
    {
        static readonly HashSet<string> JointFields = new HashSet<string> { "type", "parent", "axis", "pivot", "min", "max", "speed", "home" };
        static readonly HashSet<string> ProgramFields = new HashSet<string> { "version", "flags", "rules" };
        static readonly HashSet<string> RuleFields = new HashSet<string> { "id", "label", "trigger", "cooldown", "when", "do", "enabled" };
        static readonly HashSet<string> SensorCondFields = new HashSet<string> { "sensor", "op", "value", "joint" };
        static readonly HashSet<string> BoolSensorCondFields = new HashSet<string> { "sensor", "is" };
        static readonly HashSet<string> FlagCondFields = new HashSet<string> { "flag", "is" };
        static readonly Dictionary<string, HashSet<string>> ActionFields = new Dictionary<string, HashSet<string>>
        {
            [ProgramAction.MoveJoint] = new HashSet<string> { "action", "joint", "target", "speed" },
            [ProgramAction.SetBehavior] = new HashSet<string> { "action", "behavior", "on" },
            [ProgramAction.SetParameter] = new HashSet<string> { "action", "behavior", "parameter", "value" },
            [ProgramAction.SetFlag] = new HashSet<string> { "action", "name", "value" },
            [ProgramAction.Wait] = new HashSet<string> { "action", "seconds" },
        };

        // ---------------- 関節 ----------------
        public static JointSpec ParseJoint(JToken t, string at, ValidationReport r)
        {
            if (!(t is JObject o)) { r.Errors.Add(at + " はオブジェクトです"); return null; }
            Reject(o, JointFields, at, r);
            var j = new JointSpec();
            j.Type = S(o, "type", at, r, true, 8);
            if (j.Type != null && j.Type != JointSpec.Hinge && j.Type != JointSpec.Slider) { r.Errors.Add($"{at}.type は hinge か slider です: {j.Type}"); return null; }
            j.Parent = S(o, "parent", at, r, false, LabCatalog.MaxIdLength);
            j.Axis = S(o, "axis", at, r, true, 2);
            if (j.Axis != null && j.Axis != "x" && j.Axis != "y" && j.Axis != "z") r.Errors.Add($"{at}.axis は x / y / z です");
            if (o.TryGetValue("pivot", out var pv))
            {
                if (pv is JArray a && a.Count == 3 && a.All(x => x.Type == JTokenType.Float || x.Type == JTokenType.Integer))
                {
                    j.Pivot = new Vector3(a[0].Value<float>(), a[1].Value<float>(), a[2].Value<float>());
                    if (j.Pivot.magnitude > 2f) r.Errors.Add($"{at}.pivot が遠すぎます（2m 以内）");
                }
                else r.Errors.Add($"{at}.pivot は数値3つの配列です");
            }
            bool hinge = j.Type == JointSpec.Hinge;
            float lim = hinge ? ProgramCatalog.HingeLimit : ProgramCatalog.SliderLimit;
            j.Min = N(o, "min", at, r, -lim, lim) ?? 0f;
            j.Max = N(o, "max", at, r, -lim, lim) ?? 0f;
            if (j.Min >= j.Max) r.Errors.Add($"{at} の min は max より小さくします");
            j.Speed = N(o, "speed", at, r, hinge ? ProgramCatalog.HingeSpeedMin : ProgramCatalog.SliderSpeedMin, hinge ? ProgramCatalog.HingeSpeedMax : ProgramCatalog.SliderSpeedMax) ?? 0f;
            var home = N(o, "home", at, r, -lim, lim, required: false);
            j.Home = home ?? Mathf.Clamp(0f, j.Min, j.Max);
            if (home.HasValue && (j.Home < j.Min || j.Home > j.Max)) r.Errors.Add($"{at}.home は min〜max の中にします");
            return j;
        }

        /// <summary>関節どうしの関係（親の存在・循環・深さ・数）と、駆動部の部品の有無。</summary>
        public static void ValidateJoints(InventionDna dna, ValidationReport r)
        {
            var joints = dna.Parts.Where(p => p.Joint != null).ToList();
            if (joints.Count == 0) return;
            if (joints.Count > ProgramCatalog.MaxJoints) r.Errors.Add($"関節は {ProgramCatalog.MaxJoints} 個までです（{joints.Count} 個）");
            if (!dna.Parts.Any(p => p.Type == "motor")) r.Errors.Add("関節を持つ発明には駆動部（motor）の部品が必要です");
            foreach (var p in joints)
            {
                if (string.IsNullOrEmpty(p.Joint.Parent)) continue;
                if (p.Joint.Parent == p.Id) { r.Errors.Add($"関節 {p.Id} の親が自分自身です"); continue; }
                if (dna.FindPart(p.Joint.Parent) == null) { r.Errors.Add($"関節 {p.Id} の親 {p.Joint.Parent} がありません"); continue; }
                // 親をたどる：循環と深さ
                int depth = 1;
                var seen = new HashSet<string> { p.Id };
                string cur = p.Joint.Parent;
                while (!string.IsNullOrEmpty(cur))
                {
                    if (!seen.Add(cur)) { r.Errors.Add($"関節 {p.Id} の親子関係が循環しています"); break; }
                    var cp = dna.FindPart(cur);
                    if (cp?.Joint == null) break;
                    depth++;
                    cur = cp.Joint.Parent;
                }
                if (depth > ProgramCatalog.MaxJointDepth) r.Errors.Add($"関節 {p.Id} の親子の深さが {ProgramCatalog.MaxJointDepth} を超えています");
            }
        }

        // ---------------- 動作プログラム ----------------
        public static MotionProgram ParseProgram(JToken t, InventionDna dna, ValidationReport r)
        {
            if (t == null || t.Type == JTokenType.Null) return null;
            if (!(t is JObject o)) { r.Errors.Add("program はオブジェクトです"); return null; }
            Reject(o, ProgramFields, "program", r);
            var p = new MotionProgram();
            var ver = o.TryGetValue("version", out var vt) ? vt : null;
            if (ver == null || ver.Type != JTokenType.Integer || ver.Value<int>() != 1) r.Errors.Add("program.version は 1 です");
            if (o.TryGetValue("flags", out var ft))
            {
                if (!(ft is JArray fa)) r.Errors.Add("program.flags は文字列の配列です");
                else
                {
                    if (fa.Count > ProgramCatalog.MaxFlags) r.Errors.Add($"フラグは {ProgramCatalog.MaxFlags} 個までです");
                    foreach (var f in fa.Take(ProgramCatalog.MaxFlags + 1))
                    {
                        if (f.Type != JTokenType.String || !DnaJson.IsSafeId((string)f)) { r.Errors.Add("フラグ名は英小文字・数字・-・_ です"); continue; }
                        if (p.Flags.Contains((string)f)) r.Errors.Add($"フラグ {(string)f} が重複しています");
                        else p.Flags.Add((string)f);
                    }
                }
            }
            if (!o.TryGetValue("rules", out var rt) || !(rt is JArray ra) || ra.Count == 0) { r.Errors.Add("program.rules（ルールの配列）がありません"); return null; }
            if (ra.Count > ProgramCatalog.MaxRules) { r.Errors.Add($"ルールは {ProgramCatalog.MaxRules} 個までです（{ra.Count} 個）"); return null; }
            var ids = new HashSet<string>();
            for (int i = 0; i < ra.Count; i++)
            {
                var rule = ParseRule(ra[i], $"program.rules[{i}]", dna, p, r);
                if (rule == null) continue;
                if (!ids.Add(rule.Id)) r.Errors.Add($"ルール id が重複しています: {rule.Id}");
                p.Rules.Add(rule);
            }
            if (r.Errors.Count == 0) AddWarnings(p, dna, r);
            return p;
        }

        static ProgramRule ParseRule(JToken t, string at, InventionDna dna, MotionProgram prog, ValidationReport r)
        {
            if (!(t is JObject o)) { r.Errors.Add(at + " はオブジェクトです"); return null; }
            Reject(o, RuleFields, at, r);
            var rule = new ProgramRule();
            rule.Id = S(o, "id", at, r, true, LabCatalog.MaxIdLength) ?? "";
            if (rule.Id.Length > 0 && !DnaJson.IsSafeId(rule.Id)) r.Errors.Add($"{at}.id は英小文字・数字・-・_ です");
            rule.Label = S(o, "label", at, r, false, ProgramCatalog.MaxLabelLength) ?? "";
            rule.Trigger = S(o, "trigger", at, r, false, 8) ?? ProgramRule.Rise;
            if (rule.Trigger != ProgramRule.Rise && rule.Trigger != ProgramRule.While) r.Errors.Add($"{at}.trigger は rise か while です");
            rule.Cooldown = N(o, "cooldown", at, r, 0f, ProgramCatalog.MaxCooldown, required: false) ?? 0f;
            if (o.TryGetValue("enabled", out var en)) { if (en.Type == JTokenType.Boolean) rule.Enabled = en.Value<bool>(); else r.Errors.Add($"{at}.enabled は真偽です"); }

            // 条件：{ "all": [...] } か { "any": [...] }（1段だけ）
            if (!(o.TryGetValue("when", out var wt) && wt is JObject wo) || wo.Count != 1 || !(wo.Properties().First().Name == "all" || wo.Properties().First().Name == "any"))
            { r.Errors.Add($"{at}.when は {{\"all\": [...]}} か {{\"any\": [...]}} です"); }
            else
            {
                rule.Mode = wo.Properties().First().Name;
                if (!(wo[rule.Mode] is JArray items) || items.Count == 0) r.Errors.Add($"{at}.when.{rule.Mode} は1つ以上の条件の配列です");
                else if (items.Count > ProgramCatalog.MaxConditions) r.Errors.Add($"{at} の条件は {ProgramCatalog.MaxConditions} つまでです");
                else for (int k = 0; k < items.Count; k++)
                {
                    var c = ParseCondition(items[k], $"{at}.when.{rule.Mode}[{k}]", dna, prog, r);
                    if (c != null) rule.When.Add(c);
                }
            }

            if (!(o.TryGetValue("do", out var dt) && dt is JArray da) || da.Count == 0) r.Errors.Add($"{at}.do（動作の配列）がありません");
            else if (da.Count > ProgramCatalog.MaxActionsPerRule) r.Errors.Add($"{at} の動作は {ProgramCatalog.MaxActionsPerRule} 個までです");
            else for (int k = 0; k < da.Count; k++)
            {
                var a = ParseAction(da[k], $"{at}.do[{k}]", dna, prog, r);
                if (a == null) continue;
                if (rule.Trigger == ProgramRule.While && (a.Action == ProgramAction.Wait || a.Action == ProgramAction.MoveJoint))
                    r.Errors.Add($"{at}：while のルールでは wait・move_joint を使えません（set_behavior・set_parameter・set_flag だけ）");
                rule.Do.Add(a);
            }
            return rule;
        }

        static ProgramCondition ParseCondition(JToken t, string at, InventionDna dna, MotionProgram prog, ValidationReport r)
        {
            if (!(t is JObject o)) { r.Errors.Add(at + " はオブジェクトです"); return null; }
            if (o.ContainsKey("all") || o.ContainsKey("any")) { r.Errors.Add($"{at}：条件の入れ子はできません"); return null; }
            var c = new ProgramCondition();
            if (o.ContainsKey("flag"))
            {
                Reject(o, FlagCondFields, at, r);
                c.Flag = S(o, "flag", at, r, true, LabCatalog.MaxIdLength);
                if (c.Flag != null && !prog.Flags.Contains(c.Flag)) r.Errors.Add($"{at}：フラグ {c.Flag} は flags に宣言されていません");
                if (o.TryGetValue("is", out var it) && it.Type == JTokenType.Boolean) c.Is = it.Value<bool>(); else r.Errors.Add($"{at}.is（真偽）がありません");
                return c;
            }
            c.Sensor = S(o, "sensor", at, r, true, 32);
            var info = c.Sensor != null ? ProgramCatalog.Sensor(c.Sensor) : null;
            if (c.Sensor != null && info == null) { r.Errors.Add($"{at}：未知のセンサー {Short(c.Sensor)}（使用可能: {string.Join(", ", ProgramCatalog.Sensors.Select(s => s.Id))}）"); return null; }
            if (info == null) return null;
            string why = MissingRequirement(info, dna, o.Value<string>("joint"));
            if (why != null) { r.Errors.Add($"{at}：センサー {info.Id} には {why} が必要です"); return null; }
            if (info.IsBool)
            {
                // 真偽のセンサーは { "sensor": …, "is": true/false }
                Reject(o, BoolSensorCondFields, at, r);
                if (o.TryGetValue("is", out var bt) && bt.Type == JTokenType.Boolean) c.Is = bt.Value<bool>(); else r.Errors.Add($"{at}.is（真偽）がありません");
            }
            else
            {
                Reject(o, SensorCondFields, at, r);
                c.Op = S(o, "op", at, r, true, 2);
                if (c.Op != null && !ProgramCatalog.Ops.Contains(c.Op)) r.Errors.Add($"{at}.op は < <= > >= です");
                float max = info.Max, min = info.Min;
                if (info.Id == "target_distance") max = dna.GetParameter("detect", "rangeM", info.Max);
                if (info.Id == "joint_position")
                {
                    c.Joint = S(o, "joint", at, r, true, LabCatalog.MaxIdLength);
                    var jp = c.Joint != null ? dna.FindPart(c.Joint) : null;
                    if (jp?.Joint == null) { r.Errors.Add($"{at}：関節 {c.Joint} がありません"); return null; }
                    min = jp.Joint.Min; max = jp.Joint.Max;
                }
                c.Value = N(o, "value", at, r, min, max) ?? 0f;
            }
            return c;
        }

        static ProgramAction ParseAction(JToken t, string at, InventionDna dna, MotionProgram prog, ValidationReport r)
        {
            if (!(t is JObject o)) { r.Errors.Add(at + " はオブジェクトです"); return null; }
            var a = new ProgramAction { Action = S(o, "action", at, r, true, 32) };
            if (a.Action == null) return null;
            if (!ActionFields.TryGetValue(a.Action, out var fields)) { r.Errors.Add($"{at}：未知の動作 {Short(a.Action)}（使用可能: {string.Join(", ", ProgramCatalog.Actions)}）"); return null; }
            Reject(o, fields, at, r);
            switch (a.Action)
            {
                case ProgramAction.MoveJoint:
                {
                    a.Joint = S(o, "joint", at, r, true, LabCatalog.MaxIdLength);
                    var jp = a.Joint != null ? dna.FindPart(a.Joint) : null;
                    if (jp?.Joint == null) { r.Errors.Add($"{at}：関節 {a.Joint} がありません"); return null; }
                    var j = jp.Joint;
                    a.Target = N(o, "target", at, r, j.Min, j.Max) ?? j.Home;
                    bool hinge = j.IsHinge;
                    a.Speed = N(o, "speed", at, r, hinge ? ProgramCatalog.HingeSpeedMin : ProgramCatalog.SliderSpeedMin, j.Speed, required: false);
                    break;
                }
                case ProgramAction.SetBehavior:
                    a.Behavior = S(o, "behavior", at, r, true, 32);
                    if (a.Behavior != null && dna.FindBehavior(a.Behavior) == null) r.Errors.Add($"{at}：機能 {a.Behavior} はこの発明にありません");
                    if (o.TryGetValue("on", out var on) && on.Type == JTokenType.Boolean) a.On = on.Value<bool>(); else r.Errors.Add($"{at}.on（真偽）がありません");
                    break;
                case ProgramAction.SetParameter:
                {
                    a.Behavior = S(o, "behavior", at, r, true, 32);
                    a.Parameter = S(o, "parameter", at, r, true, 32);
                    var pinfo = a.Behavior != null && a.Parameter != null ? LabCatalog.Behavior(a.Behavior)?.Param(a.Parameter) : null;
                    if (a.Behavior != null && dna.FindBehavior(a.Behavior) == null) { r.Errors.Add($"{at}：機能 {a.Behavior} はこの発明にありません"); return null; }
                    if (pinfo == null) { r.Errors.Add($"{at}：{a.Behavior}.{a.Parameter} は未対応のパラメータです"); return null; }
                    a.Value = N(o, "value", at, r, pinfo.Min, pinfo.Max) ?? pinfo.Default;
                    break;
                }
                case ProgramAction.SetFlag:
                    a.Name = S(o, "name", at, r, true, LabCatalog.MaxIdLength);
                    if (a.Name != null && !prog.Flags.Contains(a.Name)) r.Errors.Add($"{at}：フラグ {a.Name} は flags に宣言されていません");
                    if (o.TryGetValue("value", out var fv) && fv.Type == JTokenType.Boolean) a.FlagValue = fv.Value<bool>(); else r.Errors.Add($"{at}.value（真偽）がありません");
                    break;
                case ProgramAction.Wait:
                    a.Seconds = N(o, "seconds", at, r, ProgramCatalog.MinWait, ProgramCatalog.MaxWait) ?? ProgramCatalog.MinWait;
                    break;
            }
            return a;
        }

        /// <summary>センサーに必要な機能・部品がなければ、その説明。あれば null。</summary>
        static string MissingRequirement(SensorInfo s, InventionDna dna, string joint)
        {
            if (s.Requires == null) return null;
            if (s.Requires.StartsWith("behavior:")) { var b = s.Requires.Substring(9); return dna.FindBehavior(b) != null ? null : $"機能「{LabCatalog.Behavior(b)?.LabelJa ?? b}」"; }
            if (s.Requires.StartsWith("part:")) { var p = s.Requires.Substring(5); return dna.Parts.Any(x => x.Type == p) ? null : $"部品「{LabCatalog.Part(p)?.LabelJa ?? p}」"; }
            if (s.Requires == "mount:body") return ProgramCatalog.IsBodyMount(dna.Mount) ? null : "体に付ける取り付け位置";
            if (s.Requires == "joint") return dna.Parts.Any(x => x.Joint != null) ? null : "関節";
            return null;
        }

        /// <summary>拒否はしないが知らせること（仕様 28.5）。</summary>
        static void AddWarnings(MotionProgram p, InventionDna dna, ValidationReport r)
        {
            foreach (var rule in p.Rules)
            {
                foreach (var c in rule.When.Where(c => !c.IsFlag))
                {
                    var s = ProgramCatalog.Sensor(c.Sensor);
                    if (s == null || s.IsBool) continue;
                    float min = s.Min, max = s.Id == "target_distance" ? dna.GetParameter("detect", "rangeM", s.Max) : s.Max;
                    if (c.Joint != null) { var j = dna.FindPart(c.Joint)?.Joint; if (j != null) { min = j.Min; max = j.Max; } }
                    bool never = (c.Op == "<" && c.Value <= min) || (c.Op == ">" && c.Value >= max);
                    if (never) r.Warnings.Add($"ルール「{Name(rule)}」の条件 {c.Sensor} {c.Op} {c.Value} は一度も成り立ちません");
                }
                // 関節を動かした直後に wait なしで別の目標へ動かす（前の目標に届かない）
                for (int k = 0; k + 1 < rule.Do.Count; k++)
                {
                    var a = rule.Do[k];
                    if (a.Action != ProgramAction.MoveJoint) continue;
                    var next = rule.Do.Skip(k + 1).TakeWhile(x => x.Action != ProgramAction.Wait).FirstOrDefault(x => x.Action == ProgramAction.MoveJoint && x.Joint == a.Joint);
                    if (next != null) r.Warnings.Add($"ルール「{Name(rule)}」：関節 {a.Joint} を待たずに別の目標へ動かすため、{a.Target} に届きません");
                }
            }
            foreach (var f in p.Flags)
                if (!p.Rules.Any(x => x.When.Any(c => c.Flag == f)))
                    r.Warnings.Add($"フラグ {f} は条件に使われていません");
        }

        static string Name(ProgramRule r) => string.IsNullOrEmpty(r.Label) ? r.Id : r.Label;

        // ---------------- 書き出し ----------------
        public static JObject JointToJson(JointSpec j)
        {
            var o = new JObject { ["type"] = j.Type };
            if (!string.IsNullOrEmpty(j.Parent)) o["parent"] = j.Parent;
            o["axis"] = j.Axis;
            o["pivot"] = new JArray(R(j.Pivot.x), R(j.Pivot.y), R(j.Pivot.z));
            o["min"] = R(j.Min); o["max"] = R(j.Max); o["speed"] = R(j.Speed); o["home"] = R(j.Home);
            return o;
        }

        public static JObject ProgramToJson(MotionProgram p)
        {
            var rules = new JArray();
            foreach (var rule in p.Rules)
            {
                var when = new JArray();
                foreach (var c in rule.When)
                {
                    if (c.IsFlag) when.Add(new JObject { ["flag"] = c.Flag, ["is"] = c.Is });
                    else if (ProgramCatalog.Sensor(c.Sensor)?.IsBool == true) when.Add(new JObject { ["sensor"] = c.Sensor, ["is"] = c.Is });
                    else
                    {
                        var co = new JObject { ["sensor"] = c.Sensor, ["op"] = c.Op, ["value"] = R(c.Value) };
                        if (c.Joint != null) co["joint"] = c.Joint;
                        when.Add(co);
                    }
                }
                var dos = new JArray();
                foreach (var a in rule.Do)
                {
                    var ao = new JObject { ["action"] = a.Action };
                    switch (a.Action)
                    {
                        case ProgramAction.MoveJoint: ao["joint"] = a.Joint; ao["target"] = R(a.Target); if (a.Speed.HasValue) ao["speed"] = R(a.Speed.Value); break;
                        case ProgramAction.SetBehavior: ao["behavior"] = a.Behavior; ao["on"] = a.On; break;
                        case ProgramAction.SetParameter: ao["behavior"] = a.Behavior; ao["parameter"] = a.Parameter; ao["value"] = R(a.Value); break;
                        case ProgramAction.SetFlag: ao["name"] = a.Name; ao["value"] = a.FlagValue; break;
                        case ProgramAction.Wait: ao["seconds"] = R(a.Seconds); break;
                    }
                    dos.Add(ao);
                }
                var ro = new JObject { ["id"] = rule.Id, ["label"] = rule.Label, ["trigger"] = rule.Trigger, ["cooldown"] = R(rule.Cooldown), ["when"] = new JObject { [rule.Mode] = when }, ["do"] = dos };
                if (!rule.Enabled) ro["enabled"] = false;
                rules.Add(ro);
            }
            return new JObject { ["version"] = p.Version, ["flags"] = new JArray(p.Flags), ["rules"] = rules };
        }

        // ---------------- 小さな部品 ----------------
        static double R(float f) => Math.Round(f, 4);
        static string Short(string s) => s == null ? "" : (s.Length > 32 ? s.Substring(0, 32) + "…" : s);

        static void Reject(JObject o, HashSet<string> allowed, string at, ValidationReport r)
        {
            foreach (var p in o.Properties())
                if (!allowed.Contains(p.Name)) r.Errors.Add($"{at} に未知のフィールド: {Short(p.Name)}");
        }

        static string S(JObject o, string key, string at, ValidationReport r, bool required, int maxLen)
        {
            if (!o.TryGetValue(key, out var t) || t.Type == JTokenType.Null) { if (required) r.Errors.Add($"必須項目 {at}.{key} がありません"); return null; }
            if (t.Type != JTokenType.String) { r.Errors.Add($"{at}.{key} は文字列です"); return null; }
            var s = (string)t;
            if (s.Length > maxLen) { r.Errors.Add($"{at}.{key} が長すぎます（{maxLen}文字まで）"); return null; }
            return s;
        }

        /// <summary>数値。範囲外は拒否（設計時は補正しない）。</summary>
        static float? N(JObject o, string key, string at, ValidationReport r, float min, float max, bool required = true)
        {
            if (!o.TryGetValue(key, out var t) || t.Type == JTokenType.Null) { if (required) r.Errors.Add($"必須項目 {at}.{key} がありません"); return null; }
            if (t.Type != JTokenType.Float && t.Type != JTokenType.Integer) { r.Errors.Add($"{at}.{key} は数値です"); return null; }
            float v = t.Value<float>();
            if (float.IsNaN(v) || float.IsInfinity(v)) { r.Errors.Add($"{at}.{key} が不正な数値です"); return null; }
            if (v < min - 1e-4f || v > max + 1e-4f) { r.Errors.Add($"{at}.{key} = {v} は範囲 {min}〜{max} の外です"); return null; }
            return v;
        }
    }
}
