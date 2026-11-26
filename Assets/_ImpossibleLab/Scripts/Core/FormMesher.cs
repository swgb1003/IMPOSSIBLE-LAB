using System.Collections.Generic;
using UnityEngine;

namespace ImpossibleLab.Core
{
    /// <summary>
    /// 部品の形のメッシュを作る。
    /// ・DNA の形のうち、組み立てるときに作るもの（ドーム・円すい・輪）。
    /// ・形の仕上げ（見た目のみ）：薄い円柱の覆い（キャノピー）→ 傘のように中央が盛り上がったドーム（DNA の形は円柱のまま）。
    /// </summary>
    public static class FormMesher
    {
        public sealed class Result
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<int> Triangles = new List<int>();
        }

        /// <summary>ドームにする覆い：厚さが直径の 1/5 より薄い円柱。</summary>
        public static bool IsDomeCanopy(PartSpec p) =>
            p.Type == "canopy" && p.ShapeOrDefault == "cylinder" && Mathf.Abs(p.Scale.y) < 0.2f * Mathf.Max(Mathf.Abs(p.Scale.x), Mathf.Abs(p.Scale.z));

        /// <summary>ドームの高さ：半径の 28%（最大 25 cm）。</summary>
        public static float DomeHeight(float radius) => Mathf.Min(radius * 0.28f, 0.25f);

        /// <summary>
        /// 傘の形の板（m 単位、部品の中心が原点）。縁は半径 R・高さ ±t/2 で、元の円盤の縁と同じ場所。中央は縁から H だけ盛り上がる。
        /// 外の面・内の面・縁の帯からなる。面は外から見て Unity の表。
        /// </summary>
        public static Result Dome(float radius, float thickness, float height, int rings = 14, int segments = 56)
        {
            var r = new Result();
            float R = Mathf.Max(1e-4f, radius), t = Mathf.Max(1e-4f, thickness), H = Mathf.Max(1e-4f, height);
            float rho = (R * R + H * H) / (2f * H); // 球の半径
            float Cap(float x) => Mathf.Sqrt(Mathf.Max(0f, rho * rho - x * x)) - (rho - H);
            Vector3 SphereNormal(float x, float z) => new Vector3(x, Cap(new Vector2(x, z).magnitude) + rho - H, z).normalized;

            // 外の面（+1）と内の面（-1）
            foreach (int side in new[] { 1, -1 })
            {
                int start = r.Vertices.Count;
                for (int i = 0; i <= rings; i++)
                {
                    float rr = R * i / rings;
                    for (int j = 0; j < segments; j++)
                    {
                        float a = Mathf.PI * 2f * j / segments;
                        float x = Mathf.Cos(a) * rr, z = Mathf.Sin(a) * rr;
                        r.Vertices.Add(new Vector3(x, Cap(rr) + side * t * 0.5f, z));
                        r.Normals.Add(SphereNormal(x, z) * side);
                    }
                }
                for (int i = 0; i < rings; i++)
                    for (int j = 0; j < segments; j++)
                    {
                        int a = start + i * segments + j, b = start + i * segments + (j + 1) % segments;
                        int c = a + segments, d = b + segments;
                        r.Triangles.AddRange(new[] { a, c, b, b, c, d });
                    }
            }

            // 縁の帯（外向き）
            int rim = r.Vertices.Count;
            for (int k = 0; k < 2; k++)
                for (int j = 0; j < segments; j++)
                {
                    float a = Mathf.PI * 2f * j / segments;
                    r.Vertices.Add(new Vector3(Mathf.Cos(a) * R, (k == 0 ? -0.5f : 0.5f) * t, Mathf.Sin(a) * R));
                    r.Normals.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
                }
            for (int j = 0; j < segments; j++)
            {
                int a = rim + j, b = rim + (j + 1) % segments, c = a + segments, d = b + segments;
                r.Triangles.AddRange(new[] { a, c, b, b, c, d });
            }
            Orient(r.Vertices, r.Normals, r.Triangles);
            return r;
        }

        /// <summary>組み立てるときに作る形（DNA の形）。</summary>
        public static bool IsMadeShape(string shape) => shape == "dome" || shape == "cone" || shape == "ring";

        /// <summary>ドームの板の厚さ：直径の 3%（3 mm〜1.5 cm）。</summary>
        public static float DomeThickness(float diameter) => Mathf.Clamp(diameter * 0.03f, 0.003f, 0.015f);
        /// <summary>円すいの上の直径の割合。</summary>
        public const float ConeTop = 0.3f;
        /// <summary>輪の太さ（直径の 1/4 まで）。</summary>
        public static float RingTube(Vector3 size) => Mathf.Min(size.y, Mathf.Max(size.x, size.z) * 0.25f);

        /// <summary>体積（m³）と表面積（m²）。重さの計算に使う（ドームは薄い板、表面積は両面）。</summary>
        public static void Measure(string shape, Vector3 size, out float solid, out float area)
        {
            float R = (size.x + size.z) * 0.25f, H = size.y;
            switch (shape)
            {
                case "dome":
                {
                    float t = DomeThickness(R * 2f), h = Mathf.Max(H - t, t * 0.2f);
                    float cap = Mathf.PI * (R * R + h * h);
                    solid = cap * t; area = cap * 2f; return;
                }
                case "cone":
                {
                    float r = R * ConeTop;
                    solid = Mathf.PI * H / 3f * (R * R + R * r + r * r);
                    area = Mathf.PI * (R + r) * Mathf.Sqrt((R - r) * (R - r) + H * H) + Mathf.PI * (R * R + r * r); return;
                }
                default: // ring
                {
                    float rt = RingTube(size) * 0.5f, rc = Mathf.Max(R - rt, rt);
                    solid = 2f * Mathf.PI * Mathf.PI * rc * rt * rt; area = 4f * Mathf.PI * Mathf.PI * rc * rt; return;
                }
            }
        }

        /// <summary>
        /// 組み立てるときに作る形のメッシュ。部品の Transform の大きさ（size）を掛けると実寸になる座標で返す（中心が原点、高さは ±size.y/2）。
        /// </summary>
        public static Result MadeShape(string shape, Vector3 size)
        {
            size = new Vector3(Mathf.Max(1e-4f, Mathf.Abs(size.x)), Mathf.Max(1e-4f, Mathf.Abs(size.y)), Mathf.Max(1e-4f, Mathf.Abs(size.z)));
            float R = Mathf.Max(size.x, size.z) * 0.5f;
            Result m;
            switch (shape)
            {
                case "dome":
                {
                    float t = DomeThickness(R * 2f), h = Mathf.Max(size.y - t, t * 0.2f);
                    m = Dome(R, t, h);
                    Shift(m, -(h + t) * 0.5f + t * 0.5f);
                    break;
                }
                case "cone": m = Cone(R, R * ConeTop, size.y); break;
                default: m = Ring(R, RingTube(size) * 0.5f); break;
            }
            // 実寸 → 部品の座標（x・z は直径 R*2 を size に合わせる。法線は拡大の逆の転置で写す）
            var scale = new Vector3(size.x / (R * 2f), 1f, size.z / (R * 2f));
            var unit = new Vector3(1f / size.x, 1f / size.y, 1f / size.z);
            for (int i = 0; i < m.Vertices.Count; i++)
            {
                var v = Vector3.Scale(m.Vertices[i], scale);
                m.Vertices[i] = Vector3.Scale(v, unit);
                m.Normals[i] = Vector3.Scale(Vector3.Scale(m.Normals[i], new Vector3(1f / scale.x, 1f, 1f / scale.z)), size).normalized;
            }
            return m;
        }

        static void Shift(Result m, float dy)
        {
            for (int i = 0; i < m.Vertices.Count; i++) m.Vertices[i] += new Vector3(0f, dy, 0f);
        }

        /// <summary>円すい台（下の半径 R・上の半径 r・高さ H、中心が原点）。側面はなめらか、上と下はふた。</summary>
        public static Result Cone(float R, float r, float H, int segments = 48)
        {
            var m = new Result();
            float slope = (R - r) / Mathf.Max(1e-5f, H);
            for (int k = 0; k < 2; k++)
                for (int j = 0; j < segments; j++)
                {
                    float a = Mathf.PI * 2f * j / segments, rad = k == 0 ? R : r;
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    m.Vertices.Add(dir * rad + new Vector3(0f, k == 0 ? -H * 0.5f : H * 0.5f, 0f));
                    m.Normals.Add(new Vector3(dir.x, slope, dir.z).normalized);
                }
            for (int j = 0; j < segments; j++)
            {
                int a = j, b = (j + 1) % segments, c = a + segments, d = b + segments;
                m.Triangles.AddRange(new[] { a, c, b, b, c, d });
            }
            foreach (int side in new[] { -1, 1 })
            {
                int center = m.Vertices.Count;
                float rad = side < 0 ? R : r, y = side * H * 0.5f;
                m.Vertices.Add(new Vector3(0f, y, 0f)); m.Normals.Add(new Vector3(0f, side, 0f));
                for (int j = 0; j < segments; j++)
                {
                    float a = Mathf.PI * 2f * j / segments;
                    m.Vertices.Add(new Vector3(Mathf.Cos(a) * rad, y, Mathf.Sin(a) * rad)); m.Normals.Add(new Vector3(0f, side, 0f));
                }
                for (int j = 0; j < segments; j++) m.Triangles.AddRange(new[] { center, center + 1 + j, center + 1 + (j + 1) % segments });
            }
            Orient(m.Vertices, m.Normals, m.Triangles);
            return m;
        }

        /// <summary>寝かせた輪（外の半径 R・太さの半径 rt、中心が原点、穴は上下に抜ける）。</summary>
        public static Result Ring(float R, float rt, int segments = 56, int sides = 18)
        {
            var m = new Result();
            float rc = Mathf.Max(R - rt, rt);
            for (int j = 0; j < segments; j++)
            {
                float a = Mathf.PI * 2f * j / segments;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (int k = 0; k < sides; k++)
                {
                    float b = Mathf.PI * 2f * k / sides;
                    var n = dir * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    m.Vertices.Add(dir * rc + n * rt);
                    m.Normals.Add(n);
                }
            }
            for (int j = 0; j < segments; j++)
                for (int k = 0; k < sides; k++)
                {
                    int a = j * sides + k, b = j * sides + (k + 1) % sides;
                    int c = ((j + 1) % segments) * sides + k, d = ((j + 1) % segments) * sides + (k + 1) % sides;
                    m.Triangles.AddRange(new[] { a, c, b, b, c, d });
                }
            Orient(m.Vertices, m.Normals, m.Triangles);
            return m;
        }

        /// <summary>三角形の表（Unity の向き）を、頂点の法線の向きにそろえる。</summary>
        public static void Orient(List<Vector3> v, List<Vector3> n, List<int> tri)
        {
            for (int i = 0; i < tri.Count; i += 3)
            {
                var face = Vector3.Cross(v[tri[i + 1]] - v[tri[i]], v[tri[i + 2]] - v[tri[i]]);
                if (Vector3.Dot(face, n[tri[i]] + n[tri[i + 1]] + n[tri[i + 2]]) < 0f) (tri[i + 1], tri[i + 2]) = (tri[i + 2], tri[i + 1]);
            }
        }
    }
}
