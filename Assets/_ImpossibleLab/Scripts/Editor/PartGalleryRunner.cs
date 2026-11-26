using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ImpossibleLab.EditorTools
{
    /// <summary>
    /// 品番の一覧の画像を撮る（PartGallery）。Play Mode で組み立てる（アプリと同じ見た目）。
    /// Unity.exe -batchmode -projectPath . -executeMethod ImpossibleLab.EditorTools.PartGalleryRunner.Run -ilabGallery
    /// </summary>
    [InitializeOnLoad]
    public static class PartGalleryRunner
    {
        static bool Requested => Array.IndexOf(Environment.GetCommandLineArgs(), "-ilabGallery") >= 0 || SessionState.GetBool("ilabGallery", false);
        static readonly string OutDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "verify", "parts"));
        static double _deadline;

        static PartGalleryRunner()
        {
            if (!Requested) return;
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.update += Poll;
        }

        [MenuItem("IMPOSSIBLE LAB/8. 品番の一覧の画像を撮る")]
        public static void Run()
        {
            if (Directory.Exists(OutDir)) foreach (var f in Directory.GetFiles(OutDir)) File.Delete(f);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool("ilabGallery", true);
            PartGallery.Finished = false;
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            _deadline = EditorApplication.timeSinceStartup + 600;
            var g = new GameObject("PartGallery").AddComponent<PartGallery>();
            g.Assets = AssetDatabase.LoadAssetAtPath<LabAssets>(ProjectSetup.LabAssetsPath);
            g.OutputDir = OutDir;
        }

        static void Poll()
        {
            if (!EditorApplication.isPlaying) return;
            bool timeout = _deadline > 0 && EditorApplication.timeSinceStartup > _deadline;
            if (!PartGallery.Finished && !timeout) return;
            int code = timeout ? 2 : PartGallery.ExitCode;
            Debug.Log("[ILAB_GALLERY] exit " + code + (timeout ? " (timeout)" : ""));
            EditorApplication.update -= Poll;
            SessionState.SetBool("ilabGallery", false);
            if (Application.isBatchMode) EditorApplication.Exit(code);
            else EditorApplication.ExitPlaymode();
        }
    }
}
