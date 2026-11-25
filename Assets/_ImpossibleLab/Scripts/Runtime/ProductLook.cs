using System.Collections.Generic;
using System.Linq;
using ImpossibleLab.Core;
using UnityEngine;

namespace ImpossibleLab
{
    /// <summary>
    /// 試作品を製品らしく見せる決まり（見た目のみ。DNA・実験の計算・当たり判定は変えない）。
    /// ・角の丸みを実寸で一定にする（ProductFit）。
    /// ・センサーのレンズは真円のまま。
    /// ・外装が上下に重なるときは下を濃い色に塗り分ける。上の外装には、大きさに関係なく同じ大きさのネジ・通気口を付ける。
    /// </summary>
    public static class ProductLook
    {
        /// <summary>切り替え（見比べ用）。</summary>
        public static bool Enabled = true;

        /// <summary>塗り分け・細部を付ける外装の部品タイプ。</summary>
        static readonly HashSet<string> Housing = new HashSet<string> { "body", "container", "battery" };

        /// <summary>角の丸みを一定にするキット。丸み自体が形のもの（緩衝材）、薄い覆い、輪の形（車輪・推進ユニット）は従来どおり。</summary>
        public static bool Fits(string kitKey, string shape)
        {
            if (shape != "box" && shape != "cylinder") return false;
            switch (kitKey)
            {
                case "KIT_canopy_cylinder": case "KIT_cushion_box": case "KIT_wheel_cylinder": case "KIT_thruster_cylinder": return false;
                default: return true;
            }
        }

        // ---- メッシュの作り置き（同じ大きさの部品は同じメッシュを使う） ----
        static readonly Dictionary<string, Mesh> _cache = new Dictionary<string, Mesh>();
        const int CacheLimit = 400;

        static string Key(Mesh src, Vector3 s, string mode) => $"{src.GetInstanceID()}|{mode}|{s.x:F4}|{s.y:F4}|{s.z:F4}";

        static Mesh Remember(string key, Mesh m)
        {
            // 上限を超えたら覚えるのをやめて作り直す（使用中のメッシュは消さない。使われなくなったものは UnloadUnusedAssets で消える）
            if (_cache.Count >= CacheLimit) _cache.Clear();
            _cache[key] = m;
            return m;
        }

        /// <summary>キットのメッシュを、Transform の scale を掛けたとき角の丸みが一定になる形にしたもの。読めないメッシュはそのまま返す。</summary>
        public static Mesh Fit(Mesh src, Vector3 scale, bool cylinder)
        {
            if (src == null || !src.isReadable) return src;
            string key = Key(src, scale, cylinder ? "cyl" : "box");
            if (_cache.TryGetValue(key, out var hit) && hit != null) return hit;
            var m = Object.Instantiate(src);
            m.name = src.name + "_fit";
            var v = m.vertices; var n = m.normals; var t = m.tangents;
            ProductFit.Apply(v, n.Length == v.Length ? n : null, t.Length == v.Length ? t : null, new ProductFit.Frame(scale, cylinder));
            m.vertices = v;
            if (n.Length == v.Length) m.normals = n;
            if (t.Length == v.Length) m.tangents = t;
            m.RecalculateBounds();
            return Remember(key, m);
        }

        /// <summary>センサーのレンズ（キットの前面 z=0.3）を、縦横の小さい方の大きさで真円にしたもの。</summary>
        public static Mesh Lens(Mesh src, Vector3 scale)
        {
            if (src == null || !src.isReadable) return src;
            string key = Key(src, scale, "lens");
            if (_cache.TryGetValue(key, out var hit) && hit != null) return hit;
            var m = Object.Instantiate(src);
            m.name = src.name + "_round";
            var v = m.vertices; var n = m.normals;
            float size = Mathf.Min(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
            ProductFit.ApplyUniform(v, n.Length == v.Length ? n : null, new Vector3(0, 0, 0.3f), scale, size);
            m.vertices = v;
            if (n.Length == v.Length) m.normals = n;
            m.RecalculateBounds();
            return Remember(key, m);
        }

        // ---- 塗り分け ----
        static float Bottom(PartSpec p) => p.Position.y - Mathf.Abs(p.Scale.y) * 0.5f;
        static float Top(PartSpec p) => p.Position.y + Mathf.Abs(p.Scale.y) * 0.5f;

        static bool IsHousing(PartSpec p) =>
            Housing.Contains(p.Type) && (p.ShapeOrDefault == "box" || p.ShapeOrDefault == "cylinder") && PartLook.Surface(p) == PartSurface.Plastic;

        static bool Overlaps(PartSpec a, PartSpec b) =>
            Mathf.Abs(a.Position.x - b.Position.x) < (Mathf.Abs(a.Scale.x) + Mathf.Abs(b.Scale.x)) * 0.5f &&
            Mathf.Abs(a.Position.z - b.Position.z) < (Mathf.Abs(a.Scale.z) + Mathf.Abs(b.Scale.z)) * 0.5f;

        /// <summary>下を濃い色にする外装の部品 id。外装（樹脂の本体・格納部・電池）が上下に重なるときだけ、いちばん下のもの。無ければ null。</summary>
        public static string LowerShell(InventionDna dna)
        {
            var housing = dna.Parts.Where(IsHousing).ToList();
            if (housing.Count < 2) return null;
            var low = housing.OrderBy(Bottom).First();
            if (Mathf.Min(Mathf.Abs(low.Scale.x), Mathf.Abs(low.Scale.z)) < 0.12f) return null;
            float mid = (Bottom(low) + Top(low)) * 0.5f;
            bool stacked = housing.Any(p => p != low && Overlaps(p, low) && Bottom(p) >= mid);
            return stacked ? low.Id : null;
        }

        /// <summary>下の外装の色：本体色を濃い灰へ寄せる（同じ色味のまま暗く）。</summary>
        public static int Darker(int rgb)
        {
            const int dark = 0x24282D;
            int Ch(int sh) => Mathf.RoundToInt(Mathf.Lerp((rgb >> sh) & 0xFF, (dark >> sh) & 0xFF, 0.72f));
            return (Ch(16) << 16) | (Ch(8) << 8) | Ch(0);
        }

        // ---- 細部（ネジ・通気口）：実寸で付ける ----
        const float MinFootprint = 0.15f;

        /// <summary>いちばん上の外装に、大きさに関係なく同じ大きさのネジ（上面の4隅）と通気口（左右の面）を付ける。</summary>
        public static void AddDetails(InventionView view, InventionDna dna, LabAssets assets)
        {
            var top = dna.Parts.Where(IsHousing)
                .Where(p => Mathf.Min(Mathf.Abs(p.Scale.x), Mathf.Abs(p.Scale.z)) >= MinFootprint)
                .OrderByDescending(Top).FirstOrDefault();
            if (top == null || !view.Parts.TryGetValue(top.Id, out var part)) return;
            var mf = part.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
            bool cylinder = top.ShapeOrDefault == "cylinder";
            var f = new ProductFit.Frame(part.localScale, cylinder);
            // 細部は実寸の座標で置く（部品の scale を打ち消す子の下）
            var holder = new GameObject("ProductDetail").transform;
            holder.SetParent(part, false);
            holder.localScale = new Vector3(1f / f.Scale.x, 1f / f.Scale.y, 1f / f.Scale.z);
            float topY = mf.sharedMesh.bounds.max.y * f.Scale.y;
            var steel = assets.For(PartSurface.Steel);
            var dark = assets.For(PartSurface.Rubber);

            // ネジ：直径 8 mm、上面から 0.8 mm 出す
            for (int i = 0; i < 4; i++)
            {
                float sx = (i & 1) == 0 ? -1f : 1f, sz = (i & 2) == 0 ? -1f : 1f;
                Vector3 at;
                if (cylinder)
                {
                    float a = Mathf.PI * 0.25f + Mathf.PI * 0.5f * i;
                    at = new Vector3(Mathf.Cos(a) * f.Half.x * 0.72f, topY, Mathf.Sin(a) * f.Half.z * 0.72f);
                }
                else
                {
                    float inset = f.Radius(0.08f) + 0.018f;
                    at = new Vector3(sx * (f.Half.x - inset), topY, sz * (f.Half.z - inset));
                }
                Piece(holder, "Screw", PrimitiveType.Cylinder, at + Vector3.down * 0.0004f, new Vector3(0.008f, 0.0012f, 0.008f), steel);
            }

            // 通気口：箱の左右の面の後ろ寄りに、長さ 5 cm・高さ 4 mm の溝を4本（面の高さ 10 cm・奥行き 15 cm 以上のとき）
            if (!cylinder && f.Half.y * 2f >= 0.1f && f.Half.z * 2f >= MinFootprint)
            {
                float z0 = -f.Half.z * 0.4f;
                for (int side = -1; side <= 1; side += 2)
                    for (int k = 0; k < 4; k++)
                    {
                        float y = (k - 1.5f) * 0.011f + f.Half.y * 0.15f;
                        Piece(holder, "Vent", PrimitiveType.Cube, new Vector3(side * (f.Half.x + 0.0003f), y, z0), new Vector3(0.002f, 0.004f, 0.05f), dark);
                    }
            }
        }

        static void Piece(Transform parent, string name, PrimitiveType type, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            if (mat != null) r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
