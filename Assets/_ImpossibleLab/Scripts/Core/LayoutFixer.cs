using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ImpossibleLab.Core
{
    /// <summary>
    /// AI が数字だけで決めた部品の配置を整える（部品の位置だけを動かす。大きさ・素材・機能は変えない）。
    /// 1. 左右の対になる部品をきっちり対称にする。
    /// 2. 箱どうしで半分以上めり込んだ部品を、3割だけ食い込む位置まで押し出す（中に完全に入った部品は内蔵部品として残す）。
    /// 3. どこにも触れていない部品（のまとまり）を、いちばん近い部品の面に付ける。
    /// 4. 地面を走る製品（車輪あり）で、車輪が床に着く高さより下に出た部品を、床から浮く高さまで持ち上げる。
    /// 部品の位置は実験の計算に使っていない（質量・寸法・機能だけ）ので、実験の結果は変わらない。
    /// キャノピー（頭上に浮かぶ覆いは設計どおり）と、関節で動く部品・その親は動かさない。
    /// </summary>
    public static class LayoutFixer
    {
        /// <summary>触れているとみなす隙間（m）。</summary>
        public const float Touch = 0.005f;
        /// <summary>付けるときに食い込ませる深さ（m）。</summary>
        public const float Embed = 0.002f;
        /// <summary>これより遠くに離れた部品は、意図して離したものとして動かさない（m）。</summary>
        public const float MaxMove = 0.3f;
        const float Pair = 0.03f;
        /// <summary>地面を走る製品で、車輪以外の部品の底と床のあいだに空ける隙間（m）。</summary>
        public const float Clearance = 0.01f;

        /// <summary>部品の半分の大きさ（発明の座標の向き）。車輪の円柱は横倒しなので x と y を入れ替える。</summary>
        public static Vector3 Half(PartSpec p)
        {
            var s = new Vector3(Mathf.Abs(p.Scale.x), Mathf.Abs(p.Scale.y), Mathf.Abs(p.Scale.z)) * 0.5f;
            return p.Type == "wheel" && p.ShapeOrDefault == "cylinder" ? new Vector3(s.y, s.x, s.z) : s;
        }

        /// <summary>2つの部品の箱の隙間（重なっていれば負）。</summary>
        public static float Gap(PartSpec a, PartSpec b)
        {
            Vector3 ha = Half(a), hb = Half(b), d = a.Position - b.Position;
            float g = float.MinValue;
            for (int k = 0; k < 3; k++) g = Mathf.Max(g, Mathf.Abs(d[k]) - ha[k] - hb[k]);
            return g;
        }

        static float Volume(PartSpec p) { var h = Half(p); return h.x * h.y * h.z; }

        /// <summary>
        /// 配置を整え、位置を動かした部品の数を返す。movable を渡すとその部品だけを動かす（改良で足した部品など）。
        /// </summary>
        public static int Apply(InventionDna dna, ICollection<string> movable = null)
        {
            if (dna?.Parts == null || dna.Parts.Count < 2) return 0;
            var before = dna.Parts.ToDictionary(p => p, p => p.Position);
            var jointed = new HashSet<string>(dna.Parts.Where(p => p.Joint != null).Select(p => p.Id));
            foreach (var p in dna.Parts) if (p.Joint != null && !string.IsNullOrEmpty(p.Joint.Parent)) jointed.Add(p.Joint.Parent);
            bool CanMove(PartSpec p) => p.Type != "canopy" && !jointed.Contains(p.Id) && (movable == null || movable.Contains(p.Id));
            var main = dna.Parts.Where(p => p.Type != "canopy").OrderByDescending(Volume).FirstOrDefault() ?? dna.Parts[0];

            Symmetrize(dna, CanMove);
            PushOut(dna, CanMove, main);
            Attach(dna, CanMove, main);
            Symmetrize(dna, CanMove);
            LiftAboveFloor(dna, CanMove);

            int moved = 0;
            foreach (var p in dna.Parts)
            {
                p.Position = new Vector3(
                    Mathf.Clamp(p.Position.x, -LabCatalog.PositionLimitXZ, LabCatalog.PositionLimitXZ),
                    Mathf.Clamp(p.Position.y, LabCatalog.PositionMinY, LabCatalog.PositionMaxY),
                    Mathf.Clamp(p.Position.z, -LabCatalog.PositionLimitXZ, LabCatalog.PositionLimitXZ));
                if ((p.Position - before[p]).sqrMagnitude > 1e-6f) moved++;
            }
            return moved;
        }

        /// <summary>
        /// 地面を走る製品（取り付け位置が地面で、車輪がある）で、車輪が床に着く高さから Clearance より下に出た部品を持ち上げる。
        /// 床に当たって走れなくなる部品（車輪の下に突き出たモーターなど）をなくす。持ち上げた部品の数を返す。
        /// canMove を省くと、キャノピーと関節で動く部品・その親以外を動かす（保存してある設計を開くとき）。
        /// </summary>
        public static int LiftAboveFloor(InventionDna dna, System.Func<PartSpec, bool> canMove = null)
        {
            if (dna?.Parts == null || dna.Mount != "ground") return 0;
            var wheels = dna.Parts.Where(p => p.Type == "wheel").ToList();
            if (wheels.Count == 0) return 0;
            if (canMove == null)
            {
                var jointed = new HashSet<string>(dna.Parts.Where(p => p.Joint != null).Select(p => p.Id));
                foreach (var p in dna.Parts) if (p.Joint != null && !string.IsNullOrEmpty(p.Joint.Parent)) jointed.Add(p.Joint.Parent);
                canMove = p => p.Type != "canopy" && !jointed.Contains(p.Id);
            }
            float floor = wheels.Min(w => w.Position.y - Half(w).y), limit = floor + Clearance;
            int lifted = 0;
            foreach (var p in dna.Parts)
            {
                if (p.Type == "wheel" || !canMove(p)) continue;
                float bottom = p.Position.y - Half(p).y;
                if (bottom >= limit - 1e-5f) continue;
                p.Position += Vector3.up * (limit - bottom);
                lifted++;
            }
            return lifted;
        }

        static bool SameSize(PartSpec a, PartSpec b)
        {
            for (int k = 0; k < 3; k++)
            {
                float x = Mathf.Abs(a.Scale[k]), y = Mathf.Abs(b.Scale[k]);
                if (Mathf.Abs(x - y) > 0.15f * Mathf.Max(x, y) + 1e-4f) return false;
            }
            return true;
        }

        /// <summary>左右の対（同じ種類・形・ほぼ同じ大きさで、x がほぼ反対、y・z がほぼ同じ）を鏡写しにそろえる。中心近くの1つだけの部品は x=0 に。</summary>
        static void Symmetrize(InventionDna dna, System.Func<PartSpec, bool> canMove)
        {
            var used = new HashSet<PartSpec>();
            foreach (var a in dna.Parts)
            {
                if (used.Contains(a) || a.Position.x <= 0.01f) continue;
                var b = dna.Parts.Where(q => q != a && !used.Contains(q) && q.Position.x < -0.01f && q.Type == a.Type && q.ShapeOrDefault == a.ShapeOrDefault && SameSize(a, q)
                                             && Mathf.Abs(q.Position.x + a.Position.x) <= Pair && Mathf.Abs(q.Position.y - a.Position.y) <= Pair && Mathf.Abs(q.Position.z - a.Position.z) <= Pair)
                    .OrderBy(q => (new Vector3(-q.Position.x, q.Position.y, q.Position.z) - a.Position).sqrMagnitude).FirstOrDefault();
                if (b == null) continue;
                used.Add(a); used.Add(b);
                if (!canMove(a) || !canMove(b)) continue;
                var m = new Vector3((a.Position.x - b.Position.x) * 0.5f, (a.Position.y + b.Position.y) * 0.5f, (a.Position.z + b.Position.z) * 0.5f);
                a.Position = m;
                b.Position = new Vector3(-m.x, m.y, m.z);
            }
            foreach (var p in dna.Parts)
                if (!used.Contains(p) && canMove(p) && p.Position.x != 0f && Mathf.Abs(p.Position.x) <= 0.015f)
                    p.Position = new Vector3(0f, p.Position.y, p.Position.z);
        }

        /// <summary>箱どうしで、小さい方の部品が押し出す向きの厚みの半分以上めり込んでいたら、3割の食い込みまで押し出す。</summary>
        static void PushOut(InventionDna dna, System.Func<PartSpec, bool> canMove, PartSpec main)
        {
            foreach (var a in dna.Parts.OrderBy(Volume))
            {
                if (!canMove(a) || a == main || a.ShapeOrDefault != "box") continue;
                foreach (var b in dna.Parts)
                {
                    if (b == a || b.ShapeOrDefault != "box" || Volume(b) < Volume(a) || b.Type == "canopy") continue;
                    Vector3 ha = Half(a), hb = Half(b), d = a.Position - b.Position;
                    bool inside = true;
                    int axis = -1; float best = float.MaxValue;
                    for (int k = 0; k < 3; k++)
                    {
                        float pen = ha[k] + hb[k] - Mathf.Abs(d[k]); // その向きの重なり
                        if (pen <= 0f) { axis = -2; break; }
                        if (Mathf.Abs(d[k]) + ha[k] > hb[k] + Touch) inside = false;
                        // 押し出す向き：重なりが浅い向き（中心が同じなら外へ出しにくいので候補にしない）
                        if (Mathf.Abs(d[k]) > 1e-4f && pen < best) { best = pen; axis = k; }
                    }
                    if (axis < 0 || inside) continue;
                    float size = ha[axis] * 2f;
                    if (best <= 0.5f * size) continue;
                    float sign = Mathf.Sign(d[axis]);
                    var pos = a.Position;
                    pos[axis] = b.Position[axis] + sign * (hb[axis] + ha[axis] - 0.3f * size);
                    a.Position = pos;
                }
            }
        }

        /// <summary>主な部品（いちばん大きい部品）とつながっていない部品のまとまりを、近い順に、つながっている部品の面に付ける。</summary>
        static void Attach(InventionDna dna, System.Func<PartSpec, bool> canMove, PartSpec main)
        {
            var parts = dna.Parts;
            for (int guard = 0; guard < parts.Count; guard++)
            {
                // キャノピーと動かせない部品も、ほかの部品の付け先になる
                var anchored = Connected(parts, new HashSet<PartSpec>(parts.Where(p => p == main || !canMove(p))));
                var rest = parts.Where(p => !anchored.Contains(p)).ToList();
                if (rest.Count == 0) return;

                // 離れた部品のまとまりごとに、付ける動きの候補を出し、いちばん短いものから付ける
                List<PartSpec> bestGroup = null; Vector3 bestMove = Vector3.zero; float bestLen = float.MaxValue;
                var seen = new HashSet<PartSpec>();
                foreach (var start in rest)
                {
                    if (seen.Contains(start)) continue;
                    var group = Connected(rest, new HashSet<PartSpec> { start }).ToList();
                    foreach (var g in group) seen.Add(g);
                    if (!group.All(canMove)) continue;
                    foreach (var a in group)
                        foreach (var b in anchored)
                        {
                            var mv = Move(a, b);
                            if (mv.magnitude < bestLen) { bestLen = mv.magnitude; bestMove = mv; bestGroup = group; }
                        }
                }
                if (bestGroup == null || bestLen > MaxMove) return;
                foreach (var g in bestGroup) g.Position += bestMove;
            }
        }

        /// <summary>a を b の面に付ける動き：いちばん近い面の向きでは 2 mm 食い込ませ、ほかの向きでは中心を b の範囲に入れる。</summary>
        static Vector3 Move(PartSpec a, PartSpec b)
        {
            Vector3 ha = Half(a), hb = Half(b), d = a.Position - b.Position;
            Vector3 best = Vector3.zero; float len = float.MaxValue;
            for (int k = 0; k < 3; k++)
            {
                var mv = Vector3.zero;
                float sign = d[k] >= 0f ? 1f : -1f;
                mv[k] = b.Position[k] + sign * (hb[k] + ha[k] - Embed) - a.Position[k];
                if (mv[k] * sign > 0f) mv[k] = Mathf.Min(0f, mv[k] * sign) * sign; // 既に重なる向きへは押し出さない
                for (int j = 0; j < 3; j++)
                {
                    if (j == k) continue;
                    if (Mathf.Abs(d[j]) - ha[j] - hb[j] < -Embed) continue; // その向きでは既に重なっている
                    float lo = b.Position[j] - hb[j], hi = b.Position[j] + hb[j];
                    mv[j] = Mathf.Clamp(a.Position[j], lo, hi) - a.Position[j];
                }
                if (mv.magnitude < len) { len = mv.magnitude; best = mv; }
            }
            return best;
        }

        static HashSet<PartSpec> Connected(List<PartSpec> parts, HashSet<PartSpec> seed)
        {
            var set = new HashSet<PartSpec>(seed);
            var queue = new Queue<PartSpec>(seed);
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                foreach (var q in parts)
                    if (!set.Contains(q) && Gap(p, q) <= Touch) { set.Add(q); queue.Enqueue(q); }
            }
            return set;
        }
    }
}
