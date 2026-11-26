using System.Collections;
using System.Collections.Generic;
using System.IO;
using ImpossibleLab.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace ImpossibleLab
{
    /// <summary>
    /// 確かめ用：品番を 1 つずつ組み立てて撮り、一覧の画像にする（アプリと同じ組み立て・素材・陰り）。
    /// 起動は Editor の PartGalleryRunner（-ilabGallery）。画像は Logs/verify/parts/page-N.png、並びは index.json。
    /// </summary>
    public sealed class PartGallery : MonoBehaviour
    {
        public LabAssets Assets;
        public string OutputDir;
        public static bool Finished;
        public static int ExitCode;

        const int Cell = 220, Cols = 10, Rows = 6;

        static InventionDna OnePart(PartModelInfo m)
        {
            var dna = new InventionDna { Id = "gallery", Name = "見本", Purpose = "品番の見本", Mount = "fixed" };
            dna.Tags.Add("daily");
            dna.Parts.Add(new PartSpec { Id = "p", Type = m.Type, Shape = m.Shape, Scale = m.Size, Model = m.Id });
            dna.FunctionMap.Add(new FunctionNeed { Need = "見本", Status = "unverifiable", Note = "見本の部品" });
            return dna;
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(OutputDir);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.45f, 0.5f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.22f, 0.25f);
            // 周りの明るさと映り込み：研究所に近い明るい空から作る（背景は暗いまま）
            RenderSettings.skybox = new Material(Shader.Find("Skybox/Procedural"));
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            DynamicGI.UpdateEnvironment();
            var light = new GameObject("Key").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.3f; light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            var cam = new GameObject("Cam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.085f, 0.1f);
            cam.fieldOfView = 24f; cam.nearClipPlane = 0.001f; cam.farClipPlane = 50f;
            var rt = new RenderTexture(Cell, Cell, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;

            var builder = new InventionBuilder(Assets);
            var models = PartModels.All;
            var index = new JArray();
            int perPage = Cols * Rows, pages = (models.Count + perPage - 1) / perPage, fails = 0;
            for (int page = 0; page < pages; page++)
            {
                var sheet = new Texture2D(Cell * Cols, Cell * Rows, TextureFormat.RGB24, false);
                var fill = new Color32[sheet.width * sheet.height];
                for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(18, 22, 26, 255);
                sheet.SetPixels32(fill);
                for (int k = 0; k < perPage; k++)
                {
                    int n = page * perPage + k;
                    if (n >= models.Count) break;
                    var m = models[n];
                    var built = builder.Build(OnePart(m), transform);
                    if (built.View == null) { fails++; Debug.LogWarning($"[ILAB_GALLERY] FAIL {m.Id}: {string.Join(" / ", built.Warnings)}"); continue; }
                    yield return null;
                    var b = new Bounds();
                    bool first = true;
                    foreach (var r in built.View.GetComponentsInChildren<Renderer>())
                    {
                        if (!r.enabled) continue;
                        if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
                    }
                    float radius = Mathf.Max(0.004f, b.extents.magnitude);
                    // 前（+Z）の右上から見る
                    float yaw = 30f * Mathf.Deg2Rad, pitch = 26f * Mathf.Deg2Rad;
                    var off = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));
                    cam.transform.position = b.center + off * (radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.08f);
                    cam.transform.LookAt(b.center);
                    cam.Render();
                    RenderTexture.active = rt;
                    int col = k % Cols, row = k / Cols;
                    sheet.ReadPixels(new Rect(0, 0, Cell, Cell), col * Cell, (Rows - 1 - row) * Cell);
                    RenderTexture.active = null;
                    index.Add(new JObject { ["id"] = m.Id, ["page"] = page, ["col"] = col, ["row"] = row });
                    Destroy(built.View.gameObject);
                }
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(OutputDir, $"page-{page + 1}.png"), sheet.EncodeToPNG());
                Destroy(sheet);
            }
            File.WriteAllText(Path.Combine(OutputDir, "index.json"), new JObject { ["cell"] = Cell, ["cols"] = Cols, ["rows"] = Rows, ["items"] = index }.ToString());
            Debug.Log($"[ILAB_GALLERY] {models.Count} models, {pages} pages, {fails} failed");
            ExitCode = fails == 0 ? 0 : 1;
            Finished = true;
        }
    }
}
