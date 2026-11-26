using System;
using System.Collections.Generic;
using System.Linq;
using ImpossibleLab.Core;
using UnityEngine;

namespace ImpossibleLab
{
    /// <summary>発明ライブラリに公開する流れの段（保存の画面の中で進む）。</summary>
    public enum PublishStep { None, Confirm, Consent, Sending, ConfirmWithdraw }

    /// <summary>
    /// 発明ライブラリ（docs/PUBLISH_LIBRARY.md 4章）：保存の画面から公開の確認 → 同意 → 送信。
    /// 公開した発明の状態（公開中・検討中・採用）と、採用の知らせ（次に開いたとき一度だけ）を PC から読む。
    /// </summary>
    public sealed partial class LabApp
    {
        public LibraryLocal Library { get; private set; }
        ILibraryTransport _library;
        /// <summary>中央の試作品を JPEG で撮る（画面が設定する。撮れなければ null）。</summary>
        [NonSerialized] public Func<InventionDna, byte[]> CaptureProduct;

        [NonSerialized] public PublishStep Publish = PublishStep.None;
        [NonSerialized] public bool PublishAnonymous = true;
        [NonSerialized] public string PublishName = "";
        /// <summary>最後に失敗した理由（保存の画面に出す。次に公開を始めると消える）。</summary>
        [NonSerialized] public string PublishError;
        /// <summary>PC から読んだ自分の公開（読めていなければ空）。</summary>
        [NonSerialized] public List<MinePublication> Mine = new List<MinePublication>();
        public bool MineLoaded { get; private set; }
        bool _mineBusy;
        /// <summary>まだ知らせていない採用（発明と採用）。最初の指示の画面に一度だけ出す。</summary>
        [NonSerialized] public List<(MinePublication pub, MineAdoption adoption)> AdoptionNotices = new List<(MinePublication, MineAdoption)>();

        /// <summary>サイトの入口（PC のブラウザで開く）。</summary>
        public const string SiteAddress = "http://127.0.0.1:8787/";
        /// <summary>発案者が Quest の番号で入るサイトの画面。</summary>
        public const string PairAddress = "http://127.0.0.1:8787/pair";

        /// <summary>サイトに入る番号（PC のサイトに、この端末の発案者として入る。10 分有効）。</summary>
        [NonSerialized] public string PairCode;
        [NonSerialized] public DateTime PairExpiresUtc;
        [NonSerialized] public string PairError;
        bool _pairBusy;
        public bool PairBusy => _pairBusy;
        /// <summary>今使える番号（期限が切れていれば null）。</summary>
        public string PairCodeNow => PairCode != null && DateTime.UtcNow < PairExpiresUtc ? PairCode : null;

        void InitLibrary()
        {
            Library = LibraryLocal.Load(Application.persistentDataPath);
            try { Library.Save(Application.persistentDataPath); } catch (Exception e) { Debug.LogWarning("[ILAB] library.json を保存できません: " + e.Message); }
            _library = new LibraryClient(Proxy, () => Library.CreatorKey);
            Sm.Changed += (from, to) =>
            {
                if (to != LabState.Saved) { Publish = PublishStep.None; PairCode = null; PairError = null; }
                // 保存の画面・一覧と、最初の指示の画面に来たときに読み直す（採用の知らせを、アプリを開き直さなくても出す）
                bool reread = to == LabState.Saved || to == LabState.Gallery || (to == LabState.Drafting && from != LabState.Idle) || (to == LabState.Idle && from != LabState.Drafting);
                if (reread && ProxyOnline == true) RefreshMine();
            };
        }

        /// <summary>Editor 検証専用：PC とつながっている扱いを保つ（つながりの確認で上書きしない）。</summary>
        [NonSerialized] internal bool VerifyHoldsPc;
        /// <summary>Editor 検証（DemoAutopilot）専用：PC の代わりに送った中身を受け取る。</summary>
        internal void SetLibraryTransportForVerify(ILibraryTransport t) { if (t != null) _library = t; }

        /// <summary>この発明の公開（取り下げたものは除く）。</summary>
        public MinePublication MineFor(string inventionId) =>
            inventionId == null ? null : Mine.FirstOrDefault(m => m.InventionId == inventionId && m.Live);

        public string PublishProblemNow => LibraryProtocol.PublishProblem(Current, ProxyOnline);

        /// <summary>画面の作り直しの判定に使う（状態・数・知らせが変わったら描き直す）。</summary>
        public string LibrarySignature =>
            $"{Publish}{PublishAnonymous}{PublishError}|{MineLoaded}|{string.Join(",", Mine.Select(m => $"{m.Id}{m.Status}{m.WantsVr}{m.WantsSite}{m.Adoptions.Count}"))}|{AdoptionNotices.Count}|{PairCodeNow}{PairError}{_pairBusy}";

        // ---------------- 公開の流れ ----------------
        public void StartPublish()
        {
            if (Sm.State != LabState.Saved || Current == null) return;
            var problem = PublishProblemNow;
            if (problem != null) { ShowToast(problem); return; }
            PublishError = null;
            PublishName = Library.DisplayName ?? "";
            PublishAnonymous = string.IsNullOrWhiteSpace(PublishName);
            Publish = PublishStep.Confirm;
            Notify();
        }

        public void SetPublishAnonymous(bool anonymous) { if (Publish != PublishStep.Confirm) return; PublishAnonymous = anonymous; Notify(); }
        public void SetPublishName(string name) { PublishName = (name ?? "").Replace("\n", " ").Trim(); }

        public void PublishToConsent()
        {
            if (Publish != PublishStep.Confirm) return;
            if (!PublishAnonymous && string.IsNullOrWhiteSpace(PublishName)) { ShowToast("表示名を入れるか、匿名で載せるを選んでください"); return; }
            Publish = PublishStep.Consent;
            Notify();
        }

        public void PublishBack() { if (Publish == PublishStep.Consent) { Publish = PublishStep.Confirm; Notify(); } }
        public void CancelPublish() { if (Publish == PublishStep.Sending) return; Publish = PublishStep.None; Notify(); }

        /// <summary>同意して公開する：公開データ・3D・試作品の写真を作って PC に送る。</summary>
        public async void AgreeAndPublish()
        {
            if (Publish != PublishStep.Consent || Current == null) return;
            var problem = PublishProblemNow;
            if (problem != null) { PublishFailed(problem); return; }
            Publish = PublishStep.Sending;
            Notify();
            var dna = Current.Current;
            var opt = new PublishOptions { Anonymous = PublishAnonymous, DisplayName = PublishAnonymous ? "" : PublishName, AgreedAt = LabClock.NowIso() };
            string body;
            try
            {
                var pub = PublicationBuilder.Build(Current, opt);
                var glb = ProductExport.Export(dna, Assets).Glb;
                var images = new List<(string, string, int, byte[])>();
                byte[] jpg = null;
                try { jpg = CaptureProduct?.Invoke(dna); } catch (Exception e) { Debug.LogWarning("[ILAB] 試作品の写真を撮れません: " + e.Message); }
                if (jpg != null) images.Add(("stage", $"IMPOSSIBLE LAB で組み立てた Rev.{dna.Revision} の試作品", dna.Revision, jpg));
                body = LibraryProtocol.PublishBody(pub, glb, images);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ILAB] publish build failed: " + e);
                PublishFailed("公開するデータを作れませんでした");
                return;
            }
            var (code, resp, err) = await _library.SendAsync("POST", "/library", body, 60);
            var (id, error) = LibraryProtocol.ParsePublish(code, resp, err);
            Debug.Log($"[ILAB] publish status={code} ok={id != null}");
            if (id == null) { PublishFailed(error); return; }
            Library.Published[dna.Id] = id;
            if (!opt.Anonymous) Library.DisplayName = opt.DisplayName;
            SaveLibrary();
            Publish = PublishStep.None;
            ShowToast("発明ライブラリーに公開しました。PC のサイトで見られます");
            RefreshMine();
        }

        void PublishFailed(string reason)
        {
            PublishError = reason;
            Publish = PublishStep.None; // 失敗したら理由を出して保存の画面に戻る（4章）
            ShowToast("公開できませんでした：" + reason);
        }

        // ---------------- 取り下げ ----------------
        public void AskWithdraw() { if (Sm.State == LabState.Saved && MineFor(Current?.Current.Id) != null) { Publish = PublishStep.ConfirmWithdraw; Notify(); } }

        public async void ConfirmWithdraw()
        {
            var m = MineFor(Current?.Current.Id);
            if (Publish != PublishStep.ConfirmWithdraw || m == null) return;
            Publish = PublishStep.Sending;
            Notify();
            var (code, resp, err) = await _library.SendAsync("POST", $"/library/{m.Id}/withdraw", "{}", 20);
            Publish = PublishStep.None;
            if (err != null || code != 200) { ShowToast("公開をやめられませんでした：" + (err ?? LibraryProtocol.ErrorMessage(code, null))); return; }
            m.Status = "withdrawn";
            ShowToast("公開をやめました。サイトの一覧から消えます");
            RefreshMine();
        }

        // ---------------- 状態と採用の知らせ ----------------
        /// <summary>自分の公開の状態を PC から読み、まだ知らせていない採用を集める。</summary>
        public async void RefreshMine()
        {
            if (_mineBusy || _library == null) return;
            _mineBusy = true;
            try
            {
                var (code, body, err) = await _library.SendAsync("GET", "/library/mine", null, 10);
                if (err != null || code != 200) return;
                Mine = LibraryProtocol.ParseMine(body);
                MineLoaded = true;
                AdoptionNotices = Mine.Where(m => m.Live).SelectMany(m => m.Adoptions.Select(a => (m, a)))
                    .Where(x => !Library.SeenAdoptions.Contains(x.a.Id)).OrderBy(x => x.a.At, StringComparer.Ordinal).ToList();
                Notify();
            }
            catch (Exception e) { Debug.LogWarning("[ILAB] library mine failed: " + e.Message); }
            finally { _mineBusy = false; }
        }

        // ---------------- サイトに入る番号 ----------------
        /// <summary>PC のサイトに、この端末の発案者として入るための6桁の番号を PC にもらう（番号はサイトで1回だけ使える）。</summary>
        public async void RequestPairCode()
        {
            if (_pairBusy || _library == null) return;
            _pairBusy = true; PairError = null;
            Notify();
            try
            {
                var (code, body, err) = await _library.SendAsync("POST", "/library/pair", "{}", 10);
                var (pair, expires, error) = LibraryProtocol.ParsePair(code, body, err);
                PairCode = pair; PairExpiresUtc = expires; PairError = error;
            }
            catch (Exception e) { PairCode = null; PairError = "番号をもらえませんでした"; Debug.LogWarning("[ILAB] library pair failed: " + e.Message); }
            finally { _pairBusy = false; Notify(); }
        }

        /// <summary>採用の知らせを読んだ（次からは出さない。保存の画面の公開の状態には残る）。</summary>
        public void DismissAdoptionNotices()
        {
            foreach (var x in AdoptionNotices) Library.SeenAdoptions.Add(x.adoption.Id);
            AdoptionNotices.Clear();
            SaveLibrary();
            Notify();
        }

        void SaveLibrary()
        {
            try { Library.Save(Application.persistentDataPath); }
            catch (Exception e) { Debug.LogWarning("[ILAB] library.json を保存できません: " + e.Message); }
        }
    }
}
