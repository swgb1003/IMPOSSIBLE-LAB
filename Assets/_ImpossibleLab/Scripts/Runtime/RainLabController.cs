using System;
using System.Collections.Generic;
using ImpossibleLab.Core;
using UnityEngine;

namespace ImpossibleLab
{
    /// <summary>
    /// RainLab（実験場）。RainSimulator を実時間で進め、計算された雨粒の軌跡と同じものだけを粒子で描く（見た目と計測を一致させる）。
    /// </summary>
    public sealed class RainLabController : MonoBehaviour
    {
        public ParticleSystem Rain;
        public Transform MannequinHeadTop;   // 判定円筒の頭頂（y = 1.75）
        public Renderer WetZone;             // 判定用シルエット
        public Material WetZoneNormal, WetZoneHit;
        public Transform DeviceAnchor;
        public Transform PlayerSpot;
        public Transform ConsoleSpot;
        public Transform BridgeSpot;
        public int MaxVisualDropsPerSecond = 450;

        RainSimulator _sim;
        float _elapsed, _duration, _hitFlash;
        readonly List<RainSimulator.DropSample> _samples = new List<RainSimulator.DropSample>(512);
        int _visualBudget;
        public bool Running => _sim != null && !_sim.Finished;
        public float Elapsed => _elapsed;
        public RainSimulator Simulator => _sim;
        public event Action<RainExperimentResult> Completed;

        public void Begin(RainSimulator sim, RainExperimentConfig cfg)
        {
            _sim = sim; _elapsed = 0f; _duration = cfg.Clamped().DurationSec;
            if (Rain != null) { Rain.Clear(); Rain.Play(); }
        }

        public void Abort()
        {
            _sim = null;
            if (Rain != null) Rain.Clear();
            SetHit(false);
        }

        void Update()
        {
            if (_hitFlash > 0f) { _hitFlash -= Time.deltaTime; if (_hitFlash <= 0f) SetHit(false); }
            if (_sim == null || _sim.Finished) return;
            _elapsed += Time.deltaTime;
            int target = Mathf.Min(_sim.TotalDrops, Mathf.CeilToInt(_sim.TotalDrops * Mathf.Clamp01(_elapsed / _duration)));
            int due = target - Mathf.RoundToInt(_sim.Progress * _sim.TotalDrops);
            if (due <= 0) return;
            _samples.Clear();
            _sim.Step(due, _samples);
            _visualBudget = Mathf.CeilToInt(MaxVisualDropsPerSecond * Time.deltaTime);
            int stride = Mathf.Max(1, _samples.Count / Mathf.Max(1, _visualBudget));
            var origin = transform.position;
            for (int i = 0; i < _samples.Count; i++)
            {
                var s = _samples[i];
                if (s.Outcome == RainSimulator.DropOutcome.HitBody) { _hitFlash = 0.15f; SetHit(true); }
                if (i % stride != 0 || Rain == null) continue;
                var ep = new ParticleSystem.EmitParams
                {
                    position = origin + s.Start,
                    velocity = _sim.DropVelocity,
                    startLifetime = Mathf.Max(0.02f, s.EndT),
                    startColor = s.Outcome == RainSimulator.DropOutcome.HitBody ? (Color32)LabTheme.AccentWarning : (Color32)new Color(0.75f, 0.85f, 0.95f, 0.8f),
                    applyShapeToPosition = false,
                };
                Rain.Emit(ep, 1);
            }
            if (_sim.Finished)
            {
                var r = _sim.Result;
                Completed?.Invoke(r);
            }
        }

        void SetHit(bool on)
        {
            if (WetZone != null && WetZoneNormal != null && WetZoneHit != null) WetZone.sharedMaterial = on ? WetZoneHit : WetZoneNormal;
        }
    }
}
