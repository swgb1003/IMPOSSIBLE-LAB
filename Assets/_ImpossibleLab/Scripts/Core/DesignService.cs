using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ImpossibleLab.Core
{
    /// <summary>S02 の段階表示。実際の処理段階に対応させ、擬似的な％は出さない。</summary>
    public enum DesignStage { IntentAnalysis, FeatureSelection, PartDesign, Assembly, Complete }

    public static class DesignStageText
    {
        public static readonly DesignStage[] All = { DesignStage.IntentAnalysis, DesignStage.FeatureSelection, DesignStage.PartDesign, DesignStage.Assembly, DesignStage.Complete };
        public static string Label(DesignStage s)
        {
            switch (s)
            {
                case DesignStage.IntentAnalysis: return "意図解析";
                case DesignStage.FeatureSelection: return "機能選定";
                case DesignStage.PartDesign: return "部品設計";
                case DesignStage.Assembly: return "組み立て";
                default: return "完成";
            }
        }
    }

    public enum DesignStatus { Success, Unsupported, InvalidInput, Failed, Offline, Cancelled }

    public sealed class DesignOutcome
    {
        public DesignStatus Status;
        public InventionDna Dna;
        public ValidationReport Report;
        public string Message = "";
        public string Source = "offline";
        public List<string> DesignPoints = new List<string>();
        public List<string> Suggestions = new List<string>();
    }

    public sealed class RevisionOutcome
    {
        public DesignStatus Status;
        public RevisionProposal Proposal;
        public string Message = "";
    }

    public interface IInventionDesignService
    {
        string DisplayName { get; }
        Task<DesignOutcome> GenerateAsync(string requestText, IProgress<DesignStage> progress, CancellationToken ct);
        Task<RevisionOutcome> ReviseAsync(InventionDna current, RainExperimentResult lastExperiment, string requestText, CancellationToken ct);
    }

    public static class RequestRules
    {
        public const int MaxRequestLength = 200;
        public static string Check(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "作りたい道具を入力するか、クイック発明から選んでください";
            if (text.Trim().Length > MaxRequestLength) return $"指示が長すぎます（{MaxRequestLength}文字以内）";
            return null;
        }
    }

    /// <summary>
    /// ネットワーク不要のサンプル設計サービス（仕様2 MUST-11, 6.3）。AIではないことをUIに明示する。
    /// キーワードでサンプル設計図を選び、必ず DnaJson で検証する。
    /// </summary>
    public sealed class OfflineDesignService : IInventionDesignService
    {
        readonly Dictionary<string, string> _samples; // category -> json
        readonly int _stageDelayMs;

        public OfflineDesignService(Dictionary<string, string> samplesByCategory, int stageDelayMs = 0)
        {
            _samples = samplesByCategory;
            _stageDelayMs = stageDelayMs;
        }

        public string DisplayName => "オフライン（サンプル設計図）";

        static readonly string[] RainWords = { "雨", "傘", "濡れ", "ぬれ", "防水", "かさ", "umbrella", "rain" };
        static readonly string[] MobilityWords = { "飛", "靴", "シューズ", "浮", "移動" };
        static readonly string[] CarrierWords = { "運", "荷物", "ついてくる", "追従", "ロボット" };

        public async Task<DesignOutcome> GenerateAsync(string requestText, IProgress<DesignStage> progress, CancellationToken ct)
        {
            var err = RequestRules.Check(requestText);
            if (err != null) return new DesignOutcome { Status = DesignStatus.InvalidInput, Message = err };
            string text = requestText.Trim().ToLowerInvariant();

            progress?.Report(DesignStage.IntentAnalysis);
            await Pause(ct);
            string category = RainWords.Any(text.Contains) ? "protection"
                : MobilityWords.Any(text.Contains) ? "mobility"
                : CarrierWords.Any(text.Contains) ? "carrier" : null;

            progress?.Report(DesignStage.FeatureSelection);
            await Pause(ct);
            if (category == null || !_samples.TryGetValue(category, out var json))
            {
                var cat = category == null ? null : LabCatalog.Category(category);
                return new DesignOutcome
                {
                    Status = DesignStatus.Unsupported,
                    Message = cat == null
                        ? "オフラインでは、この指示に対応する設計図がありません。"
                        : $"「{cat.LabelJa}」カテゴリの設計図はこの版では未対応です。",
                    Suggestions = { "対応している発明：ハンズフリー雨具・追従運搬ロボット・空飛ぶ靴", "例：「手を使わず雨を防ぐ装置を作って」「荷物を運んでついてくるロボットを作って」" },
                };
            }

            progress?.Report(DesignStage.PartDesign);
            await Pause(ct);
            var report = DnaJson.Parse(json);
            if (!report.IsValid)
                return new DesignOutcome { Status = DesignStatus.Failed, Report = report, Message = "サンプル設計図の検証に失敗しました: " + report.Summary };

            var dna = report.Dna;
            var points = new List<string>();
            foreach (var b in dna.Behaviors)
            {
                var info = LabCatalog.Behavior(b.Type);
                points.Add(info.LabelJa + "：" + string.Join("、", info.Parameters.Select(p => $"{p.LabelJa} {LabCatalog.FormatValue(p, b.Parameters[p.Key])}")));
            }
            return new DesignOutcome
            {
                Status = DesignStatus.Success, Dna = dna, Report = report, Source = "offline",
                Message = "オフラインのサンプル設計図を使用しました（AI未接続）", DesignPoints = points,
            };
        }

        public async Task<RevisionOutcome> ReviseAsync(InventionDna current, RainExperimentResult last, string requestText, CancellationToken ct)
        {
            var err = RequestRules.Check(requestText);
            if (err != null) return new RevisionOutcome { Status = DesignStatus.InvalidInput, Message = err };
            await Pause(ct);
            var proposal = OfflineRevisionRules.Propose(current, requestText);
            if (proposal.Operations.Count == 0)
                return new RevisionOutcome { Status = DesignStatus.Unsupported, Proposal = proposal, Message = "この指示はオフライン改良ルールでは対応できません。" };
            return new RevisionOutcome { Status = DesignStatus.Success, Proposal = proposal, Message = proposal.Summary };
        }

        async Task Pause(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_stageDelayMs > 0) await Task.Delay(_stageDelayMs, ct);
        }
    }

    /// <summary>オフライン改良ルール。キーワード→許可された update_parameter 操作。</summary>
    public static class OfflineRevisionRules
    {
        sealed class Rule
        {
            public string[] Words;
            public string Behavior, Parameter;
            public float Delta;
            public string Text;
            /// <summary>このカテゴリの発明にだけ使う（null なら全カテゴリ）。</summary>
            public string Category;
        }

        static readonly Rule[] Rules =
        {
            // 防護（ハンズフリー雨具）
            new Rule { Category = "protection", Words = new[] { "横", "側面", "吹き込", "斜め" }, Behavior = "rain_shield", Parameter = "skirtLength", Delta = 0.25f, Text = "側面カバーを伸ばして横からの雨を防ぐ" },
            new Rule { Category = "protection", Words = new[] { "範囲", "広", "大き" }, Behavior = "rain_shield", Parameter = "radius", Delta = 0.2f, Text = "防護半径を広げる" },
            new Rule { Category = "protection", Words = new[] { "狭", "小さ", "コンパクト", "軽" }, Behavior = "rain_shield", Parameter = "radius", Delta = -0.15f, Text = "防護半径を小さくする" },
            new Rule { Category = "protection", Words = new[] { "低く", "近づけ", "下げ" }, Behavior = "follow_head", Parameter = "heightOffset", Delta = -0.1f, Text = "頭上との距離を縮める" },
            new Rule { Category = "protection", Words = new[] { "高く", "上げ" }, Behavior = "follow_head", Parameter = "heightOffset", Delta = 0.1f, Text = "頭上との距離を広げる" },
            new Rule { Category = "protection", Words = new[] { "漏れ", "遮水" }, Behavior = "rain_shield", Parameter = "effectiveness", Delta = 0.05f, Text = "遮水効率を上げる" },
            // 運搬（追従運搬ロボット）
            new Rule { Category = "carrier", Words = new[] { "よけ", "避け", "回避", "ぶつか", "障害" }, Behavior = "avoid_obstacle", Parameter = "detectionRange", Delta = 0.8f, Text = "障害物の検知距離を延ばしてよける" },
            new Rule { Category = "carrier", Words = new[] { "速", "遅れ", "急い" }, Behavior = "follow_user", Parameter = "speed", Delta = 0.4f, Text = "追従速度を上げる" },
            new Rule { Category = "carrier", Words = new[] { "近く", "そば", "離れないで" }, Behavior = "follow_user", Parameter = "followDistance", Delta = -0.3f, Text = "追従距離を縮める" },
            new Rule { Category = "carrier", Words = new[] { "離れて", "距離をとって", "遠く" }, Behavior = "follow_user", Parameter = "followDistance", Delta = 0.4f, Text = "追従距離を広げる" },
            new Rule { Category = "carrier", Words = new[] { "重い", "たくさん", "積", "大きな荷物" }, Behavior = "carry_load", Parameter = "capacity", Delta = 10f, Text = "積載量を増やす" },
            // 移動（空飛ぶ靴）
            new Rule { Category = "mobility", Words = new[] { "高く", "浮", "上がら" }, Behavior = "hover", Parameter = "maxHeight", Delta = 0.5f, Text = "最大高度を上げる" },
            new Rule { Category = "mobility", Words = new[] { "高く", "浮", "上がら", "揚力" }, Behavior = "hover", Parameter = "lift", Delta = 0.2f, Text = "揚力を上げる" },
            new Rule { Category = "mobility", Words = new[] { "速", "遅い", "時間がかか" }, Behavior = "move_forward", Parameter = "thrust", Delta = 0.25f, Text = "推力を上げる" },
            new Rule { Category = "mobility", Words = new[] { "速", "遅い", "時間がかか" }, Behavior = "move_forward", Parameter = "speedLimit", Delta = 0.6f, Text = "速度上限を上げる" },
            new Rule { Category = "mobility", Words = new[] { "ゆっくり", "安全" }, Behavior = "move_forward", Parameter = "speedLimit", Delta = -0.4f, Text = "速度上限を下げる" },
            // 共通
            new Rule { Words = new[] { "風", "安定", "揺れ", "ぶれ", "流され" }, Behavior = "stabilize", Parameter = "stabilizationStrength", Delta = 0.3f, Text = "姿勢安定を強めて風で揺れにくくする" },
        };

        static readonly Dictionary<string, string[]> Suggested = new Dictionary<string, string[]>
        {
            ["protection"] = new[] { "防ぐ範囲を広げて", "横からの雨も防いで", "風に流されないようにして", "もう少し低くして" },
            ["carrier"] = new[] { "障害物をよけて", "もっと速くついてきて", "重い荷物も運んで", "もっと近くでついてきて" },
            ["mobility"] = new[] { "風で揺れないようにして", "もっと速く飛んで", "もっと高く飛んで", "ゆっくり安全に飛んで" },
        };

        static readonly Dictionary<string, string> Capabilities = new Dictionary<string, string>
        {
            ["protection"] = "防護範囲 / 横からの雨 / 風への強さ / 高さ / 遮水効率",
            ["carrier"] = "障害物の回避 / 追従速度 / 追従距離 / 積載量 / 揺れにくさ",
            ["mobility"] = "高度・揚力 / 速さ / 揺れにくさ",
        };

        public static IEnumerable<string> SuggestedRequests => Suggested["protection"];
        public static IEnumerable<string> SuggestedRequestsFor(string category) =>
            category != null && Suggested.TryGetValue(category, out var s) ? s : Suggested["protection"];

        public static RevisionProposal Propose(InventionDna current, string request)
        {
            var p = new RevisionProposal { Source = "offline" };
            string text = request.Trim();
            var used = new HashSet<string>();
            var texts = new List<string>();
            foreach (var rule in Rules)
            {
                if (rule.Category != null && rule.Category != current.Category) continue;
                if (!rule.Words.Any(text.Contains)) continue;
                string key = rule.Behavior + "." + rule.Parameter;
                if (!used.Add(key)) continue;
                var b = current.FindBehavior(rule.Behavior);
                var info = LabCatalog.Behavior(rule.Behavior)?.Param(rule.Parameter);
                if (b == null || info == null) { p.Notes.Add($"「{rule.Text}」にはこの発明に無い機能が必要です"); continue; }
                float now = b.Parameters[rule.Parameter];
                float target = UnityEngine.Mathf.Clamp(now + rule.Delta, info.Min, info.Max);
                if (UnityEngine.Mathf.Approximately(now, target)) { p.Notes.Add($"{info.LabelJa}はすでに許可範囲の限界です（{LabCatalog.FormatValue(info, now)}）"); continue; }
                p.Operations.Add(new RevisionOperation { Op = RevisionOps.UpdateParameter, BehaviorType = rule.Behavior, Parameter = rule.Parameter, Value = target });
                texts.Add(rule.Text);
            }
            if (p.Operations.Count == 0 && p.Notes.Count == 0)
            {
                string cat = current.Category != null && Capabilities.ContainsKey(current.Category) ? current.Category : "protection";
                p.Notes.Add("対応できる改良：" + Capabilities[cat]);
                p.Notes.Add("例：「" + string.Join("」「", SuggestedRequestsFor(cat)) + "」");
            }
            p.Summary = texts.Count > 0 ? string.Join("、", texts) : "改良案なし";
            return p;
        }
    }

    public static class LabClock
    {
        public static string NowIso() => DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
    }
}
