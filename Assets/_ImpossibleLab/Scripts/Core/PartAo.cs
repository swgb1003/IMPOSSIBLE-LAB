using UnityEngine;

namespace ImpossibleLab.Core
{
    /// <summary>
    /// 部品どうしのすき間の陰り（AO）の計算（見た目のみ）。隣の部品を箱か円柱で近似し、面から外へ3段の位置で距離を測る。
    /// Occlusion は Art/Shaders/PartLit.shader の IlabPartAO と同じ式（シェーダーを変えたらここも変える）。
    /// </summary>
    public static class PartAo
    {
        public const int Steps = 3;

        /// <summary>陰りが届く距離（m）：発明のいちばん長い辺の 6%（1.2〜5 cm）。</summary>
        public static float Reach(float size) => Mathf.Clamp(size * 0.06f, 0.012f, 0.05f);

        /// <summary>
        /// 箱（または円柱）：箱の座標への変換（拡大は含まない＝m 単位）と、半分の大きさ（w：0＝箱、1＝円柱。円柱は y が軸で x が半径）。
        /// round は、横の2辺がほぼ同じとき円柱として扱う。
        /// </summary>
        public static void BoxOf(Matrix4x4 localToWorld, Bounds meshBounds, bool round, out Matrix4x4 boxFromWorld, out Vector4 half)
        {
            Vector3 ax = localToWorld.GetColumn(0), ay = localToWorld.GetColumn(1), az = localToWorld.GetColumn(2);
            Vector3 size = new Vector3(ax.magnitude, ay.magnitude, az.magnitude);
            Vector3 center = localToWorld.MultiplyPoint3x4(meshBounds.center);
            var rot = Quaternion.LookRotation(az.sqrMagnitude > 1e-12f ? az : Vector3.forward, ay.sqrMagnitude > 1e-12f ? ay : Vector3.up);
            boxFromWorld = Matrix4x4.TRS(center, rot, Vector3.one).inverse;
            Vector3 e = Vector3.Scale(meshBounds.extents, size);
            bool cylinder = round && Mathf.Abs(e.x - e.z) <= 0.1f * Mathf.Max(e.x, e.z);
            half = cylinder ? new Vector4(Mathf.Max(e.x, e.z), e.y, 0f, 1f) : new Vector4(e.x, e.y, e.z, 0f);
        }

        /// <summary>2つの範囲のすき間（重なっていれば 0）。</summary>
        public static float Gap(Bounds a, Bounds b) => Vector3.Max(Vector3.zero, Vector3.Max(a.min - b.max, b.min - a.max)).magnitude;

        /// <summary>箱の座標の点から箱（円柱）の表面までの距離（中は負）。</summary>
        public static float Distance(Vector3 p, Vector4 half)
        {
            if (half.w > 0.5f)
            {
                var d = new Vector2(Mathf.Abs(new Vector2(p.x, p.z).magnitude) - half.x, Mathf.Abs(p.y) - half.y);
                return Mathf.Min(Mathf.Max(d.x, d.y), 0f) + Vector2.Max(d, Vector2.zero).magnitude;
            }
            var q = new Vector3(Mathf.Abs(p.x) - half.x, Mathf.Abs(p.y) - half.y, Mathf.Abs(p.z) - half.z);
            return Vector3.Max(q, Vector3.zero).magnitude + Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f);
        }

        /// <summary>
        /// 明るさの割合（1＝陰りなし）。p・n は部品自身の座標の点と外向きの向き（m 単位の長さ）、boxes は部品自身の座標 → 隣の箱の座標。
        /// </summary>
        public static float Occlusion(Vector3 p, Vector3 n, Matrix4x4[] boxes, Vector4[] halves, int count, float reach, float strength)
        {
            if (count <= 0) return 1f;
            float occ = 0f, weight = 1f;
            for (int j = 1; j <= Steps; j++)
            {
                float h = reach * j / Steps;
                var q = p + n * h;
                float d = 1e5f;
                for (int i = 0; i < count; i++) d = Mathf.Min(d, Distance(boxes[i].MultiplyPoint3x4(q), halves[i]));
                occ += Mathf.Clamp(h - d, 0f, h) * weight;
                weight *= 0.6f;
            }
            return 1f - Mathf.Clamp01(occ / (reach * 1.093f)) * strength;
        }
    }
}
