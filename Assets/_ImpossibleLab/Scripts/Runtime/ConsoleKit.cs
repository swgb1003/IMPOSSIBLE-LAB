using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ImpossibleLab
{
    /// <summary>ボタンの種類。Primary は画面に1つ（右下の主役）、Warm は改良案の採用だけ。</summary>
    public enum ConKind { Normal, Primary, Warm, On, Quiet }

    /// <summary>
    /// 輪の計器コンソールの部品（docs/design/console6.js の Unity 版）。
    /// 座標は設計図と同じ 1600×900、左上が原点。文字の y は基線（ベースライン）。
    /// 重なりの順：図形 → 押せる部品 → 明滅する図形 → 文字。
    /// 部品は作り直しのたびに捨てずに使い回す（<see cref="Clear"/> で使い始め、<see cref="Finish"/> で余りを隠す）。
    /// Quest では、画面を変えるたびに数百個の部品を作って壊すと 0.1〜0.3 秒止まるため。
    /// </summary>
    public sealed class ConsoleLayer
    {
        public const float W = 1600f, H = 900f;
        /// <summary>計器の中心と、1 m の長さ（px）。</summary>
        public static readonly Vector2 C = new Vector2(800f, 372f);
        public const float M = 140f;
        /// <summary>左右の板の位置と幅。</summary>
        public const float LX = 28f, RX = 1118f, PW = 454f;

        public readonly RectTransform Root;
        public VecGraphic G { get; private set; }
        public VecGraphic Ghost { get; private set; }
        readonly RectTransform _fx, _hits, _texts;

        /// <summary>使い回す部品の置き場。種類ごとに、作った順に並べておき、前から順に使う。</summary>
        sealed class Pool<T> where T : class
        {
            readonly Transform _parent;
            readonly System.Func<Transform, T> _create;
            readonly System.Func<T, GameObject> _go;
            readonly List<T> _items = new List<T>();
            int _used;
            public Pool(Transform parent, System.Func<Transform, T> create, System.Func<T, GameObject> go) { _parent = parent; _create = create; _go = go; }
            public void Begin() => _used = 0;
            public T Take()
            {
                T it;
                if (_used < _items.Count) { it = _items[_used]; var g = _go(it); if (!g.activeSelf) g.SetActive(true); }
                else { it = _create(_parent); _items.Add(it); }
                _used++;
                return it;
            }
            public void End() { for (int i = _used; i < _items.Count; i++) { var g = _go(_items[i]); if (g.activeSelf) g.SetActive(false); } }
        }

        sealed class BtnSlot { public VecGraphic G; public Button B; public TextMeshProUGUI Label; }
        sealed class HitSlot { public Image I; public Button B; }

        readonly Pool<TextMeshProUGUI> _textPool;
        readonly Pool<BtnSlot> _btnPool, _triPool;
        readonly Pool<HitSlot> _hitPool;
        readonly Pool<TMP_InputField> _inputPool;
        readonly Pool<VecGraphic> _fxPool;

        public ConsoleLayer(Transform parent)
        {
            Root = UiKit.Rect("Console", parent); UiKit.Stretch(Root);
            var bg = UiKit.Image("Bg", Root, LabTheme.ConBg, raycast: true); UiKit.Stretch(bg.rectTransform);
            G = NewGraphic("Shapes", Root);
            _fx = UiKit.Rect("Fx", Root); UiKit.Stretch(_fx);
            _hits = UiKit.Rect("Hits", Root); UiKit.Stretch(_hits);
            Ghost = NewGraphic("Ghost", Root);
            Ghost.gameObject.AddComponent<ConsolePulse>();
            _texts = UiKit.Rect("Texts", Root); UiKit.Stretch(_texts);

            // 押せる部品は種類ごとの入れ物に分ける。重なったときは後の入れ物が上（行の当たり判定 → ボタン → 三角 → 入力欄）
            var rows = UiKit.Rect("Rows", _hits); UiKit.Stretch(rows);
            var btns = UiKit.Rect("Buttons", _hits); UiKit.Stretch(btns);
            var tris = UiKit.Rect("Tris", _hits); UiKit.Stretch(tris);
            var inputs = UiKit.Rect("Inputs", _hits); UiKit.Stretch(inputs);

            _textPool = new Pool<TextMeshProUGUI>(_texts, tr => UiKit.Text(tr, "", LabTheme.ConMinFont, LabTheme.ConInk), t => t.gameObject);
            _btnPool = new Pool<BtnSlot>(btns, tr =>
            {
                var g = NewGraphic("Btn", tr, raycast: true);
                var b = g.gameObject.AddComponent<Button>();
                var t = UiKit.Text(g.transform, "", LabTheme.ConMinFont, LabTheme.ConInk, bold: true, align: TextAlignmentOptions.Center, name: "Label");
                t.textWrappingMode = TextWrappingModes.NoWrap;
                UiKit.Stretch(t.rectTransform, 12f, 12f, 0f, 0f);
                return new BtnSlot { G = g, B = b, Label = t };
            }, x => x.G.gameObject);
            _triPool = new Pool<BtnSlot>(tris, tr =>
            {
                var g = NewGraphic("Tri", tr, raycast: true);
                return new BtnSlot { G = g, B = g.gameObject.AddComponent<Button>() };
            }, x => x.G.gameObject);
            _hitPool = new Pool<HitSlot>(rows, tr =>
            {
                var img = UiKit.Image("Hit", tr, LabTheme.ConCy, raycast: true);
                var btn = img.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;
                var cb = btn.colors;
                cb.normalColor = new Color(1f, 1f, 1f, 0f);
                cb.highlightedColor = VecGraphic.Soft(Color.white, 0.09f);
                cb.selectedColor = cb.normalColor;
                cb.pressedColor = VecGraphic.Soft(Color.white, 0.18f);
                cb.disabledColor = cb.normalColor;
                cb.colorMultiplier = 1f; cb.fadeDuration = 0.08f;
                btn.colors = cb;
                return new HitSlot { I = img, B = btn };
            }, x => x.I.gameObject);
            _inputPool = new Pool<TMP_InputField>(inputs, tr =>
            {
                var input = UiKit.Input(tr, "", 0, null);
                input.GetComponent<Image>().color = LabTheme.ConWell;
                input.pointSize = 20f;
                input.textComponent.fontSize = 20f; input.textComponent.color = LabTheme.ConInk;
                if (input.placeholder is TMP_Text ph) { ph.fontSize = 19f; ph.color = LabTheme.ConMut; ph.fontStyle = FontStyles.Normal; ph.alpha = 1f; }
                var area = input.textViewport; if (area != null) UiKit.Stretch(area, 14f, 14f, 4f, 4f);
                return input;
            }, x => x.gameObject);
            _fxPool = new Pool<VecGraphic>(_fx, tr => NewGraphic("LiveArc", tr), x => x.gameObject);
        }

        // 真ん中の列に置く画像（試作品の立体の写し）。図形の上、押せる部品と文字の下。触れて動かすと drag に動いた量（px）を渡す
        RawImage _winImg;
        ConsoleDrag _winDrag;
        bool _winUsed;

        /// <summary>設計図の座標の四角に画像を置く。押したまま動かすと、動いた量（px・右と上が正）を drag に渡す。</summary>
        public void Window(Texture tex, float x, float y, float w, float h, System.Action<Vector2> drag)
        {
            if (_winImg == null)
            {
                _winImg = new GameObject("Window", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                _winImg.transform.SetParent(Root, false);
                _winImg.transform.SetSiblingIndex(G.transform.GetSiblingIndex() + 1);
                _winImg.raycastTarget = true;
                _winDrag = _winImg.gameObject.AddComponent<ConsoleDrag>();
            }
            _winUsed = true;
            if (!_winImg.gameObject.activeSelf) _winImg.gameObject.SetActive(true);
            Place(_winImg.rectTransform, x, y, w, h);
            if (_winImg.texture != tex) _winImg.texture = tex;
            _winDrag.Moved = drag;
        }

        /// <summary>作り直しを始める。部品は消さずに、前から順に使い直す。</summary>
        public void Clear()
        {
            _winUsed = false;
            G.BeginShapes(); Ghost.BeginShapes();
            _textPool.Begin(); _btnPool.Begin(); _triPool.Begin(); _hitPool.Begin(); _inputPool.Begin(); _fxPool.Begin();
            // 押したボタンが選ばれたまま別の場所で使い直されると色が残るので、選択を外す（入力中の欄はそのまま）
            var es = EventSystem.current;
            var sel = es != null ? es.currentSelectedGameObject : null;
            if (sel != null && sel.GetComponent<TMP_InputField>() == null && sel.transform.IsChildOf(Root)) es.SetSelectedGameObject(null);
        }

        /// <summary>作り直しの終わり。使わなかった部品を隠し、図形が前と違うときだけ描き直す。</summary>
        public void Finish()
        {
            _textPool.End(); _btnPool.End(); _triPool.End(); _hitPool.End(); _inputPool.End(); _fxPool.End();
            if (_winImg != null && _winImg.gameObject.activeSelf != _winUsed) _winImg.gameObject.SetActive(_winUsed);
            G.EndShapes(); Ghost.EndShapes();
        }

        static VecGraphic NewGraphic(string name, Transform parent, bool raycast = false)
        {
            var rt = UiKit.Rect(name, parent); UiKit.Stretch(rt);
            var g = rt.gameObject.AddComponent<VecGraphic>();
            g.raycastTarget = raycast;
            return g;
        }

        /// <summary>設計図の座標（左上原点）に四角を置く。</summary>
        public static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        public static Vector2 Pol(float r, float deg) => VecGraphic.Polar(C, r, deg);
        static Color A(Color c, float a) => VecGraphic.Soft(c, a);

        // ---------------- 文字 ----------------
        /// <summary>1行の文字。anchor：-1 左、0 中央、+1 右。y は基線。</summary>
        public TextMeshProUGUI T(float x, float y, string text, float size = 18f, Color? fill = null, bool bold = false, int anchor = -1, bool halo = false)
        {
            size = Mathf.Max(size, LabTheme.ConMinFont);
            var t = NewText(text, size, fill ?? LabTheme.ConInk, bold,
                anchor < 0 ? TextAlignmentOptions.BaselineLeft : anchor == 0 ? TextAlignmentOptions.Baseline : TextAlignmentOptions.BaselineRight, halo);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(anchor < 0 ? 0f : anchor == 0 ? 0.5f : 1f, 0.5f);
            rt.sizeDelta = new Vector2(8f, size * 1.4f);
            rt.anchoredPosition = new Vector2(x, -y);
            return t;
        }

        /// <summary>使い回す文字の部品を取り出し、前に使ったときの設定を全部入れ直す（同じ値なら TMP は作り直さない）。</summary>
        TextMeshProUGUI NewText(string text, float size, Color color, bool bold, TextAlignmentOptions align, bool halo)
        {
            var t = _textPool.Take();
            var font = bold && LabTheme.FontBold != null ? LabTheme.FontBold : LabTheme.Font;
            t.font = font;
            var mat = halo ? Halo(font) : null;
            if (font != null) t.fontSharedMaterial = mat != null ? mat : font.material;
            t.fontSize = size;
            t.fontStyle = FontStyles.Normal;
            t.characterSpacing = 0f;
            t.color = color;
            t.alignment = align;
            t.text = text ?? "";
            return t;
        }

        /// <summary>1行の文字（左寄せ）。幅 w を超えた分は … にする。</summary>
        public TextMeshProUGUI TLine(float x, float y, float w, string text, float size = 17f, Color? fill = null, bool bold = false)
        {
            var t = T(x, y, text, size, fill, bold);
            t.overflowMode = TextOverflowModes.Ellipsis;
            // 高さは行の高さより十分に大きくする（Noto Sans JP は行が高く、低い枠だと … の処理で全文が消える）
            t.rectTransform.sizeDelta = new Vector2(w, t.fontSize * 3.2f);
            return t;
        }

        /// <summary>折り返す文字（枠の左上から）。</summary>
        public TextMeshProUGUI TBox(float x, float y, float w, float h, string text, float size = 17f, Color? fill = null, bool bold = false)
        {
            size = Mathf.Max(size, LabTheme.ConMinFont);
            var t = NewText(text, size, fill ?? LabTheme.ConInk, bold, TextAlignmentOptions.TopLeft, false);
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Ellipsis;
            Place(t.rectTransform, x, y, w, h);
            return t;
        }

        /// <summary>長い語を1行に収める（末尾を … にする）。</summary>
        public static string Fit(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s ?? "" : s.Substring(0, max - 1) + "…";

        // 計器の線の上でも読めるよう、文字の後ろに暗いにじみを付ける材質（フォントごとに1つを使い回す）
        static readonly Dictionary<TMP_FontAsset, Material> HaloMaterials = new Dictionary<TMP_FontAsset, Material>();
        static Material Halo(TMP_FontAsset font)
        {
            if (font == null || font.material == null) return null;
            if (HaloMaterials.TryGetValue(font, out var m) && m != null) return m;
            m = new Material(font.material) { name = font.name + " Halo" };
            m.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            m.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0.02f, 0.035f, 0.043f, 1f));
            m.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
            m.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, 0f);
            m.SetFloat(ShaderUtilities.ID_UnderlayDilate, 1f);
            m.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.1f);
            HaloMaterials[font] = m;
            return m;
        }

        // ---------------- 押せる部品 ----------------
        static void Tint(Button b, Graphic g, bool enabled)
        {
            b.targetGraphic = g;
            var cb = b.colors;
            cb.normalColor = new Color(0.88f, 0.88f, 0.88f, 1f);
            cb.highlightedColor = Color.white;
            cb.selectedColor = Color.white;
            cb.pressedColor = new Color(0.62f, 0.62f, 0.62f, 1f);
            cb.disabledColor = new Color(1f, 1f, 1f, 0.35f);
            cb.colorMultiplier = 1f; cb.fadeDuration = 0.08f;
            b.colors = cb;
            b.interactable = enabled;
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(UiKit.NotifyClick);
        }

        /// <summary>斜めのボタン。</summary>
        public Button Button(float x, float y, float w, float h, string label, UnityAction onClick, ConKind kind = ConKind.Normal, float size = 20f, bool enabled = true)
        {
            var slot = _btnPool.Take();
            var g = slot.G;
            g.name = "Btn_" + UiKit.Sanitize(label);
            Place(g.rectTransform, x, y, w, h);
            Color fill = kind == ConKind.Primary ? LabTheme.ConCy : kind == ConKind.Warm ? LabTheme.ConOr : kind == ConKind.On ? LabTheme.ConCyDeep : LabTheme.ConPlate2;
            Color stroke = kind == ConKind.Primary || kind == ConKind.On ? LabTheme.ConCy : kind == ConKind.Warm ? LabTheme.ConOr : kind == ConKind.Quiet ? LabTheme.ConEdgeHi : LabTheme.ConCyDim;
            var pts = VecGraphic.Slant(1f, 1f, w - 2f, h - 2f, 12f);
            g.BeginShapes();
            g.Poly(fill, pts);
            g.PolyLine(2f, stroke, true, pts);
            g.EndShapes();
            if (kind == ConKind.Primary && enabled) G.Glow(LabTheme.ConCy, VecGraphic.Slant(x + 1f, y + 1f, w - 2f, h - 2f, 12f));
            var btn = slot.B;
            Tint(btn, g, enabled);
            if (onClick != null) btn.onClick.AddListener(onClick);
            Color ink = kind == ConKind.Primary || kind == ConKind.Warm ? LabTheme.ConOnCy : kind == ConKind.Quiet ? LabTheme.ConMut : LabTheme.ConInk;
            var t = slot.Label;
            t.text = label ?? "";
            t.fontSize = Mathf.Max(size, LabTheme.ConMinFont);
            t.color = ink;
            if (!enabled) t.alpha = 0.45f;
            return btn;
        }

        /// <summary>三角のスイッチ（48×48 の当たり判定）。dir：-1 左向き、+1 右向き。</summary>
        public Button Tri(float x, float y, int dir, UnityAction onClick, bool sel = false, bool enabled = true, string name = "Tri")
        {
            var slot = _triPool.Take();
            var g = slot.G;
            g.name = name + (dir < 0 ? "_Prev" : "_Next");
            Place(g.rectTransform, x, y, 48f, 48f);
            var pts = dir < 0
                ? new[] { new Vector2(36f, 8f), new Vector2(36f, 40f), new Vector2(10f, 24f) }
                : new[] { new Vector2(12f, 8f), new Vector2(12f, 40f), new Vector2(38f, 24f) };
            g.BeginShapes();
            g.Poly(LabTheme.ConPlate2, pts);
            g.PolyLine(2f, sel ? LabTheme.ConOr : LabTheme.ConCy, true, pts);
            g.EndShapes();
            var btn = slot.B;
            Tint(btn, g, enabled);
            if (onClick != null) btn.onClick.AddListener(onClick);
            return btn;
        }

        /// <summary>見えない当たり判定（行・チップ）。レイを当てると薄く明るくなる。</summary>
        public Button Hit(float x, float y, float w, float h, UnityAction onClick, string name = "Hit")
        {
            var slot = _hitPool.Take();
            slot.I.name = name;
            Place(slot.I.rectTransform, x, y, w, h);
            var btn = slot.B;
            btn.interactable = true;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(UiKit.NotifyClick);
            if (onClick != null) btn.onClick.AddListener(onClick);
            return btn;
        }

        /// <summary>入力欄（黒いくぼみ）。</summary>
        public TMP_InputField Input(float x, float y, float w, float h, string placeholder, int limit, System.Action<string> onChanged, bool strong = false)
        {
            var input = _inputPool.Take();
            Place((RectTransform)input.transform, x, y, w, h);
            input.characterLimit = limit;
            input.onValueChanged.RemoveAllListeners();
            if (onChanged != null) input.onValueChanged.AddListener(v => onChanged(v));
            if (input.placeholder is TMP_Text ph) ph.text = placeholder ?? "";
            var o = input.GetComponent<Outline>();
            if (o != null) { o.effectColor = strong ? LabTheme.ConCy : LabTheme.ConCyDim; o.effectDistance = new Vector2(1.5f, -1.5f); }
            return input;
        }

        // ---------------- 板と行 ----------------
        public void Plate(float x, float y, float w, float h, string title = null, string right = null)
        {
            G.RectV(x, y, w, h, LabTheme.ConMetalTop, LabTheme.ConMetalBottom);
            G.PolyLine(1f, LabTheme.ConEdge, true, new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h));
            G.Line(new Vector2(x + 4f, y + 0.5f), new Vector2(x + w - 4f, y + 0.5f), 1f, LabTheme.ConEdgeHi);
            if (!string.IsNullOrEmpty(title)) T(x + 20f, y + 34f, title, 20f, bold: true);
            if (!string.IsNullOrEmpty(right)) T(x + w - 20f, y + 34f, right, 17f, LabTheme.ConMut, anchor: 1);
        }

        /// <summary>板の中の1行。sel はオレンジの枠（選んでいる行）。</summary>
        public void Row(float x, float y, float w, float h, string title, string sub = null, string right = null, Color? rightFill = null,
            bool sel = false, UnityAction onClick = null, float size = 19f, float ty = -1f, string name = "Row")
        {
            if (sel) SelBox(x + 8f, y, w - 16f, h);
            if (onClick != null) Hit(x + 8f, y, w - 16f, h, onClick, name);
            bool two = !string.IsNullOrEmpty(sub);
            T(x + 24f, y + (ty > 0f ? ty : two ? 27f : h * 0.5f + 7f), title, size, sel ? LabTheme.ConOr : LabTheme.ConInk, bold: sel);
            if (two) T(x + 24f, y + 50f, sub, 16f, LabTheme.ConMut);
            if (!string.IsNullOrEmpty(right)) T(x + w - 24f, y + (two ? 27f : h * 0.5f + 7f), right, 16f, rightFill ?? LabTheme.ConMut, anchor: 1);
        }

        /// <summary>選んでいることを示すオレンジの枠。</summary>
        public void SelBox(float x, float y, float w, float h)
        {
            G.Rect(x, y, w, h, A(LabTheme.ConOr, 0.07f));
            G.PolyLine(1.5f, LabTheme.ConOr, true, new Vector2(x + 0.75f, y + 0.75f), new Vector2(x + w - 0.75f, y + 0.75f), new Vector2(x + w - 0.75f, y + h - 0.75f), new Vector2(x + 0.75f, y + h - 0.75f));
        }

        /// <summary>
        /// 斜めの区画のバー（1刻み＝1区画）。on までを塗り、ghostTo までをオレンジの枠で明滅させる（改良案）。
        /// </summary>
        public void SegBar(float x, float y, float w, float h, int count, int on, Color col, int ghostTo = -1, bool glow = false)
        {
            if (count <= 0) return;
            float sw = w / count, slant = Mathf.Min(5f, sw * 0.35f);
            for (int k = 0; k < count; k++)
            {
                float sx = x + k * sw;
                var pts = new[] { new Vector2(sx + slant, y), new Vector2(sx + sw - 2f, y), new Vector2(sx + sw - 2f - slant, y + h), new Vector2(sx, y + h) };
                bool filled = k < on, ghost = !filled && k < ghostTo;
                if (filled) { G.Poly(col, pts); if (glow) G.Glow(col, pts); }
                else if (ghost) Ghost.PolyLine(2f, LabTheme.ConOr, true, pts);
                else G.PolyLine(1.5f, LabTheme.ConCyDeep, true, pts);
            }
        }

        // ---------------- 計器 ----------------
        /// <summary>発明台を上から見た輪。rings＝距離の輪、dirs＝前後左右、orbit＝回せることを示す点線の輪。</summary>
        public void Dial(bool rings = true, bool dirs = true, bool orbit = true)
        {
            G.Circle(C, 304f, 10f, new Color(0.043f, 0.063f, 0.075f, 1f));
            G.Circle(C, 309f, 1f, LabTheme.ConEdgeHi);
            G.Disc(C, 296f, LabTheme.ConDial);
            G.Circle(C, 296f, 1.5f, LabTheme.ConEdge);
            if (rings)
                foreach (float m in new[] { 0.5f, 1.0f, 1.5f })
                {
                    G.Circle(C, m * M, 1.5f, LabTheme.ConCyDeep);
                    if (m < 0.75f) continue;
                    var p = Pol(m * M, 58f);
                    T(p.x + 8f, p.y + 4f, m.ToString("0.0") + " m", 15f, LabTheme.ConMut, halo: true);
                }
            for (int a = 0; a < 360; a += 5)
            {
                bool lng = a % 30 == 0;
                G.Line(Pol(228f, a), Pol(lng ? 214f : 221f, a), lng ? 2f : 1.5f, lng ? LabTheme.ConCy : LabTheme.ConCyDim);
            }
            // 試作品を回せることを示す点線の輪（回転はさせない：キャンバスの中を毎フレーム動かすと、全体のまとめ直しが毎フレーム起きる）
            if (orbit) G.DashedCircle(C, 236f, 2f, LabTheme.ConCyDim, 0.6f, 2.4f);
            if (dirs)
                foreach (var (a, name) in new[] { (270f, "前"), (0f, "右"), (90f, "後"), (180f, "左") })
                {
                    var p = Pol(247f, a);
                    T(p.x, p.y + 6f, name, 15f, LabTheme.ConMut, anchor: 0);
                }
        }

        public static float Ang(float f) => 150f + 240f * Mathf.Clamp01(f);

        /// <summary>外周の弧ゲージ（150°〜390°）。v＝今の値、pass＝合格の線、prev＝前の版の値。</summary>
        public void Gauge(float? v, float max, float? pass = null, string passLabel = null, float? prev = null, string prevLabel = null, Color? col = null)
        {
            G.Arc(C, 270f, 16f, 150f, 390f, LabTheme.ConCyDeep);
            if (v.HasValue && v.Value > 0f && max > 0f) G.GlowArc(C, 270f, 16f, 150f, Ang(v.Value / max), col ?? LabTheme.ConCy);
            for (int k = 0; k <= 10; k++) G.Line(Pol(282f, Ang(k / 10f)), Pol(290f, Ang(k / 10f)), 1.5f, LabTheme.ConMut);
            if (pass.HasValue && max > 0f)
            {
                float a = Ang(pass.Value / max);
                G.Line(Pol(240f, a), Pol(296f, a), 4f, LabTheme.ConInk);
                if (!string.IsNullOrEmpty(passLabel))
                {
                    // 線の手前（角度の小さい側）の内側に置き、文字は計器の中心へ向かって伸ばす
                    float cos = Mathf.Cos(a * Mathf.Deg2Rad);
                    if (Mathf.Abs(cos) < 0.3f) { var p = Pol(196f, a); T(p.x, p.y + 6f, passLabel, 17f, anchor: 0, halo: true); }
                    else { var p = Pol(206f, a - 9f); T(p.x, p.y, passLabel, 17f, anchor: cos > 0f ? 1 : -1, halo: true); }
                }
            }
            if (prev.HasValue && max > 0f)
            {
                float a = Ang(prev.Value / max);
                G.Line(Pol(258f, a), Pol(284f, a), 3f, LabTheme.ConMut);
                if (!string.IsNullOrEmpty(prevLabel)) { var p = Pol(312f, a); T(Mathf.Clamp(p.x, 500f, 1100f), p.y, prevLabel, 15f, LabTheme.ConMut, anchor: 0, halo: true); }
            }
        }

        /// <summary>動いている間だけ描き直す弧（実験の経過）。<see cref="SetLive"/> で進み具合を入れる。</summary>
        public VecGraphic LiveArc() { var g = _fxPool.Take(); g.ClearShapes(); return g; }

        public static void SetLive(VecGraphic g, float fraction)
        {
            if (g == null) return;
            g.ClearShapes();
            if (fraction > 0.002f) g.GlowArc(C, 270f, 16f, 150f, Ang(fraction), LabTheme.ConCy);
        }

        /// <summary>ゲージの下の切れ目に置く大きい数字。</summary>
        public void BigNumber(string text, string unit, string label, Color? fill = null)
        {
            var t = T(C.x, 688f, string.IsNullOrEmpty(unit) ? text : $"{text}<size=40%> {unit}</size>", 76f, fill ?? LabTheme.ConCy, bold: true, anchor: 0, halo: true);
            t.characterSpacing = -2f;
            if (!string.IsNullOrEmpty(label)) T(C.x, 716f, label, 16f, LabTheme.ConMut, anchor: 0);
        }

        /// <summary>数字の代わりに置く言葉（未実験・確認待ち など）。</summary>
        public void BigWord(string text, string label, Color? fill = null)
        {
            T(C.x, 690f, text, 34f, fill ?? LabTheme.ConMut, bold: true, anchor: 0, halo: true);
            if (!string.IsNullOrEmpty(label)) T(C.x, 716f, label, 16f, LabTheme.ConMut, anchor: 0);
        }

        /// <summary>上から見た人（肩と頭）。</summary>
        public void Person()
        {
            G.Disc(C, 0.22f * M, LabTheme.ConBody, squashY: 0.12f / 0.22f);
            G.Disc(C, 0.10f * M, LabTheme.ConBody2);
        }

        /// <summary>防ぐ範囲の円。dashed は、まだ組み立てていない設計。</summary>
        public void Canopy(float radiusPx, bool dashed = false)
        {
            G.Disc(C, radiusPx, A(LabTheme.ConCy, 0.08f));
            if (dashed) G.DashedCircle(C, radiusPx, 3f, LabTheme.ConCy, 10f / radiusPx * Mathf.Rad2Deg, 8f / radiusPx * Mathf.Rad2Deg);
            else G.GlowArc(C, radiusPx, 3f, 0f, 360f, LabTheme.ConCy);
        }
    }

    /// <summary>
    /// 押したまま動かした量を、画像の上の距離（px）で知らせる。コントローラーの光線でもマウスでも同じ。
    /// 光線が当たっている点（ワールド座標）を画像の座標に直して差を取る（当たっていない間は数えない）。
    /// </summary>
    public sealed class ConsoleDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public System.Action<Vector2> Moved;
        Vector2? _last;

        Vector2? Local(PointerEventData e)
        {
            var hit = e.pointerCurrentRaycast;
            // 同じ板の上に当たっているときだけ（隣の板に移ると、面が違うので差が飛ぶ）
            var canvas = GetComponentInParent<Canvas>();
            if (hit.gameObject == null || canvas == null || !hit.gameObject.transform.IsChildOf(canvas.rootCanvas.transform)) return null;
            return transform.InverseTransformPoint(hit.worldPosition);
        }

        public void OnBeginDrag(PointerEventData e) => _last = Local(e);

        public void OnDrag(PointerEventData e)
        {
            var p = Local(e);
            if (p.HasValue && _last.HasValue) Moved?.Invoke(p.Value - _last.Value);
            if (p.HasValue) _last = p;
        }

        public void OnEndDrag(PointerEventData e) => _last = null;
    }
}
