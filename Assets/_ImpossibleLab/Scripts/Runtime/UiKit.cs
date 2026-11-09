using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace ImpossibleLab
{
    public enum ButtonStyle { Primary, Secondary, Ghost, Danger, Selected }

    /// <summary>World Space uGUI の部品生成。1 Canvas px = 1 mm。すべての色は LabTheme から。</summary>
    public static class UiKit
    {
        public const float MetersPerPixel = 0.001f;

        public static Canvas CreateCanvas(string name, Transform parent, Vector2 sizePx, Camera eventCamera)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler),
                typeof(UnityEngine.UI.GraphicRaycaster), typeof(TrackedDeviceGraphicRaycaster));
            go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = eventCamera;
            go.GetComponent<UnityEngine.UI.CanvasScaler>().dynamicPixelsPerUnit = 2f;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = sizePx;
            rt.localScale = Vector3.one * MetersPerPixel;
            return canvas;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom); rt.offsetMax = new Vector2(-right, -top);
        }

        public static UnityEngine.UI.Image Image(string name, Transform parent, Color color, bool raycast = false)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        /// <summary>不透明パネル（細い枠線付き）。角丸は使わない。</summary>
        public static RectTransform Panel(string name, Transform parent, Color? color = null, bool border = true)
        {
            var img = Image(name, parent, color ?? LabTheme.PanelAlpha, raycast: true);
            if (border)
            {
                var o = img.gameObject.AddComponent<UnityEngine.UI.Outline>();
                o.effectColor = LabTheme.Divider; o.effectDistance = new Vector2(2, -2);
            }
            return img.rectTransform;
        }

        public static UnityEngine.UI.VerticalLayoutGroup VList(RectTransform rt, int spacing = LabTheme.Space2, int padding = 0, TextAnchor align = TextAnchor.UpperLeft)
        {
            var v = rt.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            v.spacing = spacing; v.padding = new RectOffset(padding, padding, padding, padding);
            v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            v.childAlignment = align;
            return v;
        }

        public static UnityEngine.UI.HorizontalLayoutGroup HList(RectTransform rt, int spacing = LabTheme.Space2, int padding = 0, TextAnchor align = TextAnchor.MiddleLeft, bool expand = false)
        {
            var h = rt.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            h.spacing = spacing; h.padding = new RectOffset(padding, padding, padding, padding);
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = expand; h.childForceExpandHeight = false;
            h.childAlignment = align;
            return h;
        }

        public static UnityEngine.UI.LayoutElement Size(Component c, float w = -1, float h = -1, float flexW = -1, float flexH = -1)
        {
            var le = c.GetComponent<UnityEngine.UI.LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<UnityEngine.UI.LayoutElement>(); // Unityオブジェクトに ?? は使わない
            if (w >= 0) { le.preferredWidth = w; le.minWidth = w; }
            if (h >= 0) { le.preferredHeight = h; le.minHeight = h; }
            if (flexW >= 0) le.flexibleWidth = flexW;
            if (flexH >= 0) le.flexibleHeight = flexH;
            return le;
        }

        public static RectTransform Row(string name, Transform parent, int spacing = LabTheme.Space2, float height = -1)
        {
            var rt = Rect(name, parent);
            HList(rt, spacing);
            if (height > 0) Size(rt, h: height);
            return rt;
        }

        public static RectTransform Column(string name, Transform parent, int spacing = LabTheme.Space2, float width = -1)
        {
            var rt = Rect(name, parent);
            VList(rt, spacing);
            if (width > 0) Size(rt, w: width);
            return rt;
        }

        public static TextMeshProUGUI Text(Transform parent, string text, float size = LabTheme.FontBody, Color? color = null,
            bool bold = false, TextAlignmentOptions align = TextAlignmentOptions.TopLeft, string name = "Text")
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = bold && LabTheme.FontBold != null ? LabTheme.FontBold : LabTheme.Font;
            t.fontSize = size;
            t.color = color ?? LabTheme.TextPrimary;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow; // Noto Sans JPは行高が大きく、Ellipsisだと短い枠で全文が消える
            t.raycastTarget = false;
            t.richText = true;
            t.text = text;
            return t;
        }

        /// <summary>見出し：左のアクセント線＋文字（参照画像のパネル見出しに合わせる）。</summary>
        public static RectTransform Heading(Transform parent, string text, float size = LabTheme.FontHeading)
        {
            var row = Row("Heading", parent, LabTheme.Space1, size * 1.5f + 6);
            var bar = Image("Bar", row, LabTheme.AccentPrimary);
            Size(bar, w: 5, h: size + 4);
            var t = Text(row, text, size, LabTheme.TextPrimary, bold: true, align: TextAlignmentOptions.MidlineLeft);
            Size(t, flexW: 1);
            return row;
        }

        public static UnityEngine.UI.Button Button(Transform parent, string label, UnityAction onClick, ButtonStyle style = ButtonStyle.Secondary,
            float width = -1, float height = LabTheme.ButtonHeight, float fontSize = LabTheme.FontButton)
        {
            var img = Image("Button", parent, Color.white, raycast: true);
            img.gameObject.name = "Btn_" + Sanitize(label);
            var btn = img.gameObject.AddComponent<UnityEngine.UI.Button>();
            btn.targetGraphic = img;
            ApplyStyle(btn, style);
            var t = Text(img.transform, label, fontSize, TextColor(style), bold: style == ButtonStyle.Primary || style == ButtonStyle.Selected,
                align: TextAlignmentOptions.Center, name: "Label");
            float pad = width > 0 && width < 120 ? 2 : LabTheme.Space2;
            Stretch(t.rectTransform, pad, pad, 2, 2);
            t.textWrappingMode = TextWrappingModes.Normal;
            if (onClick != null) btn.onClick.AddListener(onClick);
            Size(btn, width, height);
            if (width < 0) Size(btn, flexW: 1);
            return btn;
        }

        public static void ApplyStyle(UnityEngine.UI.Button btn, ButtonStyle style)
        {
            Color baseC;
            switch (style)
            {
                case ButtonStyle.Primary: baseC = LabTheme.AccentPrimary; break;
                case ButtonStyle.Danger: baseC = LabTheme.StateError; break;
                case ButtonStyle.Selected: baseC = LabTheme.AccentLight; break;
                case ButtonStyle.Ghost: baseC = LabTheme.BgDeep; break;
                default: baseC = LabTheme.BgPanel; break;
            }
            var cb = btn.colors;
            cb.normalColor = baseC;
            cb.highlightedColor = Color.Lerp(baseC, LabTheme.AccentLight, style == ButtonStyle.Primary ? 0.45f : 0.30f);
            cb.pressedColor = Color.Lerp(baseC, LabTheme.BgDeep, 0.35f);
            cb.selectedColor = cb.highlightedColor;
            cb.disabledColor = new Color(LabTheme.BgPanel.r, LabTheme.BgPanel.g, LabTheme.BgPanel.b, 0.5f);
            cb.colorMultiplier = 1f; cb.fadeDuration = 0.08f;
            btn.colors = cb;
            var outline = btn.GetComponent<UnityEngine.UI.Outline>();
            if (outline == null) outline = btn.gameObject.AddComponent<UnityEngine.UI.Outline>();
            outline.effectDistance = new Vector2(2, -2);
            outline.effectColor = style == ButtonStyle.Secondary ? new Color(LabTheme.AccentLight.r, LabTheme.AccentLight.g, LabTheme.AccentLight.b, 0.45f)
                : style == ButtonStyle.Selected ? LabTheme.TextPrimary : new Color(0, 0, 0, 0);
            var label = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.color = TextColor(style);
        }

        static Color TextColor(ButtonStyle s) => s == ButtonStyle.Primary || s == ButtonStyle.Selected ? LabTheme.BgDeep : LabTheme.TextPrimary;

        public static void SetInteractable(UnityEngine.UI.Button b, bool on)
        {
            b.interactable = on;
            var t = b.GetComponentInChildren<TextMeshProUGUI>();
            if (t != null) t.alpha = on ? 1f : 0.45f;
        }

        public static TMP_InputField Input(Transform parent, string placeholder, int limit, Action<string> onChanged)
        {
            var img = Image("InputField", parent, LabTheme.BgDeep, raycast: true);
            img.gameObject.SetActive(false);
            var o = img.gameObject.AddComponent<UnityEngine.UI.Outline>();
            o.effectColor = LabTheme.AccentLight; o.effectDistance = new Vector2(2, -2);
            var area = Rect("TextArea", img.transform);
            Stretch(area, LabTheme.Space3, LabTheme.Space3, 8, 8);
            area.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            var ph = Text(area, placeholder, LabTheme.FontBody, LabTheme.TextSecondary, align: TextAlignmentOptions.MidlineLeft, name: "Placeholder");
            ph.fontStyle = FontStyles.Italic; ph.alpha = 0.7f; Stretch(ph.rectTransform);
            var txt = Text(area, "", LabTheme.FontBody, LabTheme.TextPrimary, align: TextAlignmentOptions.MidlineLeft, name: "Text");
            txt.textWrappingMode = TextWrappingModes.NoWrap; Stretch(txt.rectTransform);
            var input = img.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = area; input.textComponent = txt; input.placeholder = ph;
            input.fontAsset = LabTheme.Font; input.pointSize = LabTheme.FontBody;
            input.characterLimit = limit; input.lineType = TMP_InputField.LineType.SingleLine;
            input.targetGraphic = img;
            if (onChanged != null) input.onValueChanged.AddListener(s => onChanged(s));
            img.gameObject.SetActive(true);
            return input;
        }

        public static RectTransform Divider(Transform parent)
        {
            var img = Image("Divider", parent, LabTheme.Divider);
            Size(img, h: 2);
            return img.rectTransform;
        }

        public static TextMeshProUGUI Badge(Transform parent, string text, Color bg, Color fg, float width = -1)
        {
            var img = Image("Badge", parent, bg);
            var t = Text(img.transform, text, LabTheme.FontSmall, fg, bold: true, align: TextAlignmentOptions.Center);
            Stretch(t.rectTransform, LabTheme.Space1, LabTheme.Space1, 2, 2);
            Size(img, width, 40);
            return t;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var c = t.GetChild(i).gameObject;
                c.SetActive(false);
                UnityEngine.Object.Destroy(c);
            }
        }

        static string Sanitize(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var ch in s) if (!char.IsWhiteSpace(ch) && ch != '/') sb.Append(ch);
            return sb.Length > 24 ? sb.ToString(0, 24) : sb.ToString();
        }
    }
}
