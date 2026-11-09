using System;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace ImpossibleLab.EditorTools
{
    /// <summary>
    /// プロジェクト設定と素材の生成。冪等（何度実行しても既存アセットを上書き破壊しない）。
    /// バッチ実行: Unity -batchmode -projectPath . -executeMethod ImpossibleLab.EditorTools.ProjectSetup.RunAll -quit
    /// </summary>
    public static class ProjectSetup
    {
        public const string Root = "Assets/_ImpossibleLab";
        public const string MatDir = Root + "/Art/Materials";
        public const string FontDir = Root + "/Art/Fonts";
        public const string SettingsDir = Root + "/Settings";
        public const string LabAssetsPath = Root + "/Settings/LabAssets.asset";

        [MenuItem("IMPOSSIBLE LAB/1. プロジェクト設定と素材を生成")]
        public static void RunAll()
        {
            ImportTmpEssentials();
            ConfigureUrp();
            ConfigureXr();
            ConfigurePlayer();
            var assets = CreateLabAssets();
            ExportProxyCatalog();
            AssetDatabase.SaveAssets();
            Debug.Log("ILAB_SETUP_DONE font=" + (assets.Font != null) + " materials=" + (assets.Concrete != null));
        }

        [MenuItem("IMPOSSIBLE LAB/2. 研究室シーンを生成")]
        public static void RunAllAndBuildScene()
        {
            RunAll();
            SceneBuilder.Build();
        }

        static void ImportTmpEssentials()
        {
            if (AssetDatabase.FindAssets("t:TMP_Settings").Length > 0) return;
            TMP_PackageResourceImporter.ImportResources(true, false, false);
            AssetDatabase.Refresh();
        }

        // ---------------- URP（Quest向けの軽量設定） ----------------
        static void ConfigureUrp()
        {
            Directory.CreateDirectory(SettingsDir);
            const string path = SettingsDir + "/URP_Quest.asset";
            const string rendererPath = SettingsDir + "/URP_Quest_Renderer.asset";
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (asset == null)
            {
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, rendererPath);
                ResourceReloader.ReloadAllNullIn(renderer, UniversalRenderPipelineAsset.packagePath);
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, path);
            }
            asset.msaaSampleCount = 4;
            asset.supportsHDR = false;
            asset.renderScale = 1f;
            asset.supportsCameraDepthTexture = false;
            asset.supportsCameraOpaqueTexture = false;
            asset.shadowDistance = 15f;
            EditorUtility.SetDirty(asset);
            GraphicsSettings.defaultRenderPipeline = asset;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        // ---------------- XR（OpenXR / Meta Quest） ----------------
        static void ConfigureXr()
        {
            const BuildTargetGroup group = BuildTargetGroup.Android;
            Directory.CreateDirectory("Assets/XR/Settings");
            AssetDatabase.Refresh();
            if (!EditorBuildSettings.TryGetConfigObject<XRGeneralSettingsPerBuildTarget>(XRGeneralSettings.k_SettingsKey, out var targets))
            {
                targets = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(targets, "Assets/XR/Settings/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, targets, true);
            }
            if (!targets.HasSettingsForBuildTarget(group)) targets.CreateDefaultSettingsForBuildTarget(group);
            if (!targets.HasManagerSettingsForBuildTarget(group)) targets.CreateDefaultManagerSettingsForBuildTarget(group);
            targets.SettingsForBuildTarget(group).InitManagerOnStart = true;
            if (!XRPackageMetadataStore.AssignLoader(targets.ManagerSettingsForBuildTarget(group), "UnityEngine.XR.OpenXR.OpenXRLoader", group))
                Debug.LogWarning("ILAB: OpenXR loader assignment returned false (already assigned?)");
            FeatureHelpers.RefreshFeatures(group);
            var xr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (xr == null) throw new InvalidOperationException("OpenXR settings are missing.");
            xr.GetFeature<MetaQuestFeature>().enabled = true;
            xr.GetFeature<MetaQuestTouchPlusControllerProfile>().enabled = true;
            xr.GetFeature<OculusTouchControllerProfile>().enabled = true;
            xr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            EditorUtility.SetDirty(xr);
            EditorUtility.SetDirty(targets);
            EditorUtility.SetDirty(targets.SettingsForBuildTarget(group));
            EditorUtility.SetDirty(targets.ManagerSettingsForBuildTarget(group));

            // XRI Starter Assets のテレポートは Interaction Layer 31 = "Teleport" を使う
            // InteractionLayerSettings は internal 型のため、アセットをシリアライズ経由で編集する（ビット31はマスクとして名前なしでも機能）
            foreach (var guid in AssetDatabase.FindAssets("t:InteractionLayerSettings"))
            {
                var obj = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                if (obj == null) continue;
                var so = new SerializedObject(obj);
                var names = so.FindProperty("m_LayerNames");
                if (names != null && names.isArray && names.arraySize > 31 && string.IsNullOrEmpty(names.GetArrayElementAtIndex(31).stringValue))
                {
                    names.GetArrayElementAtIndex(31).stringValue = "Teleport";
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(obj);
                }
            }
        }

        /// <summary>AI設計プロキシが読むカタログ（proxy/catalog.json）を C# のカタログから書き出す。</summary>
        [MenuItem("IMPOSSIBLE LAB/6. AIプロキシ用カタログを書き出す")]
        public static void ExportProxyCatalog()
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "proxy");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "catalog.json");
            File.WriteAllText(path, ImpossibleLab.Core.LabCatalog.ToProxyJson() + "\n", new UTF8Encoding(false));
            Debug.Log("ILAB_CATALOG_EXPORTED " + path);
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "ImpossibleLab";
            PlayerSettings.productName = "IMPOSSIBLE LAB";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "jp.impossiblelab.vr");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel35;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            // AI設計プロキシ（開発中は adb reverse 経由の http://127.0.0.1）。HTTP は開発ビルドだけ許可する
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;
            // XRI は Input System を使用
            var player = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            player.FindProperty("activeInputHandler").intValue = 1;
            player.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------- 素材 ----------------
        static LabAssets CreateLabAssets()
        {
            Directory.CreateDirectory(MatDir);
            var a = AssetDatabase.LoadAssetAtPath<LabAssets>(LabAssetsPath);
            if (a == null) { a = ScriptableObject.CreateInstance<LabAssets>(); AssetDatabase.CreateAsset(a, LabAssetsPath); }
            a.Font = CreateFont("NotoSansJP-Regular");
            a.FontBold = CreateFont("NotoSansJP-Bold");
            a.Concrete = Lit("Concrete", LabTheme.SurfaceLab, 0.25f, 0f);
            a.MatteMetal = Lit("MatteMetal", new Color(0.62f, 0.66f, 0.70f), 0.45f, 0.7f);
            a.DarkMetal = Lit("DarkMetal", new Color(0.16f, 0.19f, 0.23f), 0.4f, 0.6f);
            a.RobotWhite = Lit("RobotWhite", new Color(0.93f, 0.94f, 0.95f), 0.55f, 0.05f);
            a.Glass = Transparent(Lit("Glass", new Color(0.55f, 0.78f, 0.92f, 0.16f), 0.9f, 0f));
            a.AccentBlue = Emissive(Lit("AccentBlue", LabTheme.AccentPrimary, 0.6f, 0f), LabTheme.AccentPrimary * 0.6f);
            a.AccentOrange = Lit("AccentOrange", LabTheme.AccentWarning, 0.5f, 0f);
            a.Canopy = Transparent(Lit("Canopy", new Color(LabTheme.AccentLight.r, LabTheme.AccentLight.g, LabTheme.AccentLight.b, 0.42f), 0.8f, 0f));
            a.Mannequin = Lit("Mannequin", new Color(0.78f, 0.80f, 0.82f), 0.3f, 0f);
            a.WetZone = Transparent(Lit("WetZone", new Color(LabTheme.TextSecondary.r, LabTheme.TextSecondary.g, LabTheme.TextSecondary.b, 0.12f), 0.2f, 0f));
            a.MannequinHit = Transparent(Lit("WetZoneHit", new Color(LabTheme.AccentWarning.r, LabTheme.AccentWarning.g, LabTheme.AccentWarning.b, 0.35f), 0.2f, 0f));
            a.Ground = Lit("Ground", new Color(0.30f, 0.34f, 0.36f), 0.35f, 0f);
            a.RainParticle = Particle("RainParticle");
            a.Fader = Transparent(Unlit("Fader", new Color(0, 0, 0, 0)));
            a.Fader.renderQueue = 4000;
            a.RainSample = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Data/Samples/Resources/ImpossibleLabSamples/handsfree_rain.json");
            EditorUtility.SetDirty(a);
            return a;
        }

        static TMP_FontAsset CreateFont(string fileName)
        {
            string fontPath = $"{FontDir}/{fileName}.ttf";
            string assetPath = $"{FontDir}/{fileName} SDF.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null)
            {
                // ソース更新で増えたUI文字を追加で焼き込む
                existing.TryAddCharacters(CollectUiCharacters(), out _);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            if (font == null) { Debug.LogError("ILAB: font file missing " + fontPath + "（ライセンス済み Noto Sans JP を配置してください）"); return null; }
            var fa = TMP_FontAsset.CreateFontAsset(font, 48, 5, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            fa.name = fileName + " SDF";
            AssetDatabase.CreateAsset(fa, assetPath);
            fa.atlasTextures[0].name = fileName + " Atlas";
            AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            fa.material.name = fileName + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            // UIで使う文字は事前に焼き込む（動的追加に依存しすぎない）
            fa.TryAddCharacters(CollectUiCharacters(), out string missing);
            if (!string.IsNullOrEmpty(missing)) Debug.LogWarning("ILAB: glyphs missing in " + fileName + ": " + missing.Length);
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            return fa;
        }

        static string CollectUiCharacters()
        {
            var set = new System.Collections.Generic.HashSet<char>();
            for (char c = (char)0x20; c < 0x7F; c++) set.Add(c);
            foreach (var f in Directory.GetFiles(Root, "*.*", SearchOption.AllDirectories).Where(p => p.EndsWith(".cs") || p.EndsWith(".json")))
                foreach (var ch in File.ReadAllText(f, Encoding.UTF8)) if (ch > 0x7F && !char.IsControl(ch) && !char.IsSurrogate(ch)) set.Add(ch);
            foreach (var ch in "ぁあぃいぅうぇえぉおかがきぎくぐけげこごさざしじすずせぜそぞただちぢっつづてでとどなにぬねのはばぱひびぴふぶぷへべぺほぼぽまみむめもゃやゅゆょよらりるれろわをんァアィイゥウェエォオカガキギクグケゲコゴサザシジスズセゼソゾタダチヂッツヅテデトドナニヌネノハバパヒビピフブプヘベペホボポマミムメモャヤュユョヨラリルレロワヲンヴー、。「」・！？（）") set.Add(ch);
            return new string(set.ToArray());
        }

        static Material Lit(string name, Color c, float smooth, float metal)
        {
            string p = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, p); }
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Unlit(string name, Color c)
        {
            string p = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(m, p); }
            m.SetColor("_BaseColor", c);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Particle(string name)
        {
            string p = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); AssetDatabase.CreateAsset(m, p); }
            m.SetColor("_BaseColor", Color.white);
            Transparent(m);
            return m;
        }

        static Material Emissive(Material m, Color e)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", e);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>URP Lit/Unlit を半透明（アルファブレンド）にする。Questでは使用箇所を最小限にする。</summary>
        static Material Transparent(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetShaderPassEnabled("DepthOnly", false);
            m.SetShaderPassEnabled("ShadowCaster", false);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
