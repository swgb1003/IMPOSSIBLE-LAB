using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ImpossibleLab.EditorTools
{
    /// <summary>
    /// Quest 3S 向け APK ビルド。ビルド成功は「実機で動作する」ことを意味しない（実機確認は別途必要）。
    /// Unity.exe -batchmode -nographics -quit -projectPath . -buildTarget Android -executeMethod ImpossibleLab.EditorTools.QuestBuild.BuildDevelopment
    /// </summary>
    public static class QuestBuild
    {
        [MenuItem("IMPOSSIBLE LAB/4. Quest APK（Development）をビルド")]
        public static void BuildDevelopment() => Build("Builds/Android/ImpossibleLab-dev.apk", BuildOptions.Development);

        [MenuItem("IMPOSSIBLE LAB/5. Quest APK（Release・計測用）をビルド")]
        public static void BuildRelease() => Build("Builds/Android/ImpossibleLab.apk", BuildOptions.None);

        static void Build(string path, BuildOptions options)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(NamedBuildTarget.Android, BuildTarget.Android);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            EditorUserBuildSettings.buildAppBundle = false;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { SceneBuilder.ScenePath },
                target = BuildTarget.Android,
                locationPathName = path,
                options = options,
            });
            Debug.Log($"ILAB_ANDROID_BUILD {report.summary.result} size={report.summary.totalSize} time={report.summary.totalTime}");
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new BuildFailedException("Quest APK build failed: " + report.summary.result);
        }
    }
}
