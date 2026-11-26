using ImpossibleLab.Core;
using NUnit.Framework;
using UnityEngine;

namespace ImpossibleLab.Tests
{
    /// <summary>部品どうしのすき間の陰り（見た目のみ）。シェーダーと同じ式を CPU で確かめる。</summary>
    public class PartAoTests
    {
        const float Eps = 1e-4f;

        static void Near(Vector4 expected, Vector4 actual)
        {
            for (int i = 0; i < 4; i++) Assert.AreEqual(expected[i], actual[i], Eps, $"成分 {i}");
        }

        [Test] public void Reach_IsSixPercent_Clamped()
        {
            Assert.AreEqual(0.03f, PartAo.Reach(0.5f), Eps);
            Assert.AreEqual(0.012f, PartAo.Reach(0.05f), Eps, "小さい発明でも 1.2 cm");
            Assert.AreEqual(0.05f, PartAo.Reach(2f), Eps, "大きい発明でも 5 cm まで");
        }

        [Test] public void Distance_BoxAndCylinder()
        {
            var box = new Vector4(0.1f, 0.2f, 0.3f, 0f);
            Assert.AreEqual(0.05f, PartAo.Distance(new Vector3(0.15f, 0, 0), box), Eps);
            Assert.AreEqual(-0.1f, PartAo.Distance(Vector3.zero, box), Eps, "中は負（いちばん近い面まで）");
            var cyl = new Vector4(0.1f, 0.2f, 0f, 1f);
            Assert.AreEqual(0.05f, PartAo.Distance(new Vector3(0.15f * 0.6f, 0, 0.15f * 0.8f), cyl), Eps, "円柱は横のどの向きでも半径から測る");
            Assert.AreEqual(0.1f, PartAo.Distance(new Vector3(0, 0.3f, 0), cyl), Eps);
        }

        [Test] public void BoxOf_RemovesScale_AndKeepsRotation()
        {
            var m = Matrix4x4.TRS(new Vector3(1, 2, 3), Quaternion.Euler(0, 90, 0), new Vector3(0.2f, 0.4f, 0.6f));
            PartAo.BoxOf(m, new Bounds(Vector3.zero, Vector3.one), false, out var fromWorld, out var half);
            Near(new Vector4(0.1f, 0.2f, 0.3f, 0f), half);
            // 部品の +x の面の中心（世界では -z の向き）は、箱の座標で x＝半分の幅
            var face = m.MultiplyPoint3x4(new Vector3(0.5f, 0, 0));
            Assert.AreEqual(0.1f, fromWorld.MultiplyPoint3x4(face).x, Eps);
            PartAo.BoxOf(Matrix4x4.Scale(new Vector3(0.3f, 0.1f, 0.3f)), new Bounds(Vector3.zero, Vector3.one), true, out _, out var round);
            Near(new Vector4(0.15f, 0.05f, 0f, 1f), round);
        }

        [Test] public void Gap_IsZeroWhenTouching()
        {
            var a = new Bounds(Vector3.zero, Vector3.one);
            Assert.AreEqual(0f, PartAo.Gap(a, new Bounds(new Vector3(1f, 0, 0), Vector3.one)), Eps);
            Assert.AreEqual(0.5f, PartAo.Gap(a, new Bounds(new Vector3(1.5f, 0, 0), Vector3.one)), Eps);
        }

        [Test] public void Occlusion_DarkensNextToNeighbor_AndFadesWithDistance()
        {
            // 床の板（上の面 y=0）の上に、10 cm 角の箱が置いてある。板の上の面の点の明るさを、箱からの距離ごとに測る
            const float reach = 0.03f;
            var boxes = new[] { Matrix4x4.Translate(new Vector3(0, -0.05f, 0)) }; // 板の座標 → 箱の座標（箱の中心は y=0.05）
            var halves = new[] { new Vector4(0.05f, 0.05f, 0.05f, 0f) };
            float At(float x) => PartAo.Occlusion(new Vector3(x, 0, 0), Vector3.up, boxes, halves, 1, reach, 0.85f);
            float touching = At(0.051f), near = At(0.065f), far = At(0.2f);
            Assert.Less(touching, 0.6f, "付け根は暗い");
            Assert.Less(touching, near);
            Assert.Less(near, 1f);
            Assert.AreEqual(1f, far, Eps, "届く距離より離れれば陰りなし");
            Assert.AreEqual(1f, PartAo.Occlusion(Vector3.zero, Vector3.up, boxes, halves, 0, reach, 0.85f), Eps, "隣が無ければそのまま");
        }
    }
}
