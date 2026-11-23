using System;

namespace ImpossibleLab.Core
{
    /// <summary>
    /// 部品の id（AI が付けた英語。例：handle, wheel-fl, front-eye）から、日本語の呼び名を推測する。
    /// 部品の種類（骨組み・車輪など）だけでは何の部品か分からないため、画面で id に添える。見つからなければ null。
    /// </summary>
    public static class PartNames
    {
        // 先に書いた語ほど優先（"lid-motor" は「ふた」より「駆動部」）。英小文字の語の一部で照合する
        static readonly (string word, string ja)[] Words =
        {
            ("motor", "駆動部"), ("battery", "電池"), ("pack", "電池"), ("cell", "電池"),
            ("handle", "持ち手"), ("grip", "持ち手"), ("wheel", "車輪"), ("caster", "車輪"), ("tire", "車輪"),
            ("lid", "ふた"), ("door", "扉"), ("drawer", "引き出し"), ("hood", "ひさし"), ("awning", "ひさし"),
            ("canopy", "屋根"), ("umbrella", "傘"), ("shade", "日よけ"), ("cover", "カバー"), ("skirt", "側面カバー"),
            ("eye", "センサー"), ("sensor", "センサー"), ("camera", "カメラ"), ("cam", "カメラ"),
            ("lamp", "ライト"), ("light", "ライト"), ("led", "ライト"), ("speaker", "スピーカー"), ("buzzer", "ブザー"),
            ("bumper", "バンパー"), ("cushion", "クッション"), ("pad", "クッション"), ("strap", "ベルト"), ("belt", "ベルト"),
            ("bracket", "取り付け金具"), ("mount", "取り付け金具"), ("pole", "支柱"), ("mast", "支柱"), ("stem", "支柱"),
            ("arm", "アーム"), ("claw", "つかみ具"), ("gripper", "つかみ具"), ("finger", "指"), ("leg", "脚"),
            ("tray", "受け皿"), ("basket", "かご"), ("bin", "容器"), ("box", "箱"), ("case", "本体"), ("shell", "外殻"),
            ("body", "本体"), ("base", "台"), ("seat", "座面"), ("bed", "寝床"), ("vest", "ベスト"), ("shoe", "靴"),
            ("thruster", "推進ユニット"), ("fan", "ファン"), ("propeller", "プロペラ"), ("rotor", "回転翼"),
            ("heater", "ヒーター"), ("cooler", "冷却部"), ("panel", "板"), ("plate", "板"), ("frame", "骨組み"),
            ("solar", "太陽電池"), ("antenna", "アンテナ"), ("hinge", "ちょうつがい"),
        };

        public static string Guess(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            string s = id.ToLowerInvariant();
            foreach (var (w, ja) in Words)
                if (s.IndexOf(w, StringComparison.Ordinal) >= 0) return ja;
            return null;
        }

        /// <summary>「handle（持ち手）」の形。推測できなければ id だけ。</summary>
        public static string WithJa(string id)
        {
            var ja = Guess(id);
            return ja == null ? id : $"{id}（{ja}）";
        }
    }
}
