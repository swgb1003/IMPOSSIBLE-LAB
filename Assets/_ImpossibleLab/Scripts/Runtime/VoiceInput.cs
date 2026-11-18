using System;
using System.IO;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace ImpossibleLab
{
    /// <summary>
    /// 音声入力の録音（最大 15 秒）。録音した声は WAV にしてプロキシの /v1/transcribe へ送り、PC 上の Whisper で文字にする。
    /// 声は端末にもプロキシにも保存しない（仕様 24章）。Quest ではマイクの使用許可（RECORD_AUDIO）が必要。
    /// </summary>
    public sealed class VoiceInput
    {
        public const int Rate = 16000, MaxSeconds = 15;
        public const float MinSeconds = 0.4f;
        AudioClip _clip;
        float _start;

        public bool Recording => _clip != null;
        /// <summary>直前の録音の大きさ（最大値・平均の強さ）と、かけた増幅・録音の周波数・長さ。ログ用（内容は持たない）。</summary>
        public float LastPeak, LastRms, LastGain = 1f, LastSeconds;
        public int LastRate;
        public float Elapsed => Recording ? Time.realtimeSinceStartup - _start : 0f;

        public static bool HasPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return Permission.HasUserAuthorizedPermission(Permission.Microphone);
#else
            return true;
#endif
        }

        public static void RequestPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Permission.RequestUserPermission(Permission.Microphone);
#endif
        }

        /// <summary>録音を始める。始められなければ理由を返す。</summary>
        public string Start()
        {
            if (Recording) return null;
            if (Microphone.devices == null || Microphone.devices.Length == 0) return "マイクが見つかりません";
            _clip = Microphone.Start(null, false, MaxSeconds, Rate);
            if (_clip == null) return "録音を始められません";
            _start = Time.realtimeSinceStartup;
            return null;
        }

        /// <summary>録音を止めて WAV（16bit モノラル）を返す。短すぎる・無音なら null。</summary>
        public byte[] Stop()
        {
            if (!Recording) return null;
            var clip = _clip;
            _clip = null;
            // 上限まで録り終えると録音が止まり位置が 0 になるので、そのときは全体を使う
            int pos = Microphone.IsRecording(null) ? Microphone.GetPosition(null) : clip.samples;
            Microphone.End(null);
            LastRate = clip.frequency; LastSeconds = (float)pos / Mathf.Max(1, clip.frequency); LastPeak = LastRms = 0f; LastGain = 1f;
            if (pos <= 0 || pos < clip.frequency * MinSeconds) return null;
            var data = new float[pos * clip.channels];
            clip.GetData(data, 0);
            var mono = new float[pos];
            for (int i = 0; i < pos; i++)
            {
                float s = 0f;
                for (int c = 0; c < clip.channels; c++) s += data[i * clip.channels + c];
                mono[i] = s / clip.channels;
            }
            int rate = clip.frequency;
            UnityEngine.Object.Destroy(clip);
            double sum = 0;
            foreach (var v in mono) { float a = Mathf.Abs(v); if (a > LastPeak) LastPeak = a; sum += v * v; }
            LastRms = (float)System.Math.Sqrt(sum / mono.Length);
            if (LastPeak < 0.0005f) return null; // 無音
            // 小さい声は送る前に大きくする（Whisper の無音判定で落とされないように。最大 30 倍）
            if (LastPeak < 0.5f)
            {
                LastGain = Mathf.Min(0.9f / LastPeak, 30f);
                for (int i = 0; i < mono.Length; i++) mono[i] *= LastGain;
            }
            return ToWav(mono, rate);
        }

        public void Cancel()
        {
            if (!Recording) return;
            Microphone.End(null);
            UnityEngine.Object.Destroy(_clip);
            _clip = null;
        }

        /// <summary>float の音声（−1〜1）→ 16bit PCM の WAV。</summary>
        public static byte[] ToWav(float[] samples, int rate)
        {
            using (var ms = new MemoryStream(44 + samples.Length * 2))
            using (var w = new BinaryWriter(ms))
            {
                int bytes = samples.Length * 2;
                w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' }); w.Write(36 + bytes);
                w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
                w.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' }); w.Write(16);
                w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' }); w.Write(bytes);
                foreach (var f in samples) w.Write((short)Mathf.RoundToInt(Mathf.Clamp(f, -1f, 1f) * 32767f));
                w.Flush();
                return ms.ToArray();
            }
        }
    }
}
