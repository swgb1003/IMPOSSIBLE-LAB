using System.Collections;
using UnityEngine;

namespace ImpossibleLab
{
    /// <summary>エリア移動時の暗転（VR酔い対策, 仕様3.2）。カメラ直前の板を不透明度で制御する。</summary>
    public sealed class ScreenFader : MonoBehaviour
    {
        public Renderer Quad;
        public float Duration = 0.35f;
        MaterialPropertyBlock _mpb;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        public float Alpha { get; private set; }

        void Awake() { _mpb = new MaterialPropertyBlock(); Apply(0f); }

        public IEnumerator FadeTo(float target)
        {
            float start = Alpha, t = 0f;
            if (Quad != null) Quad.enabled = true;
            while (t < Duration)
            {
                t += Time.unscaledDeltaTime;
                Apply(Mathf.Lerp(start, target, t / Duration));
                yield return null;
            }
            Apply(target);
        }

        void Apply(float a)
        {
            Alpha = a;
            if (Quad == null) return;
            _mpb ??= new MaterialPropertyBlock();
            _mpb.SetColor(BaseColor, new Color(0f, 0f, 0f, a));
            Quad.SetPropertyBlock(_mpb);
            Quad.enabled = a > 0.001f;
        }
    }
}
