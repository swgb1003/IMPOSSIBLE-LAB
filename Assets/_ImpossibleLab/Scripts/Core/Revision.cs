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
        public static bool IsValid(string op) => op == AddPart || op == RemovePart || op == UpdateParameter || op == AddBehavior || op == RemoveBehavior;
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
                    default: result.Errors.Add($"未対応の変更操作: {op.Op}"); break;
                }
            }
            if (result.Errors.Count > 0) return result;

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
                if (op.Op == RevisionOps.UpdateParameter)
                {
                    if (val == null || (val.Type != JTokenType.Float && val.Type != JTokenType.Integer)) { error = "value は数値です"; return false; }
                    op.Value = (float)val.Value<double>();
                }
                if (op.Op == RevisionOps.AddPart)
                {
                    // 部品は DNA のスキーマ検証器で検証する
                    var wrapper = new JObject { ["schemaVersion"] = 2, ["id"] = "tmp", ["name"] = "tmp", ["tags"] = new JArray("daily"), ["mount"] = "fixed", ["purpose"] = "tmp",
                        ["parts"] = new JArray(jo["part"]), ["behaviors"] = new JArray() };
                    var rep = DnaJson.Parse(wrapper.ToString());
                    if (!rep.IsValid) { error = "追加部品が不正: " + rep.Summary; return false; }
                    op.Part = rep.Dna.Parts[0];
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
