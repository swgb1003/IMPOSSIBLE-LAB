using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ImpossibleLab.Core
{
    /// <summary>AI設計プロキシとのやり取りの組み立てと解釈（通信そのものは Runtime 側）。ここは EditMode でテストする。</summary>
    public static class ProxyProtocol
    {
        public const int MaxResponseChars = 64 * 1024;

        public static string DesignRequestJson(string requestText) => new JObject { ["request"] = requestText.Trim() }.ToString(Newtonsoft.Json.Formatting.None);

        public static string ReviseRequestJson(InventionDna current, RainExperimentResult last, string instruction)
        {
            var o = new JObject { ["dna"] = DnaJson.ToJObject(current), ["instruction"] = instruction.Trim() };
            if (last != null && !last.IsRain)
            {
                var c = last.Config;
                var lr = new JObject { ["mode"] = c.Mode, ["environment"] = last.Environment };
                foreach (var m in last.Metrics) lr[m.Key] = m.Value;
                if (last.Environment == LabEnvironments.ObstacleCourse) { lr["obstacleCount"] = c.ObstacleCount; lr["payloadKg"] = c.PayloadKg; }
                else { lr["targetDistance"] = c.TargetDistance; lr["windSpeed"] = c.WindSpeed; lr["windDirectionDeg"] = c.WindDirectionDeg; }
                o["lastResult"] = lr;
            }
            else if (last != null)
            {
                var c = last.Config;
                o["lastResult"] = new JObject
                {
                    ["mode"] = c.Mode, ["coverageRatio"] = last.CoverageRatio, ["wetnessScore"] = last.WetnessScore,
                    ["headHits"] = last.HeadHits, ["upperHits"] = last.UpperHits, ["lowerHits"] = last.LowerHits,
                    ["baselineHits"] = last.BaselineHits, ["deviceHits"] = last.DeviceHits, ["driftM"] = last.DriftM,
                    ["rainIntensity"] = c.RainIntensity, ["windSpeed"] = c.WindSpeed, ["windDirectionDeg"] = c.WindDirectionDeg,
                };
                if (last.RuntimeMinutes.HasValue) o["lastResult"]["runtimeMinutes"] = last.RuntimeMinutes.Value;
            }
            return o.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>HTTPの結果を設計結果に変換する。status 0 は通信失敗（オフライン扱い）。</summary>
        public static DesignOutcome ParseDesign(long httpStatus, string body, string transportError)
        {
            if (httpStatus == 0) return new DesignOutcome { Status = DesignStatus.Offline, Message = transportError ?? "通信できません" };
            if (!TryParse(body, out var o)) return new DesignOutcome { Status = httpStatus >= 500 ? DesignStatus.Offline : DesignStatus.Failed, Message = $"AIプロキシの応答を読み取れません（HTTP {httpStatus}）" };
            string status = o.Value<string>("status");
            string message = Clip(o.Value<string>("message"), 200);
            if (httpStatus != 200 || status == "error")
            {
                bool offline = IsConnectivityProblem(httpStatus, o.Value<string>("code"));
                return new DesignOutcome { Status = offline ? DesignStatus.Offline : DesignStatus.Failed, Message = string.IsNullOrEmpty(message) ? $"AIプロキシのエラー（HTTP {httpStatus}）" : message };
            }
            if (status == "unsupported")
                return new DesignOutcome { Status = DesignStatus.Unsupported, Source = "ai", Message = string.IsNullOrEmpty(message) ? "この指示はこの版では設計できません" : message, Suggestions = Strings(o["suggestions"], 3) };
            if (status != "ok" || !(o["dna"] is JObject dna))
                return new DesignOutcome { Status = DesignStatus.Failed, Message = "AIの応答に設計データがありません" };
            var report = DnaJson.Parse(dna.ToString());
            if (!report.IsValid)
                return new DesignOutcome { Status = DesignStatus.Failed, Report = report, Source = "ai", Message = "AIの設計が検証に通りませんでした：" + report.Summary };
            return new DesignOutcome
            {
                Status = DesignStatus.Success, Dna = report.Dna, Report = report, Source = "ai",
                Message = "AI（Claude）が設計しました" + (report.Warnings.Count > 0 ? $"（範囲外の値 {report.Warnings.Count} 件を補正）" : ""),
                DesignPoints = Strings(o["designPoints"], 4),
            };
        }

        public static RevisionOutcome ParseRevision(long httpStatus, string body, string transportError)
        {
            if (httpStatus == 0) return new RevisionOutcome { Status = DesignStatus.Offline, Message = transportError ?? "通信できません" };
            if (!TryParse(body, out var o)) return new RevisionOutcome { Status = httpStatus >= 500 ? DesignStatus.Offline : DesignStatus.Failed, Message = $"AIプロキシの応答を読み取れません（HTTP {httpStatus}）" };
            string status = o.Value<string>("status");
            string message = Clip(o.Value<string>("message"), 200);
            if (httpStatus != 200 || status == "error")
            {
                bool offline = IsConnectivityProblem(httpStatus, o.Value<string>("code"));
                return new RevisionOutcome { Status = offline ? DesignStatus.Offline : DesignStatus.Failed, Message = string.IsNullOrEmpty(message) ? $"AIプロキシのエラー（HTTP {httpStatus}）" : message };
            }
            if (status == "unsupported") return new RevisionOutcome { Status = DesignStatus.Unsupported, Message = string.IsNullOrEmpty(message) ? "この指示では改良案を作れませんでした" : message };
            if (status != "ok" || !(o["proposal"] is JObject p)) return new RevisionOutcome { Status = DesignStatus.Failed, Message = "AIの応答に改良案がありません" };
            if (!RevisionEngine.TryParseProposal(p.ToString(), out var proposal, out var err))
                return new RevisionOutcome { Status = DesignStatus.Failed, Message = "AIの改良案が検証に通りませんでした：" + err };
            proposal.Source = "ai";
            return new RevisionOutcome { Status = DesignStatus.Success, Proposal = proposal, Message = proposal.Summary };
        }

        // 接続・混雑・鍵の問題だけをオフライン切替の対象にする。AIの出力が不正・拒否された場合はエラーとして見せる
        static readonly HashSet<string> ConnectivityCodes = new HashSet<string>
        {
            "no_api_key", "upstream_timeout", "upstream_unreachable", "upstream_rate_limited", "rate_limited", "upstream_auth", "upstream_error", "internal",
        };

        public static bool IsConnectivityProblem(long httpStatus, string code) =>
            httpStatus == 503 || httpStatus == 504 || (code != null && ConnectivityCodes.Contains(code));

        static bool TryParse(string body, out JObject o)
        {
            o = null;
            if (string.IsNullOrEmpty(body) || body.Length > MaxResponseChars) return false;
            try { o = JObject.Parse(body); return true; }
            catch (Newtonsoft.Json.JsonException) { return false; }
        }

        static List<string> Strings(JToken t, int max) =>
            t is JArray a ? a.Where(x => x.Type == JTokenType.String).Select(x => Clip((string)x, 120)).Where(s => s.Length > 0).Take(max).ToList() : new List<string>();

        static string Clip(string s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length > n ? s.Substring(0, n) : s);
    }
}
