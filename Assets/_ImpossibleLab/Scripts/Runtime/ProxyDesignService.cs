using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ImpossibleLab.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace ImpossibleLab
{
    /// <summary>
    /// AI設計プロキシの接続先。既定は http://127.0.0.1:8787（PC側で `adb reverse tcp:8787 tcp:8787` を実行して USB 経由で接続）。
    /// persistentDataPath/proxy.json（{"url": "...", "token": "...", "enabled": true}）で上書きできる。APIキーはここに置かない。
    /// </summary>
    public sealed class ProxyConfig
    {
        public string Url = "http://127.0.0.1:8787";
        public string Token = "";
        public bool Enabled = true;
        public float TimeoutSec = 100f;

        public static ProxyConfig Load(string persistentDataPath)
        {
            var c = new ProxyConfig();
            try
            {
                string path = Path.Combine(persistentDataPath, "proxy.json");
                if (!File.Exists(path)) return c;
                var o = JObject.Parse(File.ReadAllText(path));
                string url = o.Value<string>("url");
                if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https"))
                    c.Url = u.ToString().TrimEnd('/');
                c.Token = o.Value<string>("token") ?? "";
                c.Enabled = o.Value<bool?>("enabled") ?? true;
            }
            catch (Exception e) { Debug.LogWarning("[ImpossibleLab] proxy.json を読めません: " + e.Message); }
            return c;
        }
    }

    /// <summary>Claude（プロキシ経由）で設計・改良する。AIの出力は必ず DnaJson / RevisionEngine で検証してから使う。</summary>
    public sealed class ProxyDesignService : IInventionDesignService
    {
        readonly ProxyConfig _cfg;
        public ProxyDesignService(ProxyConfig cfg) { _cfg = cfg; }

        public string DisplayName => "AI（Claude・プロキシ経由）";

        public async Task<DesignOutcome> GenerateAsync(string requestText, IProgress<DesignStage> progress, CancellationToken ct)
        {
            var err = RequestRules.Check(requestText);
            if (err != null) return new DesignOutcome { Status = DesignStatus.InvalidInput, Message = err };
            progress?.Report(DesignStage.IntentAnalysis);
            var (code, body, transport) = await PostAsync("/v1/design", ProxyProtocol.DesignRequestJson(requestText), ct,
                onSent: () => progress?.Report(DesignStage.FeatureSelection));
            progress?.Report(DesignStage.PartDesign);
            var outcome = ProxyProtocol.ParseDesign(code, body, transport);
            if (outcome.Status == DesignStatus.Success) progress?.Report(DesignStage.Assembly);
            return outcome;
        }

        public async Task<RevisionOutcome> ReviseAsync(InventionDna current, RainExperimentResult lastExperiment, string requestText, CancellationToken ct)
        {
            var err = RequestRules.Check(requestText);
            if (err != null) return new RevisionOutcome { Status = DesignStatus.InvalidInput, Message = err };
            var (code, body, transport) = await PostAsync("/v1/revise", ProxyProtocol.ReviseRequestJson(current, lastExperiment, requestText), ct);
            return ProxyProtocol.ParseRevision(code, body, transport);
        }

        /// <summary>プロキシが動いているか（GET /health、3 秒で打ち切り）。</summary>
        public async Task<bool> PingAsync()
        {
            if (!_cfg.Enabled) return false;
            using (var req = UnityWebRequest.Get(_cfg.Url + "/health"))
            {
                req.timeout = 3;
                var tcs = new TaskCompletionSource<bool>();
                req.SendWebRequest().completed += _ => tcs.TrySetResult(true);
                await tcs.Task;
                return req.result == UnityWebRequest.Result.Success && req.responseCode == 200;
            }
        }

        /// <summary>音声（WAV）を文字にする（/v1/transcribe）。成功なら text、失敗なら error。</summary>
        public async Task<(string text, string error)> TranscribeAsync(byte[] wav, CancellationToken ct)
        {
            if (!_cfg.Enabled) return (null, "AI接続が無効のため、音声入力は使えません");
            using (var req = new UnityWebRequest(_cfg.Url + "/v1/transcribe", UnityWebRequest.kHttpVerbPOST))
            {
                req.uploadHandler = new UploadHandlerRaw(wav) { contentType = "audio/wav" };
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "audio/wav");
                if (!string.IsNullOrEmpty(_cfg.Token)) req.SetRequestHeader("x-ilab-token", _cfg.Token);
                req.timeout = 60;
                var tcs = new TaskCompletionSource<bool>();
                req.SendWebRequest().completed += _ => tcs.TrySetResult(true);
                using (ct.Register(() => { req.Abort(); tcs.TrySetCanceled(); }))
                    await tcs.Task;
                if (req.result == UnityWebRequest.Result.ConnectionError) return (null, "音声入力は PC とつないでいるときだけ使えます（プロキシに接続できません）");
                try
                {
                    var o = JObject.Parse(req.downloadHandler?.text ?? "");
                    if (req.responseCode == 200 && o.Value<string>("status") == "ok") return (o.Value<string>("text") ?? "", null);
                    return (null, o.Value<string>("message") ?? $"音声を文字にできませんでした（{req.responseCode}）");
                }
                catch (Newtonsoft.Json.JsonException) { return (null, $"音声を文字にできませんでした（{req.responseCode}）"); }
            }
        }

        /// <summary>接続できないときだけ1回再試行する（AIへの再試行2回はプロキシ側SDKが行う）。</summary>
        async Task<(long code, string body, string transportError)> PostAsync(string path, string json, CancellationToken ct, Action onSent = null)
        {
            if (!_cfg.Enabled) return (0, null, "AI接続は無効に設定されています");
            (long, string, string) last = (0, null, null);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                last = await SendOnce(path, json, ct, attempt == 0 ? onSent : null);
                if (last.Item1 != 0) return last; // HTTP 応答があれば再試行しない
                if (attempt == 0) await Task.Delay(800, ct);
            }
            return last;
        }

        async Task<(long, string, string)> SendOnce(string path, string json, CancellationToken ct, Action onSent)
        {
            using (var req = new UnityWebRequest(_cfg.Url + path, UnityWebRequest.kHttpVerbPOST))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)) { contentType = "application/json" };
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                if (!string.IsNullOrEmpty(_cfg.Token)) req.SetRequestHeader("x-ilab-token", _cfg.Token);
                req.timeout = Mathf.CeilToInt(_cfg.TimeoutSec);
                var tcs = new TaskCompletionSource<bool>();
                var op = req.SendWebRequest();
                onSent?.Invoke();
                op.completed += _ => tcs.TrySetResult(true);
                using (ct.Register(() => { req.Abort(); tcs.TrySetCanceled(); }))
                    await tcs.Task;
                if (req.result == UnityWebRequest.Result.ConnectionError)
                    return (0, null, req.error != null && req.error.Contains("timed out") ? "AIの応答がタイムアウトしました" : "AIプロキシに接続できません");
                string body = req.downloadHandler?.text;
                return (req.responseCode, body, null);
            }
        }
    }
}
