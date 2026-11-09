using TMPro;
using UnityEngine;

namespace ImpossibleLab
{
    /// <summary>デザイントークン（仕様21.2）。画面固有の色をハードコードせず、ここから参照する。</summary>
    public static class LabTheme
    {
        public static readonly Color BgDeep = Hex(0x0F172A);
        public static readonly Color BgPanel = Hex(0x1E293B);
        public static readonly Color AccentPrimary = Hex(0x0EA5E9);
        public static readonly Color AccentLight = Hex(0x38BDF8);
        public static readonly Color AccentWarning = Hex(0xF59E0B);
        public static readonly Color TextPrimary = Hex(0xF8FAFC);
        public static readonly Color TextSecondary = Hex(0xCBD5E1);
        public static readonly Color StateError = Hex(0xEF4444);
        public static readonly Color SurfaceLab = Hex(0xDCE2E7);

        // 派生（トークンから合成）
        public static readonly Color PanelAlpha = new Color(BgDeep.r, BgDeep.g, BgDeep.b, 0.94f);
        public static readonly Color Row = Color.Lerp(BgDeep, BgPanel, 0.55f);
        public static readonly Color Divider = new Color(TextSecondary.r, TextSecondary.g, TextSecondary.b, 0.18f);
        public static readonly Color Disabled = new Color(TextSecondary.r, TextSecondary.g, TextSecondary.b, 0.35f);
        public static readonly Color Success = AccentLight;

        // 文字サイズ（Canvas 1px = 1mm 基準。視距離1.0〜1.5mでの初期値。実機で要調整）
        public const float FontTitle = 40f, FontHeading = 30f, FontBody = 26f, FontSmall = 22f, FontButton = 26f;
        // 余白：8px 単位
        public const int Space1 = 8, Space2 = 16, Space3 = 24, Space4 = 32;
        public const float ButtonHeight = 64f; // 64mm。レイ操作で十分な当たり判定

        public static TMP_FontAsset Font;
        public static TMP_FontAsset FontBold;

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
        public static string ToHex(Color c) => ColorUtility.ToHtmlStringRGB(c);
    }
}
