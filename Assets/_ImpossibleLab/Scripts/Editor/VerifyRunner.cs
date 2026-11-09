using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ImpossibleLab.EditorTools
{
    /// <summary>
    /// Editor Play Mode で DemoAutopilot を走らせる検証。
    /// Unity.exe -batchmode -projectPath . -executeMethod ImpossibleLab.EditorTools.VerifyRunner.Run -ilabVerify
    /// （-nographics なし：撮影のためGPUが必要）
    /// </summary>
    [InitializeOnLoad]
    public static class VerifyRunner
    {
        static bool Requested => Array.IndexOf(Environment.GetCommandLineArgs(), "-ilabVerify") >= 0 || SessionState.GetBool("ilabVerify", false);
        static readonly string OutDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "verify"));
        static double _deadline;

        static VerifyRunner()
        {
            if (!Requested) return;
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.update += Poll;
        }

        [MenuItem("IMPOSSIBLE LAB/3. 自動検証（Play Mode）")]
        public static void Run()
        {
            if (Directory.Exists(OutDir)) foreach (var f in Directory.GetFiles(OutDir)) File.Delete(f);
            EditorSceneManager.OpenScene(SceneBuilder.ScenePath);
            SessionState.SetBool("ilabVerify", true);
            DemoAutopilot.Finished = false;
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            _deadline = EditorApplication.timeSinceStartup + 240;
            var go = new GameObject("DemoAutopilot");
            var ap = go.AddComponent<DemoAutopilot>();
            ap.OutputDir = OutDir;
        }

        static void Poll()
        {
            if (!EditorApplication.isPlaying) return;
            bool timeout = _deadline > 0 && EditorApplication.timeSinceStartup > _deadline;
            if (!DemoAutopilot.Finished && !timeout) return;
            int code = timeout ? 2 : DemoAutopilot.ExitCode;
            Debug.Log("[ILAB_VERIFY] exit " + code + (timeout ? " (timeout)" : ""));
            EditorApplication.update -= Poll;
            SessionState.SetBool("ilabVerify", false);
            if (Application.isBatchMode) EditorApplication.Exit(code);
            else EditorApplication.ExitPlaymode();
        }
    }
}
