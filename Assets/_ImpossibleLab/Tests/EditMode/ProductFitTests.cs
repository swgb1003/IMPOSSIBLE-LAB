using ImpossibleLab.Core;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>部品キットの角の丸みを実寸で一定にする変形（見た目のみ）。</summary>
    public class ProductFitTests
    {
        const float Eps = 1e-4f;

        static Vector3 World(Vector3 pre, in ProductFit.Frame f) => Vector3.Scale(pre, f.Scale);

        [Test] public void Box_KeepsSize_AndRoundsCornersEqually()
        {
            var f = new ProductFit.Frame(new Vector3(0.6f, 0.05f, 0.4f), false);
            // 外形は DNA の大きさのまま
            var corner = ProductFit.ToMeters(new Vector3(0.5f, 0.5f, 0.5f), f);
            Assert.AreEqual(0.3f, corner.x, Eps); Assert.AreEqual(0.025f, corner.y, Eps); Assert.AreEqual(0.2f, corner.z, Eps);
            // 面取り 0.08 の始まりは、どの向きでも面から同じ距離
            const float b = 0.08f;
            float dx = 0.3f - ProductFit.ToMeters(new Vector3(0.5f - b, 0, 0), f).x;
            float dy = 0.025f - ProductFit.ToMeters(new Vector3(0, 0.5f - b, 0), f).y;
            float dz = 0.2f - ProductFit.ToMeters(new Vector3(0, 0, 0.5f - b), f).z;
            Assert.AreEqual(dx, dy, Eps); Assert.AreEqual(dx, dz, Eps);
            Assert.AreEqual(f.Radius(b), dx, Eps);
            // 薄い向き（高さ 5 cm）の真ん中の帯が残る
            Assert.Less(f.K * ProductFit.Zone, 0.025f);
        }

        [Test] public void Cylinder_StaysRound_AndHeightKept()
        {
            var f = new ProductFit.Frame(new Vector3(0.5f, 0.08f, 0.5f), true); // 直径 0.5・高さ 0.16
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI / 6f;
                var p = ProductFit.ToMeters(new Vector3(Mathf.Cos(a) * 0.5f, 1f, Mathf.Sin(a) * 0.5f), f);
                Assert.AreEqual(0.25f, new Vector2(p.x, p.z).magnitude, Eps, "外周は円のまま");
                Assert.AreEqual(0.08f, p.y, Eps);
            }
            // 楕円の外形も DNA の大きさのまま
            var e = new ProductFit.Frame(new Vector3(0.6f, 0.1f, 0.3f), true);
            Assert.AreEqual(0.3f, ProductFit.ToMeters(new Vector3(0.5f, 0, 0), e).x, Eps);
            Assert.AreEqual(0.15f, ProductFit.ToMeters(new Vector3(0, 0, 0.5f), e).z, Eps);
            Assert.AreEqual(0.1f, ProductFit.ToMeters(new Vector3(0, 1f, 0), e).y, Eps);
        }

        [Test] public void Apply_DividesByScale_AndKeepsBevelNormalAt45Degrees()
        {
            var f = new ProductFit.Frame(new Vector3(0.6f, 0.05f, 0.4f), false);
            // 上と右の面の間の丸み（面取りの中ほど、45 度の法線）
            var u = new Vector3(0.5f - 0.08f + 0.08f * Mathf.Sqrt(0.5f), 0.5f - 0.08f + 0.08f * Mathf.Sqrt(0.5f), 0f);
            var verts = new[] { u };
            var normals = new[] { new Vector3(1, 1, 0).normalized };
            ProductFit.Apply(verts, normals, null, f);
            Assert.AreEqual(0f, (World(verts[0], f) - ProductFit.ToMeters(u, f)).magnitude, Eps);
            // Unity は scale の逆数を法線に掛けるので、その後の向きで比べる
            var n = new Vector3(normals[0].x / f.Scale.x, normals[0].y / f.Scale.y, normals[0].z / f.Scale.z).normalized;
            Assert.AreEqual(0f, (n - new Vector3(1, 1, 0).normalized).magnitude, 1e-3f);
        }

        [Test] public void Lens_IsRound_OnStretchedSensor()
        {
            var scale = new Vector3(0.08f, 0.04f, 0.08f);
            var verts = new[] { new Vector3(0.36f, 0, 0.3f), new Vector3(0, 0.36f, 0.3f), new Vector3(0, 0, 0.35f) };
            ProductFit.ApplyUniform(verts, null, new Vector3(0, 0, 0.3f), scale, 0.04f);
            var x = Vector3.Scale(verts[0], scale); var y = Vector3.Scale(verts[1], scale); var z = Vector3.Scale(verts[2], scale);
            Assert.AreEqual(0.36f * 0.04f, x.x, Eps); Assert.AreEqual(0.36f * 0.04f, y.y, Eps);
            Assert.AreEqual(0.3f * 0.08f, x.z, Eps, "レンズは前面に付いたまま");
            Assert.AreEqual(0.3f * 0.08f + 0.05f * 0.04f, z.z, Eps);
        }
    }
}
