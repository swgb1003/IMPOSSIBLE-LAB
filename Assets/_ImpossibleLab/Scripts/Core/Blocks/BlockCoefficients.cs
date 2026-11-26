using System.Collections.Generic;
using System.Linq;

namespace ImpossibleLab.Core
{
    /// <summary>
    /// 機能ブロック（3週目の第1弾）の係数。雨具の係数（reality_model.json）と同じく、値・幅・確度・出典を持つ。
    /// 確度：standard（標準値）/ literature（文献値・要確認）/ estimate（推定・要調査）/ tuning（調整用の仮定）。
    /// 出典の調査は6週目（docs/GENERAL_INVENTION_DESIGN.md 10章）。それまでは estimate / tuning が多い。
    /// 計算式は docs/blocks.md。
    /// </summary>
    public static class BlockCoefficients
    {
        public static readonly RealityCoefficient[] All =
        {
            // 6週目（2026-11-11）に出典を調査して更新。詳細は docs/blocks.md 6章
            C("ledEfficacy", "LEDの発光効率", "lm/W", 100f, 60f, 150f, "literature", "米国エネルギー省の調査で市販LED製品の多くが 40〜80 lm/W（2013）、ヘッドランプの製品仕様に 100 lm/W の例。器具の効率は製品差が大きい"),
            C("heaterEfficiency", "加熱部の効率（熱として届く割合）", "", 0.9f, 0.7f, 1.0f, "estimate", "電熱の大半は熱になるが、装着部の外へ逃げる分を見込む（要調査）"),
            C("peltierCop", "冷却部（ペルチェ素子）の成績係数", "", 0.5f, 0.3f, 0.7f, "literature", "熱電冷蔵庫の総説で温度差 20〜25 °C のとき 0.5 未満、素子の選定例で 0.6。温度差が小さいほど高い"),
            C("thermalCapacity", "加温・冷却する範囲の熱容量", "J/K", 600f, 300f, 1200f, "tuning", "衣服の内側の空気と布・皮膚表面の一部をまとめた値（調整用の仮定）"),
            C("thermalLoss", "加温・冷却する範囲の熱損失係数", "W/K", 1.5f, 0.8f, 3f, "tuning", "外気との温度差 1 °C あたりに逃げる熱（調整用の仮定）"),
            C("cushionDamping", "緩衝材の減衰比", "", 0.3f, 0.1f, 0.5f, "tuning", "発泡材の衝撃吸収の目安（調整用の仮定）"),
            C("bottomOutFactor", "緩衝材がつぶれ切ったときの硬さの倍率", "", 50f, 20f, 200f, "tuning", "厚みを使い切ると硬い床に近づく（調整用の仮定）"),
            C("hardSurfaceStiffness", "緩衝材なしで床に当たるときの硬さ", "N/m", 2000000f, 500000f, 5000000f, "tuning", "樹脂の外殻が硬い床に当たる場合の目安（調整用の仮定）"),
            C("gripFriction", "つかみ具の摩擦係数", "", 0.5f, 0.3f, 0.8f, "literature", "ゴムと段ボール（乾燥）0.5〜0.8（Engineering ToolBox の表）。相手の面で大きくばらつくため下限寄りを使う"),
            C("liftEfficiency", "持ち上げ機構の効率", "", 0.6f, 0.4f, 0.8f, "estimate", "モーター＋減速機の効率の代表値（要調査）"),
            C("detectPowerW", "検知センサーの消費電力", "W", 0.5f, 0.1f, 2f, "estimate", "小型の距離センサー・カメラの代表値（要調査）"),
            C("speakerEfficiency", "スピーカーの変換効率（電気→音）", "", 0.003f, 0.001f, 0.01f, "literature", "効率 = 10^((感度 dB − 112) ÷ 10)。感度 82〜92 dB/1W/1m の小型スピーカーで 0.1〜1%（例：87 dB で約 0.3%）"),
            C("backgroundNoiseDb", "周りの騒音", "dB", 55f, 45f, 65f, "literature", "WHO の屋外の生活空間の指針値 55 dB（昼間 16 時間の LAeq、1999）。実際の騒音の測定値ではなく目安"),
            C("audibleMarginDb", "気づくのに必要な騒音との差", "dB", 10f, 5f, 15f, "tuning", "騒音より大きく聞こえれば気づくと仮定（調整用の仮定）"),
            C("lightNoticeNightLx", "夜に光に気づく照度", "lx", 0.00001f, 0.000001f, 0.0001f, "estimate", "暗い所で点の光に気づく照度の目安（要調査）"),
            C("lightNoticeDayLx", "昼に光に気づく照度", "lx", 0.001f, 0.0001f, 0.01f, "estimate", "明るい所で点の光に気づく照度の目安（要調査）"),

            // 6週目：REALITY の共通層（総質量・装着部位の重さの目安）と走行（坂）
            C("shellThicknessM", "部品の外殻の厚み（樹脂・金属・ゴム・ガラス）", "m", 0.002f, 0.001f, 0.004f, "tuning", "部品は中空の殻と仮定し、表面積×厚み×密度で重さを出す（調整用の仮定）"),
            C("fabricArealDensity", "布の面密度", "kg/m²", 0.15f, 0.08f, 0.3f, "estimate", "傘・日よけ用の布の目安（要調査）"),
            C("batteryDensity", "電池の密度", "kg/m³", 2000f, 1500f, 2500f, "estimate", "リチウムイオン電池パックの体積あたりの重さの目安（要調査）"),
            // 8週目：動作プログラムの関節（REALITY の関節の消費電力量）
            C("jointMotorPowerW", "関節を動かす駆動部の電力（動いている間）", "W", 5f, 2f, 15f, "estimate", "小型のギヤードモーター・サーボが動いている間の消費電力の目安（要調査）"),
            C("mountLimitHead", "頭に付ける重さの目安", "kg", 0.5f, 0.3f, 0.6f, "literature", "VRヘッドセット Meta Quest 3 が 515 g、ロード用自転車ヘルメットが 250〜300 g（製品仕様）。長時間の快適さの基準ではない"),
            C("mountLimitBack", "背負う重さの目安", "kg", 6f, 3f, 9f, "literature", "子どものかばんは体重の 10〜15% が目安としてよく引用される（米国小児科学会の名で 5〜20% と幅があり、根拠は限られる）。体重 60 kg の 10%"),
            C("mountLimitWaist", "腰に付ける重さの目安", "kg", 3f, 1.5f, 5f, "estimate", "工具ベルト・ウエストバッグの目安（要調査）"),
            C("mountLimitWrist", "手首に付ける重さの目安", "kg", 0.2f, 0.1f, 0.4f, "estimate", "腕時計・スマートウォッチ（数十 g）より重い装置の目安（要調査）"),
            C("mountLimitHand", "手に持つ重さの目安", "kg", 1f, 0.5f, 2f, "estimate", "片手で持ち続ける道具の目安（要調査）"),
            C("mountLimitFoot", "足（片足）に付ける重さの目安", "kg", 0.5f, 0.3f, 1f, "estimate", "靴（片足 0.3〜0.5 kg 程度）の目安（要調査）"),
            C("rollingResistance", "車輪の転がり抵抗係数", "", 0.02f, 0.01f, 0.08f, "literature", "空気入りタイヤ（舗装路）0.01〜0.015、軟らかいゴムのキャスター 0.06〜0.08（Engineering ToolBox・キャスターの仕様表）"),
            C("driveEfficiency", "走行の駆動効率（モーター＋減速機）", "", 0.6f, 0.4f, 0.8f, "estimate", "持ち上げ機構と同じ代表値（要調査）"),
            C("rampMaxSlopeDeg", "車いす用スロープの最大勾配", "°", 4.76f, 4.76f, 4.76f, "standard", "米国 ADA 基準：1:12（8.33%、約 4.76°）。坂の実験の標準の角度に使う"),
            // 2026-11-27：1日使う（電池の持ち）
            C("solarDerate", "太陽電池の発電の割合（定格に対して）", "", 0.5f, 0.3f, 0.7f, "estimate", "向き・角度・熱・薄い雲で定格より下がる分の目安。体や道具に付けると真上を向き続けないため低めにする（要調査）"),
        };

        static readonly Dictionary<string, RealityCoefficient> _byId = All.ToDictionary(c => c.Id);

        public static RealityCoefficient Get(string id) => _byId.TryGetValue(id, out var c) ? c : null;
        public static float V(string id) => _byId[id].Value;

        static RealityCoefficient C(string id, string label, string unit, float value, float min, float max, string status, string source) =>
            new RealityCoefficient { Id = id, LabelJa = label, Unit = unit, Value = value, Min = min, Max = max, Status = status, Source = source };
    }
}
