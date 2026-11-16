namespace ImpossibleLab.Core
{
    /// <summary>S01 のクイック発明の例（仕様7章：6〜9件）。どれも AI 未接続でもオフラインの設計図で設計でき、実験できる。</summary>
    public static class QuickInventions
    {
        public sealed class Item
        {
            public string Title, Kind, Experiment, Request;
        }

        /// <summary>3 列 × 3 行で表示する順。1 行目は従来の 3 件、2〜3 行目は機能ブロックの発明（代表6件の依頼文）。</summary>
        public static readonly Item[] All =
        {
            new Item { Title = "手を使わない傘", Kind = "防護", Experiment = "雨と風の中で実験", Request = "手を使わず雨を防ぐ装置を作って" },
            new Item { Title = "空を飛べる靴", Kind = "移動", Experiment = "飛行デッキで実験", Request = "空を飛べる靴を作って" },
            new Item { Title = "ついてくる運搬ロボット", Kind = "運搬", Experiment = "障害物コースで実験", Request = "自動でついてくる運搬ロボットを作って" },
            new Item { Title = "頭の上の日よけ", Kind = "日よけ", Experiment = "真夏の日差しで実験", Request = "夏の通学路で日差しがきついので、頭の上で日陰を作ってくれる道具" },
            new Item { Title = "夜道の足元ライト", Kind = "照明", Experiment = "足元の明るさを計測", Request = "夜道で足元が暗くて怖いので、自動で足元を照らしてくれる道具" },
            new Item { Title = "背中を温めるヒーター", Kind = "加温", Experiment = "冬のバス停で計測", Request = "冬のバス停で待つ間、背中を温めてくれる道具" },
            new Item { Title = "段ボール持ち上げ機", Kind = "持ち上げ", Experiment = "棚へ上げる実験", Request = "床に置いた段ボールを棚の上まで持ち上げてくれる道具" },
            new Item { Title = "後ろを見守るリュック", Kind = "検知・通知", Experiment = "近づく自転車で実験", Request = "後ろから自転車が近づいてきたら教えてくれるリュック" },
            new Item { Title = "割れないスマホケース", Kind = "衝撃吸収", Experiment = "落下させて実験", Request = "スマホを落としても画面が割れないようにする道具" },
        };
    }
}
