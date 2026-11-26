using System.Collections.Generic;
using UnityEngine;

namespace ImpossibleLab.Core
{
    /// <summary>
    /// 外装を一体の殻にする計算（見た目のみ）。角を丸めた箱・円柱を、つなぎ目に丸みを付けて1つの形に合わせ（距離の場のなめらかな和）、
    /// 格子の上で表面を取り出して（Surface Nets）メッシュにする。法線は距離の場の傾きから求める（面はなめらか、角は丸い）。
    /// </summary>
    public static class ShellMesher
    {
        /// <summary>殻の材料の1つ。座標は発明の座標（m）。</summary>
        public struct Solid
        {
            public Vector3 Center;
            public Quaternion Rotation;
            /// <summary>半分の大きさ。円柱は y が軸で、x が半径（z は使わない）。</summary>
            public Vector3 Half;
            public bool Cylinder;
            /// <summary>角の丸みの半径（m）。</summary>
            public float Round;
        }

        public sealed class Result
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<int> Triangles = new List<int>();
            public float Cell;
        }

        /// <summary>角を丸めた箱の距離（中は負）。</summary>
        public static float RoundBox(Vector3 p, Vector3 half, float r)
        {
            r = Mathf.Min(r, Mathf.Min(half.x, Mathf.Min(half.y, half.z)));
            var q = new Vector3(Mathf.Abs(p.x) - half.x + r, Mathf.Abs(p.y) - half.y + r, Mathf.Abs(p.z) - half.z + r);
            return Vector3.Max(q, Vector3.zero).magnitude + Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f) - r;
        }

        /// <summary>角を丸めた円柱（y が軸）の距離（中は負）。</summary>
        public static float RoundCylinder(Vector3 p, float radius, float halfHeight, float r)
        {
            r = Mathf.Min(r, Mathf.Min(radius, halfHeight));
            var d = new Vector2(new Vector2(p.x, p.z).magnitude - radius + r, Mathf.Abs(p.y) - halfHeight + r);
            return Mathf.Min(Mathf.Max(d.x, d.y), 0f) + Vector2.Max(d, Vector2.zero).magnitude - r;
        }

        /// <summary>なめらかな和（k の幅でつなぎ目に丸みが付く）。</summary>
        public static float SmoothUnion(float a, float b, float k)
        {
            if (k <= 0f) return Mathf.Min(a, b);
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
        }

        public static float Distance(IReadOnlyList<Solid> solids, float blend, Vector3 p) => Field(Prepare(solids), blend, p);

        /// <summary>向きを逆にしておく（距離を何十万回も測るため）。</summary>
        static Solid[] Prepare(IReadOnlyList<Solid> solids)
        {
            var a = new Solid[solids.Count];
            for (int i = 0; i < a.Length; i++) { a[i] = solids[i]; a[i].Rotation = Quaternion.Inverse(solids[i].Rotation); }
            return a;
        }

        /// <summary>距離の場（solids は Prepare したもの：Rotation が逆向き）。</summary>
        static float Field(Solid[] solids, float blend, Vector3 p)
        {
            float d = float.MaxValue;
            for (int i = 0; i < solids.Length; i++)
            {
                var s = solids[i];
                var q = s.Rotation * (p - s.Center);
                float di = s.Cylinder ? RoundCylinder(q, s.Half.x, s.Half.y, s.Round) : RoundBox(q, s.Half, s.Round);
                d = i == 0 ? di : SmoothUnion(d, di, blend);
            }
            return d;
        }

        static Vector3 Gradient(Solid[] solids, float blend, Vector3 p, float e)
        {
            var g = new Vector3(
                Field(solids, blend, p + new Vector3(e, 0, 0)) - Field(solids, blend, p - new Vector3(e, 0, 0)),
                Field(solids, blend, p + new Vector3(0, e, 0)) - Field(solids, blend, p - new Vector3(0, e, 0)),
                Field(solids, blend, p + new Vector3(0, 0, e)) - Field(solids, blend, p - new Vector3(0, 0, e)));
            return g.sqrMagnitude > 1e-20f ? g.normalized : Vector3.up;
        }

        /// <summary>
        /// 殻のメッシュを作る。resolution はいちばん長い辺を何マスに分けるか（Quest の組み立て時間のため 72 まで）。
        /// 面は外から見て時計回り（Unity の表）。
        /// </summary>
        public static Result Build(IReadOnlyList<Solid> solids, float blend, int resolution = 56)
        {
            var result = new Result();
            if (solids == null || solids.Count == 0) return result;
            resolution = Mathf.Clamp(resolution, 8, 72);
            var prepared = Prepare(solids);

            // 範囲：すべての材料の外接箱に、つなぎ目の丸みと2マスを足す
            var lo = Vector3.one * float.MaxValue; var hi = Vector3.one * float.MinValue;
            foreach (var s in solids)
            {
                var ext = s.Cylinder ? new Vector3(s.Half.x, s.Half.y, s.Half.x) : s.Half;
                for (int c = 0; c < 8; c++)
                {
                    var v = s.Center + s.Rotation * Vector3.Scale(ext, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    lo = Vector3.Min(lo, v); hi = Vector3.Max(hi, v);
                }
            }
            var size = hi - lo;
            float cell = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) / resolution;
            result.Cell = cell;
            lo -= Vector3.one * (blend + cell * 2f);
            hi += Vector3.one * (blend + cell * 2f);
            int nx = Mathf.CeilToInt((hi.x - lo.x) / cell), ny = Mathf.CeilToInt((hi.y - lo.y) / cell), nz = Mathf.CeilToInt((hi.z - lo.z) / cell);

            // 格子の点の距離
            var field = new float[(nx + 1) * (ny + 1) * (nz + 1)];
            int P(int x, int y, int z) => (z * (ny + 1) + y) * (nx + 1) + x;
            for (int z = 0; z <= nz; z++)
                for (int y = 0; y <= ny; y++)
                    for (int x = 0; x <= nx; x++)
                        field[P(x, y, z)] = Field(prepared, blend, lo + new Vector3(x, y, z) * cell);

            // マスごとに、表面が通るなら頂点を1つ（辺で 0 になる点の平均）
            var vertexOf = new int[nx * ny * nz];
            int C(int x, int y, int z) => (z * ny + y) * nx + x;
            var corner = new float[8];
            for (int z = 0; z < nz; z++)
                for (int y = 0; y < ny; y++)
                    for (int x = 0; x < nx; x++)
                    {
                        int inside = 0;
                        for (int c = 0; c < 8; c++)
                        {
                            corner[c] = field[P(x + (c & 1), y + ((c >> 1) & 1), z + ((c >> 2) & 1))];
                            if (corner[c] < 0f) inside++;
                        }
                        if (inside == 0 || inside == 8) { vertexOf[C(x, y, z)] = -1; continue; }
                        var sum = Vector3.zero; int n = 0;
                        for (int a = 0; a < 8; a++)
                            for (int bit = 0; bit < 3; bit++)
                            {
                                int b = a | (1 << bit);
                                if (b == a) continue;
                                float da = corner[a], db = corner[b];
                                if ((da < 0f) == (db < 0f)) continue;
                                float t = da / (da - db);
                                var pa = new Vector3(a & 1, (a >> 1) & 1, (a >> 2) & 1);
                                var pb = new Vector3(b & 1, (b >> 1) & 1, (b >> 2) & 1);
                                sum += Vector3.Lerp(pa, pb, t); n++;
                            }
                        var v = lo + (new Vector3(x, y, z) + sum / n) * cell;
                        // 表面へ寄せる（2回）
                        for (int it = 0; it < 2; it++)
                        {
                            float d = Field(prepared, blend, v);
                            v -= Gradient(prepared, blend, v, cell * 0.25f) * d;
                        }
                        vertexOf[C(x, y, z)] = result.Vertices.Count;
                        result.Vertices.Add(v);
                        result.Normals.Add(Gradient(prepared, blend, v, cell * 0.25f));
                    }

            // 表面をまたぐ格子の辺ごとに、その辺を囲む4つのマスの頂点で四角形を作る
            void Quad(int a, int b, int c, int d, bool flip)
            {
                if (a < 0 || b < 0 || c < 0 || d < 0) return;
                if (flip) { (b, d) = (d, b); }
                result.Triangles.Add(a); result.Triangles.Add(b); result.Triangles.Add(c);
                result.Triangles.Add(a); result.Triangles.Add(c); result.Triangles.Add(d);
            }
            for (int z = 1; z < nz; z++)
                for (int y = 1; y < ny; y++)
                    for (int x = 0; x < nx; x++)
                    {
                        float d0 = field[P(x, y, z)], d1 = field[P(x + 1, y, z)];
                        if ((d0 < 0f) == (d1 < 0f)) continue;
                        Quad(vertexOf[C(x, y - 1, z - 1)], vertexOf[C(x, y, z - 1)], vertexOf[C(x, y, z)], vertexOf[C(x, y - 1, z)], d0 < 0f);
                    }
            for (int z = 1; z < nz; z++)
                for (int y = 0; y < ny; y++)
                    for (int x = 1; x < nx; x++)
                    {
                        float d0 = field[P(x, y, z)], d1 = field[P(x, y + 1, z)];
                        if ((d0 < 0f) == (d1 < 0f)) continue;
                        Quad(vertexOf[C(x - 1, y, z - 1)], vertexOf[C(x - 1, y, z)], vertexOf[C(x, y, z)], vertexOf[C(x, y, z - 1)], d0 < 0f);
                    }
            for (int z = 0; z < nz; z++)
                for (int y = 1; y < ny; y++)
                    for (int x = 1; x < nx; x++)
                    {
                        float d0 = field[P(x, y, z)], d1 = field[P(x, y, z + 1)];
                        if ((d0 < 0f) == (d1 < 0f)) continue;
                        Quad(vertexOf[C(x - 1, y - 1, z)], vertexOf[C(x, y - 1, z)], vertexOf[C(x, y, z)], vertexOf[C(x - 1, y, z)], d0 < 0f);
                    }

            // 向きをそろえる：三角形の面の向きが外（距離の場の傾き）と逆なら裏返す
            var tri = result.Triangles;
            for (int i = 0; i < tri.Count; i += 3)
            {
                var a = result.Vertices[tri[i]]; var b = result.Vertices[tri[i + 1]]; var c = result.Vertices[tri[i + 2]];
                var face = Vector3.Cross(b - a, c - a); // Unity の表の向き（RecalculateNormals と同じ式）
                var outward = result.Normals[tri[i]] + result.Normals[tri[i + 1]] + result.Normals[tri[i + 2]];
                if (Vector3.Dot(face, outward) < 0f) { (tri[i + 1], tri[i + 2]) = (tri[i + 2], tri[i + 1]); }
            }
            return result;
        }
    }
}
