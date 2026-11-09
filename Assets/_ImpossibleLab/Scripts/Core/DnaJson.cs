using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ImpossibleLab.Core
{
    public sealed class ValidationReport
    {
        public InventionDna Dna;
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public bool IsValid => Errors.Count == 0 && Dna != null;
        public string Summary => IsValid
            ? (Warnings.Count == 0 ? "検証OK" : $"検証OK（補正 {Warnings.Count} 件）")
            : string.Join(" / ", Errors.Take(3));
    }

    /// <summary>
    /// 発明DNAのJSON入出力とスキーマ検証（仕様5.2, 24.3）。
    /// AI出力・保存ファイル・サンプルはすべてここを通る。データとしてのみ扱い、コードやパスは実行しない。
    /// </summary>
    public static class DnaJson
    {
        public const int MaxJsonLength = 32 * 1024;
        static readonly Regex IdPattern = new Regex("^[a-z0-9][a-z0-9_-]{0,47}$");
        static readonly HashSet<string> RootFields = new HashSet<string>
        { "schemaVersion", "id", "name", "category", "purpose", "mode", "parts", "behaviors", "constraints", "revision", "parentRevision" };
        static readonly HashSet<string> PartFields = new HashSet<string> { "id", "type", "position", "scale" };
        static readonly HashSet<string> BehaviorFields = new HashSet<string> { "type", "targetPart", "parameters" };
        static readonly HashSet<string> ConstraintFields = new HashSet<string> { "maxParts", "maxSpeed" };

        public static bool IsSafeId(string id) => id != null && IdPattern.IsMatch(id);

        public static ValidationReport Parse(string json)
        {
            var report = new ValidationReport();
            if (string.IsNullOrWhiteSpace(json)) { report.Errors.Add("設計データが空です"); return report; }
            if (json.Length > MaxJsonLength) { report.Errors.Add("設計データが大きすぎます"); return report; }
            JToken token;
            try
            {
                using (var reader = new JsonTextReader(new System.IO.StringReader(json)) { DateParseHandling = DateParseHandling.None, MaxDepth = 16 })
                {
                    token = JToken.ReadFrom(reader);
                    if (reader.Read()) { report.Errors.Add("JSONの後ろに余分なデータがあります"); return report; }
                }
            }
            catch (JsonException e)
            {
                report.Errors.Add("JSON構文エラー: " + Shorten(e.Message));
                return report;
            }
            if (!(token is JObject obj)) { report.Errors.Add("ルートがオブジェクトではありません"); return report; }
            return Validate(obj, report);
        }

        static ValidationReport Validate(JObject root, ValidationReport r)
        {
            RejectUnknown(root, RootFields, "ルート", r);
            var dna = new InventionDna();

            var sv = Int(root, "schemaVersion", r, required: true);
            if (sv.HasValue && sv.Value != InventionDna.CurrentSchemaVersion)
                r.Errors.Add($"schemaVersion {sv.Value} は非対応です（対応: {InventionDna.CurrentSchemaVersion}）");

            dna.Id = Str(root, "id", r, true, LabCatalog.MaxIdLength) ?? "";
            if (dna.Id.Length > 0 && !IsSafeId(dna.Id)) r.Errors.Add("id は英小文字・数字・-・_ のみ使用できます");
            dna.Name = Str(root, "name", r, true, LabCatalog.MaxNameLength) ?? "";
            dna.Purpose = Str(root, "purpose", r, true, LabCatalog.MaxPurposeLength) ?? "";
            dna.Category = Str(root, "category", r, true, 32) ?? "";
            if (dna.Category.Length > 0 && LabCatalog.Category(dna.Category) == null)
                r.Errors.Add($"未対応のカテゴリ: {dna.Category}");
            var mode = Str(root, "mode", r, false, 16);
            if (mode != null && !LabModes.IsValid(mode)) r.Errors.Add($"未対応のモード: {mode}");
            dna.Mode = mode != null && LabModes.IsValid(mode) ? mode : LabModes.Dream;

            var rev = Int(root, "revision", r, required: false);
            dna.Revision = rev.HasValue ? rev.Value : 1;
            if (dna.Revision < 1 || dna.Revision > 100000) r.Errors.Add("revision が範囲外です");
            if (root.TryGetValue("parentRevision", out var pr) && pr.Type != JTokenType.Null)
            {
                if (pr.Type != JTokenType.Integer) r.Errors.Add("parentRevision は整数か null です");
                else dna.ParentRevision = pr.Value<int>();
            }

            if (root.TryGetValue("constraints", out var ct))
            {
                if (ct is JObject cobj)
                {
                    RejectUnknown(cobj, ConstraintFields, "constraints", r);
                    var mp = Int(cobj, "maxParts", r, false);
                    if (mp.HasValue) dna.Constraints.MaxParts = ClampInt(mp.Value, 1, InventionConstraints.HardMaxParts, "constraints.maxParts", r);
                    var ms = Num(cobj, "maxSpeed", r, false);
                    if (ms.HasValue) dna.Constraints.MaxSpeed = Clamp(ms.Value, 0.1f, 3f, "constraints.maxSpeed", r);
                }
                else r.Errors.Add("constraints はオブジェクトです");
            }

            ParseParts(root, dna, r);
            ParseBehaviors(root, dna, r);
            if (r.Errors.Count == 0) r.Dna = dna;
            return r;
        }

        static void ParseParts(JObject root, InventionDna dna, ValidationReport r)
        {
            if (!root.TryGetValue("parts", out var pt) || !(pt is JArray parts)) { r.Errors.Add("parts（部品配列）がありません"); return; }
            if (parts.Count == 0) { r.Errors.Add("部品が1つもありません"); return; }
            if (parts.Count > dna.Constraints.MaxParts) { r.Errors.Add($"部品数 {parts.Count} が上限 {dna.Constraints.MaxParts} を超えています"); return; }
            var ids = new HashSet<string>();
            for (int i = 0; i < parts.Count; i++)
            {
                string at = $"parts[{i}]";
                if (!(parts[i] is JObject po)) { r.Errors.Add(at + " がオブジェクトではありません"); continue; }
                RejectUnknown(po, PartFields, at, r);
                var p = new PartSpec();
                p.Id = Str(po, "id", r, true, LabCatalog.MaxIdLength, at) ?? "";
                if (p.Id.Length > 0 && !IsSafeId(p.Id)) r.Errors.Add($"{at}.id が不正です");
                if (p.Id.Length > 0 && !ids.Add(p.Id)) r.Errors.Add($"部品 id が重複しています: {p.Id}");
                p.Type = Str(po, "type", r, true, 32, at) ?? "";
                var info = LabCatalog.Part(p.Type);
                if (p.Type.Length > 0 && info == null) r.Errors.Add($"未対応の部品タイプ: {p.Type}（使用可能: {string.Join(", ", LabCatalog.PartTypeIds)}）");
                var pos = Vec(po, "position", r, at, true);
                if (pos.HasValue)
                {
                    var v = pos.Value;
                    p.Position = new Vector3(
                        Clamp(v.x, -LabCatalog.PositionLimitXZ, LabCatalog.PositionLimitXZ, at + ".position.x", r),
                        Clamp(v.y, LabCatalog.PositionMinY, LabCatalog.PositionMaxY, at + ".position.y", r),
                        Clamp(v.z, -LabCatalog.PositionLimitXZ, LabCatalog.PositionLimitXZ, at + ".position.z", r));
                }
                var sc = Vec(po, "scale", r, at, true);
                if (sc.HasValue && info != null)
                {
                    var v = sc.Value;
                    p.Scale = new Vector3(
                        Clamp(v.x, info.MinScale, info.MaxScale, at + ".scale.x", r),
                        Clamp(v.y, info.MinScale, info.MaxScale, at + ".scale.y", r),
                        Clamp(v.z, info.MinScale, info.MaxScale, at + ".scale.z", r));
                }
                dna.Parts.Add(p);
            }
        }

        static void ParseBehaviors(JObject root, InventionDna dna, ValidationReport r)
        {
            if (!root.TryGetValue("behaviors", out var bt) || !(bt is JArray arr)) { r.Errors.Add("behaviors（動作配列）がありません"); return; }
            if (arr.Count > LabCatalog.MaxBehaviors) { r.Errors.Add($"動作数が上限 {LabCatalog.MaxBehaviors} を超えています"); return; }
            var seen = new HashSet<string>();
            for (int i = 0; i < arr.Count; i++)
            {
                string at = $"behaviors[{i}]";
                if (!(arr[i] is JObject bo)) { r.Errors.Add(at + " がオブジェクトではありません"); continue; }
                RejectUnknown(bo, BehaviorFields, at, r);
                var b = new BehaviorSpec { Type = Str(bo, "type", r, true, 32, at) ?? "" };
                var info = LabCatalog.Behavior(b.Type);
                if (info == null)
                {
                    if (b.Type.Length > 0)
                        r.Errors.Add($"未対応の機能: {b.Type}（使用可能: {string.Join(", ", LabCatalog.BehaviorTypeIds)}）");
                    continue;
                }
                if (!seen.Add(b.Type)) r.Errors.Add($"機能が重複しています: {b.Type}");

                b.TargetPart = Str(bo, "targetPart", r, false, LabCatalog.MaxIdLength, at);
                if (info.RequiresTargetPart && b.TargetPart == null)
                {
                    var auto = dna.Parts.FirstOrDefault(p => info.AllowedTargetTypes.Contains(p.Type));
                    if (auto != null) { b.TargetPart = auto.Id; r.Warnings.Add($"{b.Type} の対象部品を {auto.Id} に自動設定しました"); }
                    else r.Errors.Add($"{info.LabelJa}（{b.Type}）には {string.Join("/", info.AllowedTargetTypes)} 部品が必要です");
                }
                if (b.TargetPart != null)
                {
                    var target = dna.FindPart(b.TargetPart);
                    if (target == null) r.Errors.Add($"{b.Type} の対象部品 {b.TargetPart} が存在しません");
                    else if (info.AllowedTargetTypes.Length > 0 && !info.AllowedTargetTypes.Contains(target.Type))
                        r.Errors.Add($"{b.Type} は {target.Type} 部品に接続できません");
                }

                JObject po = null;
                if (bo.TryGetValue("parameters", out var ptok))
                {
                    po = ptok as JObject;
                    if (po == null) r.Errors.Add($"{at}.parameters はオブジェクトです");
                }
                if (po != null)
                    foreach (var prop in po.Properties())
                        if (info.Param(prop.Name) == null)
                            r.Errors.Add($"{b.Type} に未対応のパラメータ: {prop.Name}");
                foreach (var pinfo in info.Parameters)
                {
                    float? v = po != null ? Num(po, pinfo.Key, r, false, $"{at}.parameters") : null;
                    b.Parameters[pinfo.Key] = v.HasValue ? Clamp(v.Value, pinfo.Min, pinfo.Max, $"{b.Type}.{pinfo.Key}", r) : pinfo.Default;
                }
                dna.Behaviors.Add(b);
            }
        }

        // ---------- serialize ----------
        public static string ToJson(InventionDna dna, Formatting formatting = Formatting.Indented) => ToJObject(dna).ToString(formatting);

        public static JObject ToJObject(InventionDna dna)
        {
            var parts = new JArray();
            foreach (var p in dna.Parts)
                parts.Add(new JObject { ["id"] = p.Id, ["type"] = p.Type, ["position"] = V(p.Position), ["scale"] = V(p.Scale) });
            var behaviors = new JArray();
            foreach (var b in dna.Behaviors)
            {
                var bo = new JObject { ["type"] = b.Type };
                if (b.TargetPart != null) bo["targetPart"] = b.TargetPart;
                var po = new JObject();
                foreach (var kv in b.Parameters.OrderBy(k => k.Key, StringComparer.Ordinal)) po[kv.Key] = Round(kv.Value);
                bo["parameters"] = po;
                behaviors.Add(bo);
            }
            return new JObject
            {
                ["schemaVersion"] = dna.SchemaVersion, ["id"] = dna.Id, ["name"] = dna.Name, ["category"] = dna.Category,
                ["purpose"] = dna.Purpose, ["mode"] = dna.Mode, ["parts"] = parts, ["behaviors"] = behaviors,
                ["constraints"] = new JObject { ["maxParts"] = dna.Constraints.MaxParts, ["maxSpeed"] = Round(dna.Constraints.MaxSpeed) },
                ["revision"] = dna.Revision,
                ["parentRevision"] = dna.ParentRevision.HasValue ? new JValue(dna.ParentRevision.Value) : JValue.CreateNull(),
            };
        }

        static JArray V(Vector3 v) => new JArray(Round(v.x), Round(v.y), Round(v.z));
        static double Round(float f) => Math.Round(f, 4);

        // ---------- helpers ----------
        static void RejectUnknown(JObject o, HashSet<string> allowed, string at, ValidationReport r)
        {
            foreach (var p in o.Properties())
                if (!allowed.Contains(p.Name)) r.Errors.Add($"{at} に未知のフィールド: {Shorten(p.Name)}");
        }

        static string Str(JObject o, string key, ValidationReport r, bool required, int maxLen, string at = null)
        {
            string label = at == null ? key : at + "." + key;
            if (!o.TryGetValue(key, out var t) || t.Type == JTokenType.Null)
            {
                if (required) r.Errors.Add($"必須項目 {label} がありません");
                return null;
            }
            if (t.Type != JTokenType.String) { r.Errors.Add($"{label} は文字列です"); return null; }
            var s = t.Value<string>().Trim();
            if (required && s.Length == 0) { r.Errors.Add($"{label} が空です"); return null; }
            if (s.Length > maxLen) { r.Errors.Add($"{label} が長すぎます（最大{maxLen}文字）"); return null; }
            return s;
        }

        static int? Int(JObject o, string key, ValidationReport r, bool required)
        {
            if (!o.TryGetValue(key, out var t) || t.Type == JTokenType.Null)
            {
                if (required) r.Errors.Add($"必須項目 {key} がありません");
                return null;
            }
            if (t.Type != JTokenType.Integer) { r.Errors.Add($"{key} は整数です"); return null; }
            try { return t.Value<int>(); }
            catch (OverflowException) { r.Errors.Add($"{key} が範囲外です"); return null; }
        }

        static float? Num(JObject o, string key, ValidationReport r, bool required, string at = null)
        {
            string label = at == null ? key : at + "." + key;
            if (!o.TryGetValue(key, out var t) || t.Type == JTokenType.Null)
            {
                if (required) r.Errors.Add($"必須項目 {label} がありません");
                return null;
            }
            if (t.Type != JTokenType.Integer && t.Type != JTokenType.Float) { r.Errors.Add($"{label} は数値です"); return null; }
            double d = t.Value<double>();
            if (double.IsNaN(d) || double.IsInfinity(d) || Math.Abs(d) > 1e6) { r.Errors.Add($"{label} が不正な数値です"); return null; }
            return (float)d;
        }

        static Vector3? Vec(JObject o, string key, ValidationReport r, string at, bool required)
        {
            if (!o.TryGetValue(key, out var t)) { if (required) r.Errors.Add($"必須項目 {at}.{key} がありません"); return null; }
            if (!(t is JArray a) || a.Count != 3) { r.Errors.Add($"{at}.{key} は数値3つの配列です"); return null; }
            var f = new float[3];
            for (int i = 0; i < 3; i++)
            {
                if (a[i].Type != JTokenType.Integer && a[i].Type != JTokenType.Float) { r.Errors.Add($"{at}.{key} に数値以外があります"); return null; }
                double d = a[i].Value<double>();
                if (double.IsNaN(d) || double.IsInfinity(d) || Math.Abs(d) > 1e6) { r.Errors.Add($"{at}.{key} が不正な数値です"); return null; }
                f[i] = (float)d;
            }
            return new Vector3(f[0], f[1], f[2]);
        }

        static float Clamp(float v, float min, float max, string label, ValidationReport r)
        {
            if (v < min || v > max)
            {
                float c = Mathf.Clamp(v, min, max);
                r.Warnings.Add($"{label} を {v.ToString("0.###", CultureInfo.InvariantCulture)} → {c.ToString("0.###", CultureInfo.InvariantCulture)} に制限しました");
                return c;
            }
            return v;
        }

        static int ClampInt(int v, int min, int max, string label, ValidationReport r)
        {
            if (v < min || v > max) { r.Warnings.Add($"{label} を {v} → {Mathf.Clamp(v, min, max)} に制限しました"); return Mathf.Clamp(v, min, max); }
            return v;
        }

        static string Shorten(string s) => s.Length > 80 ? s.Substring(0, 80) + "…" : s;
    }
}
