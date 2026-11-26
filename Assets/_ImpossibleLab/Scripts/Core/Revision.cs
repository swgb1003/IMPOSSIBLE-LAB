using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ImpossibleLab.Core
{
    public static class RevisionOps
    {
        public const string AddPart = "add_part";
        public const string RemovePart = "remove_part";
        public const string UpdateParameter = "update_parameter";
        public const string AddBehavior = "add_behavior";
        public const string RemoveBehavior = "remove_behavior";
        /// <summary>部品の形・素材の変更（S03 の手動の切り替えだけで使う。AI の改良提案では受け付けない）。</summary>
        public const string UpdatePart = "update_part";
        /// <summary>動作プログラムのルールの値の変更（enabled・when・target・speed・seconds）。S03 の手動の編集・オフライン改良・AI の改良で使う。</summary>
        public const string UpdateRule = "update_rule";
        /// <summary>ルールの追加・削除（仕様 28.8）。追加するルールは DNA の検証器で検証する。</summary>
        public const string AddRule = "add_rule";
        public const string RemoveRule = "remove_rule";
        /// <summary>関節の追加・変更（speed・min・max）。追加する関節は DNA の検証器で検証する。</summary>
        public const string AddJoint = "add_joint";
        public const string UpdateJoint = "update_joint";
        /// <summary>AI の改良提案で受け付ける操作（update_part は S03 の手動の切り替えだけ）。</summary>
        public static bool IsValid(string op) => op == AddPart || op == RemovePart || op == UpdateParameter || op == AddBehavior || op == RemoveBehavior
            || op == UpdateRule || op == AddRule || op == RemoveRule || op == AddJoint || op == UpdateJoint;
        public static readonly string[] RuleFields = { "enabled", "when", "target", "speed", "seconds" };
        public static readonly string[] JointFields = { "speed", "min", "max" };
    }

    /// <summary>改良の変更操作（仕様6.2、5週目に機能の追加・削除を追加）。</summary>
    public sealed class RevisionOperation
    {
        public string Op;
        public string BehaviorType;
        public string Parameter;
        public float Value;
        public string PartId;
        public PartSpec Part;
        /// <summary>add_behavior：対象部品（空なら使える部品を自動で選ぶ）と、パラメータ（指定のないものは既定値）。</summary>
        public string TargetPart;
        public Dictionary<string, float> Parameters = new Dictionary<string, float>();
        /// <summary>update_part：新しい形・素材（null なら変えない）。</summary>
        public string Shape;
        public string Material;
        /// <summary>品番（update_part）。"" は品番を外す、null は変えない。</summary>
        public string Model;
        /// <summary>update_rule：ルール id・変える項目（enabled / when / target / seconds）・条件や動作の番号。値は Value（enabled は 0/1）。</summary>
        public string RuleId;
        public string RuleField;
        public int Index;
        /// <summary>add_rule のルール・add_joint の関節（JSON。適用時に DNA 全体と一緒に検証する）。update_joint の項目は RuleField を使う。</summary>
        public string RuleJson;
        public string JointJson;
    }

    public sealed class RevisionProposal
    {
        public string Summary = "";
        public string Source = "offline";
        public List<RevisionOperation> Operations = new List<RevisionOperation>();
        /// <summary>対応できなかった要求と代替案（黙って無視しない）。</summary>
        public List<string> Notes = new List<string>();
    }

    public sealed class ChangeLine
    {
        public string Label;
        public string Before;
        public string After;
    }

    public sealed class RevisionResult
    {
        public InventionDna NewDna;
        public readonly List<ChangeLine> Changes = new List<ChangeLine>();
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public bool Success => Errors.Count == 0 && NewDna != null && Changes.Count > 0;
    }

    public static class RevisionEngine
    {
        public const int MaxOperations = 6;

        /// <summary>変更を複製に適用し、再検証してから新しい版を返す。元のDNAは変更しない。</summary>
        public static RevisionResult Apply(InventionDna current, RevisionProposal proposal)
        {
            var result = new RevisionResult();
            if (proposal == null || proposal.Operations.Count == 0) { result.Errors.Add("変更操作がありません"); return result; }
            if (proposal.Operations.Count > MaxOperations) { result.Errors.Add($"変更操作が多すぎます（最大{MaxOperations}）"); return result; }

            var next = current.Clone();
            foreach (var op in proposal.Operations)
            {
                switch (op.Op)
                {
                    case RevisionOps.UpdateParameter: ApplyUpdate(next, op, result); break;
                    case RevisionOps.AddPart: ApplyAdd(next, op, result); break;
                    case RevisionOps.RemovePart: ApplyRemove(next, op, result); break;
                    case RevisionOps.AddBehavior: ApplyAddBehavior(next, op, result); break;
                    case RevisionOps.RemoveBehavior: ApplyRemoveBehavior(next, op, result); break;
                    case RevisionOps.UpdatePart: ApplyUpdatePart(next, op, result); break;
                    case RevisionOps.UpdateRule: ApplyUpdateRule(next, op, result); break;
                    case RevisionOps.AddRule: ApplyAddRule(next, op, result); break;
                    case RevisionOps.RemoveRule: ApplyRemoveRule(next, op, result); break;
                    case RevisionOps.AddJoint: ApplyAddJoint(next, op, result); break;
                    case RevisionOps.UpdateJoint: ApplyUpdateJoint(next, op, result); break;
                    default: result.Errors.Add($"未対応の変更操作: {op.Op}"); break;
                }
            }
            if (result.Errors.Count > 0) return result;
            // 足した部品だけ、浮いていたら近くの部品に付ける（もとからある部品は利用者の配置のまま）
            var added = next.Parts.Where(p => current.FindPart(p.Id) == null).Select(p => p.Id).ToList();
            if (added.Count > 0) LayoutFixer.Apply(next, added);

            next.Revision = current.Revision + 1;
            next.ParentRevision = current.Revision;
            // 不変条件の保証のため、シリアライズ→スキーマ検証を必ず通す
            var report = DnaJson.Parse(DnaJson.ToJson(next));
            if (!report.IsValid) { result.Errors.AddRange(report.Errors); return result; }
            result.Warnings.AddRange(report.Warnings);
            if (result.Changes.Count == 0) result.Errors.Add("設計に変化がありません（すでに上限/下限の可能性）");
            result.NewDna = report.Dna;
            return result;
        }

        static void ApplyUpdate(InventionDna dna, RevisionOperation op, RevisionResult r)
        {
            var b = dna.FindBehavior(op.BehaviorType);
            var info = LabCatalog.Behavior(op.BehaviorType ?? "");
            var pinfo = info?.Param(op.Parameter ?? "");
            if (b == null || pinfo == null) { r.Errors.Add($"変更対象が存在しません: {op.BehaviorType}.{op.Parameter}"); return; }
            if (float.IsNaN(op.Value) || float.IsInfinity(op.Value)) { r.Errors.Add("不正な数値です"); return; }
            float before = b.Parameters[pinfo.Key];
            float after = Mathf.Clamp(op.Value, pinfo.Min, pinfo.Max);
            if (!Mathf.Approximately(after, op.Value))
                r.Warnings.Add($"{pinfo.LabelJa} は許可範囲 {LabCatalog.FormatValue(pinfo, pinfo.Min)}〜{LabCatalog.FormatValue(pinfo, pinfo.Max)} に制限しました");
            if (Mathf.Approximately(before, after)) return;
            b.Parameters[pinfo.Key] = after;
            r.Changes.Add(new ChangeLine { Label = $"{info.LabelJa}：{pinfo.LabelJa}", Before = LabCatalog.FormatValue(pinfo, before), After = LabCatalog.FormatValue(pinfo, after) });
        }

        static void ApplyAdd(InventionDna dna, RevisionOperation op, RevisionResult r)
        {
            if (op.Part == null) { r.Errors.Add("追加する部品がありません"); return; }
            if (dna.Parts.Count >= dna.Constraints.MaxParts) { r.Errors.Add($"部品数が上限 {dna.Constraints.MaxParts} です"); return; }
            if (!DnaJson.IsSafeId(op.Part.Id) || dna.FindPart(op.Part.Id) != null) { r.Errors.Add($"部品 id が不正または重複: {op.Part.Id}"); return; }
            var info = LabCatalog.Part(op.Part.Type ?? "");
            if (info == null) { r.Errors.Add($"未対応の部品タイプ: {op.Part.Type}"); return; }
            dna.Parts.Add(op.Part.Clone());
            r.Changes.Add(new ChangeLine { Label = "部品追加", Before = "—", After = $"{info.LabelJa}（{op.Part.Id}）" });
        }

        static void ApplyRemove(InventionDna dna, RevisionOperation op, RevisionResult r)
        {
            var part = dna.FindPart(op.PartId ?? "");
            if (part == null) { r.Errors.Add($"削除対象の部品がありません: {op.PartId}"); return; }
            if (dna.Parts.Count <= 1) { r.Errors.Add("最後の部品は削除できません"); return; }
            var user = dna.Behaviors.FirstOrDefault(b => b.TargetPart == part.Id);
            if (user != null) { r.Errors.Add($"{part.Id} は機能 {user.Type} が使用中のため削除できません"); return; }
            dna.Parts.Remove(part);
            r.Changes.Add(new ChangeLine { Label = "部品削除", Before = $"{LabCatalog.Part(part.Type)?.LabelJa}（{part.Id}）", After = "—" });
        }

        static void ApplyAddBehavior(InventionDna dna, RevisionOperation op, RevisionResult r)
        {
            var info = LabCatalog.Behavior(op.BehaviorType ?? "");
            if (info == null) { r.Errors.Add($"未対応の機能: {op.BehaviorType}"); return; }
            if (dna.FindBehavior(info.Type) != null) { r.Errors.Add($"機能 {info.LabelJa} はすでにあります"); return; }
            if (dna.Behaviors.Count >= LabCatalog.MaxBehaviors) { r.Errors.Add($"機能の数が上限 {LabCatalog.MaxBehaviors} です"); return; }
            string target = null;
            if (info.RequiresTargetPart)
            {
                var part = !string.IsNullOrEmpty(op.TargetPart) ? dna.FindPart(op.TargetPart) : dna.Parts.FirstOrDefault(p => info.AllowedTargetTypes.Contains(p.Type));
                if (part == null) { r.Errors.Add($"{info.LabelJa} には {string.Join("/", info.AllowedTargetTypes)} 部品が必要です（先に部品を追加してください）"); return; }
                if (!info.AllowedTargetTypes.Contains(part.Type)) { r.Errors.Add($"{info.LabelJa} は {part.Type} 部品に付けられません"); return; }
                target = part.Id;
            }
            foreach (var k in op.Parameters.Keys) if (info.Param(k) == null) { r.Errors.Add($"{info.Type} に未対応のパラメータ: {k}"); return; }
            var b = new BehaviorSpec { Type = info.Type, TargetPart = target };
            foreach (var pinfo in info.Parameters)
            {
                float v = pinfo.Default;
                if (op.Parameters.TryGetValue(pinfo.Key, out var given))
                {
                    if (float.IsNaN(given) || float.IsInfinity(given)) { r.Errors.Add("不正な数値です"); return; }
                    v = Mathf.Clamp(given, pinfo.Min, pinfo.Max);
                    if (!Mathf.Approximately(v, given)) r.Warnings.Add($"{pinfo.LabelJa} は許可範囲 {LabCatalog.FormatValue(pinfo, pinfo.Min)}〜{LabCatalog.FormatValue(pinfo, pinfo.Max)} に制限しました");
                }
                b.Parameters[pinfo.Key] = v;
            }
            dna.Behaviors.Add(b);
            r.Changes.Add(new ChangeLine { Label = "機能追加", Before = "—", After = info.LabelJa + (target != null ? $"（{target}）" : "") });
        }

        static void ApplyRemoveBehavior(InventionDna dna, RevisionOperation op, RevisionResult r)
        {
            var b = dna.FindBehavior(op.BehaviorType ?? "");
            if (b == null) { r.Errors.Add($"削除する機能がありません: {op.BehaviorType}"); return; }
            var info = LabCatalog.Behavior(b.Type);
            dna.Behaviors.Remove(b);
            // 機能対応表からも外す。検証の手段がなくなった要望は「検証できない」にして理由を残す
            foreach (var n in dna.FunctionMap)
            {
                if (!n.Blocks.Remove(b.Type)) continue;
                if (n.Blocks.Count == 0 && n.Status != "unverifiable")
                {
                    n.Status = "unverifiable";
                    n.Note = $"改良で機能「{info?.LabelJa ?? b.Type}」を外したため検証しない";
                }
            }
            r.Changes.Add(new ChangeLine { Label = "機能削除", Before = info?.LabelJa ?? b.Type, After = "—" });
        }

        /// <summary>ルールの変更。範囲の外は範囲内に補正して注意を出す（仕様 28.5：改良では補正して差分に表示）。</summary>
        /// <summary>DNA を JSON にし、手を加えてから検証器に通す（ルール・関節の参照や上限は DnaJson がまとめて確認する）。</summary>
        static InventionDna Revalidate(InventionDna dna, System.Action<JObject> change, RevisionResult r, string what)
        {
            var o = JObject.Parse(DnaJson.ToJson(dna));
            change(o);
            var rep = DnaJson.Parse(o.ToString());
            if (!rep.IsValid) { r.Errors.Add(what + "：" + rep.Summary); return null; }
            return rep.Dna;
        }

        static void ApplyAddRule(InventionDna dna, RevisionOperation op, RevisionResult r)
        {
            JObject rule;
            try { rule = JObject.Parse(op.RuleJson ?? ""); }
            catch (Newtonsoft.Json.JsonException) { r.Errors.Add("追加するルールが不正です"); return; }
            string id = rule.Value<string>("id") ?? "";
            if (dna.Program != null && dna.Program.Rules.Any(x => x.Id == id)) { r.Errors.Add($"ルール {id} はすでにあります"); return; }
            var next = Revalidate(dna, o =>
            {
                if (!(o["program"] is JObject p)) { p = new JObject { ["version"] = 1, ["flags"] = new JArray(), ["rules"] = new JArray() }; o["program"] = p; }
                ((JArray)p["rules"]).Add(rule);
            }, r, "追加するルールが不正");
            if (next == null) return;
            dna.Program = next.Program;
            var added = dna.Program.Rules.Last();
            r.Changes.Add(new ChangeLine { Label = "動きの手順：ルールを追加", Before = "なし", After = ProgramText.Rule(added, dna.Program.Rules.Count - 1, dna) });
        }

        static void ApplyRemoveRule(InventionDna dna, RevisionOperation op, RevisionResult r)
        {
            int i = dna.Program?.Rules.FindIndex(x => x.Id == op.RuleId) ?? -1;
            if (i < 0) { r.Errors.Add($"ルールがありません: {op.RuleId}"); return; }
            r.Changes.Add(new ChangeLine { Label = "動きの手順：ルールを削除", Before = ProgramText.Rule(dna.Program.Rules[i], i, dna), After = "削除" });
            dna.Program.Rules.RemoveAt(i);
            if (dna.Program.Rules.Count == 0) dna.Program = null; // ルールが無いプログラムは持たない
        }

        static void ApplyAddJoint(InventionDna dna, RevisionOperation op, RevisionResult r)
        {
            var part = dna.FindPart(op.PartId);
            if (part == null) { r.Errors.Add($"部品がありません: {op.PartId}"); return; }
            if (part.Joint != null) { r.Errors.Add($"部品 {op.PartId} にはすでに関節があります"); return; }
            JObject joint;
            try { joint = JObject.Parse(op.JointJson ?? ""); }
            catch (Newtonsoft.Json.JsonException) { r.Errors.Add("追加する関節が不正です"); return; }
            var next = Revalidate(dna, o =>
            {
                foreach (var p in (JArray)o["parts"]) if (p.Value<string>("id") == op.PartId) p["joint"] = joint;
            }, r, "追加する関節が不正");
            if (next == null) return;
            part.Joint = next.FindPart(op.PartId).Joint;
            string kind = part.Joint.IsHinge ? "回転" : "直動";
            string unit = part.Joint.IsHinge ? "°" : " m";
            r.Changes.Add(new ChangeLine { Label = $"{ProgramText.PartName(dna, part.Id)}：関節を追加", Before = "なし", After = $"{kind} {part.Joint.Min:0.##}〜{part.Joint.Max:0.##}{unit}" });
        }

        static void ApplyUpdateJoint(InventionDna dna, RevisionOperation op, RevisionResult r)
        {
            var j = dna.FindPart(op.PartId)?.Joint;
            if (j == null) { r.Errors.Add($"関節がありません: {op.PartId}"); return; }
            if (float.IsNaN(op.Value) || float.IsInfinity(op.Value)) { r.Errors.Add("不正な数値です"); return; }
            string name = ProgramText.PartName(dna, op.PartId) + "の関節";
            string unit = j.IsHinge ? "°" : " m";
            float lim = j.IsHinge ? ProgramCatalog.HingeLimit : ProgramCatalog.SliderLimit;
            float Clamp(float v, float min, float max, string what)
            {
                float c = Mathf.Clamp(v, min, max);
                if (!Mathf.Approximately(c, v)) r.Warnings.Add($"{what}は範囲 {min:0.##}〜{max:0.##} に補正しました");
                return c;
            }
            switch (op.RuleField)
            {
                case "speed":
                {
                    float v = j.IsHinge ? Clamp(op.Value, ProgramCatalog.HingeSpeedMin, ProgramCatalog.HingeSpeedMax, "関節の速さ") : Clamp(op.Value, ProgramCatalog.SliderSpeedMin, ProgramCatalog.SliderSpeedMax, "関節の速さ");
                    if (Mathf.Approximately(v, j.Speed)) return;
                    r.Changes.Add(new ChangeLine { Label = name + "：速さの上限", Before = $"{j.Speed:0.##}{unit}/秒", After = $"{v:0.##}{unit}/秒" });
                    j.Speed = v;
                    // ルールの「動かす速さ」は関節の上限を超えられない
                    if (dna.Program != null)
                        foreach (var a in dna.Program.Rules.SelectMany(x => x.Do))
                            if (a.Action == ProgramAction.MoveJoint && a.Joint == op.PartId && a.Speed > v) a.Speed = v;
                    return;
                }
                case "min":
                case "max":
                {
                    bool isMin = op.RuleField == "min";
                    float v = isMin ? Clamp(op.Value, -lim, j.Max, "関節の下限") : Clamp(op.Value, j.Min, lim, "関節の上限");
                    float before = isMin ? j.Min : j.Max;
                    if (Mathf.Approximately(v, before)) return;
                    r.Changes.Add(new ChangeLine { Label = name + (isMin ? "：動く範囲の下限" : "：動く範囲の上限"), Before = $"{before:0.##}{unit}", After = $"{v:0.##}{unit}" });
                    if (isMin) j.Min = v; else j.Max = v;
                    j.Home = Mathf.Clamp(j.Home, j.Min, j.Max);
                    // 範囲の外になった動作の目標は範囲内に補正して知らせる（仕様 28.5：改良では補正）
                    if (dna.Program != null)
                        foreach (var a in dna.Program.Rules.SelectMany(x => x.Do))
                            if (a.Action == ProgramAction.MoveJoint && a.Joint == op.PartId && (a.Target < j.Min || a.Target > j.Max))
                            {
                                a.Target = Mathf.Clamp(a.Target, j.Min, j.Max);
                                r.Warnings.Add($"動作の目標を {a.Target:0.##}{unit} に補正しました");
                            }
                    return;
                }
                default: r.Errors.Add($"未対応の関節の項目: {op.RuleField}"); return;
            }
        }

        static void ApplyUpdateRule(InventionDna dna, RevisionOperation op, RevisionResult r)
        {
            var rule = dna.Program?.Rules.Find(x => x.Id == op.RuleId);
            if (rule == null) { r.Errors.Add($"ルールがありません: {op.RuleId}"); return; }
            if (float.IsNaN(op.Value) || float.IsInfinity(op.Value)) { r.Errors.Add("不正な数値です"); return; }
            string name = $"ルール「{(string.IsNullOrEmpty(rule.Label) ? rule.Id : rule.Label)}」";
            float Clamp(float v, float min, float max, string what)
            {
                float c = Mathf.Clamp(v, min, max);
                if (!Mathf.Approximately(c, v)) r.Warnings.Add($"{what}は範囲 {min:0.##}〜{max:0.##} に補正しました");
                return c;
            }
            switch (op.RuleField)
            {
                case "enabled":
                {
                    bool on = op.Value >= 0.5f;
                    if (rule.Enabled == on) return;
                    r.Changes.Add(new ChangeLine { Label = name, Before = rule.Enabled ? "オン" : "オフ", After = on ? "オン" : "オフ" });
                    rule.Enabled = on;
                    return;
                }
                case "when":
                {
                    if (op.Index < 0 || op.Index >= rule.When.Count || rule.When[op.Index].IsFlag) { r.Errors.Add($"{name} の条件 {op.Index} は数値ではありません"); return; }
                    var c = rule.When[op.Index];
                    var s = ProgramCatalog.Sensor(c.Sensor);
                    if (s == null || s.IsBool) { r.Errors.Add($"{name} の条件 {op.Index} は数値ではありません"); return; }
                    float min = s.Min, max = c.Sensor == "target_distance" ? dna.GetParameter("detect", "rangeM", s.Max) : s.Max;
                    if (c.Sensor == "joint_position") { var j = dna.FindPart(c.Joint)?.Joint; if (j != null) { min = j.Min; max = j.Max; } }
                    float v = Clamp(op.Value, min, max, s.LabelJa);
                    if (Mathf.Approximately(v, c.Value)) return;
                    r.Changes.Add(new ChangeLine { Label = name + "：" + s.LabelJa, Before = ProgramText.Condition(c, dna), After = null });
                    c.Value = v;
                    r.Changes[r.Changes.Count - 1].After = ProgramText.Condition(c, dna);
                    return;
                }
                case "target":
                {
                    if (op.Index < 0 || op.Index >= rule.Do.Count || rule.Do[op.Index].Action != ProgramAction.MoveJoint) { r.Errors.Add($"{name} の動作 {op.Index} は関節の動きではありません"); return; }
                    var a = rule.Do[op.Index];
                    var j = dna.FindPart(a.Joint)?.Joint;
                    if (j == null) { r.Errors.Add($"関節 {a.Joint} がありません"); return; }
                    float v = Clamp(op.Value, j.Min, j.Max, "関節の目標");
                    if (Mathf.Approximately(v, a.Target)) return;
                    r.Changes.Add(new ChangeLine { Label = name + "：関節の目標", Before = ProgramText.JointValue(dna, a.Joint, a.Target), After = ProgramText.JointValue(dna, a.Joint, v) });
                    a.Target = v;
                    return;
                }
                case "speed":
                {
                    if (op.Index < 0 || op.Index >= rule.Do.Count || rule.Do[op.Index].Action != ProgramAction.MoveJoint) { r.Errors.Add($"{name} の動作 {op.Index} は関節の動きではありません"); return; }
                    var a = rule.Do[op.Index];
                    var j = dna.FindPart(a.Joint)?.Joint;
                    if (j == null) { r.Errors.Add($"関節 {a.Joint} がありません"); return; }
                    float min = j.IsHinge ? ProgramCatalog.HingeSpeedMin : ProgramCatalog.SliderSpeedMin;
                    float before = a.Speed ?? j.Speed;
                    float v = Clamp(op.Value, min, j.Speed, "動かす速さ");
                    if (Mathf.Approximately(v, before)) { r.Warnings.Add($"動かす速さはすでに関節の上限（{j.Speed:0.##}）です"); return; }
                    string unit = j.IsHinge ? "°/秒" : " m/秒";
                    r.Changes.Add(new ChangeLine { Label = name + "：動かす速さ", Before = $"{before:0.##}{unit}", After = $"{v:0.##}{unit}" });
                    a.Speed = v;
                    return;
                }
                case "seconds":
                {
                    if (op.Index < 0 || op.Index >= rule.Do.Count || rule.Do[op.Index].Action != ProgramAction.Wait) { r.Errors.Add($"{name} の動作 {op.Index} は待つ動作ではありません"); return; }
                    var a = rule.Do[op.Index];
                    float v = Clamp(op.Value, ProgramCatalog.MinWait, ProgramCatalog.MaxWait, "待つ時間");
                    if (Mathf.Approximately(v, a.Seconds)) return;
                    r.Changes.Add(new ChangeLine { Label = name + "：待つ時間", Before = $"{a.Seconds:0.#}秒", After = $"{v:0.#}秒" });
                    a.Seconds = v;
                    return;
                }
                default: r.Errors.Add($"未対応のルールの項目: {op.RuleField}"); return;
            }
        }

        static void ApplyUpdatePart(InventionDna dna, RevisionOperation op, RevisionResult r)
        {
            var part = dna.FindPart(op.PartId ?? "");
            if (part == null) { r.Errors.Add($"変更する部品がありません: {op.PartId}"); return; }
            if (op.Shape != null && LabCatalog.Shape(op.Shape) == null) { r.Errors.Add($"未対応の形: {op.Shape}"); return; }
            if (op.Material != null && LabCatalog.Material(op.Material) == null) { r.Errors.Add($"未対応の素材: {op.Material}"); return; }
            string name = $"{LabCatalog.Part(part.Type)?.LabelJa ?? part.Type}（{part.Id}）";
            if (op.Shape != null && op.Shape != part.ShapeOrDefault)
            {
                r.Changes.Add(new ChangeLine { Label = name + "：形", Before = LabCatalog.Shape(part.ShapeOrDefault)?.LabelJa ?? part.ShapeOrDefault, After = LabCatalog.Shape(op.Shape).LabelJa });
                part.Shape = op.Shape;
            }
            if (op.Material != null && op.Material != part.MaterialOrDefault)
            {
                r.Changes.Add(new ChangeLine { Label = name + "：素材", Before = LabCatalog.Material(part.MaterialOrDefault)?.LabelJa ?? part.MaterialOrDefault, After = LabCatalog.Material(op.Material).LabelJa });
                part.Material = op.Material;
            }
            // 形を変えて品番の形と合わなくなったら、品番を外す
            var had = part.ModelInfo;
            if (op.Model == null && had != null && had.Shape != part.ShapeOrDefault)
            {
                r.Changes.Add(new ChangeLine { Label = name + "：品番", Before = had.LabelJa, After = "標準" });
                part.Model = "";
            }
            if (op.Model != null && op.Model != part.Model)
            {
                var m = PartModels.Find(op.Model);
                if (op.Model.Length > 0 && (m == null || m.Type != part.Type)) { r.Errors.Add($"この部品に使えない品番: {op.Model}"); return; }
                r.Changes.Add(new ChangeLine { Label = name + "：品番", Before = part.ModelInfo?.LabelJa ?? "標準", After = m?.LabelJa ?? "標準" });
                part.Model = m?.Id ?? "";
                if (m != null && m.Shape != part.ShapeOrDefault)
                {
                    r.Changes.Add(new ChangeLine { Label = name + "：形", Before = LabCatalog.Shape(part.ShapeOrDefault)?.LabelJa ?? part.ShapeOrDefault, After = LabCatalog.Shape(m.Shape)?.LabelJa ?? m.Shape });
                    part.Shape = m.Shape;
                }
            }
        }

        /// <summary>AIプロキシが返す改良提案JSONを検証して読み込む（データとしてのみ扱う）。</summary>
        public static bool TryParseProposal(string json, out RevisionProposal proposal, out string error)
        {
            proposal = null; error = null;
            JObject o;
            try { o = JObject.Parse(json ?? ""); }
            catch (Newtonsoft.Json.JsonException) { error = "改良提案のJSONが不正です"; return false; }
            var ops = o["operations"] as JArray;
            if (ops == null || ops.Count == 0 || ops.Count > MaxOperations) { error = "operations が不正です"; return false; }
            var p = new RevisionProposal { Source = "ai", Summary = Truncate(o.Value<string>("summary"), 120) };
            if (o["notes"] is JArray notes) foreach (var n in notes.Take(5)) if (n.Type == JTokenType.String) p.Notes.Add(Truncate((string)n, 120));
            foreach (var t in ops)
            {
                if (!(t is JObject jo)) { error = "operations の要素が不正です"; return false; }
                var op = new RevisionOperation { Op = jo.Value<string>("op") };
                if (!RevisionOps.IsValid(op.Op)) { error = $"未対応の変更操作: {Truncate(op.Op, 32)}"; return false; }
                op.BehaviorType = jo.Value<string>("behavior");
                op.Parameter = jo.Value<string>("parameter");
                op.PartId = jo.Value<string>("partId");
                if (op.Op == RevisionOps.AddBehavior || op.Op == RevisionOps.RemoveBehavior)
                {
                    if (string.IsNullOrEmpty(op.BehaviorType)) { error = "behavior がありません"; return false; }
                    op.TargetPart = jo.Value<string>("targetPart");
                    if (jo["parameters"] is JObject pars)
                        foreach (var pp in pars.Properties().Take(8))
                        {
                            if (pp.Value.Type != JTokenType.Float && pp.Value.Type != JTokenType.Integer) { error = "parameters の値は数値です"; return false; }
                            op.Parameters[Truncate(pp.Name, 32)] = (float)pp.Value.Value<double>();
                        }
                    else if (jo["parameters"] != null && jo["parameters"].Type != JTokenType.Null) { error = "parameters はオブジェクトです"; return false; }
                }
                var val = jo["value"];
                if (op.Op == RevisionOps.UpdateRule || op.Op == RevisionOps.RemoveRule)
                {
                    op.RuleId = Truncate(jo.Value<string>("ruleId"), LabCatalog.MaxIdLength);
                    if (string.IsNullOrEmpty(op.RuleId)) { error = "ruleId がありません"; return false; }
                }
                if (op.Op == RevisionOps.UpdateRule || op.Op == RevisionOps.UpdateJoint)
                {
                    op.RuleField = jo.Value<string>("field");
                    var allowed = op.Op == RevisionOps.UpdateRule ? RevisionOps.RuleFields : RevisionOps.JointFields;
                    if (!allowed.Contains(op.RuleField)) { error = $"未対応の項目: {Truncate(op.RuleField, 32)}"; return false; }
                    var idx = jo["index"];
                    if (idx != null && idx.Type == JTokenType.Integer) op.Index = idx.Value<int>();
                    else if (op.Op == RevisionOps.UpdateRule && op.RuleField != "enabled") { error = "index（条件・動作の番号）がありません"; return false; }
                    if (val == null || (val.Type != JTokenType.Float && val.Type != JTokenType.Integer && val.Type != JTokenType.Boolean)) { error = "value がありません"; return false; }
                    op.Value = val.Type == JTokenType.Boolean ? (val.Value<bool>() ? 1f : 0f) : (float)val.Value<double>();
                }
                if (op.Op == RevisionOps.AddRule)
                {
                    if (!(jo["rule"] is JObject rule)) { error = "rule がありません"; return false; }
                    op.RuleJson = rule.ToString(Newtonsoft.Json.Formatting.None);
                }
                if (op.Op == RevisionOps.AddJoint || op.Op == RevisionOps.UpdateJoint)
                {
                    if (string.IsNullOrEmpty(op.PartId)) { error = "partId がありません"; return false; }
                    if (op.Op == RevisionOps.AddJoint)
                    {
                        if (!(jo["joint"] is JObject joint)) { error = "joint がありません"; return false; }
                        op.JointJson = joint.ToString(Newtonsoft.Json.Formatting.None);
                    }
                }
                if (op.Op == RevisionOps.UpdateParameter)
                {
                    if (val == null || (val.Type != JTokenType.Float && val.Type != JTokenType.Integer)) { error = "value は数値です"; return false; }
                    op.Value = (float)val.Value<double>();
                }
                if (op.Op == RevisionOps.AddPart)
                {
                    // 部品は DNA のスキーマ検証器で検証する（役割の説明は表示だけの文なので、外してから検証し、あとで付ける）
                    var partJson = jo["part"] is JObject pj ? (JObject)pj.DeepClone() : null;
                    string role = partJson?.Value<string>("role");
                    partJson?.Remove("role");
                    var wrapper = new JObject { ["schemaVersion"] = 2, ["id"] = "tmp", ["name"] = "tmp", ["tags"] = new JArray("daily"), ["mount"] = "fixed", ["purpose"] = "tmp",
                        ["parts"] = new JArray(partJson ?? jo["part"]), ["behaviors"] = new JArray() };
                    var rep = DnaJson.Parse(wrapper.ToString());
                    if (!rep.IsValid) { error = "追加部品が不正: " + rep.Summary; return false; }
                    op.Part = rep.Dna.Parts[0];
                    if (!string.IsNullOrWhiteSpace(role))
                        op.Part.Role = DnaJson.ExplainText(role, LabCatalog.MaxRoleLength);
                }
                p.Operations.Add(op);
            }
            proposal = p;
            return true;
        }

        static string Truncate(string s, int n) => s == null ? "" : (s.Length > n ? s.Substring(0, n) : s);

        public static string Describe(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
