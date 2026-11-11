using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace ImpossibleLab.EditorTools
{
    /// <summary>
    /// 日本語フォント（TMP の動的アトラス）に、アプリで使う文字をビルド前に登録する。
    /// 動的アトラスはビルド時に空にされる設定（Clear Dynamic Data On Build）のため、実機では画面に初めて出る文字を
    /// その場で描いてアトラスを GPU へ送り直しており、画面の切り替えで 0.2〜0.4 秒止まっていた（2026-11-11 の実機ログ）。
    /// ビルドの間だけ「消去しない」にして登録済みの文字を APK に含め、ビルド後は元の空の状態に戻す（大きな登録データをリポジトリに入れない）。
    /// 登録していない文字（AI が新しく使う漢字など）は、これまでどおり実行中に追加される（動的のまま）。
    /// </summary>
    public static class FontPrewarm
    {
        static readonly string[] FontPaths =
        {
            "Assets/_ImpossibleLab/Art/Fonts/NotoSansJP-Regular SDF.asset",
            "Assets/_ImpossibleLab/Art/Fonts/NotoSansJP-Bold SDF.asset",
        };

        /// <summary>登録する文字：ASCII・かな・記号と、ソース・データ・カタログ・AI の記録に出てくる文字すべて。</summary>
        public static string CollectCharacters()
        {
            var set = new HashSet<char>();
            void AddRange(int from, int to) { for (int c = from; c <= to; c++) set.Add((char)c); }
            AddRange(0x20, 0x7E);     // ASCII
            AddRange(0x3000, 0x303F); // 和文の句読点・括弧
            AddRange(0x3041, 0x3096); // ひらがな
            AddRange(0x30A1, 0x30FC); // カタカナ・長音
            AddRange(0xFF01, 0xFF5E); // 全角英数・記号
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var files = new List<string>();
            void Add(string dir, string pattern)
            {
                string full = Path.Combine(root, dir);
                if (Directory.Exists(full)) files.AddRange(Directory.GetFiles(full, pattern, SearchOption.AllDirectories));
            }
            Add("Assets/_ImpossibleLab/Scripts", "*.cs");
            Add("Assets/_ImpossibleLab/Data", "*.json");
            Add("Assets/_ImpossibleLab/Tests/EditMode/Fixtures", "*.json");
            Add("proxy", "catalog.json");
            Add("proxy/eval/results", "*.jsonl"); // AI がよく使う語（評価セット40件の出力）
            foreach (var f in files)
                foreach (char ch in File.ReadAllText(f, Encoding.UTF8))
                    if (ch >= 0x80 && !char.IsSurrogate(ch) && !char.IsControl(ch)) set.Add(ch);
            return new string(set.OrderBy(c => c).ToArray());
        }

        /// <summary>Editor での効果の確認用（-executeMethod から呼ぶ）。終わったら RestoreForVerify で戻す。</summary>
        [MenuItem("IMPOSSIBLE LAB/フォント：使う文字を登録（確認用）")]
        public static void PrepareForVerify() => Prepare();
        [MenuItem("IMPOSSIBLE LAB/フォント：元の空の状態に戻す")]
        public static void RestoreForVerify() => Restore();

        /// <summary>ビルド前：文字を登録し、ビルド時の消去を止める。戻り値は登録できた文字数。</summary>
        public static int Prepare()
        {
            string chars = CollectCharacters();
            int total = 0;
            foreach (var path in FontPaths)
            {
                var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (fa == null) { Debug.LogWarning("ILAB_FONT missing " + path); continue; }
                fa.TryAddCharacters(chars, out string missing);
                SetClearOnBuild(fa, false);
                EditorUtility.SetDirty(fa);
                total += fa.characterTable.Count;
                Debug.Log($"ILAB_FONT prewarm {fa.name}: chars={fa.characterTable.Count} atlases={fa.atlasTextures?.Length ?? 0} missing={missing?.Length ?? 0}");
            }
            AssetDatabase.SaveAssets();
            return total;
        }

        /// <summary>ビルド後：元の空の動的フォントに戻す（エディタでは使った文字だけが追加されていく状態）。</summary>
        public static void Restore()
        {
            foreach (var path in FontPaths)
            {
                var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (fa == null) continue;
                fa.ClearFontAssetData(true);
                SetClearOnBuild(fa, true);
                EditorUtility.SetDirty(fa);
            }
            AssetDatabase.SaveAssets();
        }

        // clearDynamicDataOnBuild は internal のため、シリアライズされた値を直接変える
        static void SetClearOnBuild(TMP_FontAsset fa, bool on)
        {
            var so = new SerializedObject(fa);
            var p = so.FindProperty("m_ClearDynamicDataOnBuild");
            if (p == null) { Debug.LogWarning("ILAB_FONT m_ClearDynamicDataOnBuild not found"); return; }
            p.boolValue = on;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
