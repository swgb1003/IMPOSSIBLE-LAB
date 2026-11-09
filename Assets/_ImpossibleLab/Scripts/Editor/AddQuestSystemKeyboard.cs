using System.IO;
using UnityEditor.Android;
using UnityEngine;

namespace ImpossibleLab.EditorTools
{
    /// <summary>
    /// Quest のシステムキーボード（キーボードオーバーレイ）を使えるようにする。
    /// Meta の資料では、没入型アプリがシステムキーボードを開くには AndroidManifest に
    /// &lt;uses-feature android:name="oculus.software.overlay_keyboard"&gt; が必要（Meta XR Core SDK の
    /// "Requires System Keyboard" はこの1行を追加する）。SDK を入れずに同じ1行をビルド時に追加する。
    /// 入力欄（TMP_InputField）が選択されると Unity が TouchScreenKeyboard を開く。
    /// </summary>
    sealed class AddQuestSystemKeyboard : IPostGenerateGradleAndroidProject
    {
        const string Feature = "oculus.software.overlay_keyboard";
        public int callbackOrder => 100;

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            string manifest = Path.Combine(unityLibraryPath, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifest)) { Debug.LogWarning("ILAB_KEYBOARD manifest not found: " + manifest); return; }
            string xml = File.ReadAllText(manifest);
            if (xml.Contains(Feature)) { Debug.Log("ILAB_KEYBOARD overlay_keyboard already present"); return; }
            int i = xml.IndexOf("<application", System.StringComparison.Ordinal);
            if (i < 0) { Debug.LogWarning("ILAB_KEYBOARD <application> not found; manifest unchanged"); return; }
            xml = xml.Insert(i, $"<uses-feature android:name=\"{Feature}\" android:required=\"false\" />\n  ");
            File.WriteAllText(manifest, xml);
            Debug.Log("ILAB_KEYBOARD added uses-feature " + Feature);
        }
    }
}
