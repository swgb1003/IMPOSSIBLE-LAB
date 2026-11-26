using System.Collections.Generic;
using System.Linq;

namespace ImpossibleLab.Core
{
    /// <summary>
    /// 製品の説明（概要・部品の役割・しくみ）を返す。設計に書かれた文（AI・見本）を優先し、無いときは部品と機能の対応から定型文を作る。
    /// 定型文かどうかを返すので、画面ではそれを示す（AI が書いたように見せない）。
    /// </summary>
    public static class ProductExplain
    {
        public readonly struct Text
        {
            public readonly string Value;
            /// <summary>設計に書かれていない（部品の種類・機能から作った）文。</summary>
            public readonly bool Derived;
            public Text(string value, bool derived) { Value = value ?? ""; Derived = derived; }
        }

        /// <summary>部品の種類ごとの一般的な役割。</summary>
        static readonly Dictionary<string, string> TypeRole = new Dictionary<string, string>
        {
            ["body"] = "部品をまとめて支える本体",
            ["wheel"] = "床の上を走って移動するための車輪",
            ["container"] = "荷物を入れて運ぶ格納部",
            ["sensor"] = "まわりの様子や人の位置を感じ取る",
            ["arm"] = "物に手を伸ばして動かすアーム",
            ["canopy"] = "雨や日差しをさえぎる覆い",
            ["thruster"] = "空気を押し出して浮く力・進む力を出す",
            ["stabilizer"] = "傾きや揺れを抑えて姿勢を保つ",
            ["connector"] = "部品どうしをつなぐ",
            ["frame"] = "形を支える骨組み",
            ["panel"] = "面をふさいだり、物を載せたりする板",
            ["light"] = "光を出して照らす・知らせる",
            ["heater"] = "熱を出して温める",
            ["cooler"] = "熱を逃がして冷やす",
            ["motor"] = "関節や部品を動かすモーター",
            ["gripper"] = "物をつかんで持つ",
            ["cushion"] = "ぶつかったときの衝撃をやわらげる",
            ["battery"] = "電気をためて各部に送る",
            ["solar"] = "光から電気をつくる",
            ["speaker"] = "音を出して知らせる",
            ["strap"] = "体や物に取り付ける",
        };

        public static Text Overview(InventionDna dna)
        {
            if (!string.IsNullOrEmpty(dna.Overview)) return new Text(dna.Overview, false);
            return new Text(dna.Purpose, string.IsNullOrEmpty(dna.Purpose));
        }

        /// <summary>この部品を対象にする機能の名前（無ければ空）。</summary>
        public static List<string> BehaviorsOf(InventionDna dna, PartSpec part) =>
            dna.Behaviors.Where(b => b.TargetPart == part.Id).Select(b => LabCatalog.Behavior(b.Type)?.LabelJa ?? b.Type).ToList();

        public static Text Role(InventionDna dna, PartSpec part)
        {
            if (!string.IsNullOrEmpty(part.Role)) return new Text(part.Role, false);
            var blocks = BehaviorsOf(dna, part);
            string typeRole = TypeRole.TryGetValue(part.Type, out var t) ? t : (LabCatalog.Part(part.Type)?.LabelJa ?? part.Type);
            // 走る機能を受け持つモーターは車輪を回す（関節のモーターと区別する）
            if (part.Type == "motor" && part.Joint == null && dna.Behaviors.Any(b => b.Type == "drive" && b.TargetPart == part.Id))
                typeRole = "車輪を回すモーター";
            if (part.Joint != null) typeRole += "（関節で動く）";
            return new Text(blocks.Count > 0 ? $"{string.Join("・", blocks)}の機能を受け持つ。{typeRole}" : typeRole, true);
        }

        /// <summary>しくみ：その要望の機能を、どの部品が受け持つか。</summary>
        public static Text How(InventionDna dna, FunctionNeed need)
        {
            if (!string.IsNullOrEmpty(need.How)) return new Text(need.How, false);
            if (need.Status != "unverifiable" && need.Blocks.Count == 0 && need.Metrics.Count > 0)
                return new Text($"部品の品番の値から{string.Join("・", need.Metrics.Select(m => ExperimentCatalog.Metric(m)?.LabelJa ?? m))}を計算して確かめる", true);
            if (need.Status == "unverifiable" || need.Blocks.Count == 0)
                return new Text(dna.Program != null && need.Status == "demonstrated" ? "動作プログラムの手順で動きを見せる" : "対応する機能がない", true);
            var lines = new List<string>();
            foreach (var type in need.Blocks)
            {
                var label = LabCatalog.Behavior(type)?.LabelJa ?? type;
                var b = dna.FindBehavior(type);
                var part = b != null && !string.IsNullOrEmpty(b.TargetPart) ? dna.FindPart(b.TargetPart) : null;
                lines.Add(part != null ? $"{Short(LabCatalog.Part(part.Type)?.LabelJa ?? part.Type)}（{part.Id}）が{label}を受け持つ" : $"{label}の機能で実現する");
            }
            return new Text(string.Join("。", lines), true);
        }

        /// <summary>部品の種類の名前から、補足の（…）を外す（例：キャノピー（覆い） → キャノピー）。</summary>
        static string Short(string label)
        {
            int i = label.IndexOf('（');
            return i > 0 ? label.Substring(0, i) : label;
        }

        /// <summary>要望の確かめ方の短い名前（設計の確認の画面と同じ言い方）。</summary>
        public static string StatusLabel(string status) =>
            status == "simulated" ? "実験で測る" : status == "simplified" ? "単純化して測る" : status == "demonstrated" ? "動きで見せる" : "確かめられない";
    }
}
