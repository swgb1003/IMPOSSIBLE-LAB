using ImpossibleLab.Core;
using TMPro;
using UnityEngine;

namespace ImpossibleLab
{
    /// <summary>
    /// 手元のメニュー（仕様21.3：片手でメニューを呼び出せる）。左コントローラーを顔の前（約50cm以内・視線の先）に
    /// 持ってくると、コントローラーの上に小さなパネルが出る。右手のレイ＋トリガーで押す。
    /// 中身は、離れた場所からでも使いたい操作（パネルを正面へ・高さ・座位/立位・効果音・実験停止）だけ。
    /// </summary>
    public sealed class HandMenu : MonoBehaviour
    {
        const float Scale = 0.0004f; // 1px = 0.4mm（メインパネルより近くで見るため小さく）
        LabApp _app;
        Transform _hand;
        Canvas _canvas;
        RectTransform _body;
        bool _shown;
        /// <summary>自動検証で表示を確かめるとき true。</summary>
        public bool ForceVisible;
        /// <summary>自動検証の撮影用に置き場所を固定する。</summary>
        public Pose? PoseOverride;
        public bool Shown => _shown;
        public Canvas Canvas => _canvas;

        public void Init(LabApp app) { _app = app; }

        void Start()
        {
            if (_app == null || _app.Origin == null) { enabled = false; return; }
            foreach (var t in _app.Origin.GetComponentsInChildren<Transform>(true))
                if (t.name == "Left Controller") { _hand = t; break; }
            if (_hand == null) { Debug.Log("[ILAB] hand menu: Left Controller not found, disabled"); enabled = false; return; }
            _canvas = UiKit.CreateCanvas("HandMenu", null, new Vector2(520, 410), _app.Origin.Camera);
            _canvas.transform.localScale = Vector3.one * Scale;
            var bg = UiKit.Image("Bg", _canvas.transform, LabTheme.BgPanel);
            UiKit.Stretch(bg.rectTransform);
            _body = UiKit.Rect("Body", _canvas.transform);
            UiKit.Stretch(_body, 16, 16, 12, 12);
            UiKit.VList(_body, 10);
            _app.Changed += Rebuild;
            Rebuild();
            _canvas.gameObject.SetActive(false);
        }

        void OnDestroy() { if (_app != null) _app.Changed -= Rebuild; }

        void Rebuild()
        {
            if (_body == null) return;
            for (int i = _body.childCount - 1; i >= 0; i--) Destroy(_body.GetChild(i).gameObject);
            var head = UiKit.Text(_body, "手元メニュー", LabTheme.FontSmall, LabTheme.AccentLight, bold: true, align: TextAlignmentOptions.MidlineLeft);
            UiKit.Size(head, h: 34);
            var r1 = UiKit.Row("R1", _body, 10, 72);
            UiKit.Button(r1, "パネルを正面へ", _app.RecenterToHead, ButtonStyle.Primary, -1, 72, LabTheme.FontSmall);
            var r2 = UiKit.Row("R2", _body, 10, 72);
            UiKit.Button(r2, "▲ 上げる", () => _app.NudgeConsoleHeight(+1), ButtonStyle.Secondary, -1, 72, LabTheme.FontSmall);
            UiKit.Button(r2, "▼ 下げる", () => _app.NudgeConsoleHeight(-1), ButtonStyle.Secondary, -1, 72, LabTheme.FontSmall);
            var r3 = UiKit.Row("R3", _body, 10, 72);
            UiKit.Button(r3, _app.Seated ? "座位 → 立位" : "立位 → 座位", _app.ToggleSeated, ButtonStyle.Secondary, -1, 72, LabTheme.FontSmall);
            bool sound = _app.Fx == null || _app.Fx.SoundOn;
            UiKit.Button(r3, sound ? "効果音 ON" : "効果音 OFF", ToggleSound, sound ? ButtonStyle.Selected : ButtonStyle.Ghost, -1, 72, LabTheme.FontSmall);
            var r4 = UiKit.Row("R4", _body, 10, 72);
            var stop = UiKit.Button(r4, "■ 実験を停止", _app.StopExperiment, ButtonStyle.Danger, -1, 72, LabTheme.FontSmall);
            UiKit.SetInteractable(stop, _app.Sm.State == LabState.RunningTest);
        }

        void ToggleSound()
        {
            if (_app.Fx == null) return;
            _app.Fx.SoundOn = !_app.Fx.SoundOn;
            _app.ShowToast(_app.Fx.SoundOn ? "効果音をオンにしました" : "効果音をオフにしました");
        }

        void LateUpdate()
        {
            if (_canvas == null || _hand == null) return;
            var cam = _app.Origin.Camera.transform;
            var toHand = _hand.position - cam.position;
            float dist = toHand.magnitude, facing = dist > 1e-3f ? Vector3.Dot(cam.forward, toHand / dist) : 0f;
            // ちらつかないよう、出す条件と消す条件に差をつける
            bool want = ForceVisible || (_shown ? dist < 0.6f && facing > 0.45f : dist < 0.5f && facing > 0.6f);
            if (want != _shown) { _shown = want; _canvas.gameObject.SetActive(want); }
            if (!_shown) return;
            if (PoseOverride.HasValue) { _canvas.transform.SetPositionAndRotation(PoseOverride.Value.position, PoseOverride.Value.rotation); return; }
            var pos = _hand.position + Vector3.up * 0.1f;
            _canvas.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - cam.position, Vector3.up));
        }
    }
}
