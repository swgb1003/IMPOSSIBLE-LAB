using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ImpossibleLab.EditorTools
{
    /// <summary>
    /// 研究室の見た目の確認用：シーンを開き、決まった視点からカメラで撮って Logs/snap/*.png に保存する（ゲームの動作は起こさない）。
    /// バッチ実行（-nographics なし）: Unity -batchmode -quit -projectPath . -executeMethod ImpossibleLab.EditorTools.LabSnapshot.Run
    /// </summary>
    public static class LabSnapshot
    {
        static readonly (string name, Vector3 pos, Vector3 lookAt, float fov)[] Views =
        {
            ("01_player_spot", new Vector3(0, 1.6f, -3.3f), new Vector3(0, 1.4f, 2f), 90f),
            ("02_entrance", new Vector3(0, 1.7f, -7.2f), new Vector3(0, 1.8f, 3f), 90f),
            ("03_studio", new Vector3(-2.5f, 1.6f, -1.5f), new Vector3(-7f, 1.0f, 0f), 75f),
            ("04_gallery", new Vector3(0, 1.6f, 2.5f), new Vector3(0, 1.4f, 8f), 80f),
            ("05_gate", new Vector3(2.5f, 1.7f, -1.0f), new Vector3(8f, 1.8f, 0f), 75f),
            ("06_ceiling", new Vector3(0, 1.4f, -5.5f), new Vector3(0, 5f, 0f), 90f),
            ("07_corner", new Vector3(6.5f, 2.6f, -6.5f), new Vector3(-1f, 1.0f, 2f), 90f),
        };

        [MenuItem("IMPOSSIBLE LAB/6. 研究室の見た目を撮影（Logs/snap）")]
        public static void Run()
        {
            EditorSceneManager.OpenScene(SceneBuilder.ScenePath, OpenSceneMode.Single);
            // UI・XR のカメラは使わず、確認用のカメラを置く
            var go = new GameObject("SnapshotCamera");
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 200f;
            cam.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.70f, 0.78f);
            const int W = 1600, H = 900;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "snap");
            Directory.CreateDirectory(dir);
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            foreach (var v in Views)
            {
                go.transform.position = v.pos;
                go.transform.rotation = Quaternion.LookRotation(v.lookAt - v.pos);
                cam.fieldOfView = v.fov;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dir, v.name + ".png"), tex.EncodeToPNG());
            }
            Object.DestroyImmediate(go);
            Debug.Log("ILAB_SNAPSHOT_DONE " + dir);
        }
    }
}
