using System.Text;

namespace ImpossibleLab.Core
{
    /// <summary>パネルの日本語の折り返し。TextMeshPro は漢字・かなのどこでも折り返すので、語の切れ目に改行を入れてから渡す。</summary>
    public static class JaWrap
    {
        const string Particles = "をにでがはもとへやの";
        const string BreakAfter = "、。，．）」』！？：・ ";
        const string NoLineStart = "、。，．）」』！？：・ーぁぃぅぇぉっゃゅょァィゥェォッャュョ%";
        const char NewLine = (char)10;

        static bool Hiragana(char c) => c >= 0x3041 && c <= 0x309F;
        static float Em(char c) => c < 0x2E80 ? 0.55f : 1f;
        static bool WordChar(char c) => (c > ' ' && c < 0x2E80) || (c >= 0x30A0 && c <= 0x30FF);

        /// <summary>
        /// 日本語を、助詞・読点・かっこの前後など語の切れ目だけで折り返す（全角を 1、半角を 0.55 として数える）。
        /// 切れ目が見つからない長い語だけ途中で切る。行の頭に句読点・閉じかっこ・小さい字を置かない。
        /// </summary>
        public static string Wrap(string s, float maxEm)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder();
            foreach (var para in s.Split(NewLine))
            {
                if (sb.Length > 0) sb.Append(NewLine);
                int start = 0, lastBreak = -1;
                float w = 0f;
                for (int i = 0; i < para.Length; i++)
                {
                    w += Em(para[i]);
                    if (i + 1 < para.Length && NoLineStart.IndexOf(para[i + 1]) < 0)
                    {
                        char a = para[i], b = para[i + 1];
                        // 助詞は、漢字・カタカナなどの語のすぐ後ろのときだけ切れ目にする（できます・そのもの を切らない）
                        bool particle = Particles.IndexOf(a) >= 0 && i > 0 && !Hiragana(para[i - 1]);
                        bool cut = particle || BreakAfter.IndexOf(a) >= 0 || b == '（' || b == '「' || (WordChar(a) != WordChar(b) && (a < 0x2E80 || b < 0x2E80));
                        if (cut) lastBreak = i + 1;
                    }
                    if (w <= maxEm || i == start) continue;
                    // 切れ目が行の4割より後ろにあればそこで、無ければこの字の前で切る
                    int at = lastBreak > start && lastBreak - start >= (i - start) * 0.4f ? lastBreak : i;
                    while (at < para.Length && NoLineStart.IndexOf(para[at]) >= 0) at++;
                    sb.Append(para, start, at - start).Append(NewLine);
                    start = at;
                    while (start < para.Length && para[start] == ' ') start++;
                    i = start - 1; w = 0f; lastBreak = -1;
                }
                if (start < para.Length) sb.Append(para, start, para.Length - start);
                while (sb.Length > 0 && sb[sb.Length - 1] == NewLine) sb.Length--;
            }
            return sb.ToString();
        }
    }
}
