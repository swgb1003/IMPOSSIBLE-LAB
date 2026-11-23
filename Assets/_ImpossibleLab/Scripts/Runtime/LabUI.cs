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
    /// 6画面（S01〜S06）の World Space UI。輪の計器コンソール（2026-11-23 に決めた形。docs/design/console6.html）：
    /// 1枚のパネルの中央に発明台を上から見た計器、左右に金属の板、下に6段階と操作。固定HUDは使わない（仕様21.3）。
    /// 画面ごとの中身は LabUI.Console*.cs、部品は ConsoleKit.cs・ConsoleGfx.cs。
    /// </summary>
    public sealed partial class LabUI : MonoBehaviour
    {
        public LabApp App;
        public Transform ConsoleRoot;

        Canvas _main;
        TextMeshProUGUI _toast, _liveText;
        TMP_InputField _requestInput, _revisionInput;
        string _signature;
        InputAction _menuAction, _lowerAction, _raiseAction;

        public const float MainW = ConsoleLayer.W, MainH = ConsoleLayer.H;

        void Start()
        {
            var cam = App.Origin != null ? App.Origin.Camera : Camera.main;
            _main = UiKit.CreateCanvas("MainConsole", ConsoleRoot, new Vector2(MainW, MainH), cam);
            _main.transform.localPosition = Vector3.zero;
            _main.transform.localRotation = Quaternion.Euler(22f, 0, 0); // 机のようにやや上向き
            _con = new ConsoleLayer(_main.transform);
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
              .Append('|').Append(string.Join(",", App.Stages.Values)).Append('|').Append(App.Current?.History.Count).Append('|').Append(App.CoefficientPage).Append('|').Append(_paramPage).Append('|').Append(App.PlanSignature).Append(_modulePage).Append('|').Append(_needPage).Append('|').Append(_galleryPage).Append('|').Append(App.SelectedPartIndex).Append(_galleryTag).Append(_s03Program).Append(_programPage).Append('|').Append(App.Voice).Append(App.VoiceTarget).Append(App.ProxyOnline).Append(App.TrialPersonDistance).Append('|').Append(_selParam).Append(_tabL).Append(_tabR).Append(_planPage).Append(_galleryPick);
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
            UpdateEcho();
        }

        string StatusLine()
        {
            switch (App.Sm.State)
            {
                case LabState.GeneratingDesign: return "処理中：他の操作はできません（キャンセル可）";
                case LabState.Building: return "組み立て中…";
                case LabState.RunningTest: return "実験中：停止すると記録せず初期状態に戻ります";
                default: return App.TrialStatus() ?? "状態：" + StateJa(App.Sm.State) + " ｜ 高さ：右手A▼B▲ ／ 移動：下のバーを握る";
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
            _requestInput = null; _revisionInput = null; _liveText = null; _echoText = null; _countText = null; _echoState = null; _liveArc = null;
            string tabKey = StepIndex().ToString();
            if (tabKey != _tabFor) { _tabFor = tabKey; _tabL = 0; _tabR = 0; }
            _con.Clear();
            switch (App.Sm.State)
            {
                case LabState.Idle: case LabState.Drafting: case LabState.Cancelled: BuildC01(); break;
                case LabState.GeneratingDesign: case LabState.AwaitingApproval: case LabState.Building: BuildC02(); break;
                case LabState.Error: BuildCError(); break;
                case LabState.Editing: BuildC03(); break;
                case LabState.ConfiguringTest: if (ShowingCoefficients) BuildCCoefficients(); else BuildC04(); break;
                case LabState.RunningTest: case LabState.ReviewingResult: BuildC05(); break;
                case LabState.Saved: case LabState.Gallery: BuildC06(); break;
            }
        }


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

        // ---- 機能対応表（S02）：要望ごとに ✓ 検証する／△ 単純化して検証／× 検証できない。記号・色・文字の3つで区別する ----
        // （✕ U+2715 は日本語フォントに無く□になるため × U+00D7 を使う）
        int _needPage;
        string _needPageFor;
        const int NeedsPerPage = 4;
        public int NeedPage => _needPage;
        public void StepNeedPage(int dir) { _needPage = Mathf.Max(0, _needPage + dir); Refresh(); }

        // ---------------- S03 発明品編集 ----------------
        // 性能パラメータの表示ページ（AI設計ではパラメータが多く、1画面に収まらないため）
        int _paramPage;
        string _paramPageFor;
        public int ParamPage => _paramPage;
        public void StepParamPage(int dir) { _paramPage = Mathf.Max(0, _paramPage + dir); Refresh(); }

        // ---- S03 動きの手順（仕様 28.9）：ルールごとのオン・オフ、数値の増減、「動かしてみる」。編集は版の履歴に残す ----
        bool _s03Program;
        int _programPage;
        string _programPageFor;
        public bool ProgramTab { get => _s03Program; set { _s03Program = value; Refresh(); } }

        // ---------------- S04 実験環境設定 ----------------
        // S04 は「実験計画」を選んで編集する画面（6週目）。上から：モード → 計画の候補 → ねらい → 環境条件（値の増減・追加・削除）→ シード → 開始。
        // 計測項目と合格条件は右パネル、計算の前提は左パネル（実行前に同じ計算を走らせて表示）。
        int _modulePage;

        /// <summary>
        /// 組み立て式の実験の側面：左＝シナリオと計算の前提、右＝計測項目と合格条件・事前に分かる制限。
        /// 実行前に同じ計算（ScenarioRunner）を走らせて表示する（計算は一括・数ミリ秒）。
        /// </summary>
        string _previewKey;
        CourseRun _preview;

        static string DirJa(float deg)
        {
            string[] n = { "前へ", "右前へ", "右へ", "右後ろへ", "後ろへ", "左後ろへ", "左へ", "左前へ" };
            return $"{n[Mathf.RoundToInt(deg / 45f) % 8]}（{deg:0}°）";
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
                ConsoleLayer.SetLive(_liveArc, c.Run.Duration > 0f ? c.Elapsed / c.Run.Duration : 0f);
                return;
            }
            var sim = App.RainLab != null ? App.RainLab.Simulator : null;
            if (sim == null || _liveText == null) return;
            var r = sim.Result;
            float cov = r.BaselineHits == 0 ? 1f : 1f - (float)r.DeviceHits / r.BaselineHits;
            _liveText.text = string.Format(CultureInfo.InvariantCulture,
                "経過 {0:0.0} / {1:0} 秒\n処理した雨粒 {2} / {3}\n身体への命中 <color=#{6}>{4}</color>　キャノピーで遮断 {5}\n防護率（途中経過） {7:0.0}%",
                App.RainLab.Elapsed, sim.Result.Config.DurationSec, r.DropsTotal, sim.TotalDrops, r.DeviceHits, r.Blocked, LabTheme.ToHex(LabTheme.AccentWarning), cov * 100f);
            ConsoleLayer.SetLive(_liveArc, sim.Result.Config.DurationSec > 0f ? App.RainLab.Elapsed / sim.Result.Config.DurationSec : 0f);
        }

        // ---- ギャラリーのページ（新しい順に6件ずつ）----
        public const int GalleryPerPage = 6;
        int _galleryPage, _galleryShownFor = -1;
        public int GalleryPage => _galleryPage;
        static int GalleryPages(int count) => Mathf.Max(1, (count + GalleryPerPage - 1) / GalleryPerPage);
        public void StepGalleryPage(int dir)
        {
            _galleryPage = Mathf.Clamp(_galleryPage + dir, 0, GalleryPages(GalleryFilter.Apply(App.GalleryEntries, _galleryTag).Count) - 1);
            Refresh();
        }

        // ---- タグ絞り込み（null＝すべて）。ギャラリーを開き直すと「すべて」に戻る ----
        string _galleryTag;
        public string GalleryTag => _galleryTag;
        public void SetGalleryTag(string tag)
        {
            _galleryTag = string.IsNullOrEmpty(tag) ? null : tag;
            _galleryPage = 0;
            Refresh();
        }

    }

    public static class LabAppText
    {
        public const string RainRequest = "手を使わず雨を防ぐ装置を作って";
    }
}
