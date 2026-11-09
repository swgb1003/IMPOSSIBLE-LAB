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
            sb.Append('|').Append(App.InputWarning).Append('|').Append(App.Current?.Current.Revision).Append('|').Append(App.Current?.Current.Id)
              .Append('|').Append(App.PendingRevision != null).Append(App.RevisionBusy).Append(App.RevisionMessage)
              .Append('|').Append(App.TestConfig.Describe()).Append('|').Append(App.PendingDeleteId).Append('|').Append(App.GalleryEntries.Count)
              .Append('|').Append(App.DeviceRunning).Append(App.InRainLab).Append(App.Seated).Append('|').Append(App.LastResult?.ExperimentId)
              .Append('|').Append(string.Join(",", App.Stages.Values)).Append('|').Append(App.Current?.History.Count).Append('|').Append(App.CoefficientPage).Append('|').Append(_paramPage);
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
                default: return "状態：" + StateJa(App.Sm.State) + "　｜　高さ：右手A▼ B▲ ／ 移動：下のバーをグリップ";
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
            else Para(_body, "Quest単体で文字入力しにくい場合は、下の例を選ぶだけで進められます。音声入力はこの版では未対応です。", LabTheme.FontSmall, h: 32);

            UiKit.Heading(_body, "クイック発明（例から選ぶ → 入力欄に反映）", LabTheme.FontHeading);
            var cards = UiKit.Row("Cards", _body, LabTheme.Space2, 150);
            QuickCard(cards, "手を使わない傘", "ハンズフリー雨具 ・ 実験できます", LabAppText.RainRequest, true);
            QuickCard(cards, "空を飛べる靴", "この版では設計未対応", "空を飛べる靴を作って", false);
            QuickCard(cards, "自動でついてくる運搬ロボット", "この版では設計未対応", "自動でついてくる運搬ロボットを作って", false);

            var actions = UiKit.Row("Actions", _body, LabTheme.Space2, 64);
            UiKit.Button(actions, "保存済みの発明（ギャラリー）", App.OpenGallery, ButtonStyle.Secondary, 440);
            var spacer = UiKit.Rect("Spacer", actions); UiKit.Size(spacer, flexW: 1);

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

        void QuickCard(Transform parent, string title, string note, string request, bool supported)
        {
            var b = UiKit.Button(parent, $"{title}\n<size=75%><color=#{LabTheme.ToHex(supported ? LabTheme.AccentLight : LabTheme.TextSecondary)}>{note}</color></size>",
                () => { App.PickQuickInvention(request); if (_requestInput != null) _requestInput.SetTextWithoutNotify(request); },
                ButtonStyle.Secondary, -1, 150);
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
            UiKit.Heading(_leftBody, "確認事項", 26);
            Para(_leftBody, "・AIの出力はデータとして検証し、登録済みの部品と機能だけで組み立てます\n・現実の製品としての安全性や実現性は保証しません", LabTheme.FontSmall, h: 180);

            if (s == LabState.GeneratingDesign)
            {
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
            var c1 = UiKit.Column("Info", cols, LabTheme.Space1); UiKit.Size(c1, flexW: 1);
            Para(c1, $"<b><color=#{LabTheme.ToHex(LabTheme.TextPrimary)}>{dna.Name}</color></b>", LabTheme.FontHeading, h: 42);
            Para(c1, "目的：" + dna.Purpose, LabTheme.FontBody, h: 70);
            Para(c1, "カテゴリ：" + (LabCatalog.Category(dna.Category)?.LabelJa ?? dna.Category) + "　｜　部品 " + dna.Parts.Count + " / 上限 " + dna.Constraints.MaxParts, LabTheme.FontBody, h: 36);
            var rep = App.LastOutcome?.Report;
            Para(c1, "検証：" + (rep != null ? rep.Summary : "—") + (App.LastOutcome != null ? "\n" + App.LastOutcome.Message : ""), LabTheme.FontSmall, LabTheme.AccentLight, 64);
            var c2 = UiKit.Column("Behaviors", cols, LabTheme.Space1); UiKit.Size(c2, flexW: 1);
            UiKit.Heading(c2, "動作（機能モジュール）", 26);
            foreach (var p in App.LastOutcome?.DesignPoints ?? new List<string>()) Para(c2, "・" + p, LabTheme.FontSmall, LabTheme.TextPrimary, 62);

            if (s == LabState.AwaitingApproval)
            {
                var a = UiKit.Row("Actions", _body, LabTheme.Space2, 68);
                UiKit.Button(a, "指示に戻る", App.BackToDraft, ButtonStyle.Ghost, 240);
                UiKit.Button(a, "再生成", App.Regenerate, ButtonStyle.Secondary, 220);
                var sp = UiKit.Rect("Sp", a); UiKit.Size(sp, flexW: 1);
                UiKit.Button(a, "承認して組み立てる ▶", App.ApprovePreview, ButtonStyle.Primary, 420, 68);
            }

            SideHeading(_rightBody, "PARTS", "採用機能と部品");
            var lines = dna.Parts.Select(part => ($"■ {LabCatalog.Part(part.Type)?.LabelJa}（{part.Id}）", LabTheme.FontBody, LabTheme.TextPrimary, 38f))
                .Concat(dna.Behaviors.Select(b => ($"◆ {LabCatalog.Behavior(b.Type)?.LabelJa} → {b.TargetPart}", LabTheme.FontSmall, LabTheme.AccentLight, 34f))).ToList();
            CappedLines(_rightBody, lines, 9);
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
            SideHeading(_rightBody, "SUPPORTED", "この版で対応している発明");
            Para(_rightBody, "・ハンズフリー雨具（防護カテゴリ）\n　雨・風の実験場で評価できます", LabTheme.FontBody, LabTheme.TextPrimary);
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
            _revisionInput = UiKit.Input(inRow, "例：防ぐ範囲を広げて", RequestRules.MaxRequestLength, s => App.RevisionText = s);
            _revisionInput.SetTextWithoutNotify(App.RevisionText);
            UiKit.Size(_revisionInput, flexW: 1, h: 64);
            UiKit.Button(inRow, "依頼", App.RequestRevision, ButtonStyle.Primary, 120, 64);
            var chips = UiKit.Rect("Chips", right);
            var grid = chips.gameObject.AddComponent<UnityEngine.UI.GridLayoutGroup>();
            grid.cellSize = new Vector2(298, 52); grid.spacing = new Vector2(8, 8);
            UiKit.Size(chips, h: 112);
            foreach (var sug in OfflineRevisionRules.SuggestedRequests)
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

            SideHeading(_leftBody, "DESIGN STUDIO", "部品一覧（掴む：グリップ／起動：掴んでトリガー）");
            CappedLines(_leftBody, dna.Parts.Select(part => ($"■ {LabCatalog.Part(part.Type)?.LabelJa}  <size=80%><color=#{LabTheme.ToHex(LabTheme.TextSecondary)}>{part.Id} ／ {part.Scale.x:0.00}×{part.Scale.y:0.00}×{part.Scale.z:0.00} m</color></size>", LabTheme.FontBody, LabTheme.TextPrimary, 40f)).ToList(), 6);
            Para(_leftBody, "外観プリセットはこの版では未対応（機能優先）", LabTheme.FontSmall, h: 34);

            SideHeading(_rightBody, "HISTORY", "改良履歴（元に戻す対象）");
            var hist = App.Current.History.AsEnumerable().Reverse().Take(5).ToList();
            Para(_rightBody, $"● 現在 Rev.{dna.Revision}", LabTheme.FontBody, LabTheme.AccentLight, 36);
            foreach (var h in hist) Para(_rightBody, $"○ Rev.{h.Revision}　防護半径 {h.GetParameter("rain_shield", "radius", 0):0.00} m／側面 {h.GetParameter("rain_shield", "skirtLength", 0):0.00} m", LabTheme.FontSmall, h: 34);
            if (hist.Count == 0) Para(_rightBody, "まだ改良履歴はありません", LabTheme.FontSmall, h: 34);
            var last = App.Current.Experiments.LastOrDefault();
            if (last != null)
            {
                UiKit.Heading(_rightBody, "直近の実験", 26);
                Para(_rightBody, $"Rev.{last.Revision}　{LabModes.Label(last.Config.Mode)}\n被濡れ {last.WetnessScore:0.0} 粒/秒　防護率 {last.CoverageRatio * 100f:0.0}%", LabTheme.FontSmall, LabTheme.TextPrimary, 70);
            }
        }

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
        void BuildS04()
        {
            var cfg = App.TestConfig;
            if (cfg.Mode == LabModes.Reality && App.CoefficientPage >= 0 && RealityModel.Default != null) { BuildCoefficientTable(); return; }
            Title("実験環境設定", "実験場：EXPERIMENT GATE → RainLab");
            // REALITY の注意書きは見出しと同じ行に置く（別の行にするとフッターがパネルからはみ出す）
            var modeHead = UiKit.Row("ModeHead", _body, LabTheme.Space2, 36);
            var mh = UiKit.Heading(modeHead, "実験モードの選択", 26);
            UiKit.Size(mh, w: 260);
            if (cfg.Mode == LabModes.Reality)
            {
                var warn = UiKit.Text(modeHead, "⚠ REALITY MODE は簡易モデルによる参考値です。現実の性能・安全性は保証しません", LabTheme.FontSmall, LabTheme.AccentWarning, align: TextAlignmentOptions.MidlineRight);
                UiKit.Size(warn, flexW: 1);
            }
            var modes = UiKit.Row("Modes", _body, LabTheme.Space2, 92);
            ModeCard(modes, LabModes.Dream, "DREAM MODE（自由実験）", "空想の物理で、制限なく試せる");
            ModeCard(modes, LabModes.Reality, "REALITY MODE（現実準拠）", "簡易モデルで重量・風・電力の制約を確認");

            UiKit.Heading(_body, "環境条件の設定", 26);
            var envRow = UiKit.Row("Env", _body, LabTheme.Space1, 56);
            UiKit.Button(envRow, "RainLab（雨・風）✓ 選択中", null, ButtonStyle.Selected, -1, 56, LabTheme.FontSmall);
            var ob = UiKit.Button(envRow, "ObstacleCourse（運搬向け・選択不可）", null, ButtonStyle.Ghost, -1, 56, LabTheme.FontSmall); UiKit.SetInteractable(ob, false);
            var fd = UiKit.Button(envRow, "FlightDeck（移動向け・選択不可）", null, ButtonStyle.Ghost, -1, 56, LabTheme.FontSmall); UiKit.SetInteractable(fd, false);
            var cond = UiKit.Row("Cond", _body, LabTheme.Space2, 64);
            Stepper(cond, "雨量", $"{cfg.RainIntensity:0} mm/h", "rain");
            Stepper(cond, "風速", $"{cfg.WindSpeed:0.0} m/s", "wind");
            Stepper(cond, "風向", DirJa(cfg.WindDirectionDeg), "dir");
            var misc = UiKit.Row("Misc", _body, LabTheme.Space2, 56);
            var dur = UiKit.Text(misc, $"実験時間 {cfg.DurationSec:0} 秒（固定）　｜　乱数シード {cfg.Seed}（同じシード＝同じ雨粒の並び）", LabTheme.FontSmall, LabTheme.TextSecondary, align: TextAlignmentOptions.MidlineLeft);
            UiKit.Size(dur, flexW: 1);
            if (cfg.Mode == LabModes.Reality) UiKit.Button(misc, "前提係数と出典", App.ShowCoefficients, ButtonStyle.Secondary, 240, 52, LabTheme.FontSmall);
            UiKit.Button(misc, "シード変更", App.NewSeed, ButtonStyle.Ghost, 200, 52, LabTheme.FontSmall);
            var sp = UiKit.Rect("Sp", _body); UiKit.Size(sp, flexH: 1);
            var a = UiKit.Row("Actions", _body, LabTheme.Space2, 68);
            UiKit.Button(a, "編集に戻る", App.BackToEditing, ButtonStyle.Ghost, 260);
            var sp2 = UiKit.Rect("Sp2", a); UiKit.Size(sp2, flexW: 1);
            UiKit.Button(a, "実験開始 ▶", App.StartExperiment, ButtonStyle.Primary, 360, 68);

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

        void ModeCard(Transform parent, string mode, string title, string desc)
        {
            bool sel = App.TestConfig.Mode == mode;
            var b = UiKit.Button(parent, $"{title}{(sel ? "　✓ 選択中" : "")}\n<size=78%>{desc}</size>", () => App.SetMode(mode), sel ? ButtonStyle.Selected : ButtonStyle.Secondary, -1, 104);
            UiKit.Size(b, flexW: 1);
        }

        void Stepper(Transform parent, string label, string value, string key)
        {
            var cell = UiKit.Row("St_" + key, parent, 6, 64); UiKit.Size(cell, flexW: 1);
            var l = UiKit.Text(cell, label, LabTheme.FontSmall, LabTheme.TextSecondary, align: TextAlignmentOptions.MidlineLeft); UiKit.Size(l, w: 120);
            UiKit.Button(cell, "−", () => App.StepCondition(key, -1), ButtonStyle.Secondary, 56, 56);
            var v = UiKit.Text(cell, value, LabTheme.FontBody, LabTheme.AccentLight, bold: true, align: TextAlignmentOptions.Center); UiKit.Size(v, flexW: 1);
            UiKit.Button(cell, "＋", () => App.StepCondition(key, +1), ButtonStyle.Secondary, 56, 56);
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
                Title("実験中 — RainLab", LabModes.Label(cfg.Mode) + "（" + LabModes.Caption(cfg.Mode) + "）");
                Para(_body, cfg.Describe(), LabTheme.FontSmall, h: 32);
                _liveText = UiKit.Text(_body, "", LabTheme.FontHeading, LabTheme.TextPrimary);
                UiKit.Size(_liveText, flexH: 1);
                var a = UiKit.Row("Actions", _body, LabTheme.Space2, 68);
                UiKit.Button(a, "■ 停止（記録しない）", App.StopExperiment, ButtonStyle.Danger, 360);
                BuildMethodPanel(cfg);
                BuildBridgePanel();
                UpdateLive();
                return;
            }
            var r = App.LastResult;
            if (r == null) return;
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
            foreach (var h in App.ImprovementHints(r).Take(3)) Para(c2, "・" + h, LabTheme.FontSmall, LabTheme.TextPrimary, 30);

            var a2 = UiKit.Row("Actions", _body, LabTheme.Space1, 68);
            UiKit.Button(a2, "同じ条件で再実験", App.RerunSameConditions, ButtonStyle.Secondary, 280);
            UiKit.Button(a2, cfg.Mode == LabModes.Dream ? "REALITYで比較" : "DREAMで比較", App.RerunOtherMode, ButtonStyle.Secondary, 260);
            UiKit.Button(a2, "保存して終了", App.Save, ButtonStyle.Ghost, 240);
            var sp = UiKit.Rect("Sp", a2); UiKit.Size(sp, flexW: 1);
            UiKit.Button(a2, "改良して再実験 ▶", App.ImproveAndRetest, ButtonStyle.Primary, 340, 68);
            BuildMethodPanel(r.Config, r);
            BuildBridgePanel();
        }

        void Metric(Transform parent, string label, string value)
        {
            var row = UiKit.Row("M", parent, LabTheme.Space1, 40);
            var l = UiKit.Text(row, label, LabTheme.FontSmall, LabTheme.TextSecondary, align: TextAlignmentOptions.MidlineLeft); UiKit.Size(l, flexW: 1);
            var v = UiKit.Text(row, value, LabTheme.FontSmall, LabTheme.TextPrimary, bold: true, align: TextAlignmentOptions.MidlineRight); UiKit.Size(v, w: 400);
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
            Para(_leftBody, "・雨粒を直線として、キャノピー（円板＋側面カバー）と判定用円筒（半径0.22m・高さ1.75m）との衝突を計算", LabTheme.FontSmall, LabTheme.TextPrimary, 92);
            Para(_leftBody, "・被濡れスコア＝身体に当たった雨粒数÷実験秒数\n・防護率＝1−装置あり命中÷装置なし命中", LabTheme.FontSmall, LabTheme.TextPrimary, 92);
            Para(_leftBody, "・流体解析ではありません。シード固定で再現可能", LabTheme.FontSmall, h: 34);
            if (r != null) foreach (var s in r.Limits.Take(2)) Para(_leftBody, "⚠ " + s, LabTheme.FontSmall, LabTheme.AccentWarning, 64);
        }

        void BuildBridgePanel()
        {
            SideHeading(_rightBody, "REALITY LAB", "現実化の課題（推定・要調査を含む）");
            Para(_rightBody, "<b>現実の目的</b>：両手がふさがった状態での雨対策", LabTheme.FontSmall, LabTheme.TextPrimary, 34);
            Para(_rightBody, "<b>近い既知技術</b>：ドローン・頭上追従カメラ等（具体例は要調査）", LabTheme.FontSmall, LabTheme.TextPrimary, 64);
            Para(_rightBody, "<b>未解決の課題</b>：バッテリー持続、プロペラの安全性、騒音、強風時の制御、法規制（要調査）", LabTheme.FontSmall, LabTheme.TextPrimary, 92);
            Para(_rightBody, "<b>追加検証</b>：実機の重量・推力測定、風洞試験、安全基準の確認", LabTheme.FontSmall, LabTheme.TextPrimary, 64);
            Para(_rightBody, "この表示は工学的な実現性・安全性を保証しません。", LabTheme.FontSmall, LabTheme.AccentWarning, 34);
        }

        void Update() { if (_liveText != null) UpdateLive(); }

        void UpdateLive()
        {
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
            if (entries.Count == 0)
            {
                Para(_body, "保存された発明はまだありません。発明して「保存」すると、ここに表示されます。", LabTheme.FontBody, LabTheme.TextPrimary, 80);
                var spE = UiKit.Rect("Sp", _body); UiKit.Size(spE, flexH: 1);
            }
            else
            {
                var gridRt = UiKit.Rect("Grid", _body); UiKit.Size(gridRt, flexH: 1);
                var grid = gridRt.gameObject.AddComponent<UnityEngine.UI.GridLayoutGroup>();
                grid.cellSize = new Vector2(472, 250); grid.spacing = new Vector2(16, 16);
                foreach (var e in entries.Take(6)) GalleryCard(gridRt, e);
                if (entries.Count > 6) Para(_body, $"ほか {entries.Count - 6} 件（新しい順に6件を表示）", LabTheme.FontSmall, h: 28);
            }
            var a2 = UiKit.Row("Actions", _body, LabTheme.Space2, 64);
            UiKit.Button(a2, "戻る", App.CloseGallery, ButtonStyle.Ghost, 220);
            GallerySide();
            if (App.PendingDeleteId != null) BuildDeleteDialog();
        }

        void GalleryCard(Transform parent, GalleryEntry e)
        {
            var card = UiKit.Panel("Card_" + e.Id, parent, LabTheme.BgPanel);
            UiKit.VList(card, 6, LabTheme.Space2);
            Para(card, $"<b><color=#{LabTheme.ToHex(LabTheme.TextPrimary)}>{e.Name}</color></b>", LabTheme.FontBody, h: 36);
            Para(card, $"{LabCatalog.Category(e.Category)?.LabelJa ?? e.Category} ／ Rev.{e.Revision} ／ 更新 {e.UpdatedAt}", LabTheme.FontSmall, h: 30);
            Para(card, e.Tested ? "✓ 実験済み" : "未実験", LabTheme.FontSmall, e.Tested ? LabTheme.AccentLight : LabTheme.TextSecondary, 30);
            var row = UiKit.Row("Btns", card, LabTheme.Space1, 60);
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
