using ImpossibleLab.Core;
using UnityEngine;

namespace ImpossibleLab
{
    /// <summary>
    /// 10秒ごとにフレーム時間を集計し、[ILAB] perf として ilab_log.txt に残す（AT：72fps の確認用）。
    /// 起動直後の読み込み3秒と、一時停止（ヘッドセットを外した等）の前後は集計しない。
    /// </summary>
    public sealed class PerfMonitor : MonoBehaviour
    {
        const float WindowSeconds = 10f;
        const float WarmupSeconds = 3f;
        readonly FrameStats _stats = new FrameStats();
        float _windowStart = -1f, _resumeAt;
        LabApp _app;
        string _lastState;
        bool _mixed;

        void Start()
        {
            _app = GetComponent<LabApp>();
            _resumeAt = Time.realtimeSinceStartup + WarmupSeconds;
            Debug.Log($"[ILAB] perf config refresh={TargetHz():F0}Hz quality={QualitySettings.names[QualitySettings.GetQualityLevel()]} " +
                      $"eyeScale={UnityEngine.XR.XRSettings.eyeTextureResolutionScale:F2} device={SystemInfo.deviceModel}");
        }

        void OnApplicationPause(bool paused) { if (!paused) Restart(2f); }
        void OnApplicationFocus(bool focus) { if (focus) Restart(2f); }

        void Restart(float delay)
        {
            _stats.Reset(); _windowStart = -1f; _mixed = false; _lastState = null;
            _resumeAt = Time.realtimeSinceStartup + delay;
        }

        void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _resumeAt) return;
            string state = Context();
            if (_windowStart < 0f) { _windowStart = now; _lastState = state; return; } // 最初のフレームは前の区間の時間を含むので捨てる
            if (state != _lastState) { _mixed = true; _lastState = state; }
            _stats.Add(Time.unscaledDeltaTime);
            if (now - _windowStart < WindowSeconds) return;
            Debug.Log($"[ILAB] perf {_stats.Summarize(TargetHz())} state={(_mixed ? "mixed>" : "")}{state}");
            _stats.Reset(); _windowStart = now; _mixed = false;
        }

        string Context()
        {
            if (_app == null) return "?";
            string s = _app.Sm.State.ToString();
            if (_app.ExperimentRunning) s += "+" + _app.TestConfig.Environment;
            return s;
        }

        static float TargetHz()
        {
            float hz = UnityEngine.XR.XRDevice.refreshRate;
            return hz > 1f ? hz : 72f; // 取得できないとき（Editor など）は Quest の既定 72Hz
        }
    }
}
