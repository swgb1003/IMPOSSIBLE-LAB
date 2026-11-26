using System.Collections.Generic;
using UnityEngine;

namespace ImpossibleLab.Core
{
    /// <summary>
    /// 外装を一体の殻にする部品の選び方（見た目のみ）。
    /// 本体（硬い素材の箱・円柱）と、それに触れている電池・格納部・ほかの本体（硬い素材の箱・円柱）を、つなぎ目のない1つの殻にする。
    /// 関節で動く部品と、布・発泡材・ゴムの部品は合わせない。合わせる相手が無ければ殻は作らない。
    /// </summary>
    public static class ShellPlan
    {
        static readonly HashSet<string> Members = new HashSet<string> { "body", "battery", "container" };
        /// <summary>触れているとみなすすき間（m）。</summary>
        public const float TouchGap = 0.01f;

        static bool Hard(PartSpec p)
        {
            var s = PartLook.Surface(p);
            return s == PartSurface.Plastic || s == PartSurface.Aluminum || s == PartSurface.Steel;
        }

        static bool Solid(PartSpec p) => p.Joint == null && (p.ShapeOrDefault == "box" || p.ShapeOrDefault == "cylinder") && Hard(p);

        /// <summary>部品の外接箱（発明の座標）。円柱の scale は [直径, 高さ, 直径]。</summary>
        public static Bounds BoundsOf(PartSpec p)
        {
            var s = new Vector3(Mathf.Abs(p.Scale.x), Mathf.Abs(p.Scale.y), Mathf.Abs(p.Scale.z));
            if (p.ShapeOrDefault == "cylinder") s.z = s.x = Mathf.Max(s.x, s.z);
            return new Bounds(p.Position, s);
        }

        static float Gap(Bounds a, Bounds b) => Vector3.Max(Vector3.zero, Vector3.Max(a.min - b.max, b.min - a.max)).magnitude;

        /// <summary>殻にする部品（先頭が本体）。殻を作らないときは空。</summary>
        public static List<PartSpec> Pick(InventionDna dna)
        {
            var chosen = new List<PartSpec>();
            if (dna?.Parts == null) return chosen;
            var body = dna.Parts.Find(p => p.Type == "body" && Solid(p));
            if (body == null) return chosen;
            chosen.Add(body);
            bool added = true;
            while (added)
            {
                added = false;
                foreach (var p in dna.Parts)
                {
                    if (chosen.Contains(p) || !Members.Contains(p.Type) || !Solid(p)) continue;
                    var b = BoundsOf(p);
                    if (chosen.Exists(c => Gap(BoundsOf(c), b) <= TouchGap)) { chosen.Add(p); added = true; }
                }
            }
            if (chosen.Count < 2) chosen.Clear();
            return chosen;
        }

        /// <summary>殻の材料（角の丸みは薄い向きの 1/4、4 mm〜2 cm）。</summary>
        public static ShellMesher.Solid SolidOf(PartSpec p)
        {
            var s = new Vector3(Mathf.Abs(p.Scale.x), Mathf.Abs(p.Scale.y), Mathf.Abs(p.Scale.z)) * 0.5f;
            bool cylinder = p.ShapeOrDefault == "cylinder";
            if (cylinder) s = new Vector3(Mathf.Max(s.x, s.z), s.y, 0f);
            float thin = cylinder ? Mathf.Min(s.x, s.y) : Mathf.Min(s.x, Mathf.Min(s.y, s.z));
            return new ShellMesher.Solid { Center = p.Position, Rotation = Quaternion.identity, Half = s, Cylinder = cylinder, Round = Mathf.Clamp(thin * 0.25f, 0.004f, 0.02f) };
        }

        /// <summary>つなぎ目の丸みの幅：合わせる部品のいちばん薄い向きの 30%（1〜4 cm）。</summary>
        public static float Blend(List<PartSpec> parts)
        {
            float thin = float.MaxValue;
            foreach (var p in parts)
            {
                var s = BoundsOf(p).size;
                thin = Mathf.Min(thin, Mathf.Min(s.x, Mathf.Min(s.y, s.z)));
            }
            return Mathf.Clamp(thin * 0.3f, 0.01f, 0.04f);
        }

        /// <summary>同じ殻かどうかを見分ける鍵（同じ部品・同じ大きさなら作り直さない）。</summary>
        public static string Key(List<PartSpec> parts)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var p in parts)
                sb.Append(p.ShapeOrDefault).Append(p.Position.ToString("F4")).Append(p.Scale.ToString("F4")).Append(';');
            return sb.ToString();
        }
    }
}
