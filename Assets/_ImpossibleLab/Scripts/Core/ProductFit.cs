using UnityEngine;

namespace ImpossibleLab.Core
{
    /// <summary>
    /// 部品キット（単位の大きさのメッシュ）を DNA の大きさへ引き伸ばすと、角の丸みが縦横で潰れる。
    /// 外側の幅 Zone（角の丸みが入る帯）だけは縦横同じ倍率 K で縮め、残りの真ん中を伸ばして大きさを合わせる（見た目のみ。寸法は DNA のまま）。
    /// 返す頂点は、部品の拡大縮小（Transform の scale）を掛ける前の座標。組み立て・枠表示・実験の計算は従来どおり DNA の大きさを使う。
    /// </summary>
    public static class ProductFit
    {
        /// <summary>キットの単位での、外側から角の丸みが入る幅（キットの面取りは 0.015〜0.1）。</summary>
        public const float Zone = 0.12f;
        /// <summary>角の帯の倍率の上限（キットの単位 1 あたりの m）。面取り 0.08 の箱で角の丸みは最大 2 cm。</summary>
        public const float MaxK = 0.25f;

        public readonly struct Frame
        {
            public readonly bool Cylinder;
            /// <summary>部品の Transform に掛ける大きさ（円柱は高さを半分にしたもの）。</summary>
            public readonly Vector3 Scale;
            /// <summary>キットの単位での半分の大きさ（箱 0.5、円柱の高さ 1）。</summary>
            public readonly Vector3 UnitHalf;
            /// <summary>実寸の半分の大きさ（m）。</summary>
            public readonly Vector3 Half;
            /// <summary>角の帯の倍率（キットの単位 1 あたりの m）。</summary>
            public readonly float K;

            public Frame(Vector3 scale, bool cylinder)
            {
                Cylinder = cylinder;
                Scale = new Vector3(Mathf.Max(1e-4f, Mathf.Abs(scale.x)), Mathf.Max(1e-4f, Mathf.Abs(scale.y)), Mathf.Max(1e-4f, Mathf.Abs(scale.z)));
                UnitHalf = cylinder ? new Vector3(0.5f, 1f, 0.5f) : new Vector3(0.5f, 0.5f, 0.5f);
                Half = Vector3.Scale(Scale, UnitHalf);
                float minS = Mathf.Min(Scale.x, Mathf.Min(Scale.y, Scale.z));
                float minH = Mathf.Min(Half.x, Mathf.Min(Half.y, Half.z));
                // 一番薄い向きの倍率の 1.5 倍まで（小さな部品が丸くなりすぎない）。真ん中の帯が残るよう、角の帯は半分の大きさの 9 割まで
                K = Mathf.Min(MaxK, Mathf.Min(1.5f * minS, 0.9f * minH / Zone));
            }

            /// <summary>実寸での角の丸み（キットの面取り bevel に対して）。</summary>
            public float Radius(float bevel) => K * bevel;
        }

        /// <summary>1つの向きの変換：外側の帯は K 倍、真ん中は残りの長さに合わせて伸ばす。</summary>
        static float Axis(float u, float unitHalf, float half, float k)
        {
            float a = Mathf.Abs(u), innerU = unitHalf - Zone, innerR = half - k * Zone;
            float r = a <= innerU ? a * innerR / innerU : innerR + (a - innerU) * k;
            return u < 0 ? -r : r;
        }

        /// <summary>キットの単位の点を実寸（m、部品の中心が原点）へ。円柱は半径の向きで変換するので、断面の円・楕円は保つ。</summary>
        public static Vector3 ToMeters(Vector3 u, in Frame f)
        {
            float y = Axis(u.y, f.UnitHalf.y, f.Half.y, f.K);
            if (!f.Cylinder) return new Vector3(Axis(u.x, 0.5f, f.Half.x, f.K), y, Axis(u.z, 0.5f, f.Half.z, f.K));
            float r = Mathf.Sqrt(u.x * u.x + u.z * u.z), innerU = 0.5f - Zone;
            float ax = f.Half.x - f.K * Zone, az = f.Half.z - f.K * Zone;
            if (r <= innerU) return new Vector3(u.x * ax / innerU, y, u.z * az / innerU);
            float c = u.x / r, s = u.z / r;
            var n = new Vector2(c / ax, s / az).normalized; // 内側の楕円の外向きの法線
            float off = f.K * (r - innerU);
            return new Vector3(ax * c + n.x * off, y, az * s + n.y * off);
        }

        /// <summary>
        /// 頂点・法線を変換する（法線は変換の逆転置で向きを直す）。返す座標は Transform の scale で割ったもの。
        /// tangents は null でよい。
        /// </summary>
        public static void Apply(Vector3[] verts, Vector3[] normals, Vector4[] tangents, in Frame f)
        {
            const float e = 1e-4f;
            var inv = new Vector3(1f / f.Scale.x, 1f / f.Scale.y, 1f / f.Scale.z);
            for (int i = 0; i < verts.Length; i++)
            {
                var u = verts[i];
                // 変換の傾き（列＝各向きの微分）。Transform の scale で割った座標で求める
                var cx = Vector3.Scale(ToMeters(u + new Vector3(e, 0, 0), f) - ToMeters(u - new Vector3(e, 0, 0), f), inv) / (2 * e);
                var cy = Vector3.Scale(ToMeters(u + new Vector3(0, e, 0), f) - ToMeters(u - new Vector3(0, e, 0), f), inv) / (2 * e);
                var cz = Vector3.Scale(ToMeters(u + new Vector3(0, 0, e), f) - ToMeters(u - new Vector3(0, 0, e), f), inv) / (2 * e);
                verts[i] = Vector3.Scale(ToMeters(u, f), inv);
                if (normals != null && i < normals.Length)
                {
                    var n = normals[i];
                    // 逆転置 J^-T n は、列の外積の組み合わせに比例する（行列式は正）
                    var m = n.x * Vector3.Cross(cy, cz) + n.y * Vector3.Cross(cz, cx) + n.z * Vector3.Cross(cx, cy);
                    normals[i] = m.sqrMagnitude > 1e-20f ? m.normalized : n;
                }
                if (tangents != null && i < tangents.Length)
                {
                    var t = tangents[i];
                    var m = cx * t.x + cy * t.y + cz * t.z;
                    if (m.sqrMagnitude > 1e-20f) { m.Normalize(); tangents[i] = new Vector4(m.x, m.y, m.z, t.w); }
                }
            }
        }

        /// <summary>
        /// 丸い飾り（センサーのレンズなど）を、部品が縦横で違う大きさでも真円になるようにする。
        /// anchor（キットの単位の点）を中心に、実寸で size 倍（等方）になる座標を返す（Transform の scale で割ったもの）。
        /// </summary>
        public static void ApplyUniform(Vector3[] verts, Vector3[] normals, Vector3 anchor, Vector3 scale, float size)
        {
            var s = new Vector3(Mathf.Max(1e-4f, Mathf.Abs(scale.x)), Mathf.Max(1e-4f, Mathf.Abs(scale.y)), Mathf.Max(1e-4f, Mathf.Abs(scale.z)));
            for (int i = 0; i < verts.Length; i++)
            {
                var d = verts[i] - anchor;
                verts[i] = anchor + new Vector3(d.x * size / s.x, d.y * size / s.y, d.z * size / s.z);
                if (normals != null && i < normals.Length)
                {
                    var n = Vector3.Scale(normals[i], s);
                    normals[i] = n.sqrMagnitude > 1e-20f ? n.normalized : normals[i];
                }
            }
        }
    }
}
