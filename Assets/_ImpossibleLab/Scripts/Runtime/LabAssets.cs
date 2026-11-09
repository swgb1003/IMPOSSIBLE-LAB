using TMPro;
using UnityEngine;

namespace ImpossibleLab
{
    /// <summary>エディタのセットアップで生成した素材への参照。ランタイムで任意パスのアセットをロードしない。</summary>
    [CreateAssetMenu(menuName = "IMPOSSIBLE LAB/Lab Assets")]
    public sealed class LabAssets : ScriptableObject
    {
        public TMP_FontAsset Font;
        public TMP_FontAsset FontBold;
        public Material Concrete;      // 白系コンクリート
        public Material MatteMetal;    // マット金属
        public Material DarkMetal;
        public Material RobotWhite;
        public Material Glass;
        public Material AccentBlue;    // イベント時のみ発光を強める
        public Material AccentOrange;
        public Material Canopy;        // 半透明キャノピー
        public Material Mannequin;
        public Material MannequinHit;
        public Material WetZone;       // 判定用シルエット
        public Material RainParticle;
        public Material Fader;
        public Material Ground;
        public TextAsset RainSample;
    }
}
