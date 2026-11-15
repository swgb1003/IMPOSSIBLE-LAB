using System.Linq;
using ImpossibleLab.Core;
using NUnit.Framework;

namespace ImpossibleLab.Tests
{
    /// <summary>部品の見た目（素材・機能の色・外観プリセットの塗り）。見た目のみで判定には使わない。</summary>
    public class PartLookTests
    {
        static PartSpec P(string type, string material = "") => new PartSpec { Id = "p", Type = type, Material = material };

        [Test] public void Material_DecidesSurface()
        {
            Assert.AreEqual(PartSurface.Plastic, PartLook.Surface(P("body")));
            Assert.AreEqual(PartSurface.Fabric, PartLook.Surface(P("body", "fabric")));
            Assert.AreEqual(PartSurface.Aluminum, PartLook.Surface(P("frame")));
            Assert.AreEqual(PartSurface.Steel, PartLook.Surface(P("frame", "steel")));
            Assert.AreEqual(PartSurface.Rubber, PartLook.Surface(P("wheel")));
            Assert.AreEqual(PartSurface.Foam, PartLook.Surface(P("cushion")));
            Assert.AreEqual(PartSurface.Glass, PartLook.Surface(P("solar")));
        }

        [Test] public void FunctionParts_KeepFunctionColor_AndCanopyStaysTranslucent()
        {
            Assert.AreEqual(PartSurface.AccentBlue, PartLook.Surface(P("light", "steel")));
            Assert.AreEqual(PartSurface.AccentBlue, PartLook.Surface(P("sensor")));
            Assert.AreEqual(PartSurface.AccentOrange, PartLook.Surface(P("heater")));
            Assert.AreEqual(PartSurface.Canopy, PartLook.Surface(P("canopy", "plastic")));
        }

        [Test] public void Appearance_PaintsOnlyPlastic()
        {
            var orange = LabCatalog.Appearance("safety_orange");
            Assert.AreEqual(orange.BodyRgb, PartLook.Tint(P("body"), "safety_orange"));
            Assert.IsNull(PartLook.Tint(P("body", "aluminum"), "safety_orange"));
            Assert.IsNull(PartLook.Tint(P("wheel"), "safety_orange"));
            Assert.AreEqual(orange.AccentRgb, PartLook.Tint(P("sensor"), "safety_orange"));
            // 不明な外観は既定の外観
            Assert.AreEqual(LabCatalog.Appearance(LabCatalog.DefaultAppearance).BodyRgb, PartLook.Tint(P("panel"), "no_such"));
        }

        [Test] public void EveryPartType_AndMaterial_HasASurface()
        {
            foreach (var t in LabCatalog.PartTypeIds)
                foreach (var m in LabCatalog.Materials.Select(x => x.Id))
                    Assert.DoesNotThrow(() => PartLook.Surface(P(t, m)));
        }
    }
}
