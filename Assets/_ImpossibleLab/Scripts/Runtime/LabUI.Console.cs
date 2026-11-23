using System.Collections.Generic;
using System.Linq;
using ImpossibleLab.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace ImpossibleLab
{
    /// <summary>輪の計器コンソール：上下の帯（全画面で共通）、計器の中の図、S03 編集。</summary>
    public sealed partial class LabUI
    {
        ConsoleLayer _con;
        string _selParam;   // S03 で選んでいる性能の値（"機能/キー"）。オレンジで示す

        /// <summary>S04 から開く、REALITY の前提係数の一覧を出しているか。</summary>
        bool ShowingCoefficients =>
            App.Sm.State == LabState.ConfiguringTest && App.TestConfig.Mode == LabModes.Reality && App.CoefficientPage >= 0 && RealityModel.Default != null;

        struct ConBtn
        {
            public string Label; public UnityAction OnClick; public ConKind Kind; public float W, Size; public bool Disabled;
            public ConBtn(string label, UnityAction onClick, ConKind kind = ConKind.Normal, float w = 132f, bool enabled = true, float size = 0f)
            { Label = label; OnClick = onClick; Kind = kind; W = w; Disabled = !enabled; Size = size; }
        }

        static readonly string[] ConStages = { "指示", "設計", "編集", "実験", "結果", "保存" };

        // ---------------- 枠（上の帯・下の帯） ----------------
        void ConFrame(IList<ConBtn> left, ConBtn? primary, Dictionary<int, UnityAction> nav = null)
        {
            var c = _con;
            int cur = StepIndex();
            c.T(40f, 46f, "IMPOSSIBLE LAB", 22f, bold: true);
            if (App.Current != null && cur > 0 && cur < 5)
            {
                var dna = App.Current.Current;
                var name = c.T(248f, 46f, ConsoleLayer.Fit(dna.Name, 18), 20f);
                c.T(248f + name.preferredWidth + 14f, 46f, "Rev." + dna.Revision, 20f, LabTheme.ConCy, bold: true);
            }
            // パネルの位置（VR で必要な操作。コントローラーの A／B・メニューでも同じことができる）
            float ux = 860f;
            c.Button(ux, 17f, 112f, 44f, "▼ 下げる", () => App.NudgeConsoleHeight(-1), ConKind.Quiet, 16f); ux += 118f;
            c.Button(ux, 17f, 112f, 44f, "▲ 上げる", () => App.NudgeConsoleHeight(+1), ConKind.Quiet, 16f); ux += 118f;
            c.Button(ux, 17f, 100f, 44f, "正面へ", App.RecenterToHead, ConKind.Quiet, 16f); ux += 106f;
            c.Button(ux, 17f, 146f, 44f, App.Seated ? "座位 → 立位" : "立位 → 座位", App.ToggleSeated, ConKind.Quiet, 16f);
            // PC とのつながり（点は、つながっているときだけ光る）
            var dot = new Vector2(1372f, 39f);
            if (App.ProxyOnline == true) { c.G.Disc(dot, 12f, VecGraphic.Soft(LabTheme.ConCy, 0.16f)); c.G.Disc(dot, 6f, LabTheme.ConCy); }
            else c.G.Disc(dot, 6f, App.ProxyOnline == false ? LabTheme.ConOr : LabTheme.ConCyDim);
            c.T(1388f, 46f, App.ProxyOnline == true ? "PC 接続中" : App.ProxyOnline == false ? "PC 未接続" : "PC 確認中", 17f, LabTheme.ConMut);
            bool busy = App.RevisionBusy || App.Sm.State == LabState.GeneratingDesign;
            c.T(1560f, 46f, busy ? "AI 作成中" : App.ProxyOnline == true ? "AI 待機" : "見本で設計", 17f, busy ? LabTheme.ConCy : LabTheme.ConMut, anchor: 1);

            // 6段階（今の段階だけ光る）
            const float sw = 104f; float sx = 800f - sw * 3f;
            for (int i = 0; i < ConStages.Length; i++)
            {
                float x = sx + i * sw; bool now = i == cur, done = i < cur;
                var pts = new[] { new Vector2(x + 14f, 806f), new Vector2(x + sw - 4f, 806f), new Vector2(x + sw - 18f, 850f), new Vector2(x, 850f) };
                if (now) { c.G.Glow(LabTheme.ConCy, pts); c.G.Poly(LabTheme.ConCy, pts); }
                else if (done) c.G.Poly(LabTheme.ConCyDeep, pts);
                c.G.PolyLine(1.5f, now ? LabTheme.ConCy : LabTheme.ConCyDim, true, pts);
                UnityAction go = null;
                if (!now && nav != null && nav.TryGetValue(i, out go) && go != null) c.Hit(x + 6f, 806f, sw - 14f, 44f, go, "Stage_" + ConStages[i]);
                c.T(x + sw * 0.5f - 2f, 835f, ConStages[i], 18f, now ? LabTheme.ConOnCy : done || go != null ? LabTheme.ConInk : LabTheme.ConMut, bold: now, anchor: 0);
            }
            float bx = 40f;
            foreach (var b in left)
            {
                c.Button(bx, 800f, b.W, 56f, b.Label, b.OnClick, b.Kind, b.Size > 0f ? b.Size : 20f, !b.Disabled);
                bx += b.W + 8f;
            }
            if (primary.HasValue)
            {
                var p = primary.Value;
                c.Button(1560f - p.W, 794f, p.W, 68f, p.Label, p.OnClick, p.Kind, p.Size > 0f ? p.Size : 24f, !p.Disabled);
            }
            _toast = c.T(800f, 776f, App.Toast ?? StatusLine(), 17f, LabTheme.ConMut, anchor: 0);
        }

        // ---------------- 計器の中：発明を上から見た図 ----------------
        /// <summary>部品を上から見た形（前が上）。選んでいる部品はオレンジ。戻り値は防ぐ範囲の半径（px。なければ 0）。</summary>
        float ConTopView(InventionDna dna, int selected)
        {
            var c = _con;
            const float M = ConsoleLayer.M, Limit = 205f;
            Vector2 At(Vector3 p) => new Vector2(ConsoleLayer.C.x + p.x * M, ConsoleLayer.C.y - p.z * M);
            if (MassModel.MountLimitKg(dna.Mount).HasValue) c.Person();
            var shield = dna.FindBehavior("rain_shield");
            float shieldR = 0f;
            if (shield != null)
            {
                var info = LabCatalog.Behavior("rain_shield")?.Param("radius");
                if (info != null) { shieldR = dna.GetParameter("rain_shield", "radius", info.Default) * M; c.Canopy(shieldR); }
            }
            for (int i = 0; i < dna.Parts.Count; i++)
            {
                var part = dna.Parts[i];
                var at = At(part.Position);
                float hw = Mathf.Abs(part.Scale.x) * 0.5f * M, hd = Mathf.Abs(part.Scale.z) * 0.5f * M;
                if ((at - ConsoleLayer.C).magnitude + Mathf.Max(hw, hd) > Limit) continue; // 計器の外には描かない
                bool sel = i == selected;
                Color col = sel ? LabTheme.ConOr : LabTheme.ConCyDim;
                float lw = sel ? 2.5f : 1.5f;
                if (part.ShapeOrDefault == "box")
                    c.G.PolyLine(lw, col, true, new Vector2(at.x - hw, at.y - hd), new Vector2(at.x + hw, at.y - hd), new Vector2(at.x + hw, at.y + hd), new Vector2(at.x - hw, at.y + hd));
                else
                {
                    // 円柱・球・カプセルは上から見ると円（幅と奥行きが違えばだ円）
                    int steps = 36; var pts = new Vector2[steps];
                    for (int k = 0; k < steps; k++) { float a = 2f * Mathf.PI * k / steps; pts[k] = new Vector2(at.x + hw * Mathf.Cos(a), at.y + hd * Mathf.Sin(a)); }
                    c.G.PolyLine(lw, col, true, pts);
                }
                if (sel)
                {
                    float pr = Mathf.Min(Mathf.Sqrt(hw * hw + hd * hd) + 10f, Limit);
                    foreach (float a in new[] { 45f, 135f, 225f, 315f }) c.G.Arc(at, pr, 3f, a - 9f, a + 9f, LabTheme.ConOr);
                }
            }
            if (shieldR > 0f)
            {
                var e = ConsoleLayer.Pol(shieldR, -138f); var t = ConsoleLayer.Pol(shieldR + 16f, -138f);
                c.G.Line(ConsoleLayer.C, e, 1.5f, LabTheme.ConCy);
                c.G.Disc(e, 4f, LabTheme.ConCy);
                c.T(t.x - 4f, t.y, (shieldR / M).ToString("0.00") + " m", 20f, LabTheme.ConCy, bold: true, anchor: 1, halo: true);
            }
            return shieldR;
        }

        /// <summary>外周のゲージと大きい数字：この発明の直近の実験（同じ条件の前の版があれば、その値も）。</summary>
        void ConLastResult(string labelPrefix)
        {
            var c = _con;
            var exps = App.Current.Experiments;
            var last = exps.LastOrDefault();
            var g = ResultCriteria.Headline(last);
            if (g == null)
            {
                c.Gauge(null, 100f);
                c.BigWord("未実験", "実験すると、ここに結果が出ます");
                return;
            }
            float? prev = null; string prevLabel = null;
            for (int i = exps.Count - 2; i >= 0; i--)
            {
                var e = exps[i];
                if (e.Revision == last.Revision || !e.SameConditionsAs(last, true)) continue;
                var pg = ResultCriteria.Headline(e);
                if (pg == null || pg.Key != g.Key) continue;
                prev = pg.Value; prevLabel = $"Rev.{e.Revision} {pg.ValueText}";
                break;
            }
            string pass = g.Pass.HasValue ? $"合格 {g.Pass.Value:0.##}{g.Unit}{(g.LowerIsBetter ? " 以下" : "")}" : null;
            c.Gauge(g.Value, g.Max, g.Pass, pass, prev, prevLabel);
            c.BigNumber(g.ValueText, g.Unit, labelPrefix + g.LabelJa);
        }

        // ---------------- S03 編集 ----------------
        const int ConRowsPerPage = 5;

        void BuildC03()
        {
            var c = _con;
            var dna = App.Current.Current;
            c.Dial();
            ConTopView(dna, App.SelectedPartIndex);
            ConLastResult("前回の実験の");

            // 左上：性能の値 ／ 動きの手順
            const float LX = ConsoleLayer.LX, PW = ConsoleLayer.PW;
            c.Plate(LX, 78f, PW, 468f);
            bool hasProgram = dna.Program != null && Features.MotionProgram;
            bool programTab = hasProgram && _s03Program;
            c.T(48f, 112f, "性能の値", programTab ? 18f : 20f, programTab ? LabTheme.ConMut : LabTheme.ConInk, bold: !programTab);
            if (hasProgram)
            {
                c.T(158f, 112f, "動きの手順", programTab ? 20f : 18f, programTab ? LabTheme.ConInk : LabTheme.ConMut, bold: programTab);
                c.G.Rect(programTab ? 158f : 48f, 122f, programTab ? 100f : 80f, 3f, LabTheme.ConCy);
                c.Hit(40f, 84f, 108f, 46f, () => { _s03Program = false; Refresh(); }, "Tab_Values");
                c.Hit(150f, 84f, 120f, 46f, () => { _s03Program = true; Refresh(); }, "Tab_Program");
            }
            if (programTab) ConProgramRows(dna);
            else ConValueRows(dna);

            ConReviseBox(dna);
            ConPartsBox(dna);
            ConMassBox(dna);

            var left = new List<ConBtn>();
            if (App.TrialHasProgram)
            {
                // 試用中：人型モデルを動かして、座ったままでも距離の条件を試す
                left.Add(new ConBtn("停止", App.ToggleDevice, ConKind.On, 100f));
                left.Add(new ConBtn("人を近づける", () => App.StepTrialPerson(-1), ConKind.Normal, 170f, App.TrialPersonDistance > LabApp.TrialPersonMin + 1e-3f));
                left.Add(new ConBtn("遠ざける", () => App.StepTrialPerson(+1), ConKind.Normal, 130f, App.TrialPersonDistance < LabApp.TrialPersonMax - 1e-3f));
            }
            else
            {
                left.Add(new ConBtn("元に戻す", App.Undo, ConKind.Quiet, 132f, App.CanUndo));
                left.Add(new ConBtn(App.DeviceRunning ? "停止" : "試用する", App.ToggleDevice, App.DeviceRunning ? ConKind.On : ConKind.Normal));
                left.Add(new ConBtn("保存", App.Save, ConKind.Normal, 100f));
            }
            ConFrame(left, new ConBtn("実験へ進む", App.GoToTestSetup, App.PendingRevision != null ? ConKind.Normal : ConKind.Primary, 260f),
                new Dictionary<int, UnityAction> { { 0, App.NewInvention }, { 3, App.GoToTestSetup }, { 5, App.OpenGallery } });
        }

        /// <summary>板の見出しの右に置くページ送り。</summary>
        void ConPager(float plateX, int page, int pages, UnityAction prev, UnityAction next, float plateW = ConsoleLayer.PW)
        {
            if (pages <= 1) return;
            float r = plateX + plateW;
            _con.Tri(r - 196f, 86f, -1, prev, enabled: page > 0, name: "Page");
            _con.T(r - 110f, 118f, $"{page + 1} / {pages}", 17f, LabTheme.ConMut, anchor: 0);
            _con.Tri(r - 72f, 86f, +1, next, enabled: page < pages - 1, name: "Page");
        }

        void ConValueRows(InventionDna dna)
        {
            var c = _con;
            var rows = new List<(BehaviorInfo info, ParameterInfo p, float v)>();
            foreach (var b in dna.Behaviors)
            {
                var info = LabCatalog.Behavior(b.Type);
                foreach (var p in info.Parameters) rows.Add((info, p, b.Parameters[p.Key]));
            }
            if (_paramPageFor != dna.Id) { _paramPageFor = dna.Id; _paramPage = 0; }
            int pages = Mathf.Max(1, (rows.Count + ConRowsPerPage - 1) / ConRowsPerPage);
            _paramPage = Mathf.Clamp(_paramPage, 0, pages - 1);
            ConPager(ConsoleLayer.LX, _paramPage, pages, () => StepParamPage(-1), () => StepParamPage(+1));

            // 改良案が変える値（あれば、その行をオレンジにして、案の値までを点滅する枠で示す）
            var proposed = new Dictionary<string, float>();
            if (App.PendingProposal != null && App.PendingRevision != null)
                foreach (var op in App.PendingProposal.Operations)
                    if (op.Op == RevisionOps.UpdateParameter) proposed[op.BehaviorType + "/" + op.Parameter] = op.Value;

            int shown = 0;
            foreach (var (info, p, v) in rows.Skip(_paramPage * ConRowsPerPage).Take(ConRowsPerPage))
            {
                float y = 136f + shown++ * 80f;
                string key = info.Type + "/" + p.Key;
                bool hasProp = proposed.TryGetValue(key, out float to);
                bool sel = hasProp || (proposed.Count == 0 && key == _selParam);
                Color col = sel ? LabTheme.ConOr : LabTheme.ConCy;
                if (sel) c.SelBox(36f, y, 438f, 76f);
                c.Hit(36f, y, 438f, 76f, () => { _selParam = key; Refresh(); }, "P_" + p.Key);
                c.T(52f, y + 27f, ConsoleLayer.Fit(p.LabelJa, 12), 18f, sel ? LabTheme.ConOr : LabTheme.ConInk, bold: sel);
                string now = LabCatalog.FormatValue(p, v);
                c.T(460f, y + 28f, hasProp ? $"{v.ToString(p.Format, System.Globalization.CultureInfo.InvariantCulture)} → {LabCatalog.FormatValue(p, to)}" : now, 24f, col, bold: true, anchor: 1);
                float span = Mathf.Max(1e-6f, p.Max - p.Min);
                int on = Mathf.RoundToInt(Mathf.Clamp01((v - p.Min) / span) * 20f);
                int pOn = hasProp ? Mathf.RoundToInt(Mathf.Clamp01((to - p.Min) / span) * 20f) : on;
                // 案が値を下げるときは、減る区画を点滅する枠にする
                c.SegBar(100f, y + 40f, 310f, 22f, 20, Mathf.Min(on, pOn), col, Mathf.Max(on, pOn), glow: sel);
                string bt = info.Type, pk = p.Key;
                c.Tri(44f, y + 27f, -1, () => { _selParam = key; App.StepParameter(bt, pk, -1); }, sel, name: "Step_" + pk);
                c.Tri(418f, y + 27f, +1, () => { _selParam = key; App.StepParameter(bt, pk, +1); }, sel, name: "Step_" + pk);
                // 防ぐ範囲を決める値は、計器の円のふちへ線でつなぐ
                if (sel && info.Type == "rain_shield" && (p.Key == "radius" || p.Key == "skirtLength"))
                {
                    var rInfo = info.Param("radius");
                    float r = rInfo != null ? dna.GetParameter("rain_shield", "radius", rInfo.Default) * ConsoleLayer.M : 0f;
                    var t = new Vector2(ConsoleLayer.C.x - r, ConsoleLayer.C.y);
                    c.G.PolyLine(1.5f, LabTheme.ConOr, false, new Vector2(474f, y + 38f), new Vector2(520f, y + 38f), new Vector2(t.x - 40f, t.y), t);
                    c.G.Disc(t, 5f, LabTheme.ConOr);
                }
            }
            if (rows.Count == 0) c.T(48f, 170f, "調整できる値はありません", 17f, LabTheme.ConMut);
        }

        /// <summary>動きの手順（仕様 28.9）：ルールのオン・オフ、数値の増減、「動かしてみる」。</summary>
        void ConProgramRows(InventionDna dna)
        {
            var c = _con;
            var rows = new List<System.Action<float>>();
            for (int i = 0; i < dna.Program.Rules.Count; i++)
            {
                var rule = dna.Program.Rules[i];
                int ri = i; string id = rule.Id;
                rows.Add(y => ConRuleHead(y, rule, ri));
                for (int k = 0; k < rule.When.Count; k++)
                {
                    var cond = rule.When[k];
                    var s = ProgramCatalog.Sensor(cond.Sensor);
                    if (cond.IsFlag || s == null || s.IsBool) continue;
                    int ck = k;
                    string val = cond.Sensor == "joint_position" ? ProgramText.JointValue(dna, cond.Joint, cond.Value) : $"{cond.Value:0.##}{s.Unit}";
                    rows.Add(y => ConStepRow(y, "条件：" + ProgramText.Condition(cond, dna), val, () => App.StepRule(id, "when", ck, -1), () => App.StepRule(id, "when", ck, +1)));
                }
                for (int k = 0; k < rule.Do.Count; k++)
                {
                    var a = rule.Do[k]; int ak = k;
                    if (a.Action == ProgramAction.MoveJoint)
                        rows.Add(y => ConStepRow(y, $"{ProgramText.PartName(dna, a.Joint)}の目標", ProgramText.JointValue(dna, a.Joint, a.Target),
                            () => App.StepRule(id, "target", ak, -1), () => App.StepRule(id, "target", ak, +1)));
                    else if (a.Action == ProgramAction.Wait)
                        rows.Add(y => ConStepRow(y, "待つ時間", $"{a.Seconds:0.#} 秒", () => App.StepRule(id, "seconds", ak, -1), () => App.StepRule(id, "seconds", ak, +1)));
                }
            }
            if (_programPageFor != dna.Id) { _programPageFor = dna.Id; _programPage = 0; }
            int pages = Mathf.Max(1, (rows.Count + ConRowsPerPage - 1) / ConRowsPerPage);
            _programPage = Mathf.Clamp(_programPage, 0, pages - 1);
            ConPager(ConsoleLayer.LX, _programPage, pages, () => { _programPage--; Refresh(); }, () => { _programPage++; Refresh(); });
            int shown = 0;
            foreach (var r in rows.Skip(_programPage * ConRowsPerPage).Take(ConRowsPerPage)) r(136f + shown++ * 80f);
        }

        void ConRuleHead(float y, ProgramRule rule, int index)
        {
            var c = _con;
            string label = string.IsNullOrEmpty(rule.Label) ? rule.Id : rule.Label;
            c.G.Rect(36f, y, 438f, 76f, VecGraphic.Soft(LabTheme.ConCy, 0.06f));
            c.T(52f, y + 26f, $"{ProgramText.Number(index)} {ConsoleLayer.Fit(label, 20)}", 18f, rule.Enabled ? LabTheme.ConInk : LabTheme.ConMut, bold: true);
            c.T(52f, y + 58f, rule.Trigger == ProgramRule.While ? "成り立つ間ずっと" : $"1回ずつ・間隔 {rule.Cooldown:0.#} 秒", 16f, LabTheme.ConMut);
            string id = rule.Id;
            c.Button(262f, y + 30f, 70f, 44f, rule.Enabled ? "オン" : "オフ", () => App.ToggleRule(id), rule.Enabled ? ConKind.On : ConKind.Quiet, 17f);
            c.Button(336f, y + 30f, 138f, 44f, "動かしてみる", () => App.PreviewRule(id), ConKind.Normal, 16f,
                rule.Enabled && rule.Trigger == ProgramRule.Rise && rule.Do.Any(a => a.Action == ProgramAction.MoveJoint));
        }

        void ConStepRow(float y, string label, string value, UnityAction minus, UnityAction plus)
        {
            var c = _con;
            // 1行目に名前を全部出し、2行目に増減と値を置く
            c.TLine(64f, y + 24f, 400f, label, 17f);
            c.Tri(254f, y + 30f, -1, minus, name: "PV");
            c.T(364f, y + 62f, value, 22f, LabTheme.ConCy, bold: true, anchor: 0);
            c.Tri(426f, y + 30f, +1, plus, name: "PV");
        }

        /// <summary>左下：AI に改良を頼む（頼む → 案を見る → 使うか決める）。</summary>
        void ConReviseBox(InventionDna dna)
        {
            var c = _con;
            const float LX = ConsoleLayer.LX, PW = ConsoleLayer.PW;
            if (App.RevisionBusy)
            {
                c.Plate(LX, 558f, PW, 190f, "AI に改良を頼む");
                c.T(48f, 646f, "改良案を作っています", 19f, LabTheme.ConCy);
                return;
            }
            if (App.PendingRevision != null)
            {
                c.Plate(LX, 558f, PW, 190f, "改良案", App.PendingProposal.Source == "offline" ? "見本の決まりから（AI 未接続）" : "AI から");
                c.TBox(48f, 600f, 414f, 46f, App.PendingProposal.Summary, 16f, LabTheme.ConOr);
                var lines = App.PendingRevision.Changes.Select(ch => $"{ch.Label}：{ch.Before} → {ch.After}").ToList();
                string warn = App.PendingProposal.Notes.Concat(App.PendingRevision.Warnings).FirstOrDefault();
                int room = warn != null ? 1 : 2;
                for (int i = 0; i < Mathf.Min(room, lines.Count); i++)
                {
                    bool lastSlot = i == room - 1 && lines.Count > room;
                    c.TLine(48f, 664f + i * 22f, lastSlot ? 318f : 414f, lines[i], 16f);
                    if (lastSlot) c.T(462f, 664f + i * 22f, $"ほか {lines.Count - room} 件", 16f, LabTheme.ConMut, anchor: 1);
                }
                if (warn != null) c.TLine(48f, 686f, 414f, "注意：" + warn, 16f, LabTheme.ConMut);
                c.Button(48f, 696f, 120f, 48f, "やめる", App.RejectRevision, ConKind.Quiet, 18f);
                c.Button(176f, 696f, 294f, 48f, "この案を使う", App.ApproveRevision, ConKind.Warm, 19f);
                return;
            }
            c.Plate(LX, 558f, PW, 190f, "AI に改良を頼む");
            var sugs = OfflineRevisionRules.SuggestedRequestsFor(dna).ToList();
            _revisionInput = c.Input(48f, 606f, 250f, 52f, sugs.Count > 0 ? "例：" + sugs[0] : "どう変えたいか", RequestRules.MaxRequestLength, s => App.RevisionText = s);
            _revisionInput.SetTextWithoutNotify(App.RevisionText);
            _revisionInput.onSelect.AddListener(_ => App.CancelVoice());
            bool mine = App.VoiceTarget == "revision";
            bool rec = mine && App.Voice == LabApp.VoiceState.Recording, conv = mine && App.Voice == LabApp.VoiceState.Transcribing;
            c.Button(306f, 606f, 78f, 52f, rec ? "止める" : conv ? "変換中" : "話す", () => App.ToggleVoice("revision"), rec ? ConKind.On : ConKind.Normal, 17f,
                App.Voice == LabApp.VoiceState.Idle || rec);
            c.Button(388f, 606f, 82f, 52f, "依頼", App.RequestRevision, ConKind.Normal, 18f);
            if (rec) c.T(48f, 686f, $"録音中（最大 {VoiceInput.MaxSeconds} 秒）。話し終えたら止めるを押す", 16f, LabTheme.ConCy);
            else if (conv) c.T(48f, 686f, "声を文字にしています（数秒）", 16f, LabTheme.ConCy);
            else if (App.RevisionMessage != null) c.TLine(48f, 686f, 414f, App.RevisionMessage, 16f, LabTheme.ConMut);
            for (int i = 0; i < Mathf.Min(2, sugs.Count); i++)
            {
                string text = sugs[i];
                c.Button(48f + i * 212f, 698f, 204f, 46f, ConsoleLayer.Fit(text, 11), () => { App.PickRevision(text); if (_revisionInput != null) _revisionInput.SetTextWithoutNotify(text); }, ConKind.Quiet, 16f);
            }
        }

        /// <summary>右上：部品（選ぶ → 形・素材・外観・向き）。</summary>
        void ConPartsBox(InventionDna dna)
        {
            var c = _con;
            const float RX = ConsoleLayer.RX, PW = ConsoleLayer.PW;
            int n = dna.Parts.Count, selIndex = App.SelectedPartIndex;
            c.Plate(RX, 78f, PW, 468f, "部品", n > 0 ? $"{selIndex + 1} / {n} 点" : "0 点");
            var sel = App.SelectedPart;
            const int Window = 3;
            if (n > Window)
            {
                c.Tri(RX + 232f, 86f, -1, () => App.StepSelectedPart(-1), name: "Part");
                c.Tri(RX + 282f, 86f, +1, () => App.StepSelectedPart(+1), name: "Part");
            }
            int start = Mathf.Clamp(selIndex - 1, 0, Mathf.Max(0, n - Window));
            for (int i = start; i < Mathf.Min(n, start + Window); i++)
            {
                var p = dna.Parts[i]; int delta = i - selIndex;
                var mat = LabCatalog.Material(p.MaterialOrDefault);
                c.Row(RX, 128f + (i - start) * 58f, PW, 56f, LabCatalog.Part(p.Type)?.LabelJa ?? p.Type, PartNames.WithJa(p.Id),
                    $"{LabCatalog.Shape(p.ShapeOrDefault)?.LabelJa ?? p.ShapeOrDefault}・{mat?.LabelJa ?? p.MaterialOrDefault}",
                    sel: i == selIndex, onClick: delta == 0 ? (UnityAction)null : () => App.StepSelectedPart(delta), name: "PartRow");
            }
            float y0 = 340f;
            if (sel != null)
            {
                c.T(RX + 24f, 326f, $"{sel.Scale.x:0.00}×{sel.Scale.y:0.00}×{sel.Scale.z:0.00} m ／ この部品 {MassModel.Part(sel).Kg:0.00} kg", 16f, LabTheme.ConMut);
                var mat = LabCatalog.Material(sel.MaterialOrDefault);
                ConOption(y0, "形", LabCatalog.Shape(sel.ShapeOrDefault)?.LabelJa ?? sel.ShapeOrDefault, () => App.StepPartShape(-1), () => App.StepPartShape(+1), "PartShape");
                ConOption(y0 + 50f, "素材", $"{mat?.LabelJa ?? sel.MaterialOrDefault}<size=80%>　{mat?.DensityKgM3:0} kg/m³</size>", () => App.StepPartMaterial(-1), () => App.StepPartMaterial(+1), "PartMaterial");
            }
            ConOption(y0 + 100f, "外観", LabCatalog.Appearance(dna.Appearance)?.LabelJa ?? dna.Appearance, () => App.StepAppearance(-1), () => App.StepAppearance(+1), "Appearance");
            // 発明台の上で回して眺める（右スティックでも回せる。角度は出さない：回すたびに画面を作り直さないため）
            ConOption(y0 + 150f, "向き", "45°ずつ<size=80%>　右スティックでも</size>", () => App.TurnDisplay(-45f), () => App.TurnDisplay(45f), "Turn");
        }

        /// <summary>右の板の「名前 ◀ 値 ▶」の行。</summary>
        void ConOption(float y, string label, string value, UnityAction prev, UnityAction next, string name)
        {
            var c = _con;
            const float RX = ConsoleLayer.RX;
            c.T(RX + 24f, y + 32f, label, 17f, LabTheme.ConMut);
            c.Tri(RX + 104f, y + 1f, -1, prev, name: name);
            c.T(RX + 272f, y + 32f, value, 19f, anchor: 0);
            c.Tri(RX + 394f, y + 1f, +1, next, name: name);
        }

        /// <summary>右下：総質量（推定）と、体に付けるときの目安。</summary>
        void ConMassBox(InventionDna dna)
        {
            var c = _con;
            const float RX = ConsoleLayer.RX, PW = ConsoleLayer.PW;
            c.Plate(RX, 558f, PW, 190f, "総質量（推定）");
            float kg = MassModel.TotalKg(dna);
            var limit = MassModel.MountLimitKg(dna.Mount);
            bool over = limit.HasValue && kg > limit.Value;
            Color col = over ? LabTheme.ConOr : LabTheme.ConCy;
            c.T(1518f, 606f, kg.ToString("0.00"), 44f, col, bold: true, anchor: 1);
            c.T(1524f, 606f, "kg", 20f, col, bold: true);
            if (limit.HasValue)
            {
                const float x0 = 1138f, mw = 414f;
                float scale = limit.Value * 1.5f;
                float X(float v) => x0 + Mathf.Clamp01(v / scale) * mw;
                c.G.Rect(x0, 624f, mw, 14f, LabTheme.ConCyDeep);
                c.G.Rect(x0, 624f, X(Mathf.Min(kg, limit.Value)) - x0, 14f, LabTheme.ConCy);
                if (over)
                {
                    float ox = X(limit.Value), ow = X(kg) - ox;
                    c.G.Rect(ox, 624f, ow, 14f, LabTheme.ConOr);
                    c.G.Glow(LabTheme.ConOr, new Vector2(ox, 624f), new Vector2(ox + ow, 624f), new Vector2(ox + ow, 638f), new Vector2(ox, 638f));
                }
                c.G.Line(new Vector2(X(limit.Value), 616f), new Vector2(X(limit.Value), 648f), 3f, LabTheme.ConInk);
                c.T(X(limit.Value), 672f, $"体に付ける目安 {limit.Value:0.00} kg", 16f, LabTheme.ConMut, anchor: 0);
                c.T(1138f, 718f, over ? $"目安を {kg - limit.Value:0.00} kg 超えています" : "目安の範囲内です", 17f, over ? LabTheme.ConOr : LabTheme.ConInk);
            }
            else
            {
                c.T(1138f, 652f, "体に付けない道具なので、重さの目安はありません", 16f, LabTheme.ConMut);
                var heavy = dna.Parts.OrderByDescending(p => MassModel.Part(p).Kg).FirstOrDefault();
                if (heavy != null) c.T(1138f, 700f, $"いちばん重い部品　{LabCatalog.Part(heavy.Type)?.LabelJa ?? heavy.Type} {MassModel.Part(heavy).Kg:0.00} kg", 17f);
            }
        }
    }
}
