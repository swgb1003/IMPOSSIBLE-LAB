using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ImpossibleLab.EditorTools
{
    /// <summary>
    /// 実機ビルド時、EditorOnlyObject（XR Device Simulator など）をシーンから取り除く。
    /// 実行時の Destroy では間に合わず、シミュレーターが起動直後に本物のヘッドセットを入力から外してしまうため
    /// （頭の高さが0のまま・コントローラーのポインタが出ない）、ビルドに含めないことで防ぐ。
    /// Play Mode では report が null なので何もしない（エディターでは引き続きシミュレーターを使える）。
    /// </summary>
    sealed class StripEditorOnlyObjects : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null) return;
            var targets = new List<GameObject>();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var m in root.GetComponentsInChildren<EditorOnlyObject>(true))
                    targets.Add(m.gameObject);
            foreach (var go in targets)
            {
                if (go == null) continue;
                Debug.Log($"ILAB_STRIP removed editor-only object '{go.name}' from scene '{scene.name}'");
                Object.DestroyImmediate(go);
            }
        }
    }
}
