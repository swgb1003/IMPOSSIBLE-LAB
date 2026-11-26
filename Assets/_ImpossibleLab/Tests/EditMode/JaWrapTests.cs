using System.Linq;
using ImpossibleLab.Core;
using NUnit.Framework;

namespace ImpossibleLab.Tests
{
    /// <summary>パネルの日本語の折り返し：語の途中で折り返さない・行の頭に句読点を置かない・幅に収める。</summary>
    public class JaWrapTests
    {
        static string[] Lines(string s, float em) => JaWrap.Wrap(s, em).Split((char)10);
        static float Width(string line) => line.Sum(c => c < 0x2E80 ? 0.55f : 1f);

        [Test] public void Consent_BreaksBetweenWords_AndFits()
        {
            const string s = "この発明を発明ライブラリーに公開します。名前と3D、実験の結果はだれでも見られます。依頼の言葉と設計の詳しい内容は、ログインした開発者だけが読めます。";
            var lines = Lines(s, 23.7f);
            Assert.Greater(lines.Length, 1);
            Assert.AreEqual(s, string.Concat(lines), "字は増えも減りもしない");
            foreach (var l in lines) Assert.LessOrEqual(Width(l), 24.7f, l);
            for (int i = 1; i < lines.Length; i++)
            {
                StringAssert.DoesNotStartWith("。", lines[i]);
                StringAssert.DoesNotStartWith("、", lines[i]);
                Assert.IsFalse(lines[i - 1].EndsWith("開") && lines[i].StartsWith("発"), "開発者の途中で切らない：" + JaWrap.Wrap(s, 23.7f));
            }
        }

        [Test] public void KatakanaWordsAndAddresses_StayWhole()
        {
            var lines = Lines("開発者　高瀬 真央（北辰工科大学 人間支援ロボティクス研究室）　所属は運営が確かめています", 23.7f);
            Assert.IsTrue(lines.Any(l => l.Contains("ロボティクス")), string.Join(" / ", lines));
            lines = Lines("PC のブラウザで http://127.0.0.1:8787/ を開くと見られます", 23.7f);
            Assert.IsTrue(lines.Any(l => l.Contains("http://127.0.0.1:8787/")), string.Join(" / ", lines));
        }

        [Test] public void ShortTextAndEmpty_AreUnchanged()
        {
            Assert.AreEqual("公開はいつでも取り下げられます。", JaWrap.Wrap("公開はいつでも取り下げられます。", 23.7f));
            Assert.AreEqual("", JaWrap.Wrap("", 10f));
            Assert.IsNull(JaWrap.Wrap(null, 10f));
        }
    }
}
