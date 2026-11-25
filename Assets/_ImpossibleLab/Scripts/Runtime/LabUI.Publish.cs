using System.Collections.Generic;
using System.Linq;
using ImpossibleLab.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace ImpossibleLab
{
    /// <summary>輪の計器コンソール：保存の画面から発明ライブラリへ公開する（確認・同意・送信）と、採用の知らせ。</summary>
    public sealed partial class LabUI
    {
        TMP_InputField _publishInput;

        /// <summary>公開の確認・同意・送信中の画面。保存の画面（Saved）の中の段。</summary>
        void BuildPublish(SavedInvention inv)
        {
            var c = _con;
            const float LX = ConsoleLayer.LX, PW = ConsoleLayer.PW, RX = ConsoleLayer.RX;
            var dna = inv.Current;
            int experiments = inv.Experiments.Count;
            var ink = LabTheme.ConInk; var mut = LabTheme.ConMut;

            if (App.Publish == PublishStep.Sending)
            {
                c.Dial();
                c.BigWord("送っています", "PC の発明ライブラリへ送っています", LabTheme.ConCy);
                c.Plate(LX, 78f, PW, 468f, "公開する発明");
                ConParasJa(48f, 130f, 414f, 540f, new[] { ($"{dna.Name} Rev.{dna.Revision}", LabTheme.ConCy), ("試作品の3D と写真を作って送ります。数秒かかります", ink) }, 18f);
                ConFrame(new List<ConBtn>(), null);
                return;
            }

            // 真ん中は、写真に撮って載せる試作品そのもの
            ConProduct(dna, -1);
            c.T(ConsoleLayer.C.x, 650f, "この試作品を撮って、写真1枚として載せます", 17f, mut, anchor: 0);

            if (App.Publish == PublishStep.Confirm)
            {
                string request = string.IsNullOrWhiteSpace(inv.RequestText) ? "依頼の言葉の記録はありません" : ConsoleLayer.Fit(inv.RequestText.Trim(), 70);
                c.Plate(LX, 78f, PW, 468f, "公開するもの");
                ConParasJa(48f, 130f, 414f, 540f, new[]
                {
                    ("依頼の言葉（原文のまま）", mut), (request, ink),
                    ("試作品の3D と写真1枚", ink),
                    ($"実験 {experiments} 回の条件と結果（不合格も含む）", ink),
                    ($"改良の履歴（Rev.1 から Rev.{dna.Revision} まで）", ink),
                    ("発案者の表示（右で選ぶ）", ink),
                }, 18f);
                c.Plate(LX, 558f, PW, 190f, "載せないもの");
                ConParasJa(48f, 606f, 414f, 742f, new[] { ("声そのもの", ink), ("ほかの保存した発明", ink), ("この端末の情報", ink) });

                bool anon = App.PublishAnonymous;
                c.Plate(RX, 78f, PW, 468f, "発案者の表示");
                c.Row(RX, 124f, PW, 64f, "匿名で載せる", "サイトには発案者とだけ出ます", sel: anon,
                    onClick: anon ? (UnityAction)null : () => App.SetPublishAnonymous(true), name: "PubAnon");
                c.Row(RX, 192f, PW, 64f, "表示名で載せる", "サイトに発案者のページができます", sel: !anon,
                    onClick: !anon ? (UnityAction)null : () => App.SetPublishAnonymous(false), name: "PubNamed");
                if (!anon)
                {
                    _publishInput = c.Input(RX + 20f, 276f, 414f, 56f, "表示名（40 文字まで）", LibraryLocal.MaxDisplayName, App.SetPublishName, strong: true);
                    _publishInput.SetTextWithoutNotify(App.PublishName);
                    ConParasJa(RX + 20f, 346f, 414f, 540f, new[] { ("本名でなくてかまいません。採用した人とのやりとりにも、この名前で出ます", mut) });
                }
                c.Plate(RX, 558f, PW, 190f, "載る場所");
                ConParasJa(RX + 20f, 606f, 414f, 742f, new[] { ("この PC の中の発明ライブラリ（サイト）", ink), ("インターネットには出ません", mut), ("公開はあとでやめられます", mut) });
                ConFrame(new List<ConBtn> { new ConBtn("やめる", App.CancelPublish, ConKind.Quiet, 140f) },
                    new ConBtn("次へ　同意の確認", App.PublishToConsent, ConKind.Primary, 300f));
                return;
            }

            // 同意：短い版（docs/PUBLISH_LIBRARY.md 6章）。全文はサイトの利用規約
            c.Plate(LX, 78f, PW, 670f, "公開の前に読んでください");
            ConParasJa(48f, 130f, 414f, 740f, new[]
            {
                ("この発明を発明ライブラリに公開します。研究者と開発者が読み、研究や開発の検討に使えるようになります。", ink),
                ("アイデアの権利はあなたに残ります。採用されたら知らせが届き、その先の条件は別に話し合います。", ink),
                ("公開すると、特許が取りにくくなることがあります。特許を考えているなら、公開の前に専門家に相談してください。", ink),
                ("試作品と実験は簡単な計算によるもので、安全や性能は保証しません。", ink),
                ("設計には AI の手伝いが入っています。", ink),
                ("公開はいつでも取り下げられます。", ink),
                ("採用されると、相手とあなただけが読める連絡の場ができます。", ink),
                ("18歳未満の人は、保護者の同意を得てから公開してください。", LabTheme.ConOr),
            }, 17f);
            c.Plate(RX, 78f, PW, 468f, "公開する内容");
            ConParasJa(RX + 20f, 130f, 414f, 540f, new[]
            {
                ($"{dna.Name} Rev.{dna.Revision}", LabTheme.ConCy),
                ("発案者の表示：" + (App.PublishAnonymous ? "匿名" : App.PublishName), ink),
                ($"実験 {experiments} 回・試作品の3D・写真1枚", ink),
            }, 18f);
            c.Plate(RX, 558f, PW, 190f, "利用規約");
            ConParasJa(RX + 20f, 606f, 414f, 742f, new[] { ("全文は PC のサイトの利用規約にあります", ink), (LabApp.SiteAddress, mut) });
            ConFrame(new List<ConBtn> { new ConBtn("やめる", App.CancelPublish, ConKind.Quiet, 140f), new ConBtn("戻る", App.PublishBack, ConKind.Normal, 120f) },
                new ConBtn("同意して公開する", App.AgreeAndPublish, ConKind.Primary, 320f));
        }

        /// <summary>保存の画面の「次にすること」の下：公開する・公開の状態・公開をやめる確認。</summary>
        void ConLibrarySection(SavedInvention inv)
        {
            var c = _con;
            const float RX = ConsoleLayer.RX;
            var ink = LabTheme.ConInk; var mut = LabTheme.ConMut;
            string id = inv?.Current.Id;
            var mine = App.MineFor(id);
            c.T(RX + 20f, 296f, "発明ライブラリ", 18f, mut, bold: true);

            if (App.Publish == PublishStep.ConfirmWithdraw && mine != null)
            {
                ConParasJa(RX + 20f, 316f, 414f, 470f, new[] { ("公開をやめますか", ink), ("サイトの一覧から消えます。すでに読まれた内容は戻せません", LabTheme.ConOr) });
                c.Button(RX + 20f, 476f, 200f, 52f, "やめない", App.CancelPublish, ConKind.Normal, 18f);
                c.Button(RX + 228f, 476f, 206f, 52f, "公開をやめる", App.ConfirmWithdraw, ConKind.Warm, 18f);
                return;
            }
            if (mine != null)
            {
                int wants = mine.WantsVr + mine.WantsSite;
                ConParasJa(RX + 20f, 316f, 414f, 470f, new[]
                {
                    ($"{mine.StatusJa}　Rev.{mine.Revision} を公開しています", LabTheme.ConCy),
                    ($"ほしい {wants} 人・VR で試した人 {mine.Tried} 人・採用 {mine.Adoptions.Count} 件", ink),
                    ("PC のブラウザで " + LabApp.SiteAddress + " を開くと見られます", mut),
                }, 17f);
                c.Button(RX + 20f, 476f, 200f, 52f, "公開を更新する", App.StartPublish, ConKind.Normal, 18f, App.PublishProblemNow == null);
                c.Button(RX + 228f, 476f, 206f, 52f, "公開をやめる", App.AskWithdraw, ConKind.Quiet, 18f);
                return;
            }
            string problem = App.PublishProblemNow;
            bool publishedBefore = id != null && App.Library != null && App.Library.Published.ContainsKey(id) && !App.MineLoaded;
            string note = App.PublishError != null ? "公開できませんでした：" + App.PublishError
                : problem ?? (publishedBefore ? "前に公開しています。PC とつながると状態を読みます" : "研究者と開発者が読むサイトに載せます");
            ConParasJa(RX + 20f, 316f, 414f, 410f, new[] { (note, App.PublishError != null ? LabTheme.ConOr : problem != null ? mut : ink) }, 17f);
            c.Button(RX + 20f, 420f, 414f, 56f, "発明ライブラリに公開する", App.StartPublish, ConKind.Normal, 20f, problem == null);
        }

        /// <summary>最初の指示の画面に出す採用の知らせ（今日の課題の板の代わりに、一度だけ）。</summary>
        bool ConAdoptionNotice()
        {
            if (App.AdoptionNotices.Count == 0) return false;
            var c = _con;
            const float LX = ConsoleLayer.LX, PW = ConsoleLayer.PW;
            var (pub, a) = App.AdoptionNotices[0];
            int more = App.AdoptionNotices.Count - 1;
            c.Plate(LX, 354f, PW, 394f, "採用されました");
            var who = $"{a.RoleJa}　{a.AdopterName}" + (string.IsNullOrWhiteSpace(a.AdopterAffiliation) ? "" : $"（{a.AdopterAffiliation}）");
            ConParasJa(48f, 404f, 414f, 686f, new[]
            {
                (pub.Name, LabTheme.ConCy),
                (who + (a.AdopterVerified ? "　所属は運営が確かめています" : ""), LabTheme.ConInk),
                ("理由：" + a.Reason, LabTheme.ConInk),
                (more > 0 ? $"ほかに {more} 件の採用があります" : null, LabTheme.ConInk),
                ("やりとりは PC のサイトの「やりとり」で読めます", LabTheme.ConMut),
            }, 17f);
            c.Button(48f, 692f, 200f, 46f, "読んだ", App.DismissAdoptionNotices, ConKind.Normal, 18f);
            return true;
        }

        /// <summary>保存した発明の一覧の行に出す公開の状態（公開していなければ空）。</summary>
        string GalleryPublishState(string inventionId)
        {
            var m = App.MineFor(inventionId);
            return m != null ? "・" + m.StatusJa : "";
        }

        /// <summary>折り返す文を上から積む（語の切れ目だけで折り返す）。</summary>
        float ConParasJa(float x, float y, float w, float yMax, IEnumerable<(string text, Color col)> items, float size = 16f) =>
            ConParas(x, y, w, yMax, items.Select(p => (JaWrap.Wrap(p.text, w / size - 0.6f), p.col)), size);

        /// <summary>真ん中の試作品を、全体が見える向きで撮って JPEG にする（撮れなければ null）。</summary>
        byte[] CaptureProductJpg(InventionDna dna)
        {
            if (dna == null) return null;
            if (_product == null) _product = ProductPreview.Create(App.Assets);
            _product.Show(dna, -1);
            if (!_product.Shown || !(_product.Texture is RenderTexture rt)) return null;
            _product.RenderNow();
            var prev = RenderTexture.active;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                var jpg = tex.EncodeToJPG(85);
                return jpg.Length <= LibraryProtocol.MaxJpegBytes ? jpg : tex.EncodeToJPG(60);
            }
            finally
            {
                RenderTexture.active = prev;
                Destroy(tex);
            }
        }
    }
}
