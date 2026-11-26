using System.Collections.Generic;
using System.Linq;
using ImpossibleLab.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace ImpossibleLab
{
    /// <summary>輪の計器コンソール：S04 実験の条件、S05 結果・実験中。</summary>
    public sealed partial class LabUI
    {
        int _tabL, _tabR, _planPage;
        VecGraphic _liveArc;
        string _tabFor;

        /// <summary>板の切り替えを選ぶ（検証用）。</summary>
        public void SetConsoleTabs(int left, int right) { _tabL = left; _tabR = right; Refresh(); }

        /// <summary>板の見出しに並べる切り替え（選んでいる方に下線）。</summary>
        void ConTabs(float plateX, float plateY, string[] labels, int index, System.Action<int> set, string name)
        {
            var c = _con; float tx = plateX + 20f;
            for (int i = 0; i < labels.Length; i++)
            {
                bool on = i == index; int k = i;
                var t = c.T(tx, plateY + 34f, labels[i], on ? 20f : 18f, on ? LabTheme.ConInk : LabTheme.ConMut, bold: on);
                float w = t.preferredWidth;
                if (on) c.G.Rect(tx, plateY + 44f, w, 3f, LabTheme.ConCy);
                else c.Hit(tx - 8f, plateY + 6f, w + 16f, 46f, () => set(k), name + i);
                tx += w + 28f;
            }
        }

        /// <summary>折り返す文を上から積む。yMax を超える分は出さない（最後の文は … で切る）。</summary>
        float ConParas(float x, float y, float w, float yMax, IEnumerable<(string text, Color col)> items, float size = 16f)
        {
            foreach (var (text, col) in items)
            {
                if (string.IsNullOrEmpty(text)) continue;
                if (y + size * 1.3f > yMax) break;
                var t = _con.TBox(x, y, w, yMax - y, text, size, col);
                float h = Mathf.Min(t.GetPreferredValues(text, w, 0f).y, yMax - y);
                t.rectTransform.sizeDelta = new Vector2(w, h);
                y += h + 8f;
            }
            return y;
        }

        static readonly System.Text.RegularExpressions.Regex ValueWithUnit = new System.Text.RegularExpressions.Regex(@"(\d+(?:\.\d+)?) ([^\s／/・]+)");

        /// <summary>条件の文で、数と単位（「2 時間」など）の間で折り返さないようにする（TextMeshPro の nobr）。</summary>
        static string NoBreakValues(string s) => string.IsNullOrEmpty(s) ? s : ValueWithUnit.Replace(s, "<nobr>$1 $2</nobr>");

        // ---------------- 計算の前提（S04・S05 共通） ----------------
        List<(string, Color)> ConMethodLines(RainExperimentConfig cfg, RainExperimentResult r, ExperimentPlan plan = null)
        {
            var ink = LabTheme.ConInk; var mut = LabTheme.ConMut;
            var list = new List<(string, Color)>();
            if (r != null && r.IsScenarioLab)
            {
                foreach (var a in r.Assumptions.Take(3)) list.Add((a, ink));
                list.Add(("計算は開始時に一括で行い、実験場では記録を再生します（シード固定で再現できます）", mut));
            }
            else if (plan != null && !PlanCandidates.IsLegacy(plan))
            {
                // 組み立て式の実験の実行中（結果ができる前）は、シナリオの説明を出す（雨の実験の説明を出さない）
                list.Add((ExperimentCatalog.Scenario(plan.Scenario)?.Description ?? "", ink));
                list.Add(("計算は開始時に一括で行い、実験場では記録を再生します（シード固定で再現できます）", mut));
            }
            else if (cfg.Environment == LabEnvironments.ObstacleCourse)
            {
                list.Add(("案内役が決まったルートを歩き、ロボットは追従速度・追従距離・検知距離に従って動きます（0.02 秒刻みの計算）", ink));
                list.Add(("衝突回数は、障害物の円柱に触れた回数です。到着の遅れは、案内役の到着から、ロボットが追従距離まで近づくまでの秒数です", ink));
                list.Add(("ゲーム的な運動モデルです。シード固定で再現できます", mut));
            }
            else if (cfg.Environment == LabEnvironments.FlightDeck)
            {
                list.Add(("人型モデルが靴で離陸し、目標地点まで飛んで着地します（0.02 秒刻みの計算）。横風と突風で横に揺れます", ink));
                list.Add(("安定度＝100×(1−横揺れの RMS÷0.4 m)。着地点のずれは、目標地点の中心からの距離です", ink));
                list.Add(("観察モード（視点は動きません）。シード固定で再現できます", mut));
            }
            else
            {
                list.Add(("雨粒を直線として、キャノピー（円板＋側面カバー）と、判定用の円筒（半径 0.22 m・高さ 1.75 m）との衝突を計算します", ink));
                list.Add(("被濡れスコア＝身体に当たった雨粒の数÷実験の秒数。防護率＝1−装置あり命中÷装置なし命中", ink));
                list.Add(("流体解析ではありません。シード固定で再現できます", mut));
            }
            if (r != null) foreach (var l in r.Limits.Take(2)) list.Add(("注意：" + l, LabTheme.ConOr));
            return list;
        }

        /// <summary>要望をどこまで確かめたか／現実にするときの課題（仕様：根拠と不確実性を示す）。</summary>
        void ConRequests(float x, float y, float yMax, ExperimentPlan plan, bool done)
        {
            var dna = App.Current?.Current;
            var items = new List<(string, Color)>();
            if (dna != null)
            {
                var rows = App.RequestRows(dna, plan, out bool derived);
                string Names(IEnumerable<FunctionRow> xs) => string.Join("、", xs.Select(v => v.Need));
                var here = rows.Where(v => v.CoveredHere).ToList();
                var elsewhere = rows.Where(v => v.Verifiable && !v.CoveredHere && v.PlanTitle != null).ToList();
                var none = rows.Where(v => v.Verifiable && !v.CoveredHere && v.PlanTitle == null).ToList();
                var unv = rows.Where(v => !v.Verifiable).ToList();
                items.Add(($"この実験で{(done ? "確かめた" : "確かめる")} {here.Count} 件：" + (here.Count > 0 ? Names(here) : "なし"), LabTheme.ConCy));
                if (elsewhere.Count > 0) items.Add(($"別の実験で確かめる {elsewhere.Count} 件：" + Names(elsewhere), LabTheme.ConInk));
                if (none.Count > 0) items.Add(($"実験の候補がない {none.Count} 件：" + Names(none), LabTheme.ConMut));
                if (unv.Count > 0) items.Add(($"確かめられない {unv.Count} 件：" + Names(unv), LabTheme.ConMut));
                if (derived) items.Add(("AI の対応表がないため、機能ごとに数えています", LabTheme.ConMut));
            }
            string cat = dna?.Category;
            items.Add((cat == "carrier" ? "現実にするときの課題（推定）：人混み・段差での安全、歩道のルール。近い技術は追従型の運搬ロボット"
                : cat == "mobility" ? "現実にするときの課題（推定）：人を浮かせる電力（数十〜数百 kW）、噴流・転落の安全、法規制"
                : cat == "protection" ? "現実にするときの課題（推定）：電池、プロペラの安全、騒音、強風。近い技術はドローン・頭上追従カメラ"
                : "現実にするときの課題：試作品での計測と、係数の出典の確認", LabTheme.ConInk));
            items.Add(("実現性・安全性は保証しません", LabTheme.ConMut));
            ConParas(x, y, 414f, yMax, items);
        }

        // ---------------- S04 実験の条件 ----------------
        const int ConPlansPerPage = 3, ConEnvRowsPerPage = 5;

        void BuildC04()
        {
            var c = _con;
            const float LX = ConsoleLayer.LX, PW = ConsoleLayer.PW, RX = ConsoleLayer.RX;
            var cfg = App.TestConfig;
            var dna = App.Current.Current;
            var removed = new List<string>();
            var plan = App.ActivePlan(removed);
            bool legacy = App.PlanIsLegacy;
            var errors = new List<string>();
            if (plan != null) ExperimentPlanJson.Validate(plan, dna, errors);
            var mods = plan?.Modules ?? new List<PlanModule>();

            c.Dial();
            ConEnvironment(mods, ConTopView(dna, -1));

            // 左上：実験を選ぶ ／ 計算の前提
            c.Plate(LX, 78f, PW, 468f);
            ConTabs(LX, 78f, new[] { "実験を選ぶ", "計算の前提" }, _tabL, i => { _tabL = i; Refresh(); }, "TabL");
            CourseRun preview = null; string fail = null;
            if (!legacy && plan != null)
            {
                // 実行前に同じ計算を走らせる。発明の版と計画が同じなら使い回す
                string key = dna.Id + "#" + dna.Revision + "#" + ExperimentPlanJson.ToJson(plan);
                if (errors.Count == 0 && key == _previewKey) preview = _preview;
                else if (errors.Count == 0)
                {
                    try { preview = ScenarioRunner.RunPlan(dna, plan); _preview = preview; _previewKey = key; }
                    catch (System.ArgumentException e) { fail = e.Message; }
                }
                else fail = errors[0];
            }
            if (_tabL == 0)
            {
                int n = App.PlanOptions.Count;
                int pages = Mathf.Max(1, (n + ConPlansPerPage - 1) / ConPlansPerPage);
                _planPage = Mathf.Clamp(_planPage, 0, pages - 1);
                ConPager(LX, _planPage, pages, () => { _planPage--; Refresh(); }, () => { _planPage++; Refresh(); });
                for (int i = _planPage * ConPlansPerPage; i < Mathf.Min(n, (_planPage + 1) * ConPlansPerPage); i++)
                {
                    var o = App.PlanOptions[i]; int idx = i; bool sel = i == App.PlanIndex;
                    float y = 132f + (i - _planPage * ConPlansPerPage) * 138f;
                    if (sel) c.SelBox(36f, y, 438f, 130f); else c.Hit(36f, y, 438f, 130f, () => App.SelectPlan(idx), "Plan" + i);
                    c.TLine(52f, y + 36f, 330f, o.Title, 21f, sel ? LabTheme.ConOr : LabTheme.ConInk, bold: sel);
                    c.T(460f, y + 36f, o.FromAi ? "AI の案" : "標準", 16f, LabTheme.ConMut, anchor: 1);
                    string aim = o.Aim.Length > 0 ? o.Aim : ExperimentCatalog.Scenario(o.Plan?.Scenario)?.Description ?? "";
                    c.T(52f, y + 74f, "確かめる", 16f, LabTheme.ConMut); c.TLine(128f, y + 74f, 336f, aim, 16f);
                    var ms = o.Plan != null ? PlanEditor.ExpectedMetrics(o.Plan, dna).Where(m => m.Primary).Select(m => m.LabelJa).ToList() : new List<string>();
                    c.T(52f, y + 104f, "測る", 16f, LabTheme.ConMut); c.TLine(128f, y + 104f, 336f, ms.Count > 0 ? string.Join("、", ms) : "測れる項目がありません", 16f);
                }
                if (n == 0) c.T(48f, 170f, "実験の候補がありません", 17f, LabTheme.ConMut);
            }
            else
            {
                var items = new List<(string, Color)>();
                if (legacy && cfg.Environment == LabEnvironments.RainLab)
                    foreach (var s in ModeRules.Evaluate(dna, cfg.Clamped()).Assumptions.Take(4)) items.Add((s, LabTheme.ConInk));
                else if (legacy)
                    foreach (var s in CourseSimulator.Run(dna, cfg).Result.Assumptions.Take(4)) items.Add((s, LabTheme.ConInk));
                else
                {
                    items.Add((ExperimentCatalog.Scenario(plan?.Scenario)?.Description ?? plan?.Scenario, LabTheme.ConInk));
                    if (preview != null) foreach (var s in preview.Result.Assumptions.Take(4)) items.Add((s, LabTheme.ConInk));
                    else if (fail != null) items.Add(("実行できません：" + fail, LabTheme.ConOr));
                }
                ConParas(48f, 136f, 414f, 540f, items);
            }

            // 左下：物理の決まり
            c.Plate(LX, 558f, PW, 190f, "物理の決まり");
            bool dream = cfg.Mode == LabModes.Dream;
            c.Button(48f, 606f, 204f, 52f, "DREAM", () => App.SetMode(LabModes.Dream), dream ? ConKind.On : ConKind.Normal);
            c.Button(258f, 606f, 204f, 52f, "REALITY", () => App.SetMode(LabModes.Reality), dream ? ConKind.Normal : ConKind.On);
            c.T(48f, 692f, dream ? "重さと電力の制限なしで試します" : "簡易モデルの参考値です（保証はしません）", 17f);
            if (!dream && legacy) c.Button(48f, 700f, 220f, 44f, "前提係数と出典", App.ShowCoefficients, ConKind.Quiet, 17f);
            else c.T(48f, 722f, dream ? "まず形と大きさを決めるときに" : "電池の容量と装置の重さが結果に入ります", 16f, LabTheme.ConMut);

            ConEnvBox(plan, mods, dna, legacy, cfg);

            // 右下：合格の基準
            var crit = legacy ? (plan != null ? plan.Criteria : new List<PlanCriterion>()) : PlanEditor.EffectiveCriteria(plan, dna);
            string src = legacy ? "実験場の基準" : plan.Criteria.Count > 0 ? (App.SelectedPlan?.FromAi == true ? "AI の案の条件" : "計画の条件") : "目安（要出典）";
            c.Plate(RX, 558f, PW, 190f, "合格の基準", src);
            var lines = new List<(string, Color)>();
            if (!legacy && plan.Scenario == ExperimentCatalog.ApproachDevice)
            {
                var ex = plan.Expect ?? ProgramExpectation.FromProgram(dna);
                lines.Add(("期待：" + (ex != null ? ex.Describe(dna) : "決められません（距離の条件で関節を動かすルールがない）"), LabTheme.ConInk));
            }
            if (crit.Count == 0) lines.Add(("なし：測れる項目に目安がないため、結果は合否なしになります", LabTheme.ConMut));
            foreach (var k in crit.Take(3)) lines.Add((PlanText.Criterion(k) + (string.IsNullOrEmpty(k.Reason) ? "" : $"（{k.Reason}）"), LabTheme.ConInk));
            foreach (var s in removed.Take(1)) lines.Add(("外した計測：" + s, LabTheme.ConMut));
            if (preview != null) foreach (var l in preview.Result.Limits.Where(l => !l.StartsWith("合格条件", System.StringComparison.Ordinal)).Take(1)) lines.Add(("注意：" + l, LabTheme.ConOr));
            if (App.PlanRejected.Count > 0) lines.Add(($"使えなかった AI の案 {App.PlanRejected.Count} 件：{App.PlanRejected[0]}", LabTheme.ConMut));
            ConParas(RX + 20f, 606f, 414f, 742f, lines);

            if (errors.Count > 0) c.T(1560f, 776f, ConsoleLayer.Fit("実行できません：" + errors[0], 30), 16f, LabTheme.ConOr, anchor: 1);
            ConFrame(new List<ConBtn> { new ConBtn("編集に戻る", App.BackToEditing, ConKind.Quiet, 150f) },
                new ConBtn("実験を始める", App.StartExperiment, ConKind.Primary, 280f, errors.Count == 0),
                new Dictionary<int, UnityAction> { { 2, App.BackToEditing } });
        }

        /// <summary>計器の中に環境を描く：雨（粒の数＝雨量）、風（向きと強さ）。外周のゲージは先頭の条件の値。</summary>
        void ConEnvironment(List<PlanModule> mods, float shieldR)
        {
            var c = _con;
            var rain = mods.FirstOrDefault(m => m.Type == "rain");
            if (rain != null && rain.Params.TryGetValue("intensity", out float mm))
            {
                uint seed = 7u;
                float Rnd() { seed = seed * 1664525u + 1013904223u; return (seed >> 8) / 16777216f; }
                int n = Mathf.RoundToInt(mm * 5f);
                for (int i = 0; i < n; i++)
                {
                    float r = Mathf.Sqrt(Rnd()) * 205f, a = Rnd() * 360f;
                    if (r < shieldR + 4f) continue; // 防ぐ範囲の中には描かない
                    c.G.Disc(ConsoleLayer.Pol(r, a), 2.4f, VecGraphic.Soft(LabTheme.ConCy, 0.7f));
                }
            }
            var wind = mods.FirstOrDefault(m => m.Type == "wind");
            if (wind != null && wind.Params.TryGetValue("speed", out float speed) && wind.Params.TryGetValue("directionDeg", out float deg) && speed > 0f)
            {
                // 風は deg の向きへ吹く（0°＝前＝上）。風上の側に三角を置き、中心へ向ける
                float rad = deg * Mathf.Deg2Rad;
                var u = new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad)); var v = new Vector2(-u.y, u.x);
                var tip = ConsoleLayer.C - u * 150f;
                c.G.Poly(LabTheme.ConOr, tip, tip - u * 34f + v * 20f, tip - u * 34f - v * 20f);
                foreach (float off in new[] { -64f, 64f })
                {
                    var s = ConsoleLayer.C - u * 204f + v * off; var e = s + u * (24f + speed * 9f);
                    c.G.Dashed(s, e, 2.5f, LabTheme.ConOr, 14f, 8f);
                    c.G.Poly(LabTheme.ConOr, e + u * 4f, e - u * 12f + v * 8f, e - u * 12f - v * 8f);
                }
            }
            var first = mods.FirstOrDefault(m => ExperimentCatalog.Module(m.Type) != null && m.Params.Count > 0);
            if (first == null) { c.Gauge(null, 100f); c.BigWord("条件なし", "その場で装置の性能だけを測ります"); return; }
            var info = ExperimentCatalog.Module(first.Type);
            var p = info.Params.First(x => first.Params.ContainsKey(x.Key));
            float val = first.Params[p.Key];
            c.Gauge(val - p.Min, Mathf.Max(1e-6f, p.Max - p.Min));
            string rest = string.Join("　", mods.Where(m => m != first).Select(m => ExperimentCatalog.Module(m.Type)?.LabelJa).Where(s => s != null).Take(3));
            c.BigNumber(val.ToString(p.Integer ? "0" : "0.##", System.Globalization.CultureInfo.InvariantCulture), p.Unit, p.LabelJa + (rest.Length > 0 ? " ／ ほかの条件：" + rest : ""));
        }

        /// <summary>右上：環境（条件ごとの値、足す・外す、シード）。</summary>
        void ConEnvBox(ExperimentPlan plan, List<PlanModule> mods, InventionDna dna, bool legacy, RainExperimentConfig cfg)
        {
            var c = _con;
            const float RX = ConsoleLayer.RX, PW = ConsoleLayer.PW;
            c.Plate(RX, 78f, PW, 468f, "環境");
            var rows = new List<(PlanModule m, ModuleInfo info, ModuleParam p, bool first)>();
            foreach (var m in mods)
            {
                var info = ExperimentCatalog.Module(m.Type);
                if (info == null) continue;
                bool first = true;
                foreach (var p in info.Params.Where(x => m.Params.ContainsKey(x.Key))) { rows.Add((m, info, p, first)); first = false; }
            }
            int pages = Mathf.Max(1, (rows.Count + ConEnvRowsPerPage - 1) / ConEnvRowsPerPage);
            _modulePage = Mathf.Clamp(_modulePage, 0, pages - 1);
            ConPager(RX, _modulePage, pages, () => { _modulePage--; Refresh(); }, () => { _modulePage++; Refresh(); });
            if (rows.Count == 0) c.T(RX + 20f, 160f, "条件なし（その場で装置の性能だけを測ります）", 16f, LabTheme.ConMut);
            int shown = 0;
            foreach (var (m, info, p, first) in rows.Skip(_modulePage * ConEnvRowsPerPage).Take(ConEnvRowsPerPage))
            {
                float y = 124f + shown++ * 64f;
                float v = m.Params[p.Key];
                string value = m.Type == "wind" && p.Key == "directionDeg" ? DirJa(v) : PlanText.FormatParam(p, v);
                string mod = m.Type, key = p.Key;
                if (first) c.TLine(RX + 20f, y + 24f, 136f, info.LabelJa, 17f, bold: true);
                c.TLine(RX + 20f, y + (first ? 48f : 38f), 136f, p.LabelJa, 16f, LabTheme.ConMut);
                c.Tri(RX + 156f, y + 8f, -1, () => App.StepPlanParam(mod, key, -1), name: "St_" + key);
                c.T(RX + 262f, y + 40f, value, value.Length > 7 ? 17f : 22f, LabTheme.ConCy, bold: true, anchor: 0);
                c.Tri(RX + 320f, y + 8f, +1, () => App.StepPlanParam(mod, key, +1), name: "St_" + key);
                if (first && !legacy)
                {
                    bool can = PlanEditor.CanRemove(App.EditPlan, mod);
                    c.Button(RX + 374f, y + 10f, 66f, 44f, can ? "外す" : "必須", () => App.RemovePlanModule(mod), ConKind.Quiet, 16f, can);
                }
            }
            // 下の帯：条件を足す、シード、初期値
            if (legacy) c.T(RX + 20f, 480f, "この実験場は、値の変更だけができます", 16f, LabTheme.ConMut);
            else
            {
                var addable = PlanEditor.Addable(App.EditPlan, dna);
                c.T(RX + 20f, 482f, addable.Count > 0 ? "足す" : "足せる条件はありません", 16f, LabTheme.ConMut);
                int i = 0;
                foreach (var type in addable.Take(3))
                {
                    string t = type;
                    c.Button(RX + 66f + i++ * 126f, 452f, 120f, 44f, ConsoleLayer.Fit(ExperimentCatalog.Module(type).LabelJa, 5), () => App.AddPlanModule(t), ConKind.Quiet, 16f);
                }
            }
            c.T(RX + 20f, 530f, $"シード {cfg.Seed}", 16f, LabTheme.ConMut);
            c.Button(RX + 150f, 500f, 100f, 44f, "変える", App.NewSeed, ConKind.Quiet, 16f);
            if (!legacy) c.Button(RX + 290f, 500f, 150f, 44f, "初期値に戻す", App.ResetPlan, ConKind.Quiet, 16f);
        }

        /// <summary>REALITY の前提係数と出典（値・幅・確度）。S04 から開く。</summary>
        void BuildCCoefficients()
        {
            var c = _con;
            const float LX = ConsoleLayer.LX, RX = ConsoleLayer.RX, PW = ConsoleLayer.PW, TW = 1062f;
            var model = RealityModel.Default;
            int pages = Mathf.Max(1, App.CoefficientPageCount);
            int page = Mathf.Clamp(App.CoefficientPage, 0, pages - 1);
            c.Plate(LX, 78f, TW, 670f, "REALITY の前提係数と出典");
            ConPager(LX, page, pages, () => App.StepCoefficientPage(-1), () => App.StepCoefficientPage(+1), TW);
            c.T(LX + 20f, 148f, "幅は、各係数の下限から上限までで計算します", 16f, LabTheme.ConMut);
            float rowH = Mathf.Min(78f, 570f / Mathf.Max(1, LabApp.CoefficientsPerPage));
            int i = 0;
            foreach (var k in model.Coefficients.Skip(page * LabApp.CoefficientsPerPage).Take(LabApp.CoefficientsPerPage))
            {
                float y = 166f + i++ * rowH;
                c.G.Rect(LX + 8f, y, TW - 16f, rowH - 6f, VecGraphic.Soft(LabTheme.ConCy, 0.05f));
                string range = Mathf.Approximately(k.Min, k.Max) ? "固定" : k.FormatValue(k.Min) + "〜" + k.FormatValue(k.Max);
                c.TLine(LX + 24f, y + 27f, 420f, k.LabelJa, 18f, bold: true);
                c.T(LX + 470f, y + 27f, k.FormatValue(k.Value), 19f, LabTheme.ConCy, bold: true);
                c.T(LX + 640f, y + 27f, range, 16f, LabTheme.ConMut);
                c.TLine(LX + 24f, y + 52f, 760f, k.Source, 16f, LabTheme.ConMut);
                bool sure = k.Status == "standard", soft = k.Status == "tuning" || k.Status == "design";
                c.T(LX + TW - 24f, y + 27f, model.StatusLabel(k.Status), 16f, sure ? LabTheme.ConCy : soft ? LabTheme.ConMut : LabTheme.ConOr, bold: true, anchor: 1);
            }
            c.Plate(RX, 78f, PW, 400f, "計算式");
            ConParas(RX + 20f, 130f, 414f, 470f, new[]
            {
                ("総重量 = 本体 + キャノピー面積×面密度 + 側面カバー面積×面密度 + 電池", LabTheme.ConInk),
                ("風荷重 F = ½ρ Cd A v²（A：横から見た面積）", LabTheme.ConInk),
                ("必要推力 T = √(重さ² + F²)", LabTheme.ConInk),
                ("ホバー電力 = T^1.5 / √(2ρA) ÷ (FM × 駆動効率) + 制御電力（運動量理論）", LabTheme.ConInk),
                ("稼働時間 = 電池容量 ÷ 消費電力", LabTheme.ConInk),
                ("風の中で誘導電力が下がる効果は無視しており、電力は多めに見積もっています。", LabTheme.ConMut),
            });
            c.Plate(RX, 490f, PW, 258f, "確度の内訳");
            var st = new List<(string, Color)>();
            st.Add((string.Join("　", new[] { "standard", "literature", "design", "estimate", "tuning" }.Select(x => $"{model.StatusLabel(x)} {model.CountStatus(x)} 件")), LabTheme.ConInk));
            st.Add(("要調査・要確認の値は、文献や市販品の公開仕様で確かめてから確定します。詳細は docs/reality_model.md。", LabTheme.ConOr));
            ConParas(RX + 20f, 540f, 414f, 742f, st);
            ConFrame(new List<ConBtn> { new ConBtn("条件に戻る", App.HideCoefficients, ConKind.Quiet, 150f) }, null);
        }

        // ---------------- S05 結果・実験中 ----------------
        void BuildC05()
        {
            var c = _con;
            const float LX = ConsoleLayer.LX, PW = ConsoleLayer.PW, RX = ConsoleLayer.RX;
            var cfg = App.TestConfig.Clamped();
            var dna = App.Current.Current;
            if (App.Sm.State == LabState.RunningTest) { BuildC05Running(cfg, dna); return; }
            var r = App.LastResult;
            if (r == null) { ConFrame(new List<ConBtn>(), new ConBtn("改良して再実験", App.ImproveAndRetest, ConKind.Primary, 300f)); return; }
            bool ok = r.Success;

            c.Dial();
            ConTopView(dna, -1);
            var g = ResultCriteria.Headline(r);
            if (g != null)
            {
                float? prev = null; string prevLabel = null;
                var pg = ResultCriteria.Headline(App.BaselineResult);
                if (pg != null && pg.Key == g.Key) { prev = pg.Value; prevLabel = $"Rev.{App.BaselineResult.Revision} {pg.ValueText}"; }
                string pass = g.Pass.HasValue ? $"合格 {g.Pass.Value:0.##}{g.Unit}{(g.LowerIsBetter ? " 以下" : "")}" : null;
                c.Gauge(g.Value, g.Max, g.Pass, pass, prev, prevLabel);
                string tail = ok ? "合格" : g.HasCriterion && !g.Passed ? $"合格まであと {Mathf.Abs(g.Pass.Value - g.Value):0.#}{g.Unit}" : "要改良";
                c.BigNumber(g.ValueText, g.Unit, $"{g.LabelJa} ／ {tail}");
            }
            else { c.Gauge(null, 100f); c.BigWord(ok ? "合格" : "要改良", "計測値がありません"); }

            // 左上：結果 ／ 計算の前提
            c.Plate(LX, 78f, PW, 468f);
            ConTabs(LX, 78f, new[] { "結果", "計算の前提" }, _tabL, i => { _tabL = i; Refresh(); }, "TabL");
            if (_tabL == 1) ConParas(48f, 136f, 414f, 540f, ConMethodLines(r.Config ?? cfg, r));
            else if (r.IsRain) ConRainHits(r);
            else ConCriteria(r, ok);

            // 左下：この実験の条件
            var plan = ResultCriteria.PlanOf(r);
            c.Plate(LX, 558f, PW, 190f, "この実験の条件", $"Rev.{r.Revision}・{LabModes.Label(plan?.Mode ?? cfg.Mode)}");
            ConParas(48f, 606f, 414f, 742f, new[] { (NoBreakValues(r.DescribeConditions()), LabTheme.ConInk) }, 17f);

            // 右上：次の一手 ／ 要望の確認
            c.Plate(RX, 78f, PW, 468f);
            ConTabs(RX, 78f, new[] { "次に変えるところ", "要望の確認" }, _tabR, i => { _tabR = i; Refresh(); }, "TabR");
            if (_tabR == 1) ConRequests(RX + 20f, 136f, 540f, plan, true);
            else
            {
                var items = new List<(string, Color)>();
                bool hasTimeline = r.Timeline.Count > 0;
                foreach (var h in App.ImprovementHints(r).Take(hasTimeline ? 1 : 2)) items.Add((h, LabTheme.ConInk));
                if (hasTimeline)
                {
                    // 動作プログラムが動いた順（仕様 28.9）
                    var tl = r.Timeline.Where(t => !t.Contains(" set_flag ")).Select(t => ProgramText.TimelineLine(t, dna)).ToList();
                    items.Add(($"動いた順（{r.Timeline.Count(t => t.Contains(" start "))} 回）：" + string.Join(" → ", tl.Take(3)) + (tl.Count > 3 ? $" …ほか {tl.Count - 3} 件" : ""), LabTheme.ConMut));
                }
                if (items.Count == 0) items.Add(("計測値から出せる改善案はありません", LabTheme.ConMut));
                ConParas(RX + 20f, 136f, 414f, 322f, items, 17f);
                c.Button(RX + 20f, 330f, 414f, 56f, "同じ条件でもう一度", App.RerunSameConditions);
                c.Button(RX + 20f, 398f, 414f, 56f, cfg.Mode == LabModes.Dream ? "REALITY で電力・重さ・費用も見る" : "DREAM で比べる", App.RerunOtherMode);
                c.Button(RX + 20f, 466f, 414f, 56f, "保存して終わる", App.Save, ConKind.Quiet);
            }

            // 右下：前の版（または別の決まり）と比べる
            RainExperimentResult a = null, b = null; string head = "前の版と比べる", pair = null;
            if (App.BaselineResult != null) { a = App.BaselineResult; b = r; pair = $"Rev.{a.Revision} → Rev.{r.Revision}"; }
            else if (App.OtherModeResult != null)
            {
                a = App.OtherModeResult.Config.Mode == LabModes.Dream ? App.OtherModeResult : r;
                b = a == r ? App.OtherModeResult : r;
                head = "物理の決まりで比べる"; pair = "DREAM → REALITY";
            }
            c.Plate(RX, 558f, PW, 190f, head, pair);
            if (a != null)
            {
                int i = 0;
                foreach (var m in ExperimentComparer.Compare(a, b).Take(4))
                {
                    float y = 626f + i++ * 30f;
                    c.TLine(RX + 20f, y, 170f, m.Label, 16f, LabTheme.ConMut);
                    c.T(RX + 356f, y, $"{m.Before} → {m.After}", 16f, anchor: 1);
                    c.T(RX + 434f, y, m.Verdict, 16f, m.Verdict == "改善" ? LabTheme.ConCy : m.Verdict == "悪化" ? LabTheme.ConOr : LabTheme.ConMut, bold: true, anchor: 1);
                }
            }
            else ConParas(RX + 20f, 610f, 414f, 742f, new[] { ("比べる相手がまだありません。改良してもう一度実験するか、物理の決まりを切り替えると、ここに差が出ます。", LabTheme.ConMut) });

            ConFrame(new List<ConBtn> { new ConBtn("製品の説明", OpenExplain, ConKind.Quiet, 160f) }, new ConBtn("改良して再実験", App.ImproveAndRetest, ConKind.Primary, 300f),
                new Dictionary<int, UnityAction> { { 2, App.ImproveAndRetest } });
        }

        void BuildC05Running(RainExperimentConfig cfg, InventionDna dna)
        {
            var c = _con;
            const float LX = ConsoleLayer.LX, PW = ConsoleLayer.PW, RX = ConsoleLayer.RX;
            var rp = App.RunningPlan; bool lab = rp != null;
            c.Dial(orbit: false);
            ConTopView(dna, -1);
            c.Gauge(null, 100f);
            _liveArc = c.LiveArc();
            c.BigWord("実験中", lab ? PlanCandidates.ScenarioLabel(rp.Scenario) : LabEnvironments.LabelJa(cfg.Environment), LabTheme.ConCy);

            c.Plate(LX, 78f, PW, 468f, "いまの計測");
            _liveText = c.TBox(48f, 136f, 414f, 400f, "", 22f);
            _liveText.overflowMode = TextOverflowModes.Overflow;
            c.Plate(LX, 558f, PW, 190f, "この実験の条件", LabModes.Label(cfg.Mode));
            ConParas(48f, 606f, 414f, 742f, new[] { (NoBreakValues(lab ? PlanText.Describe(rp) : cfg.Describe()), LabTheme.ConInk) }, 17f);
            c.Plate(RX, 78f, PW, 468f, "要望の確認");
            ConRequests(RX + 20f, 136f, 540f, rp ?? ExperimentPlan.FromConfig(App.TestConfig), false);
            c.Plate(RX, 558f, PW, 190f, "計算の前提");
            var res = lab && App.Course?.Run != null && App.Course.Run.Result.IsScenarioLab ? App.Course.Run.Result : null;
            ConParas(RX + 20f, 606f, 414f, 742f, ConMethodLines(cfg, res, rp));
            ConFrame(new List<ConBtn> { new ConBtn("停止（記録しない）", App.StopExperiment, ConKind.Normal, 260f) }, null);
            UpdateLive();
        }

        /// <summary>雨の実験：当たった場所ごとの粒の数と割合。</summary>
        void ConRainHits(RainExperimentResult r)
        {
            var c = _con;
            int total = r.HeadHits + r.UpperHits + r.LowerHits;
            c.T(462f, 112f, $"{r.Config.DurationSec:0} 秒で {total} 粒", 17f, LabTheme.ConMut, anchor: 1);
            var parts = new[] { ("頭", r.HeadHits), ("上半身", r.UpperHits), ("下半身", r.LowerHits) };
            for (int i = 0; i < parts.Length; i++)
            {
                var (name, n) = parts[i];
                float y = 136f + i * 100f;
                float share = total > 0 ? (float)n / total : 0f;
                Color col = n == 0 ? LabTheme.ConMut : LabTheme.ConCy;
                c.T(52f, y + 24f, name, 19f);
                var num = c.T(52f, y + 70f, n.ToString(), 44f, col, bold: true);
                c.T(52f + num.preferredWidth + 8f, y + 70f, "粒", 17f, LabTheme.ConMut);
                c.T(460f, y + 70f, $"{Mathf.RoundToInt(share * 100f)}%", 26f, col, bold: true, anchor: 1);
                c.SegBar(52f, y + 78f, 408f, 14f, 40, Mathf.RoundToInt(share * 40f), LabTheme.ConCy);
            }
            var lines = new[]
            {
                ("装置なしで当たる数", $"{r.BaselineHits} 粒（雨粒 {r.DropsTotal}）"),
                ("キャノピーで遮った数", $"{r.Blocked} 粒"),
                ("風による装置のずれ", $"{r.DriftM:0.00} m" + (r.CanHoldPosition ? "" : "（位置を保てない）")),
                ("消費エネルギー", r.EnergyWh.HasValue ? $"{r.EnergyWh.Value:0.000} Wh ／ 約 {r.RuntimeMinutes ?? 0:0} 分" : "DREAM では考えません"),
            };
            for (int i = 0; i < lines.Length; i++)
            {
                c.T(52f, 458f + i * 25f, lines[i].Item1, 16f, LabTheme.ConMut);
                c.T(460f, 458f + i * 25f, lines[i].Item2, 16f, anchor: 1);
            }
        }

        /// <summary>合格条件ごとの判定と、そのほかの計測値。</summary>
        void ConCriteria(RainExperimentResult r, bool ok)
        {
            var c = _con;
            var checks = ResultCriteria.Evaluate(r);
            int passedN = checks.Count(k => k.Passed);
            // 条件はすべて満たしたのに不合格（積荷を運べない・電池切れなど）のときは、その理由を出す
            string why = checks.Count == 0 ? "合格条件なし（測れる項目に目安がありません）"
                : ok ? $"合格条件 {checks.Count} 件をすべて満たしました"
                : passedN < checks.Count ? $"合格条件 {passedN} / {checks.Count} 件を満たしました"
                : "条件は満たしましたが、" + (r.Limits.FirstOrDefault() ?? "ほかの条件を満たしません");
            c.TLine(48f, 152f, 414f, why, 16f, ok || checks.Count == 0 ? LabTheme.ConMut : LabTheme.ConOr);
            float y = 166f;
            foreach (var k in checks.Take(4))
            {
                c.G.Rect(36f, y, 438f, 58f, VecGraphic.Soft(LabTheme.ConCy, 0.05f));
                c.TLine(52f, y + 25f, 290f, k.Target, 17f);
                c.TLine(52f, y + 48f, 290f, string.IsNullOrEmpty(k.Reason) ? (r.IsScenarioLab ? "簡易モデルの計算" : "シミュレーションの実測") : k.Reason, 16f, LabTheme.ConMut);
                c.T(460f, y + 25f, k.Actual, 20f, k.Measured ? LabTheme.ConCy : LabTheme.ConMut, bold: true, anchor: 1);
                c.T(460f, y + 48f, !k.Measured ? "測れず" : k.Passed ? "達成" : "未達", 16f, !k.Measured ? LabTheme.ConMut : k.Passed ? LabTheme.ConCy : LabTheme.ConOr, bold: true, anchor: 1);
                y += 62f;
            }
            var judged = new HashSet<string>(checks.Select(k => k.Metric));
            var others = r.Metrics.Where(m => !judged.Contains(m.Key)).OrderBy(m => m.Primary ? 0 : 1).ToList();
            if (others.Count == 0 || y > 500f) return;
            y += 22f;
            c.T(52f, y, "そのほかの計測値", 16f, LabTheme.ConMut);
            foreach (var m in others)
            {
                y += 26f;
                if (y > 536f) break;
                c.TLine(52f, y, 280f, (m.Primary ? "" : "参考：") + m.LabelJa, 16f);
                c.T(460f, y, m.Display, 16f, LabTheme.ConCy, anchor: 1);
            }
        }
    }
}
