using System.Collections.Generic;
using ImpossibleLab.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace ImpossibleLab
{
    /// <summary>
    /// 部品どうしのすき間・付け根の陰り（Art/Shaders/PartLit.shader、計算の式は PartAo）。見た目だけで、DNA・実験の計算・当たり判定は変えない。
    /// ・組み立てたあと、不透明で光らない部品の素材を、同じ性質の PartLit の素材に替える（元の素材ごとに1つ作って使い回す）。
    /// ・部品の見た目（メッシュ）ごとに、ほかの部品の近いもの最大4つを箱か円柱にして渡す。
    /// ・関節で部品が動いたとき（発明の中での位置が変わったとき）だけ渡し直す。発明全体が動くだけなら渡し直さない。
    /// </summary>
    public static class PartShading
    {
        /// <summary>切り替え（見比べ用）。</summary>
        public static bool Enabled = true;

        public const int MaxNeighbors = 4;
        /// <summary>陰りの強さ（0〜1）。</summary>
        public const float Strength = 0.85f;

        static readonly Dictionary<Material, Material> _shaded = new Dictionary<Material, Material>();

        /// <summary>src と同じ性質の PartLit の素材。替えない素材（半透明・光る・Lit 以外）はそのまま返す。</summary>
        public static Material Shaded(Material src, Material template)
        {
            if (src == null || template == null || src.shader == null || src.shader.name != "Universal Render Pipeline/Lit") return src;
            if (src.HasProperty("_Surface") && src.GetFloat("_Surface") > 0.5f) return src;
            if (src.IsKeywordEnabled("_EMISSION") || src.IsKeywordEnabled("_ALPHATEST_ON")) return src;
            if (_shaded.TryGetValue(src, out var hit) && hit != null) return hit;
            var m = new Material(template) { name = src.name + "_Shade" };
            CopyProperties(src, m);
            Keyword(m, "_NORMALMAP", src.IsKeywordEnabled("_NORMALMAP"));
            Keyword(m, "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A", src.IsKeywordEnabled("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A"));
            m.renderQueue = src.renderQueue;
            _shaded[src] = m;
            return m;
        }

        /// <summary>同じ名前の性質の値を写す（キーワードは写さない。シェーダーごとにキーワードの組が違うため）。</summary>
        static void CopyProperties(Material src, Material dst)
        {
            var shader = src.shader;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                string name = shader.GetPropertyName(i);
                if (!dst.HasProperty(name)) continue;
                switch (shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color: dst.SetColor(name, src.GetColor(name)); break;
                    case ShaderPropertyType.Vector: dst.SetVector(name, src.GetVector(name)); break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range: dst.SetFloat(name, src.GetFloat(name)); break;
                    case ShaderPropertyType.Int: dst.SetInteger(name, src.GetInteger(name)); break;
                    case ShaderPropertyType.Texture:
                        dst.SetTexture(name, src.GetTexture(name));
                        dst.SetTextureScale(name, src.GetTextureScale(name));
                        dst.SetTextureOffset(name, src.GetTextureOffset(name));
                        break;
                }
            }
        }

        static void Keyword(Material m, string k, bool on) { if (on) m.EnableKeyword(k); else m.DisableKeyword(k); }

        /// <summary>組み立てた発明の部品の素材を替え、陰りの計算に使う隣の部品を渡す。</summary>
        public static void Apply(InventionView view, Material template)
        {
            if (!Enabled || view == null || template == null) return;
            var owner = new Dictionary<Transform, string>();
            foreach (var kv in view.Parts) owner[kv.Value] = kv.Key;
            var items = new List<PartShadingState.Item>();
            foreach (var r in view.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r.enabled) continue; // 殻にまとめて表示を止めた部品
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                string part = null;
                for (var t = r.transform; t != null && t != view.transform; t = t.parent)
                    if (owner.TryGetValue(t, out part)) break;
                if (part == null) continue;
                var shaded = Shaded(r.sharedMaterial, template);
                bool receiver = shaded != r.sharedMaterial || (shaded != null && shaded.shader == template.shader);
                if (receiver) r.sharedMaterial = shaded;
                var spec = view.Dna?.Parts.Find(p => p.Id == part);
                // 箱・円柱に近い形だけを陰りの元にする（輪の穴・ドームの内側を箱とみなすと、ない陰りが出る）
                bool boxLike = spec == null || !FormMesher.IsMadeShape(spec.ShapeOrDefault);
                bool occluder = boxLike && r.shadowCastingMode != ShadowCastingMode.Off && !IsTransparent(r.sharedMaterial);
                if (!receiver && !occluder) continue;
                bool round = spec != null && (spec.ShapeOrDefault == "cylinder" || spec.ShapeOrDefault == "capsule");
                items.Add(new PartShadingState.Item { Renderer = r, Mesh = mf.sharedMesh, Part = part, Receiver = receiver, Occluder = occluder, Round = round });
            }
            if (items.Count == 0) return;
            var state = view.gameObject.AddComponent<PartShadingState>();
            state.Init(view.transform, items, Strength);
        }

        static bool IsTransparent(Material m) => m == null || (m.HasProperty("_Surface") && m.GetFloat("_Surface") > 0.5f) || m.renderQueue >= 2500;
    }

    /// <summary>発明ごとの陰りの状態。部品の発明の中での位置が変わったら、隣の部品を渡し直す。</summary>
    public sealed class PartShadingState : MonoBehaviour
    {
        public sealed class Item
        {
            public Renderer Renderer;
            public Mesh Mesh;
            public string Part;
            public bool Receiver, Occluder, Round;
            public Matrix4x4 Relative;
        }

        static readonly int BoxId = Shader.PropertyToID("_IlabAOBox");
        static readonly int HalfId = Shader.PropertyToID("_IlabAOHalf");
        static readonly int ParamsId = Shader.PropertyToID("_IlabAOParams");

        Transform _root;
        List<Item> _items;
        float _strength;
        MaterialPropertyBlock _mpb;
        readonly Matrix4x4[] _boxes = new Matrix4x4[PartShading.MaxNeighbors];
        readonly Vector4[] _halves = new Vector4[PartShading.MaxNeighbors];

        /// <summary>渡している隣の部品の数（確かめ用）。部品の見た目ごと。</summary>
        public int NeighborCount(Renderer r)
        {
            var it = _items?.Find(x => x.Renderer == r);
            if (it == null || _mpb == null) return 0;
            it.Renderer.GetPropertyBlock(_mpb);
            return Mathf.RoundToInt(_mpb.GetVector(ParamsId).x);
        }

        public void Init(Transform root, List<Item> items, float strength)
        {
            _root = root; _items = items; _strength = strength;
            _mpb = new MaterialPropertyBlock();
            Refresh();
        }

        void LateUpdate()
        {
            if (_items == null) return;
            var toRoot = _root.worldToLocalMatrix;
            foreach (var it in _items)
            {
                if (it.Renderer == null) continue;
                var rel = toRoot * it.Renderer.localToWorldMatrix;
                if (!Same(rel, it.Relative)) { Refresh(); return; }
            }
        }

        static bool Same(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 16; i++) if (Mathf.Abs(a[i] - b[i]) > 1e-4f) return false;
            return true;
        }

        /// <summary>部品ごとに、いちばん近いほかの部品を最大4つ選んで渡す。</summary>
        public void Refresh()
        {
            var toRoot = _root.worldToLocalMatrix;
            var whole = new Bounds();
            bool first = true;
            var boxes = new (Matrix4x4 fromWorld, Vector4 half, Bounds bounds)[_items.Count];
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                if (it.Renderer == null) continue;
                it.Relative = toRoot * it.Renderer.localToWorldMatrix;
                PartAo.BoxOf(it.Renderer.localToWorldMatrix, it.Mesh.bounds, it.Round, out var fw, out var half);
                boxes[i] = (fw, half, it.Renderer.bounds);
                if (first) { whole = it.Renderer.bounds; first = false; } else whole.Encapsulate(it.Renderer.bounds);
            }
            float reach = PartAo.Reach(Mathf.Max(whole.size.x, Mathf.Max(whole.size.y, whole.size.z)));

            var near = new List<(int index, float gap, float volume)>();
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                if (it.Renderer == null || !it.Receiver) continue;
                near.Clear();
                for (int j = 0; j < _items.Count; j++)
                {
                    var other = _items[j];
                    if (j == i || other.Renderer == null || !other.Occluder || other.Part == it.Part) continue;
                    float gap = PartAo.Gap(boxes[i].bounds, boxes[j].bounds);
                    if (gap > reach) continue;
                    var s = boxes[j].bounds.size;
                    near.Add((j, gap, s.x * s.y * s.z));
                }
                // 近い順（同じならば大きい部品を先に）
                near.Sort((a, b) => a.gap != b.gap ? a.gap.CompareTo(b.gap) : b.volume.CompareTo(a.volume));
                int n = Mathf.Min(PartShading.MaxNeighbors, near.Count);
                var localToWorld = it.Renderer.localToWorldMatrix;
                for (int k = 0; k < PartShading.MaxNeighbors; k++)
                {
                    if (k < n)
                    {
                        var b = boxes[near[k].index];
                        _boxes[k] = b.fromWorld * localToWorld; // 部品自身の座標 → 隣の部品の箱の座標
                        _halves[k] = b.half;
                    }
                    else { _boxes[k] = Matrix4x4.identity; _halves[k] = Vector4.zero; }
                }
                it.Renderer.GetPropertyBlock(_mpb);
                _mpb.SetMatrixArray(BoxId, _boxes);
                _mpb.SetVectorArray(HalfId, _halves);
                _mpb.SetVector(ParamsId, new Vector4(n, reach, _strength, 0f));
                it.Renderer.SetPropertyBlock(_mpb);
            }
        }
    }
}
