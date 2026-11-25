using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ImpossibleLab
{
    /// <summary>
    /// コンソールの図形（板・輪・弧・目盛り・斜めのバー・三角）を1つのメッシュにまとめて描く。
    /// 座標は設計図（docs/design/console6.js）と同じ：左上が原点、x は右、y は下、単位は Canvas の px。
    /// 1画面ぶんの図形が1回の描画で済むので、Quest でも軽い。レイの当たり判定は持たない（ボタンは別に置く）。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class VecGraphic : MaskableGraphic
    {
        List<UIVertex> _verts = new List<UIVertex>(4096);
        List<int> _tris = new List<int>(8192);
        // BeginShapes〜EndShapes の間は描き直しの合図を止め、最後に前の図形と比べて違うときだけ合図する
        List<UIVertex> _prevVerts = new List<UIVertex>(4096);
        List<int> _prevTris = new List<int>(8192);
        bool _batch;

        /// <summary>
        /// 薄く重ねる色（光のにじみ・選択の地）。設計図の不透明度は sRGB での重ね方が前提なので、
        /// リニア色空間では同じ見た目になるように不透明度を直す（黒い地に明るい色を薄く重ねると、そのままでは濃く出る）。
        /// </summary>
        public static Color Soft(Color c, float alpha)
        {
            if (QualitySettings.activeColorSpace == ColorSpace.Linear) alpha = Mathf.Max(1.5f / 255f, Mathf.Pow(alpha, 2.2f));
            return new Color(c.r, c.g, c.b, alpha);
        }

        public void ClearShapes() { _verts.Clear(); _tris.Clear(); Dirty(); }

        void Dirty() { if (!_batch) SetVerticesDirty(); }

        /// <summary>図形を描き直し始める。<see cref="EndShapes"/> までに描いた図形が前と同じなら、メッシュを作り直さない。</summary>
        public void BeginShapes()
        {
            // 前の作り直しが途中で止まっていた（例外など）ときは、比べる相手を残したまま描き始め直す
            if (_batch) { _verts.Clear(); _tris.Clear(); return; }
            (_prevVerts, _verts) = (_verts, _prevVerts);
            (_prevTris, _tris) = (_tris, _prevTris);
            _verts.Clear(); _tris.Clear();
            _batch = true;
        }

        public void EndShapes()
        {
            if (!_batch) return;
            _batch = false;
            if (!SameAsPrevious()) SetVerticesDirty();
        }

        bool SameAsPrevious()
        {
            if (_verts.Count != _prevVerts.Count || _tris.Count != _prevTris.Count) return false;
            for (int i = 0; i < _tris.Count; i++) if (_tris[i] != _prevTris[i]) return false;
            for (int i = 0; i < _verts.Count; i++)
            {
                UIVertex a = _verts[i], b = _prevVerts[i];
                if (a.position != b.position || a.color.r != b.color.r || a.color.g != b.color.g || a.color.b != b.color.b || a.color.a != b.color.a) return false;
            }
            return true;
        }

        Vector3 Local(Vector2 p)
        {
            var r = rectTransform.rect;
            return new Vector3(r.xMin + p.x, r.yMax - p.y, 0f);
        }

        int V(Vector2 p, Color32 c)
        {
            var v = UIVertex.simpleVert;
            v.position = Local(p); v.color = c;
            _verts.Add(v);
            return _verts.Count - 1;
        }

        /// <summary>凸多角形（扇形に分割する）。</summary>
        public void Poly(Color c, params Vector2[] pts)
        {
            if (pts.Length < 3) return;
            Color32 c32 = c;
            int i0 = V(pts[0], c32), prev = V(pts[1], c32);
            for (int i = 2; i < pts.Length; i++) { int cur = V(pts[i], c32); _tris.Add(i0); _tris.Add(prev); _tris.Add(cur); prev = cur; }
            Dirty();
        }

        public void Rect(float x, float y, float w, float h, Color c) =>
            Poly(c, new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h));

        /// <summary>上下で色が変わる四角（金属の板の面）。</summary>
        public void RectV(float x, float y, float w, float h, Color top, Color bottom)
        {
            Color32 t = top, b = bottom;
            int i0 = V(new Vector2(x, y), t), i1 = V(new Vector2(x + w, y), t), i2 = V(new Vector2(x + w, y + h), b), i3 = V(new Vector2(x, y + h), b);
            _tris.Add(i0); _tris.Add(i1); _tris.Add(i2);
            _tris.Add(i0); _tris.Add(i2); _tris.Add(i3);
            Dirty();
        }

        /// <summary>斜めの四角（左上と右下を slant だけ寄せた平行四辺形）の4点。</summary>
        public static Vector2[] Slant(float x, float y, float w, float h, float slant) => new[]
        {
            new Vector2(x + slant, y), new Vector2(x + w, y), new Vector2(x + w - slant, y + h), new Vector2(x, y + h),
        };

        /// <summary>光って見える縁：輪郭を太く・薄く重ねる。</summary>
        public void Glow(Color c, params Vector2[] pts)
        {
            PolyLine(18f, Soft(c, 0.06f), true, pts);
            PolyLine(9f, Soft(c, 0.14f), true, pts);
        }

        public void Line(Vector2 a, Vector2 b, float width, Color c)
        {
            var d = b - a;
            if (d.sqrMagnitude < 1e-6f) return;
            var n = new Vector2(-d.y, d.x).normalized * (width * 0.5f);
            Poly(c, a + n, b + n, b - n, a - n);
        }

        /// <summary>折れ線（閉じるなら最後と最初もつなぐ）。角は丸めない。</summary>
        public void PolyLine(float width, Color c, bool closed, params Vector2[] pts)
        {
            for (int i = 0; i + 1 < pts.Length; i++) Line(pts[i], pts[i + 1], width, c);
            if (closed && pts.Length > 2) Line(pts[pts.Length - 1], pts[0], width, c);
        }

        /// <summary>点線。</summary>
        public void Dashed(Vector2 a, Vector2 b, float width, Color c, float on, float off)
        {
            float len = (b - a).magnitude; if (len < 1e-3f) return;
            var dir = (b - a) / len;
            for (float t = 0; t < len; t += on + off) Line(a + dir * t, a + dir * Mathf.Min(len, t + on), width, c);
        }

        public static Vector2 Polar(Vector2 center, float r, float deg)
        {
            float rad = deg * Mathf.Deg2Rad;
            return new Vector2(center.x + r * Mathf.Cos(rad), center.y + r * Mathf.Sin(rad));
        }

        /// <summary>弧（太さのある輪の一部）。角度は度、+x から時計回り（y が下なので見た目も時計回り）。</summary>
        public void Arc(Vector2 center, float r, float width, float a0, float a1, Color c)
        {
            if (a1 <= a0) return;
            int steps = Mathf.Max(2, Mathf.CeilToInt((a1 - a0) / 3f));
            float ri = r - width * 0.5f, ro = r + width * 0.5f;
            Color32 c32 = c;
            int pi = V(Polar(center, ri, a0), c32), po = V(Polar(center, ro, a0), c32);
            for (int i = 1; i <= steps; i++)
            {
                float a = Mathf.Lerp(a0, a1, (float)i / steps);
                int ci = V(Polar(center, ri, a), c32), co = V(Polar(center, ro, a), c32);
                _tris.Add(pi); _tris.Add(po); _tris.Add(co);
                _tris.Add(pi); _tris.Add(co); _tris.Add(ci);
                pi = ci; po = co;
            }
            Dirty();
        }

        public void Circle(Vector2 center, float r, float width, Color c) => Arc(center, r, width, 0f, 360f, c);

        /// <summary>点線の輪（on・off は度）。</summary>
        public void DashedCircle(Vector2 center, float r, float width, Color c, float onDeg, float offDeg)
        {
            for (float a = 0; a < 360f - 0.01f; a += onDeg + offDeg) Arc(center, r, width, a, Mathf.Min(360f, a + onDeg), c);
        }

        public void Disc(Vector2 center, float r, Color c, float squashY = 1f)
        {
            int steps = Mathf.Max(12, Mathf.CeilToInt(r * 0.8f));
            Color32 c32 = c;
            int ic = V(center, c32), prev = V(center + new Vector2(r, 0), c32);
            for (int i = 1; i <= steps; i++)
            {
                float a = 2f * Mathf.PI * i / steps;
                int cur = V(center + new Vector2(r * Mathf.Cos(a), r * Mathf.Sin(a) * squashY), c32);
                _tris.Add(ic); _tris.Add(prev); _tris.Add(cur); prev = cur;
            }
            Dirty();
        }

        /// <summary>光って見える弧：同じ弧を太く・薄く重ねる（ぼかしの代わり。後処理を使わないので軽い）。</summary>
        public void GlowArc(Vector2 center, float r, float width, float a0, float a1, Color c)
        {
            Arc(center, r, width + 22f, a0, a1, Soft(c, 0.07f));
            Arc(center, r, width + 10f, a0, a1, Soft(c, 0.16f));
            Arc(center, r, width, a0, a1, c);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            for (int i = 0; i < _verts.Count; i++) vh.AddVert(_verts[i]);
            for (int i = 0; i + 2 < _tris.Count; i += 3) vh.AddTriangle(_tris[i], _tris[i + 1], _tris[i + 2]);
        }
    }

    /// <summary>ゆっくり明滅させる（改良案の区画・確認待ちの弧）。</summary>
    public sealed class ConsolePulse : MonoBehaviour
    {
        public float Period = 1.6f, Min = 0.35f;
        CanvasRenderer _cr;
        void Awake() { _cr = GetComponent<CanvasRenderer>(); }
        void Update() { if (_cr != null) _cr.SetAlpha(Mathf.Lerp(Min, 1f, 0.5f + 0.5f * Mathf.Cos(Time.unscaledTime * 2f * Mathf.PI / Period))); }
    }
}
