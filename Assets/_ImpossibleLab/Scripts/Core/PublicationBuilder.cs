using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ImpossibleLab.Core
{
    /// <summary>公開するときに利用者が選ぶこと（公開の確認と同意の画面で決める）。</summary>
    public sealed class PublishOptions
    {
        /// <summary>表示名。匿名なら使わない。</summary>
        public string DisplayName = "";
        public bool Anonymous = true;
        /// <summary>同意した時刻（端末の時刻、ISO 形式）。</summary>
        public string AgreedAt = "";
    }

    /// <summary>
    /// 保存した発明から、発明ライブラリに送る公開データを作る（docs/PUBLISH_LIBRARY.md 3・4章）。
    /// 形は PC 側の proxy/src/library/schema.ts の Publication と同じ。サイトに出す文と計算済みの値まで、ここで作る。
    /// 文字数と数の上限は PC 側と同じに切る（PC 側でも確かめる）。設計に無いことは書かない。
    /// </summary>
    public static class PublicationBuilder
    {
        public const int Format = 1;
        /// <summary>同意の文面の版（PC 側の CONSENT_VERSION と同じ）。</summary>
        public const string ConsentVersion = "2026-11-26";

        static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        public static JObject Build(SavedInvention inv, PublishOptions opt)
        {
            if (inv?.Current == null) throw new ArgumentNullException(nameof(inv));
            opt = opt ?? new PublishOptions();
            bool anonymous = opt.Anonymous || string.IsNullOrWhiteSpace(opt.DisplayName);
            return new JObject
            {
                ["format"] = Format,
                ["creator"] = new JObject { ["displayName"] = anonymous ? "" : Line(opt.DisplayName.Trim(), 40), ["anonymous"] = anonymous },
                ["consent"] = new JObject { ["version"] = ConsentVersion, ["agreed"] = true, ["agreedAt"] = Line(opt.AgreedAt, 32) },
                ["invention"] = Invention(inv),
            };
        }

        public static JObject Invention(SavedInvention inv)
        {
            var dna = inv.Current;
            var needs = Needs(dna);
            var experiments = inv.Experiments.Skip(Math.Max(0, inv.Experiments.Count - 40)).ToList();
            return new JObject
            {
                ["id"] = dna.Id,
                ["revision"] = dna.Revision,
                ["name"] = Line(dna.Name, 60),
                ["summary"] = Text(ProductExplain.Overview(dna).Value, 400),
                ["purpose"] = Text(dna.Purpose, 200),
                ["tags"] = new JArray(dna.Tags.Take(6).Select(t => Labeled(t, LabCatalog.Tag(t)?.LabelJa ?? t))),
                ["sceneTags"] = new JArray(),
                ["mount"] = Labeled(dna.Mount, LabCatalog.Mount(dna.Mount)?.LabelJa ?? dna.Mount),
                ["request"] = Text(inv.RequestText, 400),
                ["designSource"] = inv.DesignSource == "ai" || inv.DesignSource == "offline" ? inv.DesignSource : "",
                ["needs"] = new JArray(needs.Take(12).Select(n => new JObject
                {
                    ["need"] = Line(n.Need, 120),
                    ["status"] = n.Status,
                    ["blocks"] = new JArray(n.Blocks.Take(8).Select(b => Labeled(b, LabCatalog.Behavior(b)?.LabelJa ?? b))),
                    ["how"] = Text(ProductExplain.How(dna, n).Value, 300),
                    ["note"] = Text(n.Note, 300),
                })),
                ["parts"] = new JArray(dna.Parts.Take(24).Select(p => Part(dna, p))),
                ["experiments"] = new JArray(experiments.Select(Experiment)),
                ["revisions"] = new JArray(Revisions(inv).Take(40)),
                ["research"] = Research(dna, needs, experiments),
                ["developer"] = Developer(dna, needs, experiments),
            };
        }

        // ---------------- 要望・部品 ----------------
        /// <summary>機能対応表。AI の対応表が無い設計は、機能ごとの行を作る（アプリの設計の確認の画面と同じ）。</summary>
        static List<FunctionNeed> Needs(InventionDna dna)
        {
            if (dna.FunctionMap.Count > 0) return dna.FunctionMap;
            var rows = FunctionMapView.Build(dna, PlanCandidates.Build(dna, null), out _);
            return rows.Select(r => new FunctionNeed { Need = r.Need, Status = r.Status, Blocks = new List<string>(r.Blocks), Note = r.Note }).ToList();
        }

        static JObject Part(InventionDna dna, PartSpec p)
        {
            var mass = MassModel.Part(p).Kg;
            var model = p.ModelInfo;
            var o = new JObject
            {
                ["id"] = p.Id,
                ["type"] = Line(p.Type, 32),
                ["label"] = Line(LabCatalog.Part(p.Type)?.LabelJa ?? p.Type, 40),
                ["role"] = Text(ProductExplain.Role(dna, p).Value, 300),
                ["shape"] = Line(LabCatalog.Shape(p.ShapeOrDefault)?.LabelJa ?? p.ShapeOrDefault, 40),
                ["material"] = Line(LabCatalog.Material(p.MaterialOrDefault)?.LabelJa ?? p.MaterialOrDefault, 40),
                ["sizeM"] = new JArray(Size(p.Scale.x), Size(p.Scale.y), Size(p.Scale.z)),
                ["massKg"] = float.IsNaN(mass) || float.IsInfinity(mass) ? null : new JValue(Math.Round(Mathf.Clamp(mass, 0f, 1000f), 3)),
            };
            // 品番（あれば）：名前と価格の目安
            if (model != null)
            {
                o["model"] = Line(model.LabelJa, 60);
                o["priceJpy"] = Math.Round(Mathf.Clamp(model.PriceJpy * PartModels.SizeRatio(p, model), 0f, 10000000f));
            }
            return o;
        }

        static double Size(float v) => Math.Round(Mathf.Clamp(Mathf.Abs(v), 0f, 10f), 3);

        // ---------------- 実験 ----------------
        static JObject Experiment(RainExperimentResult r)
        {
            string mode = r.IsScenarioLab && r.Plan != null ? r.Plan.Mode : r.Config?.Mode ?? LabModes.Dream;
            var metrics = Metrics(r);
            bool? passed = r.IsRain ? r.Success : r.Passed;
            string criteria = !string.IsNullOrEmpty(r.Criteria) ? RatioCriteria(r.Criteria)
                : r.IsRain ? $"雨を防いだ割合 {RainExperimentResult.SuccessCoverage * 100:0} % 以上" : "";
            return new JObject
            {
                ["revision"] = Math.Max(1, r.Revision),
                ["title"] = Line(Title(r), 80),
                ["mode"] = mode == LabModes.Reality ? LabModes.Reality : LabModes.Dream,
                ["conditions"] = new JArray(Conditions(r).Take(12).Select(c => new JObject { ["label"] = Line(c.label, 40), ["value"] = Line(c.value, 40) })),
                ["metrics"] = new JArray(metrics.Take(16)),
                ["passed"] = passed.HasValue ? new JValue(passed.Value) : JValue.CreateNull(),
                ["criteria"] = Text(criteria, 200),
                ["seed"] = Mathf.Clamp(r.IsScenarioLab && r.Plan != null ? r.Plan.Seed : r.Config?.Seed ?? 0, 0, 1000000000),
                ["notes"] = new JArray(r.Limits.Concat(r.Assumptions).Distinct().Take(10).Select(n => Text(n, 160))),
                ["at"] = Line(r.StartTime ?? "", 32),
            };
        }

        /// <summary>実験の名前：組み立て式は場面（＋主な条件）、既存の実験場はその名前。</summary>
        public static string Title(RainExperimentResult r)
        {
            if (r.IsScenarioLab && r.Plan != null)
            {
                string scene = PlanCandidates.ScenarioLabel(r.Plan.Scenario);
                var main = r.Plan.Modules.Select(m => ExperimentCatalog.Module(m.Type)?.LabelJa).FirstOrDefault(x => !string.IsNullOrEmpty(x));
                return main != null ? $"{scene}（{main}）" : scene;
            }
            return r.Environment == LabEnvironments.ObstacleCourse ? "障害物コース" : r.Environment == LabEnvironments.FlightDeck ? "飛行デッキ" : "雨の実験場";
        }

        /// <summary>条件：アプリの条件の説明（「雨量 30 mm/h / 風速 4.0 m/s」など）を項目と値に分ける。モード・場面・シードは別の欄にあるので除く。</summary>
        public static List<(string label, string value)> Conditions(RainExperimentResult r)
        {
            var text = r.DescribeConditions();
            var pieces = text.Split(new[] { " ／ ", " / " }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
            var list = new List<(string, string)>();
            string modeLabel = LabModes.Label(r.IsScenarioLab && r.Plan != null ? r.Plan.Mode : r.Config?.Mode ?? LabModes.Dream);
            string sceneLabel = r.IsScenarioLab && r.Plan != null ? PlanCandidates.ScenarioLabel(r.Plan.Scenario) : null;
            foreach (var p in pieces)
            {
                if (p == modeLabel || p == sceneLabel || p.StartsWith("シード", StringComparison.Ordinal)) continue;
                // 「10 秒」のように数字で始まる項目は、長さの指定（時間）
                if (char.IsDigit(p[0])) { list.Add((p.EndsWith("秒", StringComparison.Ordinal) ? "時間" : "条件", p)); continue; }
                int sp = p.IndexOf(' ');
                if (sp > 0) list.Add((p.Substring(0, sp), p.Substring(sp + 1)));
                else list.Add(("条件", p));
            }
            return list;
        }

        /// <summary>計測：組み立て式の計測の一覧。雨の実験場（一覧が無い）は防いだ割合などをここで作る。</summary>
        static List<JObject> Metrics(RainExperimentResult r)
        {
            var list = new List<JObject>();
            if (r.Metrics.Count > 0)
            {
                foreach (var m in r.Metrics)
                {
                    var info = ExperimentCatalog.Metric(m.Key);
                    // 0〜1 の割合（日陰率・検知率など）は % で見せる
                    bool ratio = info != null && info.Min == 0f && info.Max == 1f && string.IsNullOrWhiteSpace(m.Unit);
                    list.Add(Metric(m.Key, m.LabelJa, m.Value, ratio ? "%" : (m.Unit ?? "").Trim(),
                        ratio ? (m.Value * 100f).ToString("0.0", Ci) : m.Value.ToString(string.IsNullOrEmpty(m.Format) ? "0.00" : m.Format, Ci),
                        m.LowerIsBetter, m.Primary));
                }
                return list;
            }
            if (!r.IsRain) return list;
            list.Add(Metric("coverageRatio", "雨を防いだ割合", r.CoverageRatio, "%", (r.CoverageRatio * 100f).ToString("0.0", Ci), false, true));
            list.Add(Metric("wetnessScore", "体に当たった雨粒", r.WetnessScore, "粒/秒", r.WetnessScore.ToString("0.0", Ci), true, false));
            if (r.DriftM > 0f || r.Config?.Mode == LabModes.Reality) list.Add(Metric("driftM", "風で流された距離", r.DriftM, "m", r.DriftM.ToString("0.00", Ci), true, false));
            if (r.PowerW.HasValue) list.Add(Metric("powerW", "浮かび続ける電力", r.PowerW.Value, "W", r.PowerW.Value.ToString("0", Ci), true, false));
            if (r.RuntimeMinutes.HasValue) list.Add(Metric("runtimeMin", "電池で使える時間", r.RuntimeMinutes.Value, "分", r.RuntimeMinutes.Value.ToString("0.0", Ci), false, false));
            if (r.MassKg.HasValue) list.Add(Metric("massKg", "電池を含む重さ", r.MassKg.Value, "kg", r.MassKg.Value.ToString("0.00", Ci), true, false));
            if (r.EnergyWh.HasValue) list.Add(Metric("energyWh", "使った電力量", r.EnergyWh.Value, "Wh", r.EnergyWh.Value.ToString("0.000", Ci), true, false));
            return list;
        }

        /// <summary>合格の目安の中の 0〜1 の割合（「日陰率≥0.70」）を、計測と同じ % にする（「日陰率 ≥ 70 %」）。</summary>
        public static string RatioCriteria(string criteria)
        {
            foreach (var info in ExperimentCatalog.Metrics.Where(m => m.Min == 0f && m.Max == 1f && string.IsNullOrWhiteSpace(m.Unit)))
            {
                var re = new System.Text.RegularExpressions.Regex(System.Text.RegularExpressions.Regex.Escape(info.LabelJa) + @"\s*([≥≤<>]=?)\s*(0?\.\d+|1(?:\.0+)?|0)(?![\d.])");
                criteria = re.Replace(criteria, m => $"{info.LabelJa} {m.Groups[1].Value} {(float.Parse(m.Groups[2].Value, Ci) * 100f).ToString("0.#", Ci)} %");
            }
            return criteria;
        }

        static JObject Metric(string key, string label, float value, string unit, string display, bool lowerIsBetter, bool primary) => new JObject
        {
            ["key"] = Line(key, 40),
            ["label"] = Line(label, 40),
            ["value"] = float.IsNaN(value) || float.IsInfinity(value) ? 0 : Math.Round(Mathf.Clamp(value, -1e9f, 1e9f), 4),
            ["unit"] = Line(unit, 12),
            ["display"] = Line(display, 24),
            ["lowerIsBetter"] = lowerIsBetter,
            ["primary"] = primary,
        };

        // ---------------- 改良の履歴 ----------------
        /// <summary>
        /// 版ごとの改良。記録（何を頼んだか）がある版はそれを、無い版（依頼文を保存する前）は前の版との違いから作る（source = unknown）。
        /// resultChanged：その版の実験の主な計測が、同じ実験の前の版と変わった。
        /// </summary>
        static IEnumerable<JObject> Revisions(SavedInvention inv)
        {
            var versions = inv.History.Concat(new[] { inv.Current }).GroupBy(d => d.Revision).Select(g => g.Last()).OrderBy(d => d.Revision).ToList();
            for (int i = 1; i < versions.Count; i++)
            {
                var prev = versions[i - 1]; var cur = versions[i];
                var note = inv.NoteFor(cur.Revision);
                var changes = note != null && note.Changes.Count > 0
                    ? note.Changes.Select(c => (c.Label, c.Before, c.After)).ToList()
                    : Diff(prev, cur);
                yield return new JObject
                {
                    ["revision"] = cur.Revision,
                    ["parent"] = Mathf.Clamp(note?.ParentRevision ?? cur.ParentRevision ?? prev.Revision, 0, cur.Revision),
                    ["source"] = note != null ? RevisionNote.NormalizeSource(note.Source) : "unknown",
                    ["request"] = Text(note?.Request, 400),
                    ["summary"] = Text(note?.Summary, 120),
                    ["changes"] = new JArray(changes.Take(12).Select(c => new JObject { ["label"] = Line(c.Item1, 80), ["before"] = Line(c.Item2, 80), ["after"] = Line(c.Item3, 80) })),
                    ["resultChanged"] = ResultChanged(inv.Experiments, cur.Revision),
                };
            }
        }

        static bool ResultChanged(List<RainExperimentResult> exps, int revision)
        {
            foreach (var r in exps.Where(e => e.Revision == revision))
            {
                var before = exps.LastOrDefault(e => e.Revision < revision && e.SameConditionsAs(r, compareMode: true));
                if (before == null) continue;
                var a = PrimaryValue(before); var b = PrimaryValue(r);
                if (a.HasValue && b.HasValue && Mathf.Abs(a.Value - b.Value) > 1e-4f) return true;
                if (before.Success != r.Success) return true;
            }
            return false;
        }

        static float? PrimaryValue(RainExperimentResult r) =>
            r.Metrics.Count > 0 ? r.Metrics.FirstOrDefault(m => m.Primary)?.Value : r.IsRain ? r.CoverageRatio : (float?)null;

        /// <summary>前の版との違い（機能の値・部品の追加と削除・形・素材・大きさ）。改良の記録が無い版に使う。</summary>
        public static List<(string, string, string)> Diff(InventionDna a, InventionDna b)
        {
            var list = new List<(string, string, string)>();
            foreach (var bb in b.Behaviors)
            {
                var ab = a.FindBehavior(bb.Type);
                var info = LabCatalog.Behavior(bb.Type);
                if (ab == null) { list.Add(("機能", "なし", info?.LabelJa ?? bb.Type)); continue; }
                foreach (var kv in bb.Parameters)
                    if (ab.Parameters.TryGetValue(kv.Key, out var old) && Mathf.Abs(old - kv.Value) > 1e-4f)
                    {
                        var p = info?.Param(kv.Key);
                        string unit = string.IsNullOrEmpty(p?.Unit) ? "" : " " + p.Unit;
                        list.Add(($"{info?.LabelJa ?? bb.Type}：{p?.LabelJa ?? kv.Key}", old.ToString("0.00", Ci) + unit, kv.Value.ToString("0.00", Ci) + unit));
                    }
            }
            foreach (var ab in a.Behaviors.Where(x => b.FindBehavior(x.Type) == null))
                list.Add(("機能", LabCatalog.Behavior(ab.Type)?.LabelJa ?? ab.Type, "なし"));
            foreach (var bp in b.Parts)
            {
                var ap = a.FindPart(bp.Id);
                string name = LabCatalog.Part(bp.Type)?.LabelJa ?? bp.Type;
                if (ap == null) { list.Add(("部品", "なし", $"{name}（{bp.Id}）")); continue; }
                if (ap.ShapeOrDefault != bp.ShapeOrDefault) list.Add(($"{name}（{bp.Id}）の形", LabCatalog.Shape(ap.ShapeOrDefault)?.LabelJa ?? ap.ShapeOrDefault, LabCatalog.Shape(bp.ShapeOrDefault)?.LabelJa ?? bp.ShapeOrDefault));
                if (ap.MaterialOrDefault != bp.MaterialOrDefault) list.Add(($"{name}（{bp.Id}）の素材", LabCatalog.Material(ap.MaterialOrDefault)?.LabelJa ?? ap.MaterialOrDefault, LabCatalog.Material(bp.MaterialOrDefault)?.LabelJa ?? bp.MaterialOrDefault));
                if ((ap.Scale - bp.Scale).sqrMagnitude > 1e-8f) list.Add(($"{name}（{bp.Id}）の大きさ", Dim(ap.Scale), Dim(bp.Scale)));
            }
            foreach (var ap in a.Parts.Where(x => b.FindPart(x.Id) == null))
                list.Add(("部品", $"{LabCatalog.Part(ap.Type)?.LabelJa ?? ap.Type}（{ap.Id}）", "なし"));
            return list;
        }

        static string Dim(Vector3 s) => $"{Mathf.Abs(s.x).ToString("0.00", Ci)}×{Mathf.Abs(s.y).ToString("0.00", Ci)}×{Mathf.Abs(s.z).ToString("0.00", Ci)} m";

        // ---------------- 研究者向け・開発者向け ----------------
        static JObject Research(InventionDna dna, List<FunctionNeed> needs, List<RainExperimentResult> exps)
        {
            // 計算の前提：使っている機能の簡易モデルの説明と、実験で使った前提・制限
            var models = dna.Behaviors.Select(b => LabCatalog.Behavior(b.Type)).Where(i => i != null && !string.IsNullOrEmpty(i.ModelJa))
                .Select(i => $"{i.LabelJa}：{i.ModelJa}");
            var notes = exps.SelectMany(e => e.Assumptions.Concat(e.Limits));
            return new JObject
            {
                ["unverifiable"] = new JArray(needs.Where(n => n.Status == "unverifiable").Take(12).Select(n => new JObject
                {
                    ["need"] = Line(n.Need, 120),
                    ["reason"] = Text(string.IsNullOrEmpty(n.Note) ? "この版の機能と実験では確かめられない" : n.Note, 300),
                })),
                ["assumptions"] = new JArray(models.Concat(notes).Distinct().Take(20).Select(s => Text(s, 200))),
                ["simplified"] = new JArray(needs.Where(n => n.Status == "simplified").Take(20)
                    .Select(n => Text(string.IsNullOrEmpty(n.Note) ? n.Need : $"{n.Need}：{n.Note}", 200))),
            };
        }

        static JObject Developer(InventionDna dna, List<FunctionNeed> needs, List<RainExperimentResult> exps)
        {
            float total = MassModel.TotalKg(dna);
            float? tip = null;
            if (StabilityModel.Applies(dna)) { var st = StabilityModel.Evaluate(dna); if (st.Valid) tip = Mathf.Clamp(st.TipBackDeg, 0f, 90f); }
            var reality = exps.LastOrDefault(e => e.Revision == dna.Revision && (e.Config?.Mode == LabModes.Reality || e.Plan?.Mode == LabModes.Reality));
            var challenges = needs.Where(n => n.Status == "unverifiable").Select(n => string.IsNullOrEmpty(n.Note) ? $"{n.Need}は確かめられていない" : $"{n.Need}：{n.Note}").ToList();
            if (reality != null && !reality.Success)
            {
                var m = Metrics(reality).FirstOrDefault(x => x.Value<bool>("primary"));
                if (m != null)
                {
                    string unit = m.Value<string>("unit");
                    challenges.Add($"REALITY では{m.Value<string>("label")}が {m.Value<string>("display")}{(string.IsNullOrEmpty(unit) ? "" : " " + unit)} で、合格の目安に届かない");
                }
            }
            return new JObject
            {
                ["totalMassKg"] = float.IsNaN(total) ? null : new JValue(Math.Round(Mathf.Clamp(total, 0f, 10000f), 3)),
                ["tipAngleDeg"] = tip.HasValue ? new JValue(Math.Round(tip.Value, 1)) : JValue.CreateNull(),
                ["powerW"] = reality?.PowerW is float pw ? new JValue(Math.Round(Mathf.Clamp(pw, 0f, 1e7f), 1)) : JValue.CreateNull(),
                ["runtimeMin"] = reality?.RuntimeMinutes is float rm ? new JValue(Math.Round(Mathf.Clamp(rm, 0f, 1e6f), 1)) : JValue.CreateNull(),
                ["challenges"] = new JArray(challenges.Take(12).Select(s => Text(s, 200))),
            };
        }

        // ---------------- 文字 ----------------
        static JObject Labeled(string id, string label) => new JObject { ["id"] = Line(id, 32), ["label"] = Line(label, 40) };

        /// <summary>改行を空白にして、max 文字までに切る。</summary>
        public static string Line(string s, int max) => Text((s ?? "").Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' '), max);

        /// <summary>max 文字までに切る（サロゲートペアを分けない）。</summary>
        public static string Text(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length <= max) return s;
            int n = max;
            if (char.IsHighSurrogate(s[n - 1])) n--;
            return s.Substring(0, n);
        }
    }
}
