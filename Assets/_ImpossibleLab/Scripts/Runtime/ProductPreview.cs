using System.Collections.Generic;
using ImpossibleLab.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ImpossibleLab
{
    /// <summary>
    /// 画面の真ん中の列に出す、試作品の立体の写し。発明台の本物とは別に、床の下の見えない場所に組み立て、
    /// 専用のカメラで小さい画像に描く（XR には描かない・影なし）。部品を選ぶとその部品に寄り、ほかの部品は暗くする。
    /// 試用で本物が頭の上へ動いても、写しは動かない。押したまま動かすと回して見られる（触れたあとは自動で回らない）。
    /// </summary>
    public sealed class ProductPreview : MonoBehaviour
    {
        /// <summary>写しと専用カメラだけが使う層（名前のない層。ほかのカメラからは外す）。</summary>
        public const int Layer = 30;
        /// <summary>画像の大きさ（px）。画面の上の枠と同じ縦横比にする。</summary>
        public const int Width = 640, Height = 560;
        const float Fov = 30f, TurnDegPerSec = 12f, Ease = 7f, DegPerPx = 0.45f, PitchMin = -30f, PitchMax = 80f;
        static readonly Vector3 StagePos = new Vector3(0f, -40f, 0f);

        Camera _cam;
        RenderTexture _rt;
        InventionBuilder _builder;
        LabAssets _assets;
        Transform _stage;
        InventionView _copy;
        InventionDna _dna;
        int _focus = -1;
        GameObject _frame;
        readonly Dictionary<Renderer, string> _owner = new Dictionary<Renderer, string>();
        readonly Dictionary<Renderer, MaterialPropertyBlock> _orig = new Dictionary<Renderer, MaterialPropertyBlock>();
        MaterialPropertyBlock _dim;
        Vector3 _center, _centerGoal;
        // 見る向き：yaw＝前（+Z）から右回りの角度、pitch＝見下ろす角度。初めは前の右斜め、少し上から
        float _dist, _distGoal, _yaw = 26.6f, _pitch = 15f;
        string _manualFor;
        bool _snap, _shown;
        int _frameNo;

        public Texture Texture => _rt;
        /// <summary>寄っている部品の番号（-1＝全体）。</summary>
        public int Focus => _focus;
        public bool Shown => _shown;
        /// <summary>手で回したあとか（自動で回るのを止めている）。</summary>
        public bool Manual => _dna != null && _manualFor == _dna.Id;
        public float Yaw => _yaw;

        /// <summary>押したまま動かした量（px・右と上が正）で回す。製品を指でつかんで回すのと同じ向き。</summary>
        public void Orbit(Vector2 px)
        {
            if (_dna == null) return;
            _manualFor = _dna.Id;
            _yaw -= px.x * DegPerPx;
            _pitch = Mathf.Clamp(_pitch - px.y * DegPerPx, PitchMin, PitchMax);
        }

        public static ProductPreview Create(LabAssets assets)
        {
            var go = new GameObject("ProductPreview");
            var p = go.AddComponent<ProductPreview>();
            p.Init(assets);
            return p;
        }

        void Init(LabAssets assets)
        {
            _assets = assets;
            _builder = new InventionBuilder(assets);
            _stage = new GameObject("Stage").transform;
            _stage.SetParent(transform, false);
            _stage.position = StagePos;

            _rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "ProductPreview", antiAliasing = 2 };
            _rt.Create();
            var cgo = new GameObject("PreviewCamera");
            cgo.transform.SetParent(transform, false);
            _cam = cgo.AddComponent<Camera>();
            _cam.enabled = false; // LateUpdate で必要なときだけ描く
            _cam.targetTexture = _rt;
            _cam.cullingMask = 1 << Layer;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Color.black; // 板の地は画面では真っ黒に出るので合わせる（枠が浮かないように）
            _cam.fieldOfView = Fov;
            _cam.nearClipPlane = 0.01f; _cam.farClipPlane = 20f;
            _cam.allowHDR = false; _cam.allowMSAA = true;
            var data = _cam.GetUniversalAdditionalCameraData();
            data.allowXRRendering = false;
            data.renderShadows = false;
            data.renderPostProcessing = false;

            _dim = new MaterialPropertyBlock();
            // 写しの層を、ほかのカメラ（頭のカメラなど）から外す
            foreach (var c in Camera.allCameras) if (c != _cam) c.cullingMask &= ~(1 << Layer);
        }

        /// <summary>この設計を写し、focus 番の部品に寄る（-1＝全体）。同じ設計なら組み直さない。</summary>
        public void Show(InventionDna dna, int focus)
        {
            _shown = dna != null;
            if (dna == null) return;
            if (!ReferenceEquals(dna, _dna) || _copy == null) Rebuild(dna);
            if (_copy == null) { _shown = false; return; }
            focus = focus >= 0 && focus < dna.Parts.Count ? focus : -1;
            if (focus != _focus) SetFocus(focus);
        }

        public void Hide() => _shown = false;

        void Rebuild(InventionDna dna)
        {
            if (_copy != null) { _builder.Clear(_copy); _copy = null; }
            _owner.Clear(); _orig.Clear(); _frame = null; _focus = -1;
            _dna = dna;
            var r = _builder.Build(dna, _stage);
            if (r.View == null) return;
            _copy = r.View;
            // 写しは見るだけ：当たり判定を外し、影を落とさず、専用の層に置く
            foreach (var col in _copy.GetComponentsInChildren<Collider>(true)) Destroy(col);
            foreach (var t in _copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            var partOf = new Dictionary<Transform, string>();
            foreach (var kv in _copy.Parts) partOf[kv.Value] = kv.Key;
            foreach (var rend in _copy.GetComponentsInChildren<Renderer>(true))
            {
                rend.shadowCastingMode = ShadowCastingMode.Off;
                rend.receiveShadows = false;
                for (var t = rend.transform; t != null && t != _copy.transform; t = t.parent)
                    if (partOf.TryGetValue(t, out var id)) { _owner[rend] = id; break; }
                var mpb = new MaterialPropertyBlock();
                rend.GetPropertyBlock(mpb);
                _orig[rend] = mpb;
            }
            _focus = -2; // 次の SetFocus で必ず寄り直す
            _snap = true;
        }

        void SetFocus(int focus)
        {
            _focus = focus;
            string id = focus >= 0 ? _dna.Parts[focus].Id : null;
            if (_frame != null) { Destroy(_frame); _frame = null; }
            // ほかの部品は暗くする（寄っていないときは元の色）
            foreach (var kv in _orig)
            {
                var rend = kv.Key;
                if (rend == null) continue;
                bool dim = id != null && (!_owner.TryGetValue(rend, out var owner) || owner != id);
                if (!dim) { rend.SetPropertyBlock(kv.Value.isEmpty ? null : kv.Value); continue; }
                var mat = rend.sharedMaterial;
                float a = mat != null && mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor").a : 1f;
                _dim.Clear();
                _dim.SetColor("_BaseColor", new Color(0.09f, 0.11f, 0.12f, a));
                _dim.SetColor("_EmissionColor", Color.black);
                rend.SetPropertyBlock(_dim);
            }
            var b = BoundsOf(id);
            _centerGoal = b.center;
            // 全体が輪に収まる距離（球で近似）。小さい部品でも寄りすぎない
            float radius = Mathf.Max(id != null ? 0.05f : 0.15f, b.extents.magnitude);
            _distGoal = radius / Mathf.Sin(Fov * 0.5f * Mathf.Deg2Rad) * 1.05f;
            if (id != null && _copy.Parts.TryGetValue(id, out var target) && _assets != null)
            {
                _frame = PartHighlight.Create(_copy.transform, target, _assets.AccentOrange);
                foreach (var t in _frame.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            }
            if (_snap) { _center = _centerGoal; _dist = _distGoal; _snap = false; }
        }

        Bounds BoundsOf(string id)
        {
            bool any = false;
            var b = new Bounds(_copy.transform.position, Vector3.zero);
            foreach (var kv in _orig)
            {
                var rend = kv.Key;
                if (rend == null || !rend.enabled || !rend.gameObject.activeInHierarchy) continue;
                if (id != null && (!_owner.TryGetValue(rend, out var owner) || owner != id)) continue;
                if (!any) { b = rend.bounds; any = true; } else b.Encapsulate(rend.bounds);
            }
            return b;
        }

        void LateUpdate()
        {
            if (!_shown || _copy == null) return;
            float k = 1f - Mathf.Exp(-Ease * Time.unscaledDeltaTime);
            _center = Vector3.Lerp(_center, _centerGoal, k);
            _dist = Mathf.Lerp(_dist, _distGoal, k);
            if (!Manual) _yaw += TurnDegPerSec * Time.unscaledDeltaTime;
            // Quest の負担を抑えるため、2 フレームに 1 回だけ描く
            if ((++_frameNo & 1) == 1) return;
            Draw();
        }

        void Draw()
        {
            var dir = Quaternion.Euler(-_pitch, _yaw, 0f) * Vector3.forward;
            _cam.transform.SetPositionAndRotation(_center + dir * _dist, Quaternion.LookRotation(-dir));
            _cam.Render();
        }

        /// <summary>今の状態ですぐに描く（自動の確認で撮る前に使う）。</summary>
        public void RenderNow()
        {
            if (!_shown || _copy == null) return;
            _center = _centerGoal; _dist = _distGoal;
            Draw();
        }

        void OnDestroy()
        {
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
        }
    }
}
