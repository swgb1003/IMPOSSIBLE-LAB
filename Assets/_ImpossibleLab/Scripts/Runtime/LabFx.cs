using System.Collections;
using ImpossibleLab.Core;
using UnityEngine;

namespace ImpossibleLab
{
    /// <summary>
    /// イベント時だけの演出と効果音（仕様21.1：SF演出は発明生成・実験開始などイベント時に限定）。
    /// 効果音は素材ファイルを使わず、起動時に短い音をコードで作る（ライセンス確認が不要）。音声の録音・記録はしない。
    /// </summary>
    public sealed class LabFx : MonoBehaviour
    {
        public bool SoundOn = true;
        AudioSource _ui;
        AudioClip _click, _tick, _success, _warn, _start, _bump;
        LabApp _app;

        public void Init(LabApp app)
        {
            _app = app;
            if (FindFirstObjectByType<AudioListener>() == null && app.Origin != null && app.Origin.Camera != null)
                app.Origin.Camera.gameObject.AddComponent<AudioListener>();
            _ui = gameObject.AddComponent<AudioSource>();
            _ui.playOnAwake = false; _ui.spatialBlend = 0f; _ui.volume = 0.35f;
            _click = Tone("click", 0.035f, f0: 1400f, f1: 1100f, decay: 60f);
            _tick = Tone("tick", 0.06f, f0: 880f, f1: 880f, decay: 35f);
            _success = Chord("success", new[] { 660f, 880f, 1320f }, 0.11f);
            _warn = Chord("warn", new[] { 330f, 262f }, 0.16f);
            _start = Tone("start", 0.45f, f0: 300f, f1: 900f, decay: 4f);
            _bump = Tone("bump", 0.12f, f0: 140f, f1: 70f, decay: 25f);
            UiKit.Clicked += () => Play(_click, 0.6f);
            app.Sm.Changed += OnState;
        }

        void OnDestroy() { if (_app != null) _app.Sm.Changed -= OnState; }

        void OnState(LabState from, LabState to)
        {
            switch (to)
            {
                case LabState.GeneratingDesign: Play(_start, 0.5f); break;
                case LabState.AwaitingApproval: Play(_success, 0.7f); break;
                case LabState.Error: Play(_warn, 0.8f); break;
                case LabState.RunningTest: Play(_start, 0.8f); break;
                case LabState.ReviewingResult: if (_app.LastResult != null) Play(_app.LastResult.Success ? _success : _warn, 0.8f); break;
                case LabState.Saved: Play(_success, 0.6f); break;
            }
        }

        public void PartPlaced(Transform part) { Play(_tick, 0.7f); if (part != null) StartCoroutine(PopIn(part)); }
        public void Bump() => Play(_bump, 1f);

        void Play(AudioClip clip, float volume)
        {
            if (!SoundOn || _ui == null || clip == null) return;
            _ui.PlayOneShot(clip, volume);
        }

        /// <summary>部品が現れるとき、小さく出て少し膨らんでから元の大きさになる（0.3秒）。</summary>
        IEnumerator PopIn(Transform t)
        {
            var target = t.localScale;
            float e = 0f;
            while (e < 0.3f && t != null)
            {
                e += Time.deltaTime;
                float k = Mathf.Clamp01(e / 0.3f);
                float s = k < 0.7f ? Mathf.Lerp(0.2f, 1.12f, k / 0.7f) : Mathf.Lerp(1.12f, 1f, (k - 0.7f) / 0.3f);
                t.localScale = target * s;
                yield return null;
            }
            if (t != null) t.localScale = target;
        }

        /// <summary>組み立て中、発明台の上を下から上へ走査する輪（組み立ての間だけ表示）。</summary>
        public void BuildScan(Transform at, float duration, Material material)
        {
            if (at == null || material == null) return;
            StartCoroutine(ScanRoutine(at, duration, material));
        }

        IEnumerator ScanRoutine(Transform at, float duration, Material material)
        {
            var ring = Ring("BuildScan", material);
            float e = 0f;
            while (e < duration)
            {
                e += Time.deltaTime;
                float k = Mathf.Repeat(e / 1.2f, 1f);
                ring.position = at.position + Vector3.up * Mathf.Lerp(-0.1f, 0.7f, k);
                float r = Mathf.Lerp(1.0f, 0.6f, k);
                ring.localScale = new Vector3(r, 0.003f, r);
                yield return null;
            }
            Destroy(ring.gameObject);
        }

        /// <summary>実験開始時、地面に広がる輪（1秒）。</summary>
        public void StartPulse(Vector3 at, Material material)
        {
            if (material == null) return;
            StartCoroutine(PulseRoutine(at, material));
        }

        IEnumerator PulseRoutine(Vector3 at, Material material)
        {
            var ring = Ring("StartPulse", material);
            float e = 0f;
            while (e < 1f)
            {
                e += Time.deltaTime;
                float r = Mathf.Lerp(0.5f, 6f, e);
                ring.position = at + Vector3.up * 0.01f;
                ring.localScale = new Vector3(r, 0.002f, r);
                yield return null;
            }
            Destroy(ring.gameObject);
        }

        Transform Ring(string name, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        // ---- 効果音の生成（サイン波＋減衰） ----
        const int Rate = 44100;

        static AudioClip Tone(string name, float seconds, float f0, float f1, float decay)
        {
            int n = Mathf.Max(1, (int)(seconds * Rate));
            var data = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate, k = (float)i / n;
                float f = Mathf.Lerp(f0, f1, k);
                phase += 2 * Mathf.PI * f / Rate;
                float env = Mathf.Exp(-decay * t) * Mathf.Clamp01(i / (0.004f * Rate)) * (1f - k * k);
                data[i] = (float)System.Math.Sin(phase) * env * 0.6f;
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip Chord(string name, float[] notes, float noteSeconds)
        {
            int per = (int)(noteSeconds * Rate), n = per * notes.Length + Rate / 10;
            var data = new float[n];
            for (int j = 0; j < notes.Length; j++)
                for (int i = 0; i < per * 2 && j * per + i < n; i++)
                {
                    float t = (float)i / Rate;
                    float env = Mathf.Exp(-12f * t) * Mathf.Clamp01(i / (0.004f * Rate));
                    data[j * per + i] += Mathf.Sin(2 * Mathf.PI * notes[j] * t) * env * 0.45f;
                }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
