using System;
using System.Collections.Generic;

namespace ImpossibleLab.Core
{
    /// <summary>
    /// 一定時間ぶんのフレーム時間を集計する（実機の72fps確認用。描画には関わらない）。
    /// 遅いフレーム＝目標フレーム時間の1.5倍を超えたフレーム（Quest ではほぼ「1フレーム落ちた」に相当）。
    /// </summary>
    public sealed class FrameStats
    {
        readonly List<float> _dt = new List<float>(1024);
        double _sum;

        public int Count => _dt.Count;
        public double TotalSeconds => _sum;

        public void Add(float dtSeconds)
        {
            if (float.IsNaN(dtSeconds) || float.IsInfinity(dtSeconds) || dtSeconds <= 0f) return;
            _dt.Add(dtSeconds);
            _sum += dtSeconds;
        }

        public void Reset() { _dt.Clear(); _sum = 0; }

        public Summary Summarize(float targetHz)
        {
            var s = new Summary { Frames = _dt.Count, TargetHz = targetHz };
            if (_dt.Count == 0 || _sum <= 0) return s;
            s.AvgFps = (float)(_dt.Count / _sum);
            float worst = 0f; int slow = 0;
            float slowLimit = targetHz > 0f ? 1.5f / targetHz : float.MaxValue;
            foreach (var d in _dt) { if (d > worst) worst = d; if (d > slowLimit) slow++; }
            s.WorstMs = worst * 1000f;
            s.SlowPercent = 100f * slow / _dt.Count;
            var sorted = new List<float>(_dt); sorted.Sort();
            s.P95Ms = sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(sorted.Count * 0.95) - 1)] * 1000f;
            return s;
        }

        public struct Summary
        {
            public int Frames;
            public float TargetHz, AvgFps, P95Ms, WorstMs, SlowPercent;
            /// <summary>平均が目標の95%以上で、遅いフレームが1%以下なら「目標を維持」とみなす。</summary>
            public bool MeetsTarget => Frames > 0 && TargetHz > 0f && AvgFps >= TargetHz * 0.95f && SlowPercent <= 1f;
            public override string ToString() =>
                $"fps_avg={AvgFps:F1} target={TargetHz:F0} p95={P95Ms:F1}ms worst={WorstMs:F1}ms slow={SlowPercent:F1}% frames={Frames} ok={(MeetsTarget ? "yes" : "no")}";
        }
    }
}
