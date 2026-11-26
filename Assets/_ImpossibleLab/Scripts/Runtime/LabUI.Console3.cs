using System.Collections.Generic;
using System.Linq;
using ImpossibleLab.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace ImpossibleLab
{
    /// <summary>輪の計器コンソール：S01 指示、S02 設計の確認、エラー、S06 保存。</summary>
    public sealed partial class LabUI
    {
        TextMeshProUGUI _echoText, _countText, _echoState;
        string _galleryPick;

        // ---------------- S01 指示 ----------------
        void BuildC01()
        {
            var c = _con;
            const float LX = ConsoleLayer.LX, PW = ConsoleLayer.PW, RX = ConsoleLayer.RX;
            bool rec = App.VoiceTarget == "request" && App.Voice == LabApp.VoiceState.Recording;
            bool conv = App.VoiceTarget == "request" && App.Voice == LabApp.VoiceState.Transcribing;

            c.Dial(rings: false, dirs: false);
            // 外周：声を聞いている間だけ光る
            for (int k = 0; k < 60; k++)
            {
                float a = 150f + 240f / 59f * k;
                var g = rec ? c.Ghost : c.G;
                g.Line(ConsoleLayer.Pol(262f, a), ConsoleLayer.Pol(rec ? 284f : 270f, a), 5f, rec ? LabTheme.ConCy : LabTheme.ConCyDeep);
            }
            c.G.DashedCircle(ConsoleLayer.C, 150f, 2f, LabTheme.ConCyDim, 1.6f, 3.2f);
            _echoText = c.TBox(660f, 312f, 280f, 120f, "", 26f, LabTheme.ConCy, bold: true);
            _echoText.alignment = TextAlignmentOptions.Center;
            _echoState = c.T(ConsoleLayer.C.x, 690f, "", 34f, LabTheme.ConMut, bold: true, anchor: 0, halo: true);
            _countText = c.T(ConsoleLayer.C.x, 716f, "", 16f, LabTheme.ConMut, anchor: 0);

            c.Plate(LX, 78f, PW, 264f, "作りたい道具");
            _requestInput = c.Input(48f, 128f, 414f, 64f, "ここに入力する（200 文字まで）", RequestRules.MaxRequestLength, App.SetRequestText, strong: true);
            _requestInput.SetTextWithoutNotify(App.RequestText);
            _requestInput.onSelect.AddListener(_ => App.CancelVoice()); // 録音中はシステムキーボードが開かないため、入力欄を選んだら録音をやめる
            _requestInput.onValueChanged.AddListener(_ => UpdateEcho());
            c.Button(48f, 206f, 200f, 56f, rec ? "止める" : conv ? "変換中" : "話す", () => App.ToggleVoice("request"), rec ? ConKind.On : ConKind.Normal, 20f,
                App.Voice == LabApp.VoiceState.Idle || rec);
            c.Button(256f, 206f, 206f, 56f, "消す", () => { App.SetRequestText(""); if (_requestInput != null) _requestInput.SetTextWithoutNotify(""); UpdateEcho(); }, ConKind.Quiet);
            if (App.InputWarning != null) ConParas(48f, 280f, 414f, 338f, new[] { (App.InputWarning, LabTheme.ConOr) });
            else if (rec) ConParas(48f, 280f, 414f, 338f, new[] { ($"録音中（最大 {VoiceInput.MaxSeconds} 秒）。話し終えたら止めるを押す", LabTheme.ConCy) });
            else if (conv) ConParas(48f, 280f, 414f, 338f, new[] { ("声を文字にしています（数秒）", LabTheme.ConCy) });
            else if (App.ProxyOnline == false)
            {
                c.T(48f, 300f, "PC とつながっていません", 16f, LabTheme.ConMut);
                c.T(48f, 324f, "入力した言葉から、見本の設計を選びます", 16f, LabTheme.ConMut);
            }
            else
            {
                c.T(48f, 300f, "声は PC の中だけで文字にします", 16f, LabTheme.ConMut);
                c.T(48f, 324f, "声そのものは保存しません", 16f, LabTheme.ConMut);
            }

            // 採用の知らせがあれば、今日の課題の代わりに一度だけ出す
            if (!ConAdoptionNotice())
            {
                c.Plate(LX, 354f, PW, 394f, "今日の課題");
                // 行の切れ目を決めて置く（語の途中で折り返さないように）
                var task = new[]
                {
                    ("両手がふさがっていても雨を防げる道具を発明し、", LabTheme.ConInk), ("雨と風の実験で性能を確かめよう。", LabTheme.ConInk),
                    ("荷物や杖で両手がふさがる人の雨対策です。", LabTheme.ConMut), (null, LabTheme.ConMut),
                    ("ほかの道具も作れます。", LabTheme.ConInk), ("言葉で伝えると、AI が設計します。", LabTheme.ConInk),
                    ("使うのは、登録済みの部品と機能だけです。", LabTheme.ConInk), ("できた設計は、発明台の上に組み立てます。", LabTheme.ConInk),
                    (null, LabTheme.ConMut), ("設計元：" + App.Design.DisplayName, LabTheme.ConMut),
                };
                for (int i = 0; i < task.Length; i++) if (task[i].Item1 != null) c.TLine(48f, 420f + i * 28f, 414f, task[i].Item1, 17f, task[i].Item2);
            }

            c.Plate(RX, 78f, PW, 670f, "例から選ぶ", "発明 ／ 確かめる実験");
            for (int i = 0; i < Mathf.Min(9, QuickInventions.All.Length); i++)
            {
                var item = QuickInventions.All[i];
                c.Row(RX, 124f + i * 68f, PW, 64f, item.Title, $"{item.Kind}・{item.Experiment}", sel: App.RequestText == item.Request,
                    onClick: () => { App.PickQuickInvention(item.Request); if (_requestInput != null) _requestInput.SetTextWithoutNotify(item.Request); UpdateEcho(); }, name: "Quick" + i);
            }
            ConFrame(new List<ConBtn> { new ConBtn("保存した発明", App.OpenGallery, ConKind.Normal, 190f) }, new ConBtn("AI に設計させる", App.SubmitRequest, ConKind.Primary, 300f));
            UpdateEcho();
        }

        /// <summary>計器の中の文字を、入力に合わせて書き換える（入力のたびに画面を作り直さない）。</summary>
        void UpdateEcho()
        {
            if (_echoText == null) return;
            string s = App.RequestText ?? "";
            bool rec = App.VoiceTarget == "request" && App.Voice == LabApp.VoiceState.Recording;
            // 短い依頼は、助詞の後ろで2行に分ける（語の途中で折り返さない）。長い依頼は小さい字で流し込む
            string shown = rec ? "聞いています" : s.Length > 0 ? BreakJa(s) : "何を発明しますか";
            int longest = shown.Split('\n').Max(x => x.Length);
            _echoText.fontSize = s.Length > 24 && !rec ? 18f : Mathf.Clamp(Mathf.Floor(270f / Mathf.Max(1, longest)), 18f, 28f);
            _echoText.text = shown;
            _echoText.color = rec || s.Length > 0 ? LabTheme.ConCy : LabTheme.ConInk;
            if (_echoState != null) { _echoState.text = rec ? "録音中" : s.Length > 0 ? "送信できます" : "入力待ち"; _echoState.color = rec || s.Length > 0 ? LabTheme.ConCy : LabTheme.ConMut; }
            if (_countText != null) _countText.text = $"{s.Length} / {RequestRules.MaxRequestLength} 文字";
        }

        /// <summary>日本語の短い文を、助詞や読点の後ろで2行に分ける（真ん中にいちばん近い切れ目）。</summary>
        public static string BreakJa(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= 10 || s.Length > 24 || s.Contains("\n")) return s ?? "";
            const string after = "をにでがはもとへ、。";
            int mid = s.Length / 2, best = -1;
            for (int i = 2; i < s.Length - 1; i++)
                if (after.IndexOf(s[i - 1]) >= 0 && (best < 0 || Mathf.Abs(i - mid) < Mathf.Abs(best - mid))) best = i;
            return best > 0 ? s.Substring(0, best) + "\n" + s.Substring(best) : s;
        }

        // ---------------- S02 設計の確認 ----------------
        void BuildC02()
        {
            var c = _con;
            const float LX = ConsoleLayer.LX, PW = ConsoleLayer.PW, RX = ConsoleLayer.RX;
            var s = App.Sm.State;
            var dna = s == LabState.AwaitingApproval ? App.Preview : App.Current?.Current ?? App.Preview;
            if (s == LabState.GeneratingDesign) dna = null;
            if (s == LabState.AwaitingApproval && _explain && dna != null) { BuildC02Explain(dna); return; }

            c.Dial();
            // 外周：設計の進み（段階ごとの区画）
            var stages = DesignStageText.All;
            float span = 240f / stages.Length;
            for (int i = 0; i < stages.Length; i++)
            {
                var st = App.Stages[stages[i]];
                float a0 = 150f + i * span + 1.5f, a1 = 150f + (i + 1) * span - 1.5f;
                if (st == StageStatus.Done) c.G.GlowArc(ConsoleLayer.C, 270f, 16f, a0, a1, LabTheme.ConCy);
                else if (st == StageStatus.Running) c.Ghost.Arc(ConsoleLayer.C, 270f, 16f, a0, a1, LabTheme.ConOr);
                else c.G.Arc(ConsoleLayer.C, 270f, 16f, a0, a1, st == StageStatus.Failed ? LabTheme.ConOrDim : LabTheme.ConCyDeep);
            }
            if (dna != null) ConTopView(dna, -1);
            if (s == LabState.GeneratingDesign) c.BigWord("設計中", "AI の出力は、データとして検証してから使います", LabTheme.ConCy);
            else if (s == LabState.AwaitingApproval) c.BigWord("確認を待っています", "承認すると、発明台の上に組み立てます", LabTheme.ConOr);
            else c.BigWord("組み立て中", null, LabTheme.ConCy);

            // 左上：部品（設計ができるまでは依頼の内容）
            if (dna != null)
            {
                c.Plate(LX, 78f, PW, 468f, "部品", $"{dna.Parts.Count} 点・推定 {MassModel.TotalKg(dna):0.00} kg");
                int show = dna.Parts.Count <= 4 ? dna.Parts.Count : 3;
                for (int i = 0; i < show; i++)
                {
                    var p = dna.Parts[i];
                    c.Row(LX, 124f + i * 70f, PW, 66f, LabCatalog.Part(p.Type)?.LabelJa ?? p.Type, PartNames.WithJa(p.Id) + (p.Joint != null ? "（関節つき）" : ""),
                        $"{LabCatalog.Shape(p.ShapeOrDefault)?.LabelJa ?? p.ShapeOrDefault}・{LabCatalog.Material(p.MaterialOrDefault)?.LabelJa ?? p.MaterialOrDefault}");
                }
                if (show < dna.Parts.Count) c.T(52f, 124f + show * 70f + 34f, $"ほか {dna.Parts.Count - show} 点", 16f, LabTheme.ConMut);
                c.T(48f, 432f, "依頼", 16f, LabTheme.ConMut);
                ConParas(48f, 444f, 414f, 540f, new[] { (App.RequestText, LabTheme.ConInk) }, 17f);
            }
            else
            {
                c.Plate(LX, 78f, PW, 468f, "依頼");
                ConParas(48f, 130f, 414f, 540f, new[]
                {
                    (App.RequestText, LabTheme.ConInk),
                    ("AI の出力はデータとして検証し、登録済みの部品と機能だけで組み立てます。", LabTheme.ConMut),
                    ("現実の製品としての安全性や実現性は保証しません。", LabTheme.ConMut),
                }, 18f);
            }

            // 左下：設計の進み（実際の処理の段階。進み具合の％は出さない）
            c.Plate(LX, 558f, PW, 190f, "設計の進み");
            for (int i = 0; i < stages.Length; i++)
            {
                var st = App.Stages[stages[i]];
                float y = 624f + i * 27f;
                bool done = st == StageStatus.Done, now = st == StageStatus.Running, failed = st == StageStatus.Failed;
                if (done) c.G.Rect(50f, y - 13f, 14f, 14f, LabTheme.ConCy);
                c.G.PolyLine(2f, now || failed ? LabTheme.ConOr : done ? LabTheme.ConCy : LabTheme.ConCyDim, true,
                    new Vector2(50f, y - 13f), new Vector2(64f, y - 13f), new Vector2(64f, y + 1f), new Vector2(50f, y + 1f));
                c.T(76f, y, DesignStageText.Label(stages[i]), 17f, now || failed ? LabTheme.ConOr : done ? LabTheme.ConInk : LabTheme.ConMut, bold: now);
                c.T(462f, y, done ? "済み" : now ? "処理中" : failed ? "失敗" : "", 16f, LabTheme.ConMut, anchor: 1);
            }

            // 右上：要望をどう確かめるか ／ 動きの手順
            c.Plate(RX, 78f, PW, 468f);
            if (dna == null)
            {
                c.T(RX + 20f, 112f, "要望をどう確かめるか", 20f, bold: true);
                c.T(RX + 20f, 160f, "設計ができると、ここに出ます", 17f, LabTheme.ConMut);
            }
            else
            {
                bool hasProgram = dna.Program != null;
                if (hasProgram) ConTabs(RX, 78f, new[] { "要望の確かめ方", "動きの手順" }, _tabR, i => { _tabR = i; Refresh(); }, "TabR");
                else c.T(RX + 20f, 112f, "要望をどう確かめるか", 20f, bold: true);
                if (hasProgram && _tabR == 1)
                {
                    // 動きの手順：文は AI ではなく、プログラムからアプリが定型文で作る（仕様 28.9）
                    var items = ProgramText.Rules(dna).Select(t => (t, LabTheme.ConInk)).ToList();
                    foreach (var w in (App.LastOutcome?.Report?.Warnings ?? new List<string>()).Where(w => w.Contains("ルール") || w.Contains("フラグ")).Take(2)) items.Add(("注意：" + w, LabTheme.ConOr));
                    ConParas(RX + 20f, 136f, 414f, 540f, items);
                }
                else ConNeeds(dna);
            }

            // 右下：検証
            c.Plate(RX, 558f, PW, 190f, "検証");
            var lines = new List<(string, Color)>();
            if (s == LabState.GeneratingDesign) lines.Add(("設計の完了を待っています", LabTheme.ConMut));
            else if (App.LastOutcome != null)
            {
                lines.Add((App.LastOutcome.Source == "ai" ? "AI が設計しました" : "見本の設計図を使いました（AI は使っていません）", LabTheme.ConInk));
                if (App.LastOutcome.Report != null) lines.Add((App.LastOutcome.Report.Summary, LabTheme.ConCy));
                lines.Add((App.LastOutcome.Message, LabTheme.ConMut));
                foreach (var pnt in (App.LastOutcome.DesignPoints ?? new List<string>()).Take(2)) lines.Add((pnt, LabTheme.ConInk));
            }
            ConParas(RX + 20f, 606f, 414f, 742f, lines);

            var left = new List<ConBtn>(); ConBtn? primary = null;
            if (s == LabState.GeneratingDesign) left.Add(new ConBtn("キャンセル", App.CancelGeneration, ConKind.Normal, 170f));
            else if (s == LabState.AwaitingApproval)
            {
                left.Add(new ConBtn("指示に戻る", App.BackToDraft, ConKind.Quiet, 150f));
                left.Add(new ConBtn("製品の説明", () => ShowExplain(true), ConKind.Quiet, 160f));
                left.Add(new ConBtn("作り直す", App.Regenerate));
                primary = new ConBtn("承認して組み立てる", App.ApprovePreview, ConKind.Primary, 330f);
            }
            ConFrame(left, primary);
        }

        /// <summary>
        /// 製品の説明：左に概要としくみ（要望ごとに、どの部品が何をするか）、右に部品の役割。部品を選ぶと上から見た図でオレンジに光る。
        /// 設計に説明が無い（見本・古い保存データ・AI を使わない設計）ときは、部品と機能の対応から作った文であることを示す。
        /// </summary>
        void BuildC02Explain(InventionDna dna)
        {
            var c = _con;
            const float LX = ConsoleLayer.LX, PW = ConsoleLayer.PW, RX = ConsoleLayer.RX;
            int n = dna.Parts.Count;
            _explainPart = Mathf.Clamp(_explainPart, 0, Mathf.Max(0, n - 1));

            ConProduct(dna, _explainPart);
            c.BigWord(ConsoleLayer.Fit(dna.Name, 14), _zoom ? "オレンジの枠が、右で選んでいる部品" : "右で部品を選ぶと、その部品に寄ります", LabTheme.ConInk);

            // しくみ（要望ごとに、どの部品が何をするか）。設計に対応表が無いときは機能ごとの行から作る
            var rows = App.FunctionRows(dna, out bool rowsDerived);
            var needs = rows.Select((r, i) => !rowsDerived && i < dna.FunctionMap.Count ? dna.FunctionMap[i]
                : new FunctionNeed { Need = r.Need, Blocks = new List<string>(r.Blocks), Status = r.Status, Note = r.Note }).ToList();
            // 左上：製品の概要（しくみが定型文なら、そのことを下に書く）
            var ov = ProductExplain.Overview(dna);
            bool howDerived = needs.Any(nd => ProductExplain.How(dna, nd).Derived);
            c.Plate(LX, 78f, PW, 214f, "製品の概要");
            ConParas(48f, 128f, 414f, howDerived ? 258f : 280f, new[] { (ov.Derived ? "（説明がありません）" : ov.Value, LabTheme.ConInk) }, 18f);
            if (howDerived) c.T(48f, 280f, "説明がないため、しくみは部品と機能から作った文です", 15f, LabTheme.ConMut);

            // 左下：しくみ
            int pages = Mathf.Max(1, (needs.Count + ExplainNeedsPerPage - 1) / ExplainNeedsPerPage);
            _explainNeedPage = Mathf.Clamp(_explainNeedPage, 0, pages - 1);
            c.Plate(LX, 304f, PW, 444f, "しくみ");
            ConPager(LX, _explainNeedPage, pages, () => StepExplainNeedPage(-1), () => StepExplainNeedPage(+1), PW, 304f);
            float y = 356f;
            if (needs.Count == 0) c.T(48f, y + 24f, "要望の対応表がありません", 17f, LabTheme.ConMut);
            foreach (var need in needs.Skip(_explainNeedPage * ExplainNeedsPerPage).Take(ExplainNeedsPerPage))
            {
                var how = ProductExplain.How(dna, need);
                Color col = need.Status == "simulated" ? LabTheme.ConCy : need.Status == "unverifiable" ? LabTheme.ConMut : LabTheme.ConInk;
                c.G.Rect(LX + 8f, y, PW - 16f, 124f, VecGraphic.Soft(LabTheme.ConCy, 0.05f));
                c.TLine(LX + 24f, y + 28f, 280f, need.Need, 18f, bold: true);
                c.T(LX + PW - 24f, y + 28f, ProductExplain.StatusLabel(need.Status), 16f, col, bold: true, anchor: 1);
                c.TBox(LX + 24f, y + 42f, 406f, 78f, how.Value, 16f, LabTheme.ConMut);
                y += 128f;
            }

            // 右：部品の役割（選ぶと上から見た図で光る）
            c.Plate(RX, 78f, PW, 670f, "部品の役割", n > 0 ? $"{_explainPart + 1} / {n} 点" : "0 点");
            if (n > ExplainPartWindow)
            {
                c.Tri(RX + 232f, 86f, -1, () => SelectExplainPart(_explainPart - 1), enabled: _explainPart > 0, name: "Part");
                c.Tri(RX + 282f, 86f, +1, () => SelectExplainPart(_explainPart + 1), enabled: _explainPart < n - 1, name: "Part");
            }
            int start = Mathf.Clamp(_explainPart - 1, 0, Mathf.Max(0, n - ExplainPartWindow));
            for (int i = start; i < Mathf.Min(n, start + ExplainPartWindow); i++)
            {
                var p = dna.Parts[i]; int idx = i;
                c.Row(RX, 128f + (i - start) * 58f, PW, 56f, LabCatalog.Part(p.Type)?.LabelJa ?? p.Type, PartNames.WithJa(p.Id),
                    null, sel: i == _explainPart, onClick: () => SelectExplainPart(idx), name: "ExplainPart");
            }
            if (n > 0)
            {
                var sel = dna.Parts[_explainPart];
                var role = ProductExplain.Role(dna, sel);
                float ry = 128f + Mathf.Min(n, ExplainPartWindow) * 58f + 20f;
                c.T(RX + 24f, ry + 8f, "この部品の役割", 16f, LabTheme.ConMut);
                float ey = ConParas(RX + 24f, ry + 20f, 406f, ry + 130f, new[] { (role.Value, LabTheme.ConInk) }, 19f);
                var blocks = ProductExplain.BehaviorsOf(dna, sel);
                var mat = LabCatalog.Material(sel.MaterialOrDefault);
                var facts = new List<(string, Color)>
                {
                    ("受け持つ機能：" + (blocks.Count > 0 ? string.Join("・", blocks) : "なし（形を支える・まとめる部品）"), LabTheme.ConMut),
                    ($"{LabCatalog.Shape(sel.ShapeOrDefault)?.LabelJa ?? sel.ShapeOrDefault}・{mat?.LabelJa ?? sel.MaterialOrDefault}　{sel.Scale.x:0.00}×{sel.Scale.y:0.00}×{sel.Scale.z:0.00} m　{MassModel.Part(sel).Kg:0.00} kg", LabTheme.ConMut),
                };
                if (sel.Joint != null) facts.Add(("関節で動く（動作プログラムで動かす）", LabTheme.ConMut));
                if (role.Derived) facts.Add(("説明がないため、部品の種類から作った文です", LabTheme.ConMut));
                ConParas(RX + 24f, Mathf.Max(ey + 4f, ry + 100f), 406f, 740f, facts, 16f);
            }

            var st = App.Sm.State;
            if (st == LabState.AwaitingApproval)
                ConFrame(new List<ConBtn> { new ConBtn("指示に戻る", App.BackToDraft, ConKind.Quiet, 150f), new ConBtn("作り直す", App.Regenerate) },
                    new ConBtn("次へ　設計の確認", () => ShowExplain(false), ConKind.Primary, 330f));
            else
                ConFrame(new List<ConBtn>(), new ConBtn(st == LabState.Editing ? "編集に戻る" : st == LabState.ReviewingResult ? "結果に戻る" : "保存の画面に戻る",
                    () => ShowExplain(false), ConKind.Primary, 330f));
        }

        /// <summary>要望ごとの確かめ方（検証する／単純化／動きで見せる／検証できない）。</summary>
        void ConNeeds(InventionDna dna)
        {
            var c = _con;
            const float RX = ConsoleLayer.RX, PW = ConsoleLayer.PW;
            var rows = App.FunctionRows(dna, out bool derived);
            if (_needPageFor != dna.Id + "#" + dna.Revision) { _needPageFor = dna.Id + "#" + dna.Revision; _needPage = 0; }
            int pages = Mathf.Max(1, (rows.Count + NeedsPerPage - 1) / NeedsPerPage);
            _needPage = Mathf.Clamp(_needPage, 0, pages - 1);
            ConPager(RX, _needPage, pages, () => StepNeedPage(-1), () => StepNeedPage(+1));
            float y = 132f;
            if (derived) { c.TLine(RX + 20f, 150f, 414f, "AI の対応表がないため、機能ごとに出しています", 16f, LabTheme.ConMut); y = 164f; }
            if (rows.Count == 0) { c.T(RX + 20f, y + 30f, "機能がありません", 17f, LabTheme.ConMut); return; }
            foreach (var r in rows.Skip(_needPage * NeedsPerPage).Take(NeedsPerPage))
            {
                string label = r.Status == "simulated" ? "実験で測る" : r.Status == "simplified" ? "単純化して測る" : r.Status == "demonstrated" ? "動きで見せる" : "確かめられない";
                Color col = r.Status == "simulated" ? LabTheme.ConCy : r.Verifiable ? LabTheme.ConInk : LabTheme.ConMut;
                string detail;
                if (!r.Verifiable) detail = "理由：" + (r.Note.Length > 0 ? r.Note : "記載なし");
                else if (r.Status == "simplified" && r.Note.Length > 0) detail = (r.PlanTitle != null ? $"実験：{r.PlanTitle}　" : "") + "単純化：" + r.Note;
                else detail = (r.BlocksJa.Length > 0 ? "機能：" + r.BlocksJa + "　" : r.MetricsJa.Length > 0 ? "計測：" + r.MetricsJa + "　" : "") + (r.PlanTitle != null ? $"実験：{r.PlanTitle}" : "この版の実験の候補では動きません");
                c.G.Rect(RX + 8f, y, PW - 16f, 88f, VecGraphic.Soft(LabTheme.ConCy, 0.05f));
                c.TLine(RX + 24f, y + 28f, 406f, r.Need, 18f);
                c.TLine(RX + 24f, y + 54f, 406f, detail, 16f, LabTheme.ConMut);
                c.T(RX + 24f, y + 78f, label, 16f, col, bold: true);
                y += 96f;
            }
        }

        // ---------------- エラー ----------------
        void BuildCError()
        {
            var c = _con;
            const float LX = ConsoleLayer.LX, PW = ConsoleLayer.PW, RX = ConsoleLayer.RX;
            c.Dial(rings: false, dirs: false, orbit: false);
            c.Gauge(null, 100f);
            c.BigWord("設計できませんでした", null, LabTheme.ConOr);
            c.Plate(LX, 78f, PW, 670f, "起きたこと");
            var items = new List<(string, Color)> { (App.ErrorMessage ?? "不明なエラー", LabTheme.ConOr) };
            foreach (var s in App.ErrorSuggestions) items.Add((s, LabTheme.ConInk));
            ConParas(48f, 130f, 414f, 740f, items, 18f);
            c.Plate(RX, 78f, PW, 670f, "通信や AI が使えないとき");
            ConParas(RX + 20f, 130f, 414f, 740f, new[]
            {
                ("AI やネットワークが使えなくても、検証済みの見本の設計図でデモを続けられます。", LabTheme.ConInk),
                ("この版で実験できる機能：雨よけ・運搬・飛行（専用の実験場）、照明・日よけ・検知・通知・加温と冷却・衝撃吸収・つかむ・持ち上げ（組み立て式の実験）", LabTheme.ConMut),
            }, 18f);
            ConFrame(new List<ConBtn> { new ConBtn("指示に戻る", App.DismissError, ConKind.Quiet, 150f), new ConBtn("もう一度", App.SubmitRequest) },
                new ConBtn("見本の設計図（雨具）を使う", App.UseOfflineSample, ConKind.Primary, 380f, true, 21f));
        }

        // ---------------- S06 保存 ----------------
        void BuildC06()
        {
            var c = _con;
            const float LX = ConsoleLayer.LX, PW = ConsoleLayer.PW, RX = ConsoleLayer.RX;
            var dataNote = new[]
            {
                ("発明の設計（部品・機能・値）", LabTheme.ConInk), ("改良の履歴（30 版まで）", LabTheme.ConInk),
                ("実験の記録（条件・シード・計測値）", LabTheme.ConInk), ("依頼の言葉（声そのものは保存しません）", LabTheme.ConInk),
            };
            if (App.Sm.State == LabState.Saved)
            {
                var inv = App.Current; var dna = inv?.Current;
                if (inv != null && App.Publish != PublishStep.None && App.Publish != PublishStep.ConfirmWithdraw) { BuildPublish(inv); return; }
                c.Dial();
                if (dna != null) { ConTopView(dna, -1); ConLastResult("最後の実験の"); }
                c.Plate(LX, 78f, PW, 468f, "保存しました");
                ConParasJa(48f, 130f, 414f, 540f, new[]
                {
                    ($"{dna?.Name} Rev.{dna?.Revision}", LabTheme.ConCy),
                    ($"実験の記録 {inv?.Experiments.Count ?? 0} 件も一緒に保存しています", LabTheme.ConInk),
                    ("保存先（端末の中）：" + App.LastSavedPath, LabTheme.ConMut),
                }, 18f);
                c.Plate(LX, 558f, PW, 190f, "保存される内容");
                ConParas(48f, 606f, 414f, 742f, dataNote);
                c.Plate(RX, 78f, PW, 468f, "次にすること");
                c.Button(RX + 20f, 130f, 414f, 56f, "編集を続ける", App.ContinueEditing);
                c.Button(RX + 20f, 198f, 414f, 56f, "保存した発明を見る", App.OpenGallery);
                ConLibrarySection(inv);
                ConPairSection();
                ConFrame(new List<ConBtn> { new ConBtn("製品の説明", OpenExplain, ConKind.Quiet, 160f, dna != null) }, new ConBtn("新しい発明を始める", App.NewInvention, ConKind.Primary, 320f),
                    new Dictionary<int, UnityAction> { { 0, App.NewInvention }, { 2, App.ContinueEditing } });
                return;
            }

            // 一覧（新しい順に 6 件ずつ）
            if (_galleryShownFor != App.GalleryOpenCount) { _galleryShownFor = App.GalleryOpenCount; _galleryPage = 0; _galleryTag = null; }
            if (_galleryTag != null && !App.GalleryEntries.Any(x => x.Tags.Contains(_galleryTag))) _galleryTag = null;
            var entries = GalleryFilter.Apply(App.GalleryEntries, _galleryTag);
            int gPages = GalleryPages(entries.Count);
            _galleryPage = Mathf.Clamp(_galleryPage, 0, gPages - 1);
            var page = entries.Skip(_galleryPage * GalleryPerPage).Take(GalleryPerPage).ToList();
            var pick = page.FirstOrDefault(e => e.Id == _galleryPick) ?? page.FirstOrDefault();
            int pickIndex = pick != null ? entries.IndexOf(pick) : -1;

            c.Dial(rings: false, dirs: false);
            // 目盛りの上に、保存した発明の数だけ印を置く
            for (int i = 0; i < entries.Count; i++)
            {
                var p = ConsoleLayer.Pol(247f, 270f + 360f / entries.Count * i);
                if (i == pickIndex) { c.G.Disc(p, 13f, VecGraphic.Soft(LabTheme.ConOr, 0.16f)); c.G.Disc(p, 8f, LabTheme.ConOr); }
                else c.G.Disc(p, 5f, LabTheme.ConCyDim);
            }
            c.Gauge(null, 100f);
            if (pick != null)
            {
                var name = c.TBox(660f, 322f, 280f, 100f, pick.Name, 24f, LabTheme.ConInk, bold: true);
                name.alignment = TextAlignmentOptions.Center;
                c.BigWord(pick.Tested ? "実験済み" : "未実験", pick.Tested ? "開くと、最後の実験の結果が見られます" : "開いて、実験で確かめましょう", pick.Tested ? LabTheme.ConCy : LabTheme.ConMut);
            }
            else c.BigWord("まだありません", "発明して保存すると、ここに並びます");

            c.Plate(LX, 78f, PW, 468f, "分類で絞り込む");
            var counts = GalleryFilter.Counts(App.GalleryEntries);
            c.Row(LX, 124f, PW, 54f, "すべて", right: $"{App.GalleryEntries.Count} 件", sel: _galleryTag == null, onClick: _galleryTag == null ? (UnityAction)null : () => SetGalleryTag(null), name: "TagAll");
            int ti = 1;
            foreach (var (tag, n) in counts.Take(6))
            {
                string id = tag.Id; bool on = _galleryTag == id;
                c.Row(LX, 124f + ti++ * 58f, PW, 54f, tag.LabelJa, right: $"{n} 件", sel: on, onClick: on ? (UnityAction)null : () => SetGalleryTag(id), name: "Tag");
            }
            c.Plate(LX, 558f, PW, 190f, "保存される内容");
            ConParas(48f, 606f, 414f, 742f, dataNote);

            c.Plate(RX, 78f, PW, 468f, "保存した発明");
            ConPager(RX, _galleryPage, gPages, () => StepGalleryPage(-1), () => StepGalleryPage(+1));
            for (int i = 0; i < page.Count; i++)
            {
                var e = page[i]; string id = e.Id; bool on = e == pick;
                c.Row(RX, 124f + i * 68f, PW, 64f, ConsoleLayer.Fit(e.Name, 16), $"{(e.HasProgram ? "動きつき・" : "")}{ConsoleLayer.Fit(e.TagsJa, 10)}・{(e.Tested ? "実験済み" : "未実験")}{GalleryPublishState(e.Id)}", $"Rev.{e.Revision}",
                    sel: on, onClick: on ? (UnityAction)null : () => { _galleryPick = id; Refresh(); }, name: "Card_" + e.Id);
            }
            if (entries.Count == 0) ConParas(RX + 20f, 130f, 414f, 540f, new[] { ("保存した発明はまだありません。発明して保存すると、ここに並びます。", LabTheme.ConMut) }, 17f);

            if (App.PendingDeleteId != null)
            {
                // 削除の確認（元に戻せない操作なので、同じ板の中で確かめる）
                var del = App.GalleryEntries.FirstOrDefault(x => x.Id == App.PendingDeleteId);
                c.Plate(RX, 558f, PW, 190f, "削除しますか");
                ConParas(RX + 20f, 606f, 414f, 690f, new[] { (del?.Name ?? App.PendingDeleteId, LabTheme.ConInk), ("改良の履歴と実験の記録も消え、元に戻せません", LabTheme.ConOr) });
                c.Button(RX + 20f, 696f, 200f, 46f, "やめる", App.CancelDelete, ConKind.Normal, 18f);
                c.Button(RX + 228f, 696f, 206f, 46f, "削除する", App.ConfirmDelete, ConKind.Warm, 18f);
            }
            else
            {
                c.Plate(RX, 558f, PW, 190f, "選んだ発明");
                if (pick != null)
                {
                    string when = pick.UpdatedAt != null && pick.UpdatedAt.Length >= 16 ? pick.UpdatedAt.Substring(5) : pick.UpdatedAt;
                    var info = new[] { ("分類", pick.TagsJa), ("版", $"Rev.{pick.Revision}"), ("更新", when) };
                    for (int i = 0; i < info.Length; i++) { c.T(RX + 20f, 622f + i * 26f, info[i].Item1, 16f, LabTheme.ConMut); c.TLine(RX + 100f, 622f + i * 26f, 334f, info[i].Item2, 17f); }
                    string pid = pick.Id;
                    c.Button(RX + 20f, 696f, 200f, 46f, "複製", () => App.Duplicate(pid), ConKind.Normal, 18f);
                    c.Button(RX + 228f, 696f, 206f, 46f, "削除", () => App.AskDelete(pid), ConKind.Quiet, 18f);
                }
            }
            string openId = pick?.Id;
            ConFrame(new List<ConBtn> { new ConBtn("戻る", App.CloseGallery, ConKind.Quiet, 120f) },
                new ConBtn("開く", () => { if (openId != null) App.LoadFromGallery(openId); }, ConKind.Primary, 220f, openId != null));
        }
    }
}
