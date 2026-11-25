using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace ImpossibleLab
{
    /// <summary>
    /// 発明ライブラリ（PC の中継プログラムの /library）を呼ぶ。接続先とトークンは AI 設計と同じ proxy.json。
    /// 端末の発案者の鍵を x-ilab-creator で送る（鍵は URL やログに出さない）。
    /// </summary>
    public interface ILibraryTransport
    {
        Task<(long code, string body, string transportError)> SendAsync(string method, string path, string json, int timeoutSec);
    }

    public sealed class LibraryClient : ILibraryTransport
    {
        readonly ProxyConfig _cfg;
        readonly Func<string> _creatorKey;
        public LibraryClient(ProxyConfig cfg, Func<string> creatorKey) { _cfg = cfg; _creatorKey = creatorKey; }

        public async Task<(long code, string body, string transportError)> SendAsync(string method, string path, string json, int timeoutSec)
        {
            if (_cfg == null || !_cfg.Enabled) return (0, null, "PC との接続が無効に設定されています");
            using (var req = new UnityWebRequest(_cfg.Url + path, method))
            {
                if (json != null)
                {
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)) { contentType = "application/json" };
                    req.SetRequestHeader("Content-Type", "application/json");
                }
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("x-ilab-creator", _creatorKey());
                if (!string.IsNullOrEmpty(_cfg.Token)) req.SetRequestHeader("x-ilab-token", _cfg.Token);
                req.timeout = timeoutSec;
                var tcs = new TaskCompletionSource<bool>();
                req.SendWebRequest().completed += _ => tcs.TrySetResult(true);
                await tcs.Task;
                if (req.result == UnityWebRequest.Result.ConnectionError)
                    return (0, null, req.error != null && req.error.Contains("timed out") ? "PC の応答がありませんでした（時間切れ）" : "PC とつながっていません（中継のプログラムが動いているか確かめてください）");
                return (req.responseCode, req.downloadHandler?.text, null);
            }
        }
    }
}
