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
            C("ledEfficacy", "LEDの発光効率", "lm/W", 100f, 60f, 150f, "estimate", "市販のLED照明・懐中電灯の発光効率の代表値（要調査）"),
            C("heaterEfficiency", "加熱部の効率（熱として届く割合）", "", 0.9f, 0.7f, 1.0f, "estimate", "電熱の大半は熱になるが、装着部の外へ逃げる分を見込む（要調査）"),
            C("peltierCop", "冷却部（ペルチェ素子）の成績係数", "", 0.5f, 0.3f, 0.8f, "estimate", "小さい温度差で使う場合の代表値（要調査）"),
            C("thermalCapacity", "加温・冷却する範囲の熱容量", "J/K", 600f, 300f, 1200f, "tuning", "衣服の内側の空気と布・皮膚表面の一部をまとめた値（調整用の仮定）"),
            C("thermalLoss", "加温・冷却する範囲の熱損失係数", "W/K", 1.5f, 0.8f, 3f, "tuning", "外気との温度差 1 °C あたりに逃げる熱（調整用の仮定）"),
            C("cushionDamping", "緩衝材の減衰比", "", 0.3f, 0.1f, 0.5f, "tuning", "発泡材の衝撃吸収の目安（調整用の仮定）"),
            C("bottomOutFactor", "緩衝材がつぶれ切ったときの硬さの倍率", "", 50f, 20f, 200f, "tuning", "厚みを使い切ると硬い床に近づく（調整用の仮定）"),
            C("hardSurfaceStiffness", "緩衝材なしで床に当たるときの硬さ", "N/m", 2000000f, 500000f, 5000000f, "tuning", "樹脂の外殻が硬い床に当たる場合の目安（調整用の仮定）"),
            C("gripFriction", "つかみ具の摩擦係数", "", 0.5f, 0.3f, 0.8f, "estimate", "ゴムと段ボール・樹脂の摩擦係数の代表値（要調査）"),
            C("liftEfficiency", "持ち上げ機構の効率", "", 0.6f, 0.4f, 0.8f, "estimate", "モーター＋減速機の効率の代表値（要調査）"),
            C("detectPowerW", "検知センサーの消費電力", "W", 0.5f, 0.1f, 2f, "estimate", "小型の距離センサー・カメラの代表値（要調査）"),
            C("speakerEfficiency", "スピーカーの変換効率（電気→音）", "", 0.01f, 0.005f, 0.02f, "estimate", "小型スピーカーの代表値（要調査）"),
            C("backgroundNoiseDb", "周りの騒音", "dB", 55f, 45f, 65f, "estimate", "昼間の住宅地・街中の代表値（要調査）"),
            C("audibleMarginDb", "気づくのに必要な騒音との差", "dB", 10f, 5f, 15f, "tuning", "騒音より大きく聞こえれば気づくと仮定（調整用の仮定）"),
            C("lightNoticeNightLx", "夜に光に気づく照度", "lx", 0.00001f, 0.000001f, 0.0001f, "estimate", "暗い所で点の光に気づく照度の目安（要調査）"),
            C("lightNoticeDayLx", "昼に光に気づく照度", "lx", 0.001f, 0.0001f, 0.01f, "estimate", "明るい所で点の光に気づく照度の目安（要調査）"),
        };

        static readonly Dictionary<string, RealityCoefficient> _byId = All.ToDictionary(c => c.Id);

        public static RealityCoefficient Get(string id) => _byId.TryGetValue(id, out var c) ? c : null;
        public static float V(string id) => _byId[id].Value;

        static RealityCoefficient C(string id, string label, string unit, float value, float min, float max, string status, string source) =>
            new RealityCoefficient { Id = id, LabelJa = label, Unit = unit, Value = value, Min = min, Max = max, Status = status, Source = source };
    }
}
