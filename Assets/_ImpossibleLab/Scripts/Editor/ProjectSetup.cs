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
        public const string ModelDir = Root + "/Art/Models";
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
            asset.shadowDistance = 8f; // 動かない物の影は焼き込み（シャドウマスク）。実行中の影は近くの動く物だけなので短くして細かくする
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
            // 質感の画像（ArtSource/textures/make_lab_textures.py で作る。壁は Poly Haven white_plaster_02 CC0）。
            // UV は 1m = 1（Blender の lab_common.box_uv）。くり返し回数は 1m あたり
            a.Concrete = Textured(Lit("Concrete", LabTheme.SurfaceLab, 0.25f, 0f), "plaster", bump: 0.6f, tiling: 0.5f, smooth: 0.4f);
            a.MatteMetal = Textured(Lit("MatteMetal", new Color(0.62f, 0.66f, 0.70f), 0.45f, 0.7f), "brushed", bump: 0.35f, tiling: 4f, smooth: 0.7f);
            a.DarkMetal = Textured(Lit("DarkMetal", new Color(0.16f, 0.19f, 0.23f), 0.4f, 0.6f), "brushed", bump: 0.3f, tiling: 4f, smooth: 0.6f);
            a.RobotWhite = Textured(Lit("RobotWhite", new Color(0.93f, 0.94f, 0.95f), 0.62f, 0.05f), "plastic", bump: 0.5f, tiling: 3f, smooth: 0.7f); // つやのある白い樹脂
            a.Glass = Transparent(Lit("Glass", new Color(0.55f, 0.78f, 0.92f, 0.16f), 0.9f, 0f));
            a.AccentBlue = Emissive(Lit("AccentBlue", LabTheme.AccentPrimary, 0.6f, 0f), LabTheme.AccentPrimary * 0.6f);
            a.AccentOrange = Lit("AccentOrange", LabTheme.AccentWarning, 0.5f, 0f);
            a.Canopy = Transparent(Lit("Canopy", new Color(LabTheme.AccentLight.r, LabTheme.AccentLight.g, LabTheme.AccentLight.b, 0.42f), 0.8f, 0f));
            // 試作品のキャノピー：下の人は透けて見えるが、円盤としてはっきり見える水色（参考：描き直した画像）
            a.LensGlow = LensGlowMaterial();
            a.PartCanopy = Transparent(Lit("PartCanopy", new Color(0.74f, 0.87f, 0.97f, 0.85f), 0.55f, 0f));
            a.Mannequin = Lit("Mannequin", new Color(0.78f, 0.80f, 0.82f), 0.3f, 0f);
            a.WetZone = Transparent(Lit("WetZone", new Color(LabTheme.TextSecondary.r, LabTheme.TextSecondary.g, LabTheme.TextSecondary.b, 0.12f), 0.2f, 0f));
            a.MannequinHit = Transparent(Lit("WetZoneHit", new Color(LabTheme.AccentWarning.r, LabTheme.AccentWarning.g, LabTheme.AccentWarning.b, 0.35f), 0.2f, 0f));
            a.Ground = Lit("Ground", new Color(0.30f, 0.34f, 0.36f), 0.35f, 0f);
            a.RainParticle = Particle("RainParticle");
            // 部品の素材（DNA の material）。樹脂は RobotWhite、ガラスは Glass を使う
            a.PartAluminum = Textured(Lit("PartAluminum", new Color(0.80f, 0.82f, 0.85f), 0.62f, 0.85f), "brushed", bump: 0.3f, tiling: 2f, smooth: 0.72f);
            a.PartSteel = Textured(Lit("PartSteel", new Color(0.42f, 0.44f, 0.47f), 0.5f, 0.9f), "brushed", bump: 0.3f, tiling: 2f, smooth: 0.62f);
            a.PartFabric = Lit("PartFabric", new Color(0.20f, 0.25f, 0.33f), 0.08f, 0f);
            a.PartFoam = Lit("PartFoam", new Color(0.86f, 0.84f, 0.76f), 0.03f, 0f);
            a.PartRubber = Lit("PartRubber", new Color(0.09f, 0.10f, 0.11f), 0.22f, 0f);
            a.GlowOrange = Emissive(Lit("GlowOrange", LabTheme.AccentWarning, 0.4f, 0f), LabTheme.AccentWarning * 1.2f);
            a.LightPanel = Emissive(Lit("LightPanel", new Color(0.95f, 0.96f, 0.97f), 0.5f, 0f), new Color(1f, 0.99f, 0.98f) * 2.2f, bakeGI: true);
            a.FloorConcrete = FloorMaterial();
            a.Screen = Emissive(Lit("Screen", new Color(0.02f, 0.08f, 0.16f), 0.8f, 0f), new Color(0.10f, 0.45f, 0.85f) * 1.6f);
            a.Sky = SkyMaterial();
            a.PlantLeaves = LeavesMaterial();
            a.CentralDevice = ImportModel("CentralDevice", a);
            a.LabRoom = ImportModel("LabRoom", a);
            a.RobotArm = ImportModel("RobotArm", a);
            a.Workbench = ImportModel("Workbench", a);
            a.GalleryCase = ImportModel("GalleryCase", a);
            a.Gate = ImportModel("Gate", a);
            a.Plant = ImportModel("Plant", a);
            a.PartKit = ImportModel("PartKit", a, bakeAxis: true);
            a.Fader = Transparent(Unlit("Fader", new Color(0, 0, 0, 0)));
            a.Fader.renderQueue = 4000;
            a.RainSample = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Data/Samples/Resources/ImpossibleLabSamples/handsfree_rain.json");
            EditorUtility.SetDirty(a);
            return a;
        }

        /// <summary>
        /// Blender で作ったモデル（ArtSource/blender から書き出した FBX）の取り込み設定。
        /// マテリアル（MAT_*）は LabAssets のマテリアルへ置き換え、モデル側のマテリアルは作らない。ファイルが無ければ null（シーンは従来の簡易形状）。
        /// </summary>
        static GameObject ImportModel(string name, LabAssets a, bool bakeAxis = false)
        {
            string path = $"{ModelDir}/{name}.fbx";
            if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) return null;
            mi.globalScale = 1f; mi.useFileScale = true;
            mi.importCameras = false; mi.importLights = false;
            mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
            mi.importBlendShapes = false; mi.addCollider = false; mi.isReadable = false;
            mi.importNormals = ModelImporterNormals.Import;
            mi.generateSecondaryUV = !bakeAxis; // ライトの焼き込み用（部品キットは動く物なので不要）
            // 部品キットは DNA の大きさをそのまま掛けるので、軸の変換をメッシュに焼き込み、各部品の回転を無しにする
            mi.bakeAxisConversion = bakeAxis;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            var map = new (string, Material)[]
            {
                ("MAT_White", a.RobotWhite), ("MAT_Dark", a.DarkMetal), ("MAT_Metal", a.MatteMetal), ("MAT_Blue", a.AccentBlue),
                ("MAT_Orange", a.GlowOrange), ("MAT_Glass", a.Glass), ("MAT_LightWhite", a.LightPanel),
                ("MAT_Floor", a.FloorConcrete), ("MAT_Wall", a.Concrete), ("MAT_Screen", a.Screen), ("MAT_PlantLeaves", a.PlantLeaves),
            };
            foreach (var (src, m) in map)
                if (m != null) mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), src), m);
            mi.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        const string PolyHavenDir = Root + "/Art/Textures/PolyHaven";

        /// <summary>床：Poly Haven の concrete_floor_worn_001（CC0、1K の色・法線）。テクスチャが無ければ単色。</summary>
        static Material FloorMaterial()
        {
            var m = Lit("FloorConcrete", new Color(0.92f, 0.93f, 0.94f), 0.55f, 0f);
            string dir = PolyHavenDir + "/concrete_floor_worn_001";
            var diff = ImportTexture($"{dir}/concrete_floor_worn_001_diff_1k_light.jpg", 1024, normal: false);
            var nor = ImportTexture($"{dir}/concrete_floor_worn_001_nor_gl_1k.jpg", 1024, normal: true);
            if (diff != null) m.SetTexture("_BaseMap", diff);
            if (nor != null) { m.SetTexture("_BumpMap", nor); m.SetFloat("_BumpScale", 0.6f); m.EnableKeyword("_NORMALMAP"); }
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>窓の外の空：Poly Haven の kloofendal_43d_clear（CC0、トーンマップ済みを 2048×1024 に縮小）を正距円筒のスカイボックスに。</summary>
        static Material SkyMaterial()
        {
            var tex = ImportTexture(PolyHavenDir + "/kloofendal_43d_clear/kloofendal_43d_clear_2k.jpg", 2048, normal: false, sky: true);
            if (tex == null) return null;
            string p = $"{MatDir}/Sky.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) { m = new Material(Shader.Find("Skybox/Panoramic")); AssetDatabase.CreateAsset(m, p); }
            m.SetTexture("_MainTex", tex);
            m.SetFloat("_Exposure", 1.0f);
            m.SetFloat("_Rotation", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>観葉植物の葉：色＋切り抜き（アルファ）、両面。Quest では切り抜きの面積が小さいので負荷は小さい。</summary>
        static Material LeavesMaterial()
        {
            string dir = PolyHavenDir + "/potted_plant_02";
            var diff = ImportTexture($"{dir}/potted_plant_02_leaves_diff_alpha_1k.png", 1024, normal: false);
            if (diff == null) return null;
            var nor = ImportTexture($"{dir}/potted_plant_02_leaves_nor_gl_1k.png", 1024, normal: true);
            var m = Lit("PlantLeaves", Color.white, 0.35f, 0f);
            m.SetTexture("_BaseMap", diff);
            if (nor != null) { m.SetTexture("_BumpMap", nor); m.EnableKeyword("_NORMALMAP"); }
            m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.5f); m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.renderQueue = (int)RenderQueue.AlphaTest;
            m.SetOverrideTag("RenderType", "TransparentCutout");
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D ImportTexture(string path, int maxSize, bool normal, bool sky = false)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return null;
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.maxTextureSize = maxSize;
            ti.sRGBTexture = !normal;
            ti.mipmapEnabled = !sky;
            ti.wrapModeU = TextureWrapMode.Repeat;
            ti.wrapModeV = sky ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = !normal && !sky && path.EndsWith(".png");
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
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

        /// <summary>
        /// 質感の画像を貼る：色の画像（A＝つやの割合。Smoothness と掛け合わせる）と法線。金属度はマテリアルの値のまま。
        /// 画像が無ければ何もしない（従来の単色）。
        /// </summary>
        static Material Textured(Material m, string set, float bump, float tiling, float smooth)
        {
            string dir = Root + "/Art/Textures/Lab";
            var baseTex = ImportTexture($"{dir}/{set}_base.png", 1024, normal: false);
            string norPath = File.Exists($"{dir}/{set}_nor_gl.png") ? $"{dir}/{set}_nor_gl.png" : $"{dir}/{set}_nor_gl.jpg";
            var nor = ImportTexture(norPath, 1024, normal: true);
            if (baseTex == null || nor == null) return m;
            m.SetTexture("_BaseMap", baseTex);
            m.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
            m.SetTexture("_BumpMap", nor);
            m.SetFloat("_BumpScale", bump);
            m.EnableKeyword("_NORMALMAP");
            m.SetFloat("_SmoothnessTextureChannel", 1f);
            m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            m.SetFloat("_Smoothness", smooth);
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

        static Material Emissive(Material m, Color e, bool bakeGI = false)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", e);
            // bakeGI：光る面を焼き込みの光源にする（天井の照明パネルなど。実行中の負担はない）
            m.globalIlluminationFlags = bakeGI ? MaterialGlobalIlluminationFlags.BakedEmissive : MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>URP Lit/Unlit を半透明（アルファブレンド）にする。Questでは使用箇所を最小限にする。</summary>
        /// <summary>レンズの光のにじみ：中心が明るく外へ向かって消える円（64px の画像をコードで作る）を、加算で重ねる。</summary>
        static Material LensGlowMaterial()
        {
            string tp = Root + "/Art/Textures/LensGlow.png";
            if (!File.Exists(tp))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(tp));
                const int n = 64;
                var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(1f - r);
                        a = a * a * (3f - 2f * a); // なめらかに消える
                        t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                    }
                File.WriteAllBytes(tp, t.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(tp);
            }
            var tex = ImportTexture(tp, 64, normal: false);
            if (tex != null && AssetImporter.GetAtPath(tp) is TextureImporter ti && ti.wrapMode != TextureWrapMode.Clamp)
            {
                ti.wrapMode = TextureWrapMode.Clamp; ti.SaveAndReimport();
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(tp);
            }
            var m = Transparent(Unlit("LensGlow", new Color(0.35f, 0.75f, 1f, 0.9f)));
            m.SetFloat("_DstBlend", (float)BlendMode.One); // 加算（明るく光る）
            m.SetFloat("_Cull", 0f);                       // 両面
            if (tex != null) m.SetTexture("_BaseMap", tex);
            EditorUtility.SetDirty(m);
            return m;
        }

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
