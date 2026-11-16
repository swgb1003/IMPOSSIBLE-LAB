using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ImpossibleLab.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ImpossibleLab
{
    /// <summary>
    /// 6画面（S01〜S06）の World Space UI。参照画像02/03のレイアウト（中央メイン＋左右補助パネル）を
    /// VR向けに変換：上部ナビはコンソール見出しのステップ表示に、固定HUDは使わない（仕様21.3）。
    /// </summary>
    public sealed class LabUI : MonoBehaviour
    {
        public LabApp App;
        public Transform ConsoleRoot;

        Canvas _main, _left, _right;
        RectTransform _header, _body, _footer, _leftBody, _rightBody, _overlay;
        TextMeshProUGUI _toast, _liveText;
        TMP_InputField _requestInput, _revisionInput;
        string _signature;
        InputAction _menuAction, _lowerAction, _raiseAction;

        public const float MainW = 1500, MainH = 860, SideW = 520, SideH = 640;

        void Start()
        {
            var cam = App.Origin != null ? App.Origin.Camera : Camera.main;
            _main = UiKit.CreateCanvas("MainConsole", ConsoleRoot, new Vector2(MainW, MainH), cam);
            _main.transform.localPosition = Vector3.zero;
            _main.transform.localRotation = Quaternion.Euler(22f, 0, 0); // 机のようにやや上向き
            _left = UiKit.CreateCanvas("LeftPanel", ConsoleRoot, new Vector2(SideW, SideH), cam);
            _left.transform.localPosition = new Vector3(-1.18f, 0.30f, 0.12f);
            _left.transform.localRotation = Quaternion.Euler(0, -36f, 0);
            _right = UiKit.CreateCanvas("RightPanel", ConsoleRoot, new Vector2(SideW, SideH), cam);
            _right.transform.localPosition = new Vector3(1.18f, 0.30f, 0.12f);
            _right.transform.localRotation = Quaternion.Euler(0, 36f, 0);

            var mainBg = UiKit.Panel("Bg", _main.transform);
            UiKit.Stretch(mainBg);
            UiKit.VList(mainBg, LabTheme.Space2, LabTheme.Space3);
            _header = UiKit.Rect("Header", mainBg); UiKit.Size(_header, h: 72);
            UiKit.HList(_header, LabTheme.Space1);
            UiKit.Divider(mainBg);
            _body = UiKit.Rect("Body", mainBg); UiKit.Size(_body, flexH: 1);
            UiKit.VList(_body, LabTheme.Space2);
            _footer = UiKit.Rect("Footer", mainBg); UiKit.Size(_footer, h: 52);
            UiKit.HList(_footer, LabTheme.Space2);

            _leftBody = SidePanel(_left);
            _rightBody = SidePanel(_right);
            CreateGrabHandle(cam);

            // 片手でメニュー（コンソール）を呼び出す：左コントローラーのメニューボタン / PCは M キー
            _menuAction = new InputAction("CallConsole", InputActionType.Button);
            _menuAction.AddBinding("<XRController>{LeftHand}/{MenuButton}");
            _menuAction.AddBinding("<Keyboard>/m");
            _menuAction.performed += _ => App.RecenterToHead();
            _menuAction.Enable();

            // パネルが手の届かない位置にあっても高さを直せるよう、右コントローラーの A＝下げる / B＝上げる（PCは PageDown / PageUp）
            _lowerAction = new InputAction("LowerConsole", InputActionType.Button);
            _lowerAction.AddBinding("<XRController>{RightHand}/{PrimaryButton}");
            _lowerAction.AddBinding("<Keyboard>/pageDown");
            _lowerAction.performed += _ => App.NudgeConsoleHeight(-1);
            _lowerAction.Enable();
            _raiseAction = new InputAction("RaiseConsole", InputActionType.Button);
            _raiseAction.AddBinding("<XRController>{RightHand}/{SecondaryButton}");
            _raiseAction.AddBinding("<Keyboard>/pageUp");
            _raiseAction.performed += _ => App.NudgeConsoleHeight(+1);
            _raiseAction.Enable();

            App.Changed += Refresh;
            Refresh();
        }

        void OnDestroy()
        {
            if (App != null) App.Changed -= Refresh;
            _menuAction?.Dispose();
            _lowerAction?.Dispose();
            _raiseAction?.Dispose();
        }

        static RectTransform SidePanel(Canvas c)
        {
            var bg = UiKit.Panel("Bg", c.transform);
            UiKit.Stretch(bg);
            UiKit.VList(bg, LabTheme.Space2, LabTheme.Space3);
            return bg;
        }

        // ------------------------------------------------------------
        string Signature()
        {
            var s = App.Sm.State;
            string screen = s == LabState.Idle || s == LabState.Drafting ? "S01" : s.ToString();
            var sb = new StringBuilder(screen);
            sb.Append('|').Append(App.InputWarning).Append('|').Append(App.Current?.Current.Revision).Append('|').Append(App.Current?.Current.Id).Append(App.Current?.Current.Appearance)
              .Append('|').Append(App.PendingRevision != null).Append(App.RevisionBusy).Append(App.RevisionMessage)
              .Append('|').Append(App.TestConfig.Describe()).Append('|').Append(App.PendingDeleteId).Append('|').Append(App.GalleryEntries.Count)
              .Append('|').Append(App.DeviceRunning).Append(App.InRainLab).Append(App.Seated).Append('|').Append(App.LastResult?.ExperimentId)
              .Append('|').Append(string.Join(",", App.Stages.Values)).Append('|').Append(App.Current?.History.Count).Append('|').Append(App.CoefficientPage).Append('|').Append(_paramPage).Append('|').Append(App.PlanSignature).Append(_modulePage).Append('|').Append(_needPage).Append('|').Append(_galleryPage).Append('|').Append(App.SelectedPartIndex);
            return sb.ToString();
        }

        void Refresh()
        {
            if (_main == null) return;
            string sig = Signature();
            if (sig != _signature)
            {
                _signature = sig;
                Rebuild();
            }
            if (_toast != null) _toast.text = App.Toast ?? StatusLine();
            // 例の選択など、UI以外から変わった入力内容を反映（入力中は上書きしない）
            if (_requestInput != null && !_requestInput.isFocused && _requestInput.text != App.RequestText) _requestInput.SetTextWithoutNotify(App.RequestText);
            if (_revisionInput != null && !_revisionInput.isFocused && _revisionInput.text != App.RevisionText) _revisionInput.SetTextWithoutNotify(App.RevisionText);
        }

        string StatusLine()
        {
            switch (App.Sm.State)
            {
                case LabState.GeneratingDesign: return "処理中：他の操作はできません（キャンセル可）";
                case LabState.Building: return "組み立て中…";
                case LabState.RunningTest: return "実験中：停止すると記録せず初期状態に戻ります";
                default: return "状態：" + StateJa(App.Sm.State) + " ｜ 高さ：右手A▼B▲ ／ 移動：下のバーを握る";
            }
        }

        static string StateJa(LabState s)
        {
            switch (s)
            {
                case LabState.Idle: return "待機";
                case LabState.Drafting: return "指示入力";
                case LabState.GeneratingDesign: return "設計中";
                case LabState.AwaitingApproval: return "承認待ち";
                case LabState.Building: return "組み立て";
                case LabState.Editing: return "編集";
                case LabState.ConfiguringTest: return "実験設定";
                case LabState.RunningTest: return "実験中";
                case LabState.ReviewingResult: return "結果確認";
                case LabState.Saved: return "保存完了";
                case LabState.Gallery: return "ギャラリー";
                case LabState.Error: return "エラー";
                default: return "キャンセル";
            }
        }

        void Rebuild()
        {
            // 計測：作り直しにかかった時間と、フォントに実行中に追加された文字数（実機の引っかかりの原因を切り分ける）
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int glyphs0 = GlyphCount();
            RebuildScreens();
            sw.Stop();
            int added = GlyphCount() - glyphs0;
            if (sw.Elapsed.TotalMilliseconds > 20.0 || added > 0)
                Debug.Log($"[ILAB] ui rebuild state={App.Sm.State} ms={sw.Elapsed.TotalMilliseconds:F0} glyphsAdded={added} glyphs={GlyphCount()}");
        }

        static int GlyphCount() =>
            (LabTheme.Font != null ? LabTheme.Font.characterTable.Count : 0) + (LabTheme.FontBold != null ? LabTheme.FontBold.characterTable.Count : 0);

        void RebuildScreens()
        {
            UiKit.Clear(_header); UiKit.Clear(_body); UiKit.Clear(_footer); UiKit.Clear(_leftBody); UiKit.Clear(_rightBody);
            if (_overlay != null) { Destroy(_overlay.gameObject); _overlay = null; }
            _requestInput = null; _revisionInput = null; _liveText = null;
            BuildHeader();
            BuildFooter();
            switch (App.Sm.State)
            {
                case LabState.Idle: case LabState.Drafting: case LabState.Cancelled: BuildS01(); break;
                case LabState.GeneratingDesign: case LabState.AwaitingApproval: case LabState.Building: BuildS02(); break;
                case LabState.Error: BuildError(); break;
                case LabState.Editing: BuildS03(); break;
                case LabState.ConfiguringTest: BuildS04(); break;
                case LabState.RunningTest: case LabState.ReviewingResult: BuildS05(); break;
                case LabState.Saved: case LabState.Gallery: BuildS06(); break;
            }
        }

        // ---------------- Header / Footer ----------------
        static readonly string[] StepNames = { "発明指示", "AI設計", "編集", "実験設定", "結果", "保存" };

        int StepIndex()
        {
            switch (App.Sm.State)
            {
                case LabState.GeneratingDesign: case LabState.AwaitingApproval: case LabState.Building: return 1;
                case LabState.Editing: return 2;
                case LabState.ConfiguringTest: return 3;
                case LabState.RunningTest: case LabState.ReviewingResult: return 4;
                case LabState.Saved: case LabState.Gallery: return 5;
                default: return 0;
            }
        }

        void BuildHeader()
        {
            var brand = UiKit.Text(_header, "IMPOSSIBLE LAB\n<size=60%><color=#" + LabTheme.ToHex(LabTheme.TextSecondary) + ">VR Invention Platform</color></size>",
                26, bold: true, align: TextAlignmentOptions.MidlineLeft);
            UiKit.Size(brand, w: 260);
            int cur = StepIndex();
            for (int i = 0; i < StepNames.Length; i++)
            {
                bool on = i == cur, done = i < cur;
                var cell = UiKit.Image("Step" + i, _header, on ? LabTheme.AccentPrimary : LabTheme.BgPanel);
                UiKit.Size(cell, flexW: 1, h: 56);
                var t = UiKit.Text(cell.transform, $"{"①②③④⑤⑥"[i]} {StepNames[i]}{(done ? " ✓" : "")}", LabTheme.FontSmall,
                    on ? LabTheme.BgDeep : (done ? LabTheme.AccentLight : LabTheme.TextSecondary), bold: on, align: TextAlignmentOptions.Center);
                UiKit.Stretch(t.rectTransform, 4, 4, 2, 2);
            }
            var mode = App.Current != null || App.Sm.State == LabState.ConfiguringTest ? App.TestConfig.Mode : LabModes.Dream;
            var badge = UiKit.Badge(_header, LabModes.Label(mode) + "\n<size=75%>" + LabModes.Caption(mode) + "</size>",
                mode == LabModes.Reality ? LabTheme.AccentWarning : LabTheme.BgPanel, mode == LabModes.Reality ? LabTheme.BgDeep : LabTheme.AccentLight, 230);
            UiKit.Size(badge.transform.parent.GetComponent<RectTransform>(), h: 56);
        }

        void BuildFooter()
        {
            _toast = UiKit.Text(_footer, App.Toast ?? StatusLine(), LabTheme.FontSmall, LabTheme.TextSecondary, align: TextAlignmentOptions.MidlineLeft);
            UiKit.Size(_toast, flexW: 1);
            UiKit.Button(_footer, "▼ 下げる", () => App.NudgeConsoleHeight(-1), ButtonStyle.Ghost, 140, 48, LabTheme.FontSmall);
            UiKit.Button(_footer, "▲ 上げる", () => App.NudgeConsoleHeight(+1), ButtonStyle.Ghost, 140, 48, LabTheme.FontSmall);
            UiKit.Button(_footer, "パネルを正面へ", App.RecenterToHead, ButtonStyle.Ghost, 220, 48, LabTheme.FontSmall);
            UiKit.Button(_footer, App.Seated ? "座位 → 立位" : "立位 → 座位", App.ToggleSeated, ButtonStyle.Ghost, 200, 48, LabTheme.FontSmall);
        }

        RectTransform Title(string text, string sub = null)
        {
            var row = UiKit.Row("TitleRow", _body, LabTheme.Space2, 64);
            var t = UiKit.Text(row, text, LabTheme.FontTitle, bold: true, align: TextAlignmentOptions.MidlineLeft);
            UiKit.Size(t, flexW: 1);
            if (sub != null) { var s = UiKit.Text(row, sub, LabTheme.FontSmall, LabTheme.TextSecondary, align: TextAlignmentOptions.MidlineRight); UiKit.Size(s, w: 520); }
            return row;
        }

        void Para(Transform parent, string text, float size = LabTheme.FontBody, Color? color = null, float h = -1)
        {
            // 高さは TMP の推奨高さ（折り返し込み）に任せる。h は最小高さとしてのみ使う
            var t = UiKit.Text(parent, text, size, color ?? LabTheme.TextSecondary);
            if (h > 0) UiKit.Size(t).minHeight = Mathf.Min(h, size * 1.5f);
        }

        /// <summary>メイン画面の下に「パネルを移動」バーを置く。グリップで掴んで3枚まとめて動かせる。</summary>
        void CreateGrabHandle(Camera cam)
        {
            const float w = 420f, h = 46f; // px（1px = 1mm）
            var mainT = _main.transform;
            var go = new GameObject("PanelGrabHandle");
            go.transform.SetParent(ConsoleRoot, false);
            go.transform.position = mainT.TransformPoint(new Vector3(0, -MainH * 0.5f, 0)) - mainT.up * 0.045f;
            go.transform.rotation = mainT.rotation;
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(w * UiKit.MetersPerPixel + 0.04f, h * UiKit.MetersPerPixel + 0.03f, 0.03f);

            // 見た目（レーザーのUI判定を受けないよう GraphicRaycaster なしのキャンバス）
            var cgo = new GameObject("Visual", typeof(RectTransform), typeof(Canvas));
            cgo.transform.SetParent(go.transform, false);
            var canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = cam;
            var crt = (RectTransform)cgo.transform;
            crt.sizeDelta = new Vector2(w, h);
            crt.localScale = Vector3.one * UiKit.MetersPerPixel;
            var img = new GameObject("Bar", typeof(RectTransform), typeof(UnityEngine.UI.Image)).GetComponent<UnityEngine.UI.Image>();
            img.transform.SetParent(cgo.transform, false);
            UiKit.Stretch((RectTransform)img.transform);
            img.raycastTarget = false;
            var label = UiKit.Text(cgo.transform, "パネルを移動", LabTheme.FontSmall, LabTheme.BgDeep, bold: true, align: TextAlignmentOptions.Center);
            UiKit.Stretch(label.rectTransform);
            label.raycastTarget = false;

            var handle = go.AddComponent<ConsoleGrabHandle>();
            handle.Init(ConsoleRoot, cam != null ? cam.transform : null, img, label);
            handle.Released += App.RememberConsoleHeight;
        }

        void SideHeading(RectTransform side, string en, string ja)
        {
            UiKit.Heading(side, en, 30);
            Para(side, ja, LabTheme.FontSmall, LabTheme.TextSecondary, 30);
            UiKit.Divider(side);
        }

        // ---------------- S01 発明指示 ----------------
        void BuildS01()
        {
            Title("どんな発明をしますか？", "AI INVENTOR ｜ 設計元：" + App.Design.DisplayName);
            UiKit.Heading(_body, "自由な発明（アイデアを入力）", LabTheme.FontHeading);
            var row = UiKit.Row("InputRow", _body, LabTheme.Space2, 76);
            _requestInput = UiKit.Input(row, "作りたい道具のアイデアを入力する…（200文字以内）", RequestRules.MaxRequestLength, App.SetRequestText);
            _requestInput.SetTextWithoutNotify(App.RequestText);
            UiKit.Size(_requestInput, flexW: 1, h: 76);
            UiKit.Button(row, "送信 ▶", App.SubmitRequest, ButtonStyle.Primary, 200, 76);
            if (App.InputWarning != null) Para(_body, "⚠ " + App.InputWarning, LabTheme.FontBody, LabTheme.AccentWarning, 36);
            else Para(_body, "文字入力しにくい場合は下の例を選ぶだけで進められます。声で入れたいときは、Questキーボードのマイクボタンを試してください（実機で確認中）。", LabTheme.FontSmall, h: 32);

            // 見出しの行の右にギャラリーへのボタン（9 件のカードを入れるため、操作の行を見出しにまとめる）
            var quickHead = UiKit.Heading(_body, "クイック発明（例から選ぶ → 入力欄に反映）", LabTheme.FontHeading);
            UiKit.Button(quickHead, "保存済みの発明（ギャラリー）", App.OpenGallery, ButtonStyle.Secondary, 420, 50);
            // 3 列 × 3 行。カードの高さ 80 mm（レイで押しやすい 64 mm 以上）。分類は色だけでなく文字でも示す
            for (int r = 0; r < 3; r++)
            {
                var cards = UiKit.Row("Cards" + r, _body, LabTheme.Space1, QuickCardH);
                for (int c = 0; c < 3; c++)
                {
                    int i = r * 3 + c;
                    if (i < QuickInventions.All.Length) QuickCard(cards, QuickInventions.All[i]);
                }
            }


            SideHeading(_leftBody, "CENTRAL LAB", "中央研究室");
            Para(_leftBody, "AIと対話し、まだ存在しない道具をこの場所で発明できます。発明台の上に試作品が組み立てられます。", LabTheme.FontBody, LabTheme.TextPrimary, 150);
            UiKit.Heading(_leftBody, "本日のミッション", 26);
            Para(_leftBody, "「両手がふさがっていても雨を防げる道具」を発明し、雨と風の実験で性能を確かめよう。", LabTheme.FontBody, LabTheme.TextPrimary, 120);
            Para(_leftBody, "社会課題：荷物・子ども・杖などで両手がふさがる人の雨対策", LabTheme.FontSmall, h: 64);

            SideHeading(_rightBody, "INVENTION FLOW", "発明の流れ");
            FlowStep(_rightBody, "1", "アイデアを伝える", "テキストまたは例から指示");
            FlowStep(_rightBody, "2", "AIが設計・生成", "部品と機能を検証済みデータで設計");
            FlowStep(_rightBody, "3", "VRで試す", "掴む・起動する・雨の中で実験");
            FlowStep(_rightBody, "4", "改良・発展", "計測値を見て言葉で改良");
        }

        const float QuickCardH = 80f;

        void QuickCard(Transform parent, QuickInventions.Item item)
        {
            var b = UiKit.Button(parent, $"{item.Title}\n<size=75%><color=#{LabTheme.ToHex(LabTheme.AccentLight)}>{item.Kind}</color><color=#{LabTheme.ToHex(LabTheme.TextSecondary)}> ・ {item.Experiment}</color></size>",
                () => { App.PickQuickInvention(item.Request); if (_requestInput != null) _requestInput.SetTextWithoutNotify(item.Request); },
                ButtonStyle.Secondary, -1, QuickCardH);
            UiKit.Size(b, flexW: 1);
        }

        void FlowStep(Transform parent, string n, string title, string desc)
        {
            var row = UiKit.Row("Flow" + n, parent, LabTheme.Space2, 96);
            var num = UiKit.Badge(row, n, LabTheme.BgPanel, LabTheme.AccentLight, 52);
            UiKit.Size(num.transform.parent.GetComponent<RectTransform>(), h: 52);
            var t = UiKit.Text(row, title + "\n<size=78%><color=#" + LabTheme.ToHex(LabTheme.TextSecondary) + ">" + desc + "</color></size>", LabTheme.FontBody, bold: true, align: TextAlignmentOptions.MidlineLeft);
            UiKit.Size(t, flexW: 1);
        }

        // ---------------- S02 AI設計・生成 ----------------
        void BuildS02()
        {
            var s = App.Sm.State;
            Title(s == LabState.AwaitingApproval ? "設計プレビュー（承認待ち）" : s == LabState.Building ? "組み立て中…" : "AIが設計を行っています…",
                "設計元：" + (App.LastOutcome != null && s != LabState.GeneratingDesign
                    ? (App.LastOutcome.Source == "ai" ? "AI（Claude）" : "オフライン（サンプル設計図・AI未使用）") : App.Design.DisplayName));
            var stages = UiKit.Row("Stages", _body, LabTheme.Space1, 84);
            int i = 0;
            foreach (var st in DesignStageText.All)
            {
                var status = App.Stages[st];
                Color bg = status == StageStatus.Done ? LabTheme.BgPanel : status == StageStatus.Running ? LabTheme.AccentPrimary : status == StageStatus.Failed ? LabTheme.StateError : LabTheme.BgDeep;
                var cell = UiKit.Image("Stage" + i, stages, bg);
                UiKit.Size(cell, flexW: 1, h: 84);
                string st2 = status == StageStatus.Done ? "✓ 完了" : status == StageStatus.Running ? "● 処理中" : status == StageStatus.Failed ? "× 失敗" : "待機";
                var t = UiKit.Text(cell.transform, $"{"①②③④⑤"[i]} {DesignStageText.Label(st)}\n<size=80%>{st2}</size>", LabTheme.FontBody,
                    status == StageStatus.Running ? LabTheme.BgDeep : (status == StageStatus.Done ? LabTheme.AccentLight : LabTheme.TextSecondary), bold: status == StageStatus.Running, align: TextAlignmentOptions.Center);
                UiKit.Stretch(t.rectTransform, 4, 4, 4, 4);
                i++;
            }

            SideHeading(_leftBody, "YOUR IDEA", "入力したアイデア");
            Para(_leftBody, "「" + App.RequestText + "」", LabTheme.FontBody, LabTheme.TextPrimary, 110);

            if (s == LabState.GeneratingDesign)
            {
                UiKit.Heading(_leftBody, "確認事項", 26);
                Para(_leftBody, "・AIの出力はデータとして検証し、登録済みの部品と機能だけで組み立てます\n・現実の製品としての安全性や実現性は保証しません", LabTheme.FontSmall, h: 180);
                Para(_body, "段階表示は実際の処理段階に対応しています（進捗率は表示しません）。", LabTheme.FontSmall, h: 32);
                var a = UiKit.Row("Actions", _body, LabTheme.Space2, 64);
                UiKit.Button(a, "キャンセル", App.CancelGeneration, ButtonStyle.Secondary, 260);
                SideHeading(_rightBody, "FEATURES", "採用機能と部品");
                Para(_rightBody, "設計の完了を待っています…", LabTheme.FontBody);
                return;
            }

            var dna = s == LabState.AwaitingApproval ? App.Preview : App.Current?.Current ?? App.Preview;
            if (dna == null) return;
            var cols = UiKit.Row("Preview", _body, LabTheme.Space3); UiKit.Size(cols, flexH: 1);
            cols.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft; // 行数が少ないページでも上に揃える
            // 左列：設計の概要（名前・目的・分類・検証結果）
            var c1 = UiKit.Column("Info", cols, LabTheme.Space1); UiKit.Size(c1, w: 430);
            Para(c1, $"<b><color=#{LabTheme.ToHex(LabTheme.TextPrimary)}>{dna.Name}</color></b>", LabTheme.FontHeading, h: 42);
            Para(c1, "目的：" + dna.Purpose, LabTheme.FontSmall, LabTheme.TextPrimary, 60);
            Para(c1, $"分類：{LabCatalog.TagsJa(dna.Tags)}\n取り付け：{LabCatalog.Mount(dna.Mount)?.LabelJa ?? dna.Mount}　部品 {dna.Parts.Count} / 上限 {dna.Constraints.MaxParts}", LabTheme.FontSmall, h: 60);
            var rep = App.LastOutcome?.Report;
            Para(c1, "検証：" + (rep != null ? rep.Summary : "—") + (App.LastOutcome != null ? "\n" + App.LastOutcome.Message : ""), LabTheme.FontSmall, LabTheme.AccentLight, 64);
            // 右列：機能対応表
            var c2 = UiKit.Column("FunctionMap", cols, 6); UiKit.Size(c2, flexW: 1);
            BuildFunctionMap(c2, dna);

            if (s == LabState.AwaitingApproval)
            {
                var a = UiKit.Row("Actions", _body, LabTheme.Space2, 68);
                UiKit.Button(a, "指示に戻る", App.BackToDraft, ButtonStyle.Ghost, 240);
                UiKit.Button(a, "再生成", App.Regenerate, ButtonStyle.Secondary, 220);
                var sp = UiKit.Rect("Sp", a); UiKit.Size(sp, flexW: 1);
                UiKit.Button(a, "承認して組み立てる ▶", App.ApprovePreview, ButtonStyle.Primary, 420, 68);
            }

            UiKit.Heading(_leftBody, "設計の要点", 26);
            var points = App.LastOutcome?.DesignPoints ?? new List<string>();
            CappedLines(_leftBody, points.Select(p => ("・" + p, LabTheme.FontSmall, LabTheme.TextPrimary, 56f)).ToList(), 4);
            if (points.Count == 0) Para(_leftBody, "（要点の説明はありません）", LabTheme.FontSmall, h: 30);
            Para(_leftBody, "現実の製品としての安全性や実現性は保証しません。", LabTheme.FontSmall, LabTheme.AccentWarning, 30);

            SideHeading(_rightBody, "PARTS", "採用機能と部品");
            var lines = dna.Parts.Select(part => ($"■ {LabCatalog.Part(part.Type)?.LabelJa}（{part.Id}）", LabTheme.FontBody, LabTheme.TextPrimary, 38f))
                .Concat(dna.Behaviors.Select(b => ($"◆ {LabCatalog.Behavior(b.Type)?.LabelJa} → {b.TargetPart}", LabTheme.FontSmall, LabTheme.AccentLight, 34f))).ToList();
            CappedLines(_rightBody, lines, 9);
        }

        // ---- 機能対応表（S02）：要望ごとに ✓ 検証する／△ 単純化して検証／× 検証できない。記号・色・文字の3つで区別する ----
        // （✕ U+2715 は日本語フォントに無く□になるため × U+00D7 を使う）
        int _needPage;
        string _needPageFor;
        const int NeedsPerPage = 4;
        public int NeedPage => _needPage;
        public void StepNeedPage(int dir) { _needPage = Mathf.Max(0, _needPage + dir); Refresh(); }

        void BuildFunctionMap(RectTransform parent, InventionDna dna)
        {
            var rows = App.FunctionRows(dna, out bool derived);
            if (_needPageFor != dna.Id + "#" + dna.Revision) { _needPageFor = dna.Id + "#" + dna.Revision; _needPage = 0; }
            int pages = Mathf.Max(1, (rows.Count + NeedsPerPage - 1) / NeedsPerPage);
            _needPage = Mathf.Clamp(_needPage, 0, pages - 1);
            var (sim, simp, unv) = FunctionMapView.Count(rows);

            var head = UiKit.Row("MapHead", parent, LabTheme.Space1, 48);
            var h = UiKit.Heading(head, "機能対応表", 26); UiKit.Size(h, w: 170);
            var cnt = UiKit.Text(head, $"<color=#{LabTheme.ToHex(LabTheme.AccentLight)}>✓ 検証 {sim}</color>　<color=#{LabTheme.ToHex(LabTheme.TextPrimary)}>△ 単純化 {simp}</color>　<color=#{LabTheme.ToHex(LabTheme.AccentWarning)}>× 検証できない {unv}</color>",
                LabTheme.FontSmall, align: TextAlignmentOptions.MidlineLeft);
            UiKit.Size(cnt, flexW: 1);
            if (pages > 1)
            {
                var prev = UiKit.Button(head, "◀", () => StepNeedPage(-1), ButtonStyle.Secondary, 56, 48);
                UiKit.SetInteractable(prev, _needPage > 0);
                var pt = UiKit.Text(head, $"{_needPage + 1}/{pages}", LabTheme.FontSmall, LabTheme.TextPrimary, align: TextAlignmentOptions.Center); UiKit.Size(pt, w: 60);
                var next = UiKit.Button(head, "▶", () => StepNeedPage(+1), ButtonStyle.Secondary, 56, 48);
                UiKit.SetInteractable(next, _needPage < pages - 1);
            }
            if (derived)
                Para(parent, "AI の対応表がない設計のため、機能ごとに「この版の実験で動くか」を表示しています", LabTheme.FontSmall, LabTheme.TextSecondary, 28);
            if (rows.Count == 0) { Para(parent, "機能がありません", LabTheme.FontSmall, h: 30); return; }
            foreach (var r in rows.Skip(_needPage * NeedsPerPage).Take(NeedsPerPage)) NeedRow(parent, r);
        }

        void NeedRow(Transform parent, FunctionRow r)
        {
            var row = UiKit.Row("Need", parent, LabTheme.Space2, 62);
            var bg = row.gameObject.AddComponent<UnityEngine.UI.Image>(); bg.color = LabTheme.Row; bg.raycastTarget = false;
            row.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>().padding = new RectOffset(8, 12, 3, 3);
            string icon = r.Status == "simulated" ? "✓" : r.Status == "simplified" ? "△" : "×";
            string label = r.Status == "simulated" ? "検証する" : r.Status == "simplified" ? "単純化" : "検証できない";
            Color c = r.Status == "simulated" ? LabTheme.AccentLight : r.Status == "simplified" ? LabTheme.TextSecondary : LabTheme.AccentWarning;
            var badge = UiKit.Badge(row, $"{icon} {label}", c, LabTheme.BgDeep, 176);
            UiKit.Size(badge.transform.parent.GetComponent<RectTransform>(), h: 48);
            string sub = LabTheme.ToHex(LabTheme.TextSecondary);
            string detail;
            if (!r.Verifiable) detail = "理由：" + (r.Note.Length > 0 ? r.Note : "（理由の記載なし）");
            else
            {
                detail = (r.BlocksJa.Length > 0 ? "機能：" + r.BlocksJa + "　" : "") +
                    (r.PlanTitle != null ? $"実験：{r.PlanTitle}" : $"<color=#{LabTheme.ToHex(LabTheme.AccentWarning)}>⚠ この版の実験の候補では動かない</color>");
                // 単純化は「何を単純化したか」を優先して出す（2行目は1行に収める）
                if (r.Status == "simplified" && r.Note.Length > 0) detail = (r.PlanTitle != null ? $"実験：{r.PlanTitle}　" : "") + "単純化：" + r.Note;
            }
            var t = UiKit.Text(row, $"{Short(r.Need, 34)}\n<size=82%><color=#{sub}>{Short(detail, 42)}</color></size>", LabTheme.FontSmall, LabTheme.TextPrimary, align: TextAlignmentOptions.MidlineLeft);
            UiKit.Size(t, flexW: 1);
        }

        void BuildError()
        {
            Title("× 設計できませんでした");
            Para(_body, App.ErrorMessage ?? "不明なエラー", LabTheme.FontHeading, LabTheme.StateError, 90);
            foreach (var s in App.ErrorSuggestions) Para(_body, "・" + s, LabTheme.FontBody, LabTheme.TextPrimary, 40);
            var sp = UiKit.Rect("Sp", _body); UiKit.Size(sp, flexH: 1);
            var a = UiKit.Row("Actions", _body, LabTheme.Space2, 68);
            UiKit.Button(a, "指示に戻る", App.DismissError, ButtonStyle.Ghost, 260);
            UiKit.Button(a, "再試行", App.SubmitRequest, ButtonStyle.Secondary, 240);
            var sp2 = UiKit.Rect("Sp2", a); UiKit.Size(sp2, flexW: 1);
            UiKit.Button(a, "サンプル設計図（雨具）を使う", App.UseOfflineSample, ButtonStyle.Primary, 460, 68);
            SideHeading(_leftBody, "OFFLINE", "通信・AIが使えないとき");
            Para(_leftBody, "AIやネットワークが使えない場合でも、検証済みのサンプル設計図でデモを続けられます。", LabTheme.FontBody, LabTheme.TextPrimary);
            SideHeading(_rightBody, "SUPPORTED", "この版で実験できる機能");
            Para(_rightBody, "・雨よけ・運搬・飛行（専用の実験場）\n・照明・日よけ・検知・通知・加温/冷却・衝撃吸収・つかむ・持ち上げ（組み立て式の実験）", LabTheme.FontSmall, LabTheme.TextPrimary);
        }

        // ---------------- S03 発明品編集 ----------------
        // 性能パラメータの表示ページ（AI設計ではパラメータが多く、1画面に収まらないため）
        int _paramPage;
        string _paramPageFor;
        const int ParamsOnePage = 5, ParamsPerPage = 4;
        public int ParamPage => _paramPage;
        public void StepParamPage(int dir) { _paramPage = Mathf.Max(0, _paramPage + dir); Refresh(); }

        /// <summary>行を上限まで並べ、超えた分は「ほか N 件」にまとめる（側面パネルからはみ出さないように）。</summary>
        void CappedLines(Transform parent, List<(string text, float size, Color color, float h)> lines, int max)
        {
            int show = lines.Count <= max ? lines.Count : max - 1;
            for (int i = 0; i < show; i++) Para(parent, lines[i].text, lines[i].size, lines[i].color, lines[i].h);
            if (show < lines.Count) Para(parent, $"…ほか {lines.Count - show} 件", LabTheme.FontSmall, LabTheme.TextSecondary, 30);
        }

        void BuildS03()
        {
            var dna = App.Current.Current;
            Title($"{dna.Name}", $"Rev.{dna.Revision}{(dna.ParentRevision.HasValue ? "（Rev." + dna.ParentRevision + " から改良）" : "")}　{(App.Current.Tested ? "✓ 実験済み" : "未実験")}");
            var cols = UiKit.Row("Cols", _body, LabTheme.Space3); UiKit.Size(cols, flexH: 1);
            var left = UiKit.Column("Params", cols, LabTheme.Space1); UiKit.Size(left, flexW: 1.25f);
            var rows = new List<(BehaviorInfo info, ParameterInfo p, float v)>();
            foreach (var b in dna.Behaviors)
            {
                var info = LabCatalog.Behavior(b.Type);
                foreach (var p in info.Parameters) rows.Add((info, p, b.Parameters[p.Key]));
            }
            if (_paramPageFor != dna.Id) { _paramPageFor = dna.Id; _paramPage = 0; }
            int perPage = rows.Count <= ParamsOnePage ? ParamsOnePage : ParamsPerPage;
            int pages = Mathf.Max(1, (rows.Count + perPage - 1) / perPage);
            _paramPage = Mathf.Clamp(_paramPage, 0, pages - 1);
            var headRow = UiKit.Row("ParamHead", left, LabTheme.Space1, 48);
            var head = UiKit.Heading(headRow, "性能パラメータ（許可範囲・単位）", 26);
            UiKit.Size(head, flexW: 1);
            if (pages > 1)
            {
                var prev = UiKit.Button(headRow, "◀", () => StepParamPage(-1), ButtonStyle.Secondary, 64, 48);
                UiKit.SetInteractable(prev, _paramPage > 0);
                var pt = UiKit.Text(headRow, $"{_paramPage + 1} / {pages}", LabTheme.FontSmall, LabTheme.TextPrimary, align: TextAlignmentOptions.Center);
                UiKit.Size(pt, w: 80);
                var next = UiKit.Button(headRow, "▶", () => StepParamPage(+1), ButtonStyle.Secondary, 64, 48);
                UiKit.SetInteractable(next, _paramPage < pages - 1);
            }
            foreach (var (info, p, v) in rows.Skip(_paramPage * perPage).Take(perPage)) ParamRow(left, info, p, v);
            var right = UiKit.Column("Revise", cols, LabTheme.Space1); UiKit.Size(right, flexW: 1);
            UiKit.Heading(right, "AIに改良を依頼", 26);
            var inRow = UiKit.Row("RevInput", right, LabTheme.Space1, 64);
            _revisionInput = UiKit.Input(inRow, "例：" + OfflineRevisionRules.SuggestedRequestsFor(dna).First(), RequestRules.MaxRequestLength, s => App.RevisionText = s);
            _revisionInput.SetTextWithoutNotify(App.RevisionText);
            UiKit.Size(_revisionInput, flexW: 1, h: 64);
            UiKit.Button(inRow, "依頼", App.RequestRevision, ButtonStyle.Primary, 120, 64);
            var chips = UiKit.Rect("Chips", right);
            var grid = chips.gameObject.AddComponent<UnityEngine.UI.GridLayoutGroup>();
            grid.cellSize = new Vector2(298, 52); grid.spacing = new Vector2(8, 8);
            UiKit.Size(chips, h: 112);
            foreach (var sug in OfflineRevisionRules.SuggestedRequestsFor(dna))
            {
                var text = sug;
                UiKit.Button(chips, text, () => { App.PickRevision(text); if (_revisionInput != null) _revisionInput.SetTextWithoutNotify(text); }, ButtonStyle.Secondary, -1, 52, LabTheme.FontSmall);
            }
            if (App.RevisionBusy) Para(right, "改良案を作成中…", LabTheme.FontBody, LabTheme.AccentLight, 36);
            else if (App.PendingRevision != null)
            {
                Para(right, "改良案（" + (App.PendingProposal.Source == "offline" ? "オフライン改良ルール・AI未接続" : "AI") + "）：" + App.PendingProposal.Summary, LabTheme.FontSmall, LabTheme.TextPrimary, 56);
                CappedLines(right, App.PendingRevision.Changes.Select(c => ($"差分　{c.Label}：{c.Before} → <b>{c.After}</b>", LabTheme.FontSmall, LabTheme.AccentLight, 32f)).ToList(), 3);
                foreach (var n in App.PendingProposal.Notes.Concat(App.PendingRevision.Warnings).Take(1)) Para(right, "⚠ " + n, LabTheme.FontSmall, LabTheme.AccentWarning, 30);
                var ar = UiKit.Row("Approve", right, LabTheme.Space1, 56);
                UiKit.Button(ar, "却下", App.RejectRevision, ButtonStyle.Ghost, 140, 56);
                UiKit.Button(ar, "承認して適用", App.ApproveRevision, ButtonStyle.Primary, -1, 56);
            }
            else if (App.RevisionMessage != null) Para(right, App.RevisionMessage, LabTheme.FontSmall, LabTheme.AccentWarning, 110);

            var a = UiKit.Row("Actions", _body, LabTheme.Space1, 68);
            var undo = UiKit.Button(a, "元に戻す", App.Undo, ButtonStyle.Ghost, 170);
            UiKit.SetInteractable(undo, App.CanUndo);
            UiKit.Button(a, App.DeviceRunning ? "■ 停止" : "● 起動（試用）", App.ToggleDevice, App.DeviceRunning ? ButtonStyle.Selected : ButtonStyle.Secondary, 230);
            UiKit.Button(a, "保存", App.Save, ButtonStyle.Secondary, 130);
            UiKit.Button(a, "ギャラリー", App.OpenGallery, ButtonStyle.Ghost, 170);
            UiKit.Button(a, "新しい発明", App.NewInvention, ButtonStyle.Ghost, 180);
            var sp = UiKit.Rect("Sp", a); UiKit.Size(sp, flexW: 1);
            UiKit.Button(a, "実験へ ▶", App.GoToTestSetup, ButtonStyle.Primary, 260, 68);

            SideHeading(_leftBody, "DESIGN STUDIO", "部品の形・素材と外観");
            var sel = App.SelectedPart;
            if (sel != null)
            {
                StepRow("PartSelect", "部品", $"{LabCatalog.Part(sel.Type)?.LabelJa ?? sel.Type}　<size=80%>{App.SelectedPartIndex + 1} / {dna.Parts.Count}</size>", () => App.StepSelectedPart(-1), () => App.StepSelectedPart(+1), dna.Parts.Count > 1);
                Para(_leftBody, $"{sel.Id} ／ {sel.Scale.x:0.00}×{sel.Scale.y:0.00}×{sel.Scale.z:0.00} m（橙の枠）", LabTheme.FontSmall, LabTheme.TextSecondary, 30);
                StepRow("PartShape", "形", LabCatalog.Shape(sel.ShapeOrDefault)?.LabelJa ?? sel.ShapeOrDefault, () => App.StepPartShape(-1), () => App.StepPartShape(+1), true);
                var mat = LabCatalog.Material(sel.MaterialOrDefault);
                StepRow("PartMaterial", "素材", $"{mat?.LabelJa ?? sel.MaterialOrDefault}　<size=80%>{mat?.DensityKgM3:0} kg/m³</size>", () => App.StepPartMaterial(-1), () => App.StepPartMaterial(+1), true);
                float kg = MassModel.TotalKg(dna), partKg = MassModel.Part(sel).Kg;
                var limit = MassModel.MountLimitKg(dna.Mount);
                string limitText = limit.HasValue ? $"／ 体に付ける目安 {limit.Value:0.0} kg" + (kg > limit.Value ? "　<color=#" + LabTheme.ToHex(LabTheme.AccentWarning) + ">超過</color>" : "") : "";
                Para(_leftBody, $"この部品 {partKg:0.00} kg ／ 総質量（推定）{kg:0.00} kg {limitText}\n<size=85%><color=#{LabTheme.ToHex(LabTheme.TextSecondary)}>変更は改良の版（Rev.）として記録します</color></size>", LabTheme.FontSmall, LabTheme.TextPrimary, 64);
            }
            StepRow("Appearance", "外観", LabCatalog.Appearance(dna.Appearance)?.LabelJa ?? dna.Appearance, () => App.StepAppearance(-1), () => App.StepAppearance(+1), true);
            Para(_leftBody, "掴む：グリップ／起動：掴んでトリガー", LabTheme.FontSmall, LabTheme.TextSecondary, 30);

            SideHeading(_rightBody, "HISTORY", "改良履歴（元に戻す対象）");
            var hist = App.Current.History.AsEnumerable().Reverse().Take(5).ToList();
            Para(_rightBody, $"● 現在 Rev.{dna.Revision}", LabTheme.FontBody, LabTheme.AccentLight, 36);
            foreach (var h in hist) Para(_rightBody, $"○ Rev.{h.Revision}　{KeySummary(h)}", LabTheme.FontSmall, h: 34);
            if (hist.Count == 0) Para(_rightBody, "まだ改良履歴はありません", LabTheme.FontSmall, h: 34);
            var last = App.Current.Experiments.LastOrDefault();
            if (last != null)
            {
                UiKit.Heading(_rightBody, "直近の実験", 26);
                Para(_rightBody, $"Rev.{last.Revision}　{LabModes.Label(last.Config.Mode)}　{(last.Success ? "✓ 成功" : "! 要改良")}\n{ResultSummary(last)}", LabTheme.FontSmall, LabTheme.TextPrimary, 70);
            }
        }

        /// <summary>左パネルの「名前 ◀ 値 ▶」の行（部品・形・素材・外観で共通）。</summary>
        void StepRow(string name, string label, string value, UnityEngine.Events.UnityAction prev, UnityEngine.Events.UnityAction next, bool enabled)
        {
            var row = UiKit.Row(name, _leftBody, LabTheme.Space1, 52);
            var l = UiKit.Text(row, label, LabTheme.FontSmall, LabTheme.TextSecondary, align: TextAlignmentOptions.MidlineLeft); UiKit.Size(l, w: 70);
            var p = UiKit.Button(row, "◀", prev, ButtonStyle.Secondary, 56, 48);
            var v = UiKit.Text(row, value, LabTheme.FontSmall, LabTheme.AccentLight, bold: true, align: TextAlignmentOptions.Center); UiKit.Size(v, flexW: 1);
            var n = UiKit.Button(row, "▶", next, ButtonStyle.Secondary, 56, 48);
            UiKit.SetInteractable(p, enabled); UiKit.SetInteractable(n, enabled);
        }

        /// <summary>改良履歴の1行に出す主なパラメータ（カテゴリ別）。</summary>
        static string KeySummary(InventionDna d)
        {
            string F(string b, string p, string label)
            {
                var info = LabCatalog.Behavior(b)?.Param(p);
                return info == null || d.FindBehavior(b) == null ? null : label + " " + LabCatalog.FormatValue(info, d.GetParameter(b, p, info.Default));
            }
            var items = d.Category == "carrier" ? new[] { F("follow_user", "speed", "速度"), F("avoid_obstacle", "detectionRange", "検知"), F("carry_load", "capacity", "積載") }
                : d.Category == "mobility" ? new[] { F("hover", "lift", "揚力"), F("move_forward", "thrust", "推力"), F("stabilize", "stabilizationStrength", "安定") }
                : new[] { F("rain_shield", "radius", "防護半径"), F("rain_shield", "skirtLength", "側面") };
            return string.Join("／", items.Where(x => x != null));
        }

        /// <summary>結果の要約（主な指標2つ）。</summary>
        static string ResultSummary(RainExperimentResult r) => r.IsRain
            ? $"被濡れ {r.WetnessScore:0.0} 粒/秒　防護率 {r.CoverageRatio * 100f:0.0}%"
            : string.Join("　", r.Metrics.Where(m => m.Primary).Take(2).Select(m => $"{m.LabelJa} {m.Display}"));

        void ParamRow(Transform parent, BehaviorInfo b, ParameterInfo p, float value)
        {
            var row = UiKit.Row("P_" + p.Key, parent, LabTheme.Space1, 74);
            var bg = row.gameObject.AddComponent<UnityEngine.UI.Image>(); bg.color = LabTheme.Row; bg.raycastTarget = false;
            row.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>().padding = new RectOffset(12, 8, 4, 4);
            var label = UiKit.Text(row, $"{p.LabelJa}\n<size=72%><color=#{LabTheme.ToHex(LabTheme.TextSecondary)}>{b.LabelJa} ／ 範囲 {LabCatalog.FormatValue(p, p.Min)}〜{LabCatalog.FormatValue(p, p.Max)}" +
                (p.RealityMax.HasValue ? $" ／ REALITY上限 {LabCatalog.FormatValue(p, p.RealityMax.Value)}" : "") + "</color></size>", LabTheme.FontSmall, align: TextAlignmentOptions.MidlineLeft);
            UiKit.Size(label, flexW: 1);
            UiKit.Button(row, "−", () => App.StepParameter(b.Type, p.Key, -1), ButtonStyle.Secondary, 56, 50);
            var v = UiKit.Text(row, LabCatalog.FormatValue(p, value), LabTheme.FontBody, LabTheme.AccentLight, bold: true, align: TextAlignmentOptions.Center);
            UiKit.Size(v, w: 130);
            UiKit.Button(row, "＋", () => App.StepParameter(b.Type, p.Key, +1), ButtonStyle.Secondary, 56, 50);
        }

        // ---------------- S04 実験環境設定 ----------------
        // S04 は「実験計画」を選んで編集する画面（6週目）。上から：モード → 計画の候補 → ねらい → 環境条件（値の増減・追加・削除）→ シード → 開始。
        // 計測項目と合格条件は右パネル、計算の前提は左パネル（実行前に同じ計算を走らせて表示）。
        int _modulePage;
        const int ModulesPerPage = 3;

        void BuildS04()
        {
            var cfg = App.TestConfig;
            if (cfg.Mode == LabModes.Reality && App.CoefficientPage >= 0 && RealityModel.Default != null) { BuildCoefficientTable(); return; }
            var dna = App.Current.Current;
            var removed = new List<string>();
            var plan = App.ActivePlan(removed);
            bool legacy = App.PlanIsLegacy;
            var errors = new List<string>();
            if (plan != null) ExperimentPlanJson.Validate(plan, dna, errors);

            // 1) 見出し＋モード（DREAM / REALITY）
            var head = UiKit.Row("TitleRow", _body, LabTheme.Space2, 64);
            var ttl = UiKit.Text(head, "実験設定", LabTheme.FontTitle, bold: true, align: TextAlignmentOptions.MidlineLeft);
            UiKit.Size(ttl, w: 220);
            var where = UiKit.Text(head, "実験場：" + (legacy ? LabEnvironments.LabelJa(cfg.Environment) : "TEST ZONE（" + PlanCandidates.ScenarioLabel(plan?.Scenario) + "）"),
                LabTheme.FontSmall, LabTheme.TextSecondary, align: TextAlignmentOptions.MidlineLeft);
            UiKit.Size(where, flexW: 1);
            ModeToggle(head, LabModes.Dream, "DREAM", "空想の物理");
            ModeToggle(head, LabModes.Reality, "REALITY", "簡易モデル");

            // 2) 実験計画の候補（AI 案 → 標準）
            var tabs = UiKit.Row("Plans", _body, LabTheme.Space1, 60);
            var tl = UiKit.Text(tabs, "実験", LabTheme.FontSmall, LabTheme.TextSecondary, align: TextAlignmentOptions.MidlineLeft);
            UiKit.Size(tl, w: 64);
            for (int i = 0; i < App.PlanOptions.Count; i++)
            {
                var c = App.PlanOptions[i];
                bool sel = i == App.PlanIndex;
                int idx = i;
                string tag = c.FromAi ? "AI案" : "標準";
                // 1件でも横いっぱいに伸ばさない（選択肢が1つだと分かるように）。6件で 1 行に収まる幅
                float tabW = Mathf.Min(300f, (MainW - 2 * LabTheme.Space3 - 64 - LabTheme.Space1 * 7) / Mathf.Max(1, App.PlanOptions.Count));
                // 名前は1行に収める（全角 22px として、ボタンの左右の余白を除いた文字数）
                int chars = Mathf.Max(4, Mathf.FloorToInt((tabW - 28f) / LabTheme.FontSmall));
                UiKit.Button(tabs, $"<size=72%><color=#{LabTheme.ToHex(sel ? LabTheme.BgDeep : LabTheme.AccentLight)}>{tag}</color></size>\n{Short(c.Title, chars)}",
                    () => App.SelectPlan(idx), sel ? ButtonStyle.Selected : ButtonStyle.Secondary, tabW, 60, LabTheme.FontSmall);
            }
            var tabFill = UiKit.Rect("Fill", tabs); UiKit.Size(tabFill, flexW: 1);

            // 3) ねらい（REALITY の注意は同じ行の右に置く。別の行にするとフッターがパネルからはみ出す）
            var aimRow = UiKit.Row("Aim", _body, LabTheme.Space2, 36);
            var sp0 = App.SelectedPlan;
            var aim = UiKit.Text(aimRow, "ねらい：" + (sp0 != null && sp0.Aim.Length > 0 ? sp0.Aim : ExperimentCatalog.Scenario(plan?.Scenario)?.Description ?? ""),
                LabTheme.FontSmall, LabTheme.TextPrimary, align: TextAlignmentOptions.MidlineLeft);
            UiKit.Size(aim, flexW: 1);
            if (cfg.Mode == LabModes.Reality)
            {
                var warn = UiKit.Text(aimRow, "⚠ REALITY は簡易モデルによる参考値（現実の性能・安全性は保証しません）", LabTheme.FontSmall, LabTheme.AccentWarning, align: TextAlignmentOptions.MidlineRight);
                UiKit.Size(warn, w: 640);
            }

            // 4) 環境条件：見出し＋追加ボタン（＋ページ送り）
            var mods = plan?.Modules ?? new List<PlanModule>();
            int pages = Mathf.Max(1, (mods.Count + ModulesPerPage - 1) / ModulesPerPage);
            _modulePage = Mathf.Clamp(_modulePage, 0, pages - 1);
            var condHead = UiKit.Row("CondHead", _body, LabTheme.Space1, 48);
            var ch = UiKit.Heading(condHead, "環境条件", 26);
            UiKit.Size(ch, w: 150);
            if (legacy)
            {
                var note = UiKit.Text(condHead, "この実験場は値の変更のみ（条件の追加・削除はできません）", LabTheme.FontSmall, LabTheme.TextSecondary, align: TextAlignmentOptions.MidlineLeft);
                UiKit.Size(note, flexW: 1);
            }
            else
            {
                var addable = PlanEditor.Addable(App.EditPlan, dna);
                var addLabel = UiKit.Text(condHead, addable.Count > 0 ? "追加：" : "追加できる条件はありません", LabTheme.FontSmall, LabTheme.TextSecondary, align: TextAlignmentOptions.MidlineLeft);
                UiKit.Size(addLabel, w: addable.Count > 0 ? 70 : 360);
                foreach (var type in addable.Take(4))
                {
                    string t = type;
                    UiKit.Button(condHead, "＋ " + ExperimentCatalog.Module(type).LabelJa, () => App.AddPlanModule(t), ButtonStyle.Ghost, 150, 48, LabTheme.FontSmall);
                }
                var fill = UiKit.Rect("Fill", condHead); UiKit.Size(fill, flexW: 1);
                UiKit.Button(condHead, "初期値に戻す", App.ResetPlan, ButtonStyle.Ghost, 190, 48, LabTheme.FontSmall);
            }
            if (pages > 1)
            {
                var prev = UiKit.Button(condHead, "◀", () => { _modulePage--; Refresh(); }, ButtonStyle.Secondary, 56, 48);
                UiKit.SetInteractable(prev, _modulePage > 0);
                var pt = UiKit.Text(condHead, $"{_modulePage + 1}/{pages}", LabTheme.FontSmall, LabTheme.TextPrimary, align: TextAlignmentOptions.Center);
                UiKit.Size(pt, w: 60);
                var next = UiKit.Button(condHead, "▶", () => { _modulePage++; Refresh(); }, ButtonStyle.Secondary, 56, 48);
                UiKit.SetInteractable(next, _modulePage < pages - 1);
            }

            // 5) 環境モジュールの行（1行＝1モジュール、値ごとに −／＋）
            if (mods.Count == 0) Para(_body, "環境条件なし（その場で装置の性能だけを測ります）", LabTheme.FontSmall, h: 56);
            foreach (var m in mods.Skip(_modulePage * ModulesPerPage).Take(ModulesPerPage)) ModuleRow(_body, m, plan, legacy);

            var sp = UiKit.Rect("Sp", _body); UiKit.Size(sp, flexH: 1);
            // 6) シード・係数
            string seedNote = legacy
                ? (cfg.Environment == LabEnvironments.ObstacleCourse ? "同じシード＝同じ障害物の配置" : cfg.Environment == LabEnvironments.FlightDeck ? "同じシード＝同じ突風" : "同じシード＝同じ雨粒の並び")
                : "同じシード＝同じ配置・同じ結果";
            var misc = UiKit.Row("Misc", _body, LabTheme.Space2, 52);
            var dur = UiKit.Text(misc, (legacy && cfg.Environment == LabEnvironments.RainLab ? $"実験時間 {cfg.DurationSec:0} 秒（固定）　｜　" : "") + $"乱数シード {cfg.Seed}（{seedNote}）",
                LabTheme.FontSmall, LabTheme.TextSecondary, align: TextAlignmentOptions.MidlineLeft);
            UiKit.Size(dur, flexW: 1);
            if (cfg.Mode == LabModes.Reality && legacy) UiKit.Button(misc, "前提係数と出典", App.ShowCoefficients, ButtonStyle.Secondary, 240, 52, LabTheme.FontSmall);
            UiKit.Button(misc, "シード変更", App.NewSeed, ButtonStyle.Ghost, 200, 52, LabTheme.FontSmall);
            // 7) 操作
            var a = UiKit.Row("Actions", _body, LabTheme.Space2, 68);
            UiKit.Button(a, "編集に戻る", App.BackToEditing, ButtonStyle.Ghost, 260);
            var sp2 = UiKit.Rect("Sp2", a); UiKit.Size(sp2, flexW: 1);
            if (errors.Count > 0)
            {
                var et = UiKit.Text(a, "× 実行できません：" + errors[0], LabTheme.FontSmall, LabTheme.StateError, align: TextAlignmentOptions.MidlineRight);
                UiKit.Size(et, w: 620);
            }
            var start = UiKit.Button(a, "実験開始 ▶", App.StartExperiment, ButtonStyle.Primary, 360, 68);
            UiKit.SetInteractable(start, errors.Count == 0);

            if (legacy)
            {
                if (cfg.Environment != LabEnvironments.RainLab) { CourseSidePanels(cfg); return; }
                RainSidePanels(cfg);
                return;
            }
            PlanSidePanels(plan, dna, removed);
        }

        static string Short(string s, int max) => string.IsNullOrEmpty(s) ? "" : s.Length > max ? s.Substring(0, max - 1) + "…" : s;

        void ModeToggle(Transform parent, string mode, string label, string caption)
        {
            bool sel = App.TestConfig.Mode == mode;
            var b = UiKit.Button(parent, $"{label}{(sel ? " ✓" : "")}\n<size=72%>{caption}</size>", () => App.SetMode(mode), sel ? ButtonStyle.Selected : ButtonStyle.Secondary, 200, 60, LabTheme.FontSmall);
            UiKit.Size(b, h: 60);
        }

        void ModuleRow(Transform parent, PlanModule m, ExperimentPlan plan, bool legacy)
        {
            var info = ExperimentCatalog.Module(m.Type);
            if (info == null) return;
            var row = UiKit.Row("Mod_" + m.Type, parent, LabTheme.Space1, 60);
            var bg = row.gameObject.AddComponent<UnityEngine.UI.Image>(); bg.color = LabTheme.Row; bg.raycastTarget = false;
            row.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>().padding = new RectOffset(12, 8, 2, 2);
            var name = UiKit.Text(row, info.LabelJa, LabTheme.FontBody, LabTheme.TextPrimary, bold: true, align: TextAlignmentOptions.MidlineLeft);
            UiKit.Size(name, w: 130);
            foreach (var p in info.Params.Where(x => m.Params.ContainsKey(x.Key)))
            {
                float v = m.Params[p.Key];
                string value = m.Type == "wind" && p.Key == "directionDeg" ? DirJa(v) : PlanText.FormatParam(p, v);
                string mod = m.Type, key = p.Key;
                var cell = UiKit.Row("St_" + p.Key, row, 4, 56); UiKit.Size(cell, w: 372);
                var l = UiKit.Text(cell, p.LabelJa, LabTheme.FontSmall, LabTheme.TextSecondary, align: TextAlignmentOptions.MidlineLeft); UiKit.Size(l, w: 140);
                UiKit.Button(cell, "−", () => App.StepPlanParam(mod, key, -1), ButtonStyle.Secondary, 52, 52);
                var vt = UiKit.Text(cell, value, LabTheme.FontSmall, LabTheme.AccentLight, bold: true, align: TextAlignmentOptions.Center); UiKit.Size(vt, w: 116);
                UiKit.Button(cell, "＋", () => App.StepPlanParam(mod, key, +1), ButtonStyle.Secondary, 52, 52);
            }
            var rowFill = UiKit.Rect("Fill", row); UiKit.Size(rowFill, flexW: 1);
            if (!legacy)
            {
                string type = m.Type;
                var rm = UiKit.Button(row, PlanEditor.CanRemove(App.EditPlan, type) ? "外す" : "必須", () => App.RemovePlanModule(type), ButtonStyle.Ghost, 96, 52, LabTheme.FontSmall);
                UiKit.SetInteractable(rm, PlanEditor.CanRemove(App.EditPlan, type));
            }
        }

        /// <summary>雨の実験の設定の側面：このモードの前提と、適用される制限・推定値。</summary>
        void RainSidePanels(RainExperimentConfig cfg)
        {
            SideHeading(_leftBody, "MODEL", "このモードの前提");
            var e = ModeRules.Evaluate(App.Current.Current, cfg.Clamped());
            foreach (var s in e.Assumptions) Para(_leftBody, "・" + s, LabTheme.FontSmall, LabTheme.TextPrimary, 66);
            SideHeading(_rightBody, "LIMITS", "適用される制限");
            if (e.Limits.Count == 0) Para(_rightBody, cfg.Mode == LabModes.Dream ? "DREAM MODE では制限を適用しません" : "現在の設計は REALITY の上限内です", LabTheme.FontBody, LabTheme.TextPrimary);
            foreach (var s in e.Limits) Para(_rightBody, "⚠ " + s, LabTheme.FontSmall, LabTheme.AccentWarning, 70);
            if (cfg.Mode == LabModes.Reality && e.Nominal.HasValue)
            {
                var n = e.Nominal.Value; var lo = e.Pessimistic.Value; var hi = e.Optimistic.Value;
                UiKit.Divider(_rightBody);
                Para(_rightBody, "<b>推定値（中央値と仮定の幅）</b>", LabTheme.FontSmall, LabTheme.TextPrimary, 30);
                Para(_rightBody, $"消費電力 約 {n.PowerW:0} W（{hi.PowerW:0}〜{lo.PowerW:0} W）", LabTheme.FontSmall, LabTheme.TextPrimary, 30);
                Para(_rightBody, $"稼働時間 約 {n.RuntimeMinutes:0} 分（{lo.RuntimeMinutes:0}〜{hi.RuntimeMinutes:0} 分）", LabTheme.FontSmall, LabTheme.TextPrimary, 30);
                Para(_rightBody, $"総重量 約 {n.MassKg:0.00} kg ／ 風荷重 {n.WindForceN:0.0} N ／ 傾き {n.TiltDeg:0}°", LabTheme.FontSmall, LabTheme.TextPrimary, 30);
            }
        }


        /// <summary>
        /// 組み立て式の実験の側面：左＝シナリオと計算の前提、右＝計測項目と合格条件・事前に分かる制限。
        /// 実行前に同じ計算（ScenarioRunner）を走らせて表示する（計算は一括・数ミリ秒）。
        /// </summary>
        string _previewKey;
        CourseRun _preview;

        void PlanSidePanels(ExperimentPlan plan, InventionDna dna, List<string> removed)
        {
            CourseRun preview = null;
            string fail = null;
            var errors = new List<string>();
            ExperimentPlanJson.Validate(plan, dna, errors);
            // 事前計算は、発明の版と計画（モード・合格条件を含む）が同じなら使い回す（ボタンを押すたびに計算しない）
            string key = dna.Id + "#" + dna.Revision + "#" + ExperimentPlanJson.ToJson(plan);
            if (errors.Count == 0 && key == _previewKey) preview = _preview;
            else if (errors.Count == 0)
            {
                try { preview = ScenarioRunner.RunPlan(dna, plan); _preview = preview; _previewKey = key; }
                catch (System.ArgumentException e) { fail = e.Message; }
            }
            else fail = errors[0];

            SideHeading(_leftBody, "MODEL", "この実験の前提");
            Para(_leftBody, ExperimentCatalog.Scenario(plan.Scenario)?.Description ?? plan.Scenario, LabTheme.FontSmall, LabTheme.TextPrimary, 60);
            if (preview != null) CappedLines(_leftBody, preview.Result.Assumptions.Select(x => ("・" + x, LabTheme.FontSmall, LabTheme.TextPrimary, 60f)).ToList(), 4);
            else Para(_leftBody, "⚠ " + fail, LabTheme.FontSmall, LabTheme.StateError, 60);

            SideHeading(_rightBody, "CRITERIA", "計測項目と合格条件");
            var metrics = PlanEditor.ExpectedMetrics(plan, dna);
            Para(_rightBody, "<b>測る</b>：" + (metrics.Count > 0 ? string.Join("・", metrics.Where(x => x.Primary).Select(x => x.LabelJa)) : "（この条件で測れる計測項目がありません）"),
                LabTheme.FontSmall, LabTheme.TextPrimary, 60);
            var crit = PlanEditor.EffectiveCriteria(plan, dna);
            string src = plan.Criteria.Count > 0 ? (App.SelectedPlan?.FromAi == true ? "AI案の条件" : "計画の条件") : "目安（調整用・要出典）";
            Para(_rightBody, $"<b>合格条件</b>（{src}）", LabTheme.FontSmall, LabTheme.TextPrimary, 30);
            if (crit.Count == 0) Para(_rightBody, "なし：測れる計測項目に目安がありません（結果は「合否なし」）", LabTheme.FontSmall, LabTheme.AccentWarning, 30);
            foreach (var c in crit.Take(3))
                Para(_rightBody, "・" + PlanText.Criterion(c) + (string.IsNullOrEmpty(c.Reason) ? "" : $"<size=85%><color=#{LabTheme.ToHex(LabTheme.TextSecondary)}>（{c.Reason}）</color></size>"), LabTheme.FontSmall, LabTheme.AccentLight, 30);
            foreach (var r in removed.Take(2)) Para(_rightBody, "⚠ 外した計測：" + r, LabTheme.FontSmall, LabTheme.AccentWarning, 30);
            if (preview != null)
            {
                var limits = preview.Result.Limits.Where(l => !l.StartsWith("合格条件", System.StringComparison.Ordinal)).Take(2).ToList();
                foreach (var l in limits) Para(_rightBody, "⚠ " + l, LabTheme.FontSmall, LabTheme.AccentWarning, 30);
            }
            if (App.PlanRejected.Count > 0) Para(_rightBody, $"使えなかった AI 案 {App.PlanRejected.Count} 件：{App.PlanRejected[0]}", LabTheme.FontSmall, LabTheme.TextSecondary, 30);
        }

        /// <summary>障害物コース・飛行デッキの実験設定の側面：計算の前提と、実行前に分かる制限（同じ計算を先に実行して表示）。</summary>
        void CourseSidePanels(RainExperimentConfig cfg)
        {
            var preview = CourseSimulator.Run(App.Current.Current, cfg).Result;
            SideHeading(_leftBody, "MODEL", "このモードの前提");
            CappedLines(_leftBody, preview.Assumptions.Select(a => ("・" + a, LabTheme.FontSmall, LabTheme.TextPrimary, 92f)).ToList(), 5);
            SideHeading(_rightBody, "LIMITS", "適用される制限");
            var limits = preview.Limits.Where(l => !l.Contains("秒で電池切れ")).ToList();
            if (limits.Count == 0) Para(_rightBody, cfg.Mode == LabModes.Dream ? "DREAM MODE では電力・速度の制限を適用しません" : "現在の設計で事前に分かる制限はありません", LabTheme.FontBody, LabTheme.TextPrimary);
            foreach (var l in limits.Take(3)) Para(_rightBody, "⚠ " + l, LabTheme.FontSmall, LabTheme.AccentWarning, 70);
            if (cfg.Environment == LabEnvironments.FlightDeck && cfg.Mode == LabModes.Reality)
            {
                var fp = CourseSimulator.EstimateFlightPower(App.Current.Current);
                if (fp.HasValue)
                {
                    UiKit.Divider(_rightBody);
                    Para(_rightBody, "<b>推定値（中央値）</b>", LabTheme.FontSmall, LabTheme.TextPrimary, 30);
                    Para(_rightBody, $"人を浮かせる必要電力 約 {fp.Value.RequiredKW:0} kW", LabTheme.FontSmall, LabTheme.TextPrimary, 30);
                    Para(_rightBody, $"靴の電池で浮いていられる時間 約 {fp.Value.AvailableSec:0.0} 秒", LabTheme.FontSmall, LabTheme.TextPrimary, 30);
                }
            }
            Para(_rightBody, "合格条件：" + preview.Criteria, LabTheme.FontSmall, LabTheme.AccentLight, 64);
        }

        // REALITY 前提係数の一覧（値・幅・確度・出典）。ページ送りで表示する
        void BuildCoefficientTable()
        {
            var model = RealityModel.Default;
            int page = Mathf.Clamp(App.CoefficientPage, 0, Mathf.Max(0, App.CoefficientPageCount - 1));
            Title("REALITY 前提係数と出典", $"{page + 1} / {App.CoefficientPageCount} ページ ／ 幅は各係数の下限〜上限で計算");
            string sub = LabTheme.ToHex(LabTheme.TextSecondary);
            foreach (var c in model.Coefficients.Skip(page * LabApp.CoefficientsPerPage).Take(LabApp.CoefficientsPerPage))
            {
                var row = UiKit.Row("Coef_" + c.Id, _body, LabTheme.Space2, 66);
                var bg = row.gameObject.AddComponent<UnityEngine.UI.Image>(); bg.color = LabTheme.Row; bg.raycastTarget = false;
                row.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>().padding = new RectOffset(12, 12, 2, 2);
                string range = Mathf.Approximately(c.Min, c.Max) ? "固定" : c.FormatValue(c.Min) + "〜" + c.FormatValue(c.Max);
                var l = UiKit.Text(row, $"<b>{c.LabelJa}</b>　{c.FormatValue(c.Value)}　<color=#{sub}>（{range}）</color>\n<size=82%><color=#{sub}>{c.Source}</color></size>",
                    LabTheme.FontSmall, LabTheme.TextPrimary, align: TextAlignmentOptions.MidlineLeft);
                UiKit.Size(l, flexW: 1);
                Color badge = c.Status == "standard" ? LabTheme.AccentLight
                    : c.Status == "tuning" || c.Status == "design" ? LabTheme.TextSecondary : LabTheme.AccentWarning;
                UiKit.Badge(row, model.StatusLabel(c.Status), badge, LabTheme.BgDeep, 250);
            }
            var sp = UiKit.Rect("Sp", _body); UiKit.Size(sp, flexH: 1);
            var a = UiKit.Row("Actions", _body, LabTheme.Space2, 68);
            UiKit.Button(a, "設定に戻る", App.HideCoefficients, ButtonStyle.Ghost, 260);
            var sp2 = UiKit.Rect("Sp2", a); UiKit.Size(sp2, flexW: 1);
            var prev = UiKit.Button(a, "◀ 前へ", () => App.StepCoefficientPage(-1), ButtonStyle.Secondary, 200); UiKit.SetInteractable(prev, page > 0);
            var next = UiKit.Button(a, "次へ ▶", () => App.StepCoefficientPage(+1), ButtonStyle.Secondary, 200); UiKit.SetInteractable(next, page < App.CoefficientPageCount - 1);

            SideHeading(_leftBody, "MODEL", "計算式");
            Para(_leftBody, "・総重量 = 本体 + キャノピー面積×面密度 + 側面カバー面積×面密度 + 電池", LabTheme.FontSmall, LabTheme.TextPrimary, 60);
            Para(_leftBody, "・風荷重 F = ½ρ Cd A v²（A：横から見た面積）", LabTheme.FontSmall, LabTheme.TextPrimary, 60);
            Para(_leftBody, "・必要推力 T = √(重さ² + F²)", LabTheme.FontSmall, LabTheme.TextPrimary, 30);
            Para(_leftBody, "・ホバー電力 = T^1.5 / √(2ρA) ÷ (FM × 駆動効率) + 制御電力（運動量理論）", LabTheme.FontSmall, LabTheme.TextPrimary, 60);
            Para(_leftBody, "・稼働時間 = 電池容量 ÷ 消費電力", LabTheme.FontSmall, LabTheme.TextPrimary, 30);
            Para(_leftBody, "風の中で誘導電力が下がる効果は無視しており、電力は多めに見積もっています。", LabTheme.FontSmall, h: 60);
            SideHeading(_rightBody, "STATUS", "確度の内訳");
            foreach (var st in new[] { "standard", "literature", "design", "estimate", "tuning" })
                Para(_rightBody, $"・{model.StatusLabel(st)}：{model.CountStatus(st)} 件", LabTheme.FontSmall, LabTheme.TextPrimary, 30);
            Para(_rightBody, "「要調査」「要確認」の値は、文献や市販品の公開仕様で確かめてから確定します。詳細は docs/reality_model.md。", LabTheme.FontSmall, LabTheme.AccentWarning, 90);
        }

        static string DirJa(float deg)
        {
            string[] n = { "前へ", "右前へ", "右へ", "右後ろへ", "後ろへ", "左後ろへ", "左へ", "左前へ" };
            return $"{n[Mathf.RoundToInt(deg / 45f) % 8]}（{deg:0}°）";
        }

        // ---------------- S05 実験結果・AI改良 ----------------
        void BuildS05()
        {
            var cfg = App.TestConfig.Clamped();
            if (App.Sm.State == LabState.RunningTest)
            {
                var rp = App.RunningPlan;
                bool lab = rp != null;
                Title("実験中 — " + (lab ? PlanCandidates.ScenarioLabel(rp.Scenario) : LabEnvironments.LabelJa(cfg.Environment)), LabModes.Label(cfg.Mode) + "（" + LabModes.Caption(cfg.Mode) + "）");
                Para(_body, lab ? PlanText.Describe(rp) : cfg.Describe(), LabTheme.FontSmall, h: 32);
                _liveText = UiKit.Text(_body, "", LabTheme.FontHeading, LabTheme.TextPrimary);
                UiKit.Size(_liveText, flexH: 1);
                var a = UiKit.Row("Actions", _body, LabTheme.Space2, 68);
                UiKit.Button(a, "■ 停止（記録しない）", App.StopExperiment, ButtonStyle.Danger, 360);
                BuildMethodPanel(cfg, lab && App.Course?.Run != null && App.Course.Run.Result.IsScenarioLab ? App.Course.Run.Result : null);
                BuildBridgePanel();
                UpdateLive();
                return;
            }
            var r = App.LastResult;
            if (r == null) return;
            if (!r.IsRain) { BuildCourseResult(r, cfg); return; }
            bool ok = r.Success;
            Title("実験結果", $"Rev.{r.Revision} ／ {r.Config.Describe()}");
            var verdictRow = UiKit.Row("Verdict", _body, LabTheme.Space2, 60);
            var vb = UiKit.Badge(verdictRow, ok ? "✓ 防護成功（防護率80%以上）" : "! 要改良（防護率80%未満）", ok ? LabTheme.AccentLight : LabTheme.AccentWarning, LabTheme.BgDeep, 460);
            UiKit.Size(vb.transform.parent.GetComponent<RectTransform>(), h: 56);
            var mm = UiKit.Text(verdictRow, $"被濡れスコア <b>{r.WetnessScore:0.0}</b> 粒/秒（低いほどよい）　防護率 <b>{r.CoverageRatio * 100f:0.0}%</b>", LabTheme.FontHeading, align: TextAlignmentOptions.MidlineLeft);
            UiKit.Size(mm, flexW: 1);

            var cols = UiKit.Row("Cols", _body, LabTheme.Space3); UiKit.Size(cols, flexH: 1);
            var c1 = UiKit.Column("Metrics", cols, 4); UiKit.Size(c1, flexW: 1);
            UiKit.Heading(c1, "計測値（Unityシミュレーションの実測）", 24);
            Metric(c1, "雨粒総数 ／ 装置なしで身体に当たる数", $"{r.DropsTotal} ／ {r.BaselineHits}");
            Metric(c1, "装置ありで身体に当たった数", $"{r.DeviceHits}（頭 {r.HeadHits}・上半身 {r.UpperHits}・下半身 {r.LowerHits}）");
            Metric(c1, "キャノピーで遮断", r.Blocked.ToString());
            if (r.EnergyWh.HasValue)
            {
                string range = r.RuntimeMinutesMin.HasValue && r.RuntimeMinutesMax.HasValue ? $"（{r.RuntimeMinutesMin.Value:0}〜{r.RuntimeMinutesMax.Value:0} 分）" : "";
                Metric(c1, "消費エネルギー ／ 推定稼働", $"{r.EnergyWh.Value:0.000} Wh ／ 約 {r.RuntimeMinutes ?? 0:0} 分{range}");
            }
            else Metric(c1, "消費エネルギー", "— （DREAMでは考慮しない）");
            Metric(c1, "風による装置のずれ", r.CanHoldPosition ? $"{r.DriftM:0.00} m" : $"{r.DriftM:0.00} m（位置を保てない）");

            var c2 = UiKit.Column("Compare", cols, 4); UiKit.Size(c2, flexW: 1);
            UiKit.Heading(c2, "同一条件での比較", 24);
            if (App.BaselineResult != null)
            {
                Para(c2, $"Rev.{App.BaselineResult.Revision}（改良前） → Rev.{r.Revision}（今回）", LabTheme.FontSmall, h: 30);
                foreach (var m in ExperimentComparer.Compare(App.BaselineResult, r).Take(4)) CompareRow(c2, m);
            }
            else if (App.OtherModeResult != null)
            {
                var dream = App.OtherModeResult.Config.Mode == LabModes.Dream ? App.OtherModeResult : r;
                var real = dream == r ? App.OtherModeResult : r;
                Para(c2, "DREAM → REALITY（同じ版・同じ条件。計算式が異なります）", LabTheme.FontSmall, h: 30);
                foreach (var m in ExperimentComparer.Compare(dream, real).Take(4)) CompareRow(c2, m);
            }
            else Para(c2, "比較対象なし：同じ条件の別の版、または別モードの結果がまだありません。改良して再実験するか、モードを切り替えてください。", LabTheme.FontSmall, h: 100);
            UiKit.Heading(c2, "改善案（計測値からの規則）", 24);
            foreach (var h in App.ImprovementHints(r).Take(2)) Para(c2, "・" + Short(h, 64), LabTheme.FontSmall, LabTheme.TextPrimary, 30);

            var a2 = UiKit.Row("Actions", _body, LabTheme.Space1, 68);
            UiKit.Button(a2, "同じ条件で再実験", App.RerunSameConditions, ButtonStyle.Secondary, 280);
            UiKit.Button(a2, cfg.Mode == LabModes.Dream ? "REALITYで比較" : "DREAMで比較", App.RerunOtherMode, ButtonStyle.Secondary, 260);
            UiKit.Button(a2, "保存して終了", App.Save, ButtonStyle.Ghost, 240);
            var sp = UiKit.Rect("Sp", a2); UiKit.Size(sp, flexW: 1);
            UiKit.Button(a2, "改良して再実験 ▶", App.ImproveAndRetest, ButtonStyle.Primary, 340, 68);
            BuildMethodPanel(r.Config, r);
            BuildBridgePanel();
        }

        /// <summary>障害物コース・飛行デッキの結果（指標は CourseSimulator が計算した値のみ）。</summary>
        void BuildCourseResult(RainExperimentResult r, RainExperimentConfig cfg)
        {
            bool ok = r.Success;
            Title("実験結果", $"Rev.{r.Revision} ／ {r.DescribeConditions()}");
            var checks = ResultCriteria.Evaluate(r);
            int passedN = checks.Count(c => c.Passed);
            var verdictRow = UiKit.Row("Verdict", _body, LabTheme.Space2, 60);
            var vb = UiKit.Badge(verdictRow, ok ? "✓ 成功" : "! 要改良", ok ? LabTheme.AccentLight : LabTheme.AccentWarning, LabTheme.BgDeep, 220);
            UiKit.Size(vb.transform.parent.GetComponent<RectTransform>(), h: 56);
            // 合格条件はすべて満たしたのに不合格（積荷を運べない・電池切れなど、モデルが決める条件）のときは、その理由を出す
            string why = checks.Count == 0 ? "合格条件なし（測れる計測項目に目安がない）"
                : ok ? $"合格条件 {checks.Count} 件をすべて満たしました"
                : passedN < checks.Count ? $"合格条件 {passedN} / {checks.Count} 件を満たしました"
                : "合格条件は満たしましたが、" + (r.Limits.FirstOrDefault() ?? "ほかの条件を満たしません");
            var crit = UiKit.Text(verdictRow, why, LabTheme.FontBody, ok ? LabTheme.TextPrimary : LabTheme.AccentWarning, align: TextAlignmentOptions.MidlineLeft);
            UiKit.Size(crit, flexW: 1);

            var cols = UiKit.Row("Cols", _body, LabTheme.Space3); UiKit.Size(cols, flexH: 1);
            cols.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            var c1 = UiKit.Column("Metrics", cols, 4); UiKit.Size(c1, w: 700); // 固定幅（右列の長い文に押されて狭くならないように）
            UiKit.Heading(c1, "合格条件ごとの判定" + (r.IsScenarioLab ? "（簡易モデルの計算）" : "（シミュレーションの実測）"), 24);
            foreach (var c in checks.Take(4)) CriterionRow(c1, c);
            // 合格条件にない計測値（主なもの）を残りの行に
            var judged = new HashSet<string>(checks.Select(c => c.Metric));
            var others = r.Metrics.Where(m => !judged.Contains(m.Key)).OrderBy(m => m.Primary ? 0 : 1).ToList();
            int room = checks.Count <= 2 ? 3 : 1; // 判定の行（52px）が多いときは、残りの計測値を減らしてパネル内に収める
            if (others.Count > 0)
            {
                UiKit.Heading(c1, "そのほかの計測値", 22);
                foreach (var m in others.Take(room)) Metric(c1, (m.Primary ? "" : "参考：") + m.LabelJa, m.Display, 200);
            }

            var c2 = UiKit.Column("Compare", cols, 4); UiKit.Size(c2, flexW: 1);
            UiKit.Heading(c2, "同一条件での比較", 24);
            if (App.BaselineResult != null)
            {
                Para(c2, $"Rev.{App.BaselineResult.Revision}（改良前） → Rev.{r.Revision}（今回）", LabTheme.FontSmall, h: 30);
                foreach (var m in ExperimentComparer.Compare(App.BaselineResult, r).Take(4)) CompareRow(c2, m);
            }
            else if (App.OtherModeResult != null)
            {
                var dream = App.OtherModeResult.Config.Mode == LabModes.Dream ? App.OtherModeResult : r;
                var real = dream == r ? App.OtherModeResult : r;
                Para(c2, "DREAM → REALITY（同じ版・同じ条件。計算式が異なります）", LabTheme.FontSmall, h: 30);
                foreach (var m in ExperimentComparer.Compare(dream, real).Take(4)) CompareRow(c2, m);
            }
            else Para(c2, "比較対象なし：同じ条件の別の版、または別モードの結果がまだありません。改良して再実験するか、モードを切り替えてください。", LabTheme.FontSmall, h: 100);
            UiKit.Heading(c2, "改善案（計測値からの規則）", 24);
            foreach (var h in App.ImprovementHints(r).Take(2)) Para(c2, "・" + Short(h, 64), LabTheme.FontSmall, LabTheme.TextPrimary, 30);

            var a2 = UiKit.Row("Actions", _body, LabTheme.Space1, 68);
            UiKit.Button(a2, "同じ条件で再実験", App.RerunSameConditions, ButtonStyle.Secondary, 280);
            UiKit.Button(a2, cfg.Mode == LabModes.Dream ? "REALITYで比較" : "DREAMで比較", App.RerunOtherMode, ButtonStyle.Secondary, 260);
            UiKit.Button(a2, "保存して終了", App.Save, ButtonStyle.Ghost, 240);
            var sp = UiKit.Rect("Sp", a2); UiKit.Size(sp, flexW: 1);
            UiKit.Button(a2, "改良して再実験 ▶", App.ImproveAndRetest, ButtonStyle.Primary, 340, 68);
            BuildMethodPanel(r.Config, r);
            BuildBridgePanel();
        }

        /// <summary>合格条件1件：達成／未達／測れない を記号・色・文字で区別し、目標と実測を並べる。</summary>
        void CriterionRow(Transform parent, CriterionCheck c)
        {
            var row = UiKit.Row("Crit_" + c.Metric, parent, LabTheme.Space1, 52);
            var bg = row.gameObject.AddComponent<UnityEngine.UI.Image>(); bg.color = LabTheme.Row; bg.raycastTarget = false;
            row.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>().padding = new RectOffset(6, 10, 2, 2);
            string label = !c.Measured ? "— 測れず" : c.Passed ? "✓ 達成" : "× 未達";
            Color col = !c.Measured ? LabTheme.TextSecondary : c.Passed ? LabTheme.AccentLight : LabTheme.AccentWarning;
            var badge = UiKit.Badge(row, label, col, LabTheme.BgDeep, 116);
            UiKit.Size(badge.transform.parent.GetComponent<RectTransform>(), h: 44);
            string sub = LabTheme.ToHex(LabTheme.TextSecondary);
            var t = UiKit.Text(row, c.Target + (c.Reason.Length > 0 ? $"\n<size=80%><color=#{sub}>{Short(c.Reason, 30)}</color></size>" : ""), LabTheme.FontSmall, LabTheme.TextPrimary, align: TextAlignmentOptions.MidlineLeft);
            UiKit.Size(t, flexW: 1);
            var v = UiKit.Text(row, c.Actual, LabTheme.FontSmall, col, bold: true, align: TextAlignmentOptions.MidlineRight);
            UiKit.Size(v, w: 150);
        }

        void Metric(Transform parent, string label, string value, float valueWidth = 400)
        {
            var row = UiKit.Row("M", parent, LabTheme.Space1, 40);
            var l = UiKit.Text(row, label, LabTheme.FontSmall, LabTheme.TextSecondary, align: TextAlignmentOptions.MidlineLeft); UiKit.Size(l, flexW: 1);
            var v = UiKit.Text(row, value, LabTheme.FontSmall, LabTheme.TextPrimary, bold: true, align: TextAlignmentOptions.MidlineRight); UiKit.Size(v, w: valueWidth);
        }

        void CompareRow(Transform parent, MetricComparison m)
        {
            var row = UiKit.Row("C", parent, LabTheme.Space1, 38);
            var l = UiKit.Text(row, m.Label, LabTheme.FontSmall, LabTheme.TextSecondary, align: TextAlignmentOptions.MidlineLeft); UiKit.Size(l, flexW: 1);
            var v = UiKit.Text(row, $"{m.Before} → {m.After}", LabTheme.FontSmall, LabTheme.TextPrimary, align: TextAlignmentOptions.MidlineRight); UiKit.Size(v, w: 220);
            Color c = m.Verdict == "改善" ? LabTheme.AccentLight : m.Verdict == "悪化" ? LabTheme.AccentWarning : LabTheme.TextSecondary;
            string icon = m.Verdict == "改善" ? "▲ " : m.Verdict == "悪化" ? "▼ " : "＝ ";
            var vv = UiKit.Text(row, icon + m.Verdict, LabTheme.FontSmall, c, bold: true, align: TextAlignmentOptions.MidlineRight); UiKit.Size(vv, w: 150);
        }

        void BuildMethodPanel(RainExperimentConfig cfg, RainExperimentResult r = null)
        {
            SideHeading(_leftBody, "METHOD", "前提・測定方法・限界");
            if (r != null && r.IsScenarioLab)
            {
                // 組み立て式の実験：計算の前提は結果に入っている（ScenarioRunner が書く）
                CappedLines(_leftBody, r.Assumptions.Select(x => ("・" + Short(x, 64), LabTheme.FontSmall, LabTheme.TextPrimary, 60f)).ToList(), 3);
                Para(_leftBody, "・計算は開始時に一括、実験場では記録を再生（シード固定で再現可能）", LabTheme.FontSmall, h: 34);
                foreach (var l in r.Limits.Take(1)) Para(_leftBody, "⚠ " + Short(l, 64), LabTheme.FontSmall, LabTheme.AccentWarning, 64);
                return;
            }
            if (cfg.Environment == LabEnvironments.ObstacleCourse)
            {
                Para(_leftBody, "・案内役が決まったルートを歩き、ロボットは追従速度・追従距離・検知距離に従って動く（0.02秒刻みの計算）", LabTheme.FontSmall, LabTheme.TextPrimary, 92);
                Para(_leftBody, "・衝突回数＝障害物の円柱に接触した回数\n・到着の遅れ＝案内役の到着から、ロボットが追従距離まで近づくまでの秒数", LabTheme.FontSmall, LabTheme.TextPrimary, 92);
                Para(_leftBody, "・ゲーム的な運動モデル。シード固定で再現可能", LabTheme.FontSmall, h: 34);
            }
            else if (cfg.Environment == LabEnvironments.FlightDeck)
            {
                Para(_leftBody, "・人型モデルが靴で離陸し、目標地点まで飛んで着地する（0.02秒刻みの計算）。横風と突風で横に揺れる", LabTheme.FontSmall, LabTheme.TextPrimary, 92);
                Para(_leftBody, "・安定度＝100×(1−横揺れのRMS÷0.4 m)\n・着地点のずれ＝目標地点の中心からの距離", LabTheme.FontSmall, LabTheme.TextPrimary, 92);
                Para(_leftBody, "・観察モード（視点は動かない）。シード固定で再現可能", LabTheme.FontSmall, h: 34);
            }
            else
            {
                Para(_leftBody, "・雨粒を直線として、キャノピー（円板＋側面カバー）と判定用円筒（半径0.22m・高さ1.75m）との衝突を計算", LabTheme.FontSmall, LabTheme.TextPrimary, 92);
                Para(_leftBody, "・被濡れスコア＝身体に当たった雨粒数÷実験秒数\n・防護率＝1−装置あり命中÷装置なし命中", LabTheme.FontSmall, LabTheme.TextPrimary, 92);
                Para(_leftBody, "・流体解析ではありません。シード固定で再現可能", LabTheme.FontSmall, h: 34);
            }
            if (r != null) foreach (var s in r.Limits.Take(2)) Para(_leftBody, "⚠ " + s, LabTheme.FontSmall, LabTheme.AccentWarning, 64);
        }

        /// <summary>
        /// 右パネル（S05）：上＝要望の検証状況（この実験で確かめた／別の実験で確かめる／検証できない）、
        /// 下＝現実化の課題（仕様：参考技術と課題、根拠と不確実性を明示）。
        /// </summary>
        void BuildBridgePanel()
        {
            var dna = App.Current?.Current;
            var res = App.Sm.State == LabState.ReviewingResult ? App.LastResult : null;
            var plan = res != null ? ResultCriteria.PlanOf(res) : App.RunningPlan ?? ExperimentPlan.FromConfig(App.TestConfig);
            SideHeading(_rightBody, "REQUESTS", "要望の検証状況");
            if (dna != null)
            {
                var rows = App.RequestRows(dna, plan, out bool derived);
                // 1グループ＝2行まで（側面パネルは1行約21文字）。理由や実験名は S02・S04 にある
                string Names(IEnumerable<FunctionRow> xs) => Short(string.Join("、", xs.Select(x => x.Need)), 28);
                var here = rows.Where(x => x.CoveredHere).ToList();
                var elsewhere = rows.Where(x => x.Verifiable && !x.CoveredHere && x.PlanTitle != null).ToList();
                var none = rows.Where(x => x.Verifiable && !x.CoveredHere && x.PlanTitle == null).ToList();
                var unv = rows.Where(x => !x.Verifiable).ToList();
                string verb = res != null ? "確かめた" : "確かめる";
                Para(_rightBody, $"✓ この実験で{verb} {here.Count}：" + (here.Count > 0 ? Names(here) : "なし"), LabTheme.FontSmall, LabTheme.AccentLight, 30);
                if (elsewhere.Count > 0) Para(_rightBody, $"→ 別の実験で {elsewhere.Count}：" + Names(elsewhere), LabTheme.FontSmall, LabTheme.TextPrimary, 30);
                if (none.Count > 0) Para(_rightBody, $"⚠ 実験の候補なし {none.Count}：" + Names(none), LabTheme.FontSmall, LabTheme.AccentWarning, 30);
                if (unv.Count > 0) Para(_rightBody, $"× 検証できない {unv.Count}：" + Names(unv), LabTheme.FontSmall, LabTheme.AccentWarning, 30);
                if (derived) Para(_rightBody, "（AI の対応表なし：機能ごと）", LabTheme.FontSmall, LabTheme.TextSecondary, 28);
            }
            UiKit.Divider(_rightBody);
            UiKit.Heading(_rightBody, "現実化の課題（推定・要調査を含む）", 24);
            string cat = dna?.Category;
            string bridge = cat == "carrier" ? "<b>近い技術</b>：追従型の運搬ロボット\n<b>課題</b>：人混み・段差の安全、歩道のルール"
                : cat == "mobility" ? "<b>課題</b>：人を浮かせる電力（推定で数十〜数百 kW）、噴流・転落の安全、法規制"
                : cat == "protection" ? "<b>近い技術</b>：ドローン・頭上追従カメラ\n<b>課題</b>：電池、プロペラの安全、騒音、強風"
                : "<b>課題</b>：試作品での計測と、係数の出典の確認";
            Para(_rightBody, bridge, LabTheme.FontSmall, LabTheme.TextPrimary, 60);
            Para(_rightBody, "※実現性・安全性は保証しません", LabTheme.FontSmall, LabTheme.AccentWarning, 30);
        }

        void Update() { if (_liveText != null) UpdateLive(); }

        // 計測：フォントへの文字の追加は作り直しの後（描画の直前）に起きるため、フレームごとに数の変化と前のフレームの時間を記録する
        int _lastGlyphs = -1;
        void LateUpdate()
        {
            int g = GlyphCount();
            if (_lastGlyphs >= 0 && g != _lastGlyphs)
                Debug.Log($"[ILAB] glyphs added={g - _lastGlyphs} total={g} prevFrameMs={Time.unscaledDeltaTime * 1000f:F0} state={App.Sm.State}");
            _lastGlyphs = g;
        }

        void UpdateLive()
        {
            if (App.TestConfig.Environment != LabEnvironments.RainLab || App.RunningPlan != null)
            {
                var c = App.Course;
                if (c == null || c.Run == null || _liveText == null) return;
                var f = c.CurrentFrame;
                string body = string.Format(CultureInfo.InvariantCulture, "経過 {0:0.0} / {1:0.0} 秒", c.Elapsed, c.Run.Duration);
                var res = c.Run.Result;
                if (f.HasValue && res.IsScenarioLab)
                {
                    var ci = CultureInfo.InvariantCulture;
                    switch (res.Scenario)
                    {
                        case ExperimentCatalog.SitStill: body += string.Format(ci, "（{0:0} 分を早送り）\n装着部の温度 {1:0.0} °C", res.Plan?.Get("temperature", "minutes") ?? 0f, f.Value.Aux); break;
                        case ExperimentCatalog.DropTest: body += string.Format(ci, "\n高さ {0:0.00} m", f.Value.Device.y) + (f.Value.Aux > 0f ? string.Format(ci, "\n床に当たった：最大の衝撃 <color=#{1}>{0:0} G</color>", f.Value.Aux, LabTheme.ToHex(LabTheme.AccentWarning)) : ""); break;
                        case ExperimentCatalog.ClimbSlope: body += string.Format(ci, "\n坂を進んだ距離 {0:0.0} / {1:0.0} m", f.Value.Aux, res.Plan?.Get("slope", "lengthM") ?? 0f); break;
                        case ExperimentCatalog.LiftTask: body += string.Format(ci, "\n荷物の高さ {0:0.00} m", f.Value.Device.y) + (f.Value.Aux > 0f ? $"\n<color=#{LabTheme.ToHex(LabTheme.AccentWarning)}>持ち上げられない・落とした</color>" : ""); break;
                        default: body += "\n計測は開始時に計算済み：" + string.Join("　", res.Metrics.Where(m => m.Primary).Take(2).Select(m => m.LabelJa + " " + m.Display)); break;
                    }
                }
                else if (f.HasValue && res.Environment == LabEnvironments.ObstacleCourse)
                {
                    float gap = new Vector2(f.Value.Leader.x - f.Value.Device.x, f.Value.Leader.z - f.Value.Device.z).magnitude;
                    body += string.Format(CultureInfo.InvariantCulture, "\n案内役との距離 {0:0.00} m\n衝突 <color=#{2}>{1}</color> 回", gap, c.CollisionsSoFar, LabTheme.ToHex(LabTheme.AccentWarning));
                }
                else if (f.HasValue)
                {
                    float dev = f.Value.Device.z - c.Run.Layout.Start.y;
                    body += string.Format(CultureInfo.InvariantCulture, "\n高度 {0:0.00} m　横の揺れ {1:+0.00;-0.00;0.00} m\n目標まで {2:0.0} m", f.Value.Device.y, dev, Mathf.Abs(c.Run.Layout.Goal.x - f.Value.Device.x));
                }
                _liveText.text = body;
                return;
            }
            var sim = App.RainLab != null ? App.RainLab.Simulator : null;
            if (sim == null || _liveText == null) return;
            var r = sim.Result;
            float cov = r.BaselineHits == 0 ? 1f : 1f - (float)r.DeviceHits / r.BaselineHits;
            _liveText.text = string.Format(CultureInfo.InvariantCulture,
                "経過 {0:0.0} / {1:0} 秒\n処理した雨粒 {2} / {3}\n身体への命中 <color=#{6}>{4}</color>　キャノピーで遮断 {5}\n防護率（途中経過） {7:0.0}%",
                App.RainLab.Elapsed, sim.Result.Config.DurationSec, r.DropsTotal, sim.TotalDrops, r.DeviceHits, r.Blocked, LabTheme.ToHex(LabTheme.AccentWarning), cov * 100f);
        }

        // ---------------- S06 保存・ギャラリー ----------------
        void BuildS06()
        {
            if (App.Sm.State == LabState.Saved)
            {
                Title("✓ 保存しました");
                Para(_body, $"「{App.Current?.Current.Name}」Rev.{App.Current?.Current.Revision}　実験記録 {App.Current?.Experiments.Count ?? 0} 件", LabTheme.FontHeading, LabTheme.TextPrimary, 50);
                Para(_body, "保存先（端末内）：" + App.LastSavedPath, LabTheme.FontSmall, h: 64);
                var sp = UiKit.Rect("Sp", _body); UiKit.Size(sp, flexH: 1);
                var a = UiKit.Row("Actions", _body, LabTheme.Space2, 68);
                UiKit.Button(a, "編集を続ける", App.ContinueEditing, ButtonStyle.Secondary, 300);
                UiKit.Button(a, "ギャラリーを見る", App.OpenGallery, ButtonStyle.Secondary, 300);
                var sp2 = UiKit.Rect("Sp2", a); UiKit.Size(sp2, flexW: 1);
                UiKit.Button(a, "新しい発明 ▶", App.NewInvention, ButtonStyle.Primary, 300, 68);
                GallerySide();
                return;
            }
            Title("発明品ギャラリー", "共有・公開はこの版では非対応");
            var entries = App.GalleryEntries;
            if (_galleryShownFor != App.GalleryOpenCount) { _galleryShownFor = App.GalleryOpenCount; _galleryPage = 0; }
            _galleryPage = Mathf.Clamp(_galleryPage, 0, GalleryPages(entries.Count) - 1);
            if (entries.Count == 0)
            {
                Para(_body, "保存された発明はまだありません。発明して「保存」すると、ここに表示されます。", LabTheme.FontBody, LabTheme.TextPrimary, 80);
                var spE = UiKit.Rect("Sp", _body); UiKit.Size(spE, flexH: 1);
            }
            else
            {
                var gridRt = UiKit.Rect("Grid", _body); UiKit.Size(gridRt, flexH: 1);
                var grid = gridRt.gameObject.AddComponent<UnityEngine.UI.GridLayoutGroup>();
                grid.cellSize = new Vector2(472, 200); // 2段でもフッターがはみ出さない高さ（カードの中身は約 190）
                grid.spacing = new Vector2(16, 16);
                foreach (var e in entries.Skip(_galleryPage * GalleryPerPage).Take(GalleryPerPage)) GalleryCard(gridRt, e);
            }
            var a2 = UiKit.Row("Actions", _body, LabTheme.Space2, 64);
            UiKit.Button(a2, "戻る", App.CloseGallery, ButtonStyle.Ghost, 220);
            // 7件以上はページ送り（新しい順に6件ずつ）。操作の行に置く（別の行にするとフッターがパネルからはみ出す）
            int gPages = GalleryPages(entries.Count);
            if (gPages > 1)
            {
                var fill = UiKit.Rect("Fill", a2); UiKit.Size(fill, flexW: 1);
                var prev = UiKit.Button(a2, "◀ 新しい", () => StepGalleryPage(-1), ButtonStyle.Secondary, 190, 60, LabTheme.FontSmall);
                UiKit.SetInteractable(prev, _galleryPage > 0);
                var pt = UiKit.Text(a2, $"{_galleryPage + 1} / {gPages}（全 {entries.Count} 件）", LabTheme.FontSmall, LabTheme.TextPrimary, align: TextAlignmentOptions.Center);
                UiKit.Size(pt, w: 260);
                var next = UiKit.Button(a2, "古い ▶", () => StepGalleryPage(+1), ButtonStyle.Secondary, 190, 60, LabTheme.FontSmall);
                UiKit.SetInteractable(next, _galleryPage < gPages - 1);
            }
            GallerySide();
            if (App.PendingDeleteId != null) BuildDeleteDialog();
        }

        // ---- ギャラリーのページ（新しい順に6件ずつ）----
        public const int GalleryPerPage = 6;
        int _galleryPage, _galleryShownFor = -1;
        public int GalleryPage => _galleryPage;
        static int GalleryPages(int count) => Mathf.Max(1, (count + GalleryPerPage - 1) / GalleryPerPage);
        public void StepGalleryPage(int dir)
        {
            _galleryPage = Mathf.Clamp(_galleryPage + dir, 0, GalleryPages(App.GalleryEntries.Count) - 1);
            Refresh();
        }

        void GalleryCard(Transform parent, GalleryEntry e)
        {
            var card = UiKit.Panel("Card_" + e.Id, parent, LabTheme.BgPanel);
            UiKit.VList(card, 6, LabTheme.Space2);
            // 名前と説明は1行ずつに収める（折り返すと下の行と重なる）。更新日時は年を省く
            Para(card, $"<b><color=#{LabTheme.ToHex(LabTheme.TextPrimary)}>{Short(e.Name, 16)}</color></b>", LabTheme.FontBody, h: 36);
            string when = e.UpdatedAt != null && e.UpdatedAt.Length >= 16 ? e.UpdatedAt.Substring(5) : e.UpdatedAt;
            Para(card, $"{Short(e.TagsJa, 8)} ／ Rev.{e.Revision} ／ {when}", LabTheme.FontSmall, h: 30);
            Para(card, e.Tested ? "✓ 実験済み" : "未実験", LabTheme.FontSmall, e.Tested ? LabTheme.AccentLight : LabTheme.TextSecondary, 30);
            var row = UiKit.Row("Btns", card, LabTheme.Space1, 56);
            string id = e.Id;
            UiKit.Button(row, "読込", () => App.LoadFromGallery(id), ButtonStyle.Primary, -1, 56, LabTheme.FontSmall);
            UiKit.Button(row, "複製", () => App.Duplicate(id), ButtonStyle.Secondary, -1, 56, LabTheme.FontSmall);
            UiKit.Button(row, "削除", () => App.AskDelete(id), ButtonStyle.Ghost, -1, 56, LabTheme.FontSmall);
        }

        void GallerySide()
        {
            SideHeading(_leftBody, "INVENTION GALLERY", "発明品展示室");
            Para(_leftBody, "端末内に保存した発明DNA（設計・改良履歴・実験記録）を読み込み、続きから改良できます。", LabTheme.FontBody, LabTheme.TextPrimary, 160);
            SideHeading(_rightBody, "DATA", "保存される内容");
            Para(_rightBody, "・発明DNA（部品・機能・パラメータ）\n・改良履歴（最大30版）\n・実験記録（条件・シード・計測値）\n・音声や入力した文章は実験記録に保存しません", LabTheme.FontSmall, LabTheme.TextPrimary, 200);
        }

        void BuildDeleteDialog()
        {
            _overlay = UiKit.Panel("ConfirmOverlay", _main.transform, new Color(0, 0, 0, 0.7f), border: false);
            UiKit.Stretch(_overlay);
            var dlg = UiKit.Panel("Dialog", _overlay, LabTheme.BgPanel);
            dlg.anchorMin = dlg.anchorMax = new Vector2(0.5f, 0.5f); dlg.sizeDelta = new Vector2(760, 300);
            UiKit.VList(dlg, LabTheme.Space2, LabTheme.Space4);
            var name = App.GalleryEntries.FirstOrDefault(x => x.Id == App.PendingDeleteId)?.Name ?? App.PendingDeleteId;
            Para(dlg, $"「{name}」を削除しますか？", LabTheme.FontHeading, LabTheme.TextPrimary, 50);
            Para(dlg, "改良履歴と実験記録も削除され、元に戻せません。", LabTheme.FontBody, LabTheme.AccentWarning, 40);
            var row = UiKit.Row("Btns", dlg, LabTheme.Space2, 68);
            UiKit.Button(row, "キャンセル", App.CancelDelete, ButtonStyle.Secondary, -1, 68);
            UiKit.Button(row, "削除する", App.ConfirmDelete, ButtonStyle.Danger, -1, 68);
        }
    }

    public static class LabAppText
    {
        public const string RainRequest = "手を使わず雨を防ぐ装置を作って";
    }
}
