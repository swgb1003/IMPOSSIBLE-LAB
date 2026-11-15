namespace ImpossibleLab.Core
{
    /// <summary>部品の見た目の種類（表示用マテリアルの選び分け）。</summary>
    public enum PartSurface { Plastic, Aluminum, Steel, Fabric, Foam, Rubber, Glass, Canopy, AccentBlue, AccentOrange }

    /// <summary>
    /// 部品の見た目を DNA の素材・部品タイプ・外観プリセットから決める（見た目のみ。判定・性能には使わない）。
    /// ・センサー・照明・冷却は青、加熱・姿勢安定は橙（機能を色で示す）。
    /// ・キャノピーは下の人や視界が見えるよう半透明のまま。
    /// ・それ以外は素材ごとの見た目。外観プリセットの本体色は塗装できる樹脂だけに塗る。
    /// </summary>
    public static class PartLook
    {
        public static PartSurface Surface(PartSpec part)
        {
            switch (part.Type)
            {
                case "canopy": return PartSurface.Canopy;
                case "sensor": case "light": case "cooler": return PartSurface.AccentBlue;
                case "heater": case "stabilizer": return PartSurface.AccentOrange;
            }
            switch (part.MaterialOrDefault)
            {
                case "aluminum": return PartSurface.Aluminum;
                case "steel": return PartSurface.Steel;
                case "fabric": return PartSurface.Fabric;
                case "foam": return PartSurface.Foam;
                case "rubber": return PartSurface.Rubber;
                case "glass": return PartSurface.Glass;
                default: return PartSurface.Plastic;
            }
        }

        /// <summary>外観プリセットで塗る色（0xRRGGBB）。塗らない部品は null。</summary>
        public static int? Tint(PartSpec part, string appearanceId)
        {
            var look = LabCatalog.Appearance(appearanceId) ?? LabCatalog.Appearance(LabCatalog.DefaultAppearance);
            switch (Surface(part))
            {
                case PartSurface.Plastic: return look.BodyRgb;
                case PartSurface.AccentBlue: return part.Type == "cooler" ? (int?)null : look.AccentRgb;
                case PartSurface.AccentOrange: return part.Type == "stabilizer" ? look.AccentRgb : (int?)null;
                default: return null;
            }
        }
    }
}
