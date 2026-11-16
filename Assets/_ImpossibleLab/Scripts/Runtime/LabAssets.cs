using ImpossibleLab.Core;
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
        // 部品の素材（DNA の material）ごとの見た目
        public Material PartAluminum;
        public Material PartSteel;
        public Material PartFabric;
        public Material PartFoam;
        public Material PartRubber;
        // 研究室のモデル（Blender 制作、ArtSource/blender）用
        public Material GlowOrange;    // 床・台座の橙の光の線
        public Material LightPanel;    // 天井の照明面
        public GameObject CentralDevice; // 中央装置（発明台・ホログラム投影部・天井ユニット・床リング）
        public GameObject LabRoom;
        public GameObject LabDetails;    // 小物（配管・通気口・操作パネル・床の点検口・注意帯。ArtSource/blender/lab_details.py）       // 床・壁・窓・天井（見た目のみ。当たり判定はシーンの箱）
        public GameObject RobotArm;      // ARM_Base／ARM_Upper（肩）＞ARM_Fore（肘）
        public GameObject Workbench;
        public GameObject GalleryCase;
        public GameObject Gate;          // GATE_Frame／GATE_DoorL／GATE_DoorR
        public Material FloorConcrete;   // Poly Haven concrete_floor_worn_001（CC0）
        public Material Screen;          // モニターの画面
        public Material Sky;
        public GameObject Plant;         // 観葉植物（葉：Poly Haven potted_plant_02、CC0。鉢は自作）
        public GameObject PartKit;       // 部品キット（ArtSource/blender/part_kit.py、DECISIONS D-26）
        public Material PartCanopy;
        public Material LensGlow;        // センサーのレンズの光のにじみ（加算の半透明・円形のぼかし）      // 部品のキャノピー（水色の半透明。Canopy より見えやすい）
        public Material PlantLeaves;     // 葉（切り抜き・両面）             // 窓の外の空（Poly Haven kloofendal_43d_clear、CC0）

        /// <summary>部品の見た目に使うマテリアル（未生成の素材は近いものに代える）。</summary>
        public Material For(PartSurface s)
        {
            switch (s)
            {
                case PartSurface.Aluminum: return PartAluminum != null ? PartAluminum : MatteMetal;
                case PartSurface.Steel: return PartSteel != null ? PartSteel : DarkMetal;
                case PartSurface.Fabric: return PartFabric != null ? PartFabric : RobotWhite;
                case PartSurface.Foam: return PartFoam != null ? PartFoam : RobotWhite;
                case PartSurface.Rubber: return PartRubber != null ? PartRubber : DarkMetal;
                case PartSurface.Glass: return Glass;
                case PartSurface.Canopy: return PartCanopy != null ? PartCanopy : Canopy;
                case PartSurface.AccentBlue: return AccentBlue;
                case PartSurface.AccentOrange: return AccentOrange;
                default: return RobotWhite;
            }
        }
    }
}
