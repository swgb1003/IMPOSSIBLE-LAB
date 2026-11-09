using System;
using System.Threading;
using System.Threading.Tasks;

namespace ImpossibleLab.Core
{
    /// <summary>
    /// AI（プロキシ）に接続できないときだけ、オフラインのサンプル設計図・改良ルールへ自動で切り替える（仕様 AT-07）。
    /// AIの出力が不正だった場合などは切り替えずにエラーとして返す（勝手に別の設計を出さない）。
    /// </summary>
    public sealed class FallbackDesignService : IInventionDesignService
    {
        readonly IInventionDesignService _primary;
        readonly IInventionDesignService _offline;

        public FallbackDesignService(IInventionDesignService primary, IInventionDesignService offline)
        {
            _primary = primary ?? throw new ArgumentNullException(nameof(primary));
            _offline = offline ?? throw new ArgumentNullException(nameof(offline));
        }

        public IInventionDesignService Offline => _offline;

        /// <summary>直近の呼び出しでオフラインに切り替えたか。</summary>
        public bool LastUsedFallback { get; private set; }

        public string DisplayName => LastUsedFallback ? _offline.DisplayName + "（AIに接続できず切替）" : _primary.DisplayName;

        public async Task<DesignOutcome> GenerateAsync(string requestText, IProgress<DesignStage> progress, CancellationToken ct)
        {
            var outcome = await _primary.GenerateAsync(requestText, progress, ct);
            LastUsedFallback = outcome.Status == DesignStatus.Offline;
            if (!LastUsedFallback) return outcome;
            ct.ThrowIfCancellationRequested();
            var off = await _offline.GenerateAsync(requestText, progress, ct);
            off.Message = "AIに接続できなかったため（" + outcome.Message + "）、" + off.Message;
            return off;
        }

        public async Task<RevisionOutcome> ReviseAsync(InventionDna current, RainExperimentResult lastExperiment, string requestText, CancellationToken ct)
        {
            var outcome = await _primary.ReviseAsync(current, lastExperiment, requestText, ct);
            LastUsedFallback = outcome.Status == DesignStatus.Offline;
            if (!LastUsedFallback) return outcome;
            var off = await _offline.ReviseAsync(current, lastExperiment, requestText, ct);
            off.Message = "AIに接続できなかったため、オフライン改良ルールを使用しました。" + (string.IsNullOrEmpty(off.Message) ? "" : off.Message);
            return off;
        }
    }
}
