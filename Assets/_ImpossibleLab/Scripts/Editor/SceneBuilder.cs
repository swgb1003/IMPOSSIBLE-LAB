using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace ImpossibleLab.EditorTools
{
    /// <summary>
    /// 単一Hubシーン（仕様22章）を生成する。中央研究室（グレーボックス）＋実験ゲート＋RainLab。
    /// 建築寸法は仕様の初期値。プリミティブのみで構築し、外部アセットを参照しない。
    /// </summary>
    public static class SceneBuilder
    {
        public const string ScenePath = ProjectSetup.Root + "/Scenes/LabHub.unity";
        const string SamplesRoot = "Assets/Samples/XR Interaction Toolkit/3.0.8";
        static LabAssets A;
        static readonly Vector3 RainLabPos = new Vector3(0, 0, 60);

        public static void Build()
        {
            A = AssetDatabase.LoadAssetAtPath<LabAssets>(ProjectSetup.LabAssetsPath);
            if (A == null || A.Font == null) throw new System.InvalidOperationException("LabAssets/フォントが未生成です。先に ProjectSetup.RunAll を実行してください。");
            LabTheme.Font = A.Font; LabTheme.FontBold = A.FontBold;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.78f, 0.82f, 0.88f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.58f, 0.62f);
            RenderSettings.ambientGroundColor = new Color(0.30f, 0.31f, 0.33f);
            RenderSettings.fog = false;
            var lightGo = new GameObject("Key Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.1f; light.color = new Color(1f, 0.97f, 0.92f);
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(52, -30, 0);

            var env = new GameObject("Environment").transform;
            var lab = BuildCentralLab(env, out var displayPoint, out var ring, out var hologram, out var arms, out var gateL, out var gateR);
            var rain = BuildRainLab(env);

            // ---- XR ----
            new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
            var originPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SamplesRoot + "/Starter Assets/Prefabs/XR Origin (XR Rig).prefab");
            if (originPrefab == null) throw new System.InvalidOperationException("XRI Starter Assets の XR Origin が見つかりません");
            var originGo = (GameObject)PrefabUtility.InstantiatePrefab(originPrefab);
            var playerSpot = lab.Find("WorkshopPlayerSpot");
            originGo.transform.SetPositionAndRotation(playerSpot.position, playerSpot.rotation);
            var origin = originGo.GetComponent<XROrigin>();
            // テレポート＋スナップターンを標準に（仕様21.3）
            foreach (var m in originGo.GetComponentsInChildren<ControllerInputActionManager>(true))
            {
                m.smoothMotionEnabled = false;
                m.smoothTurnEnabled = false;
                EditorUtility.SetDirty(m);
            }
            var cam = origin.Camera;
            cam.nearClipPlane = 0.05f;
            cam.backgroundColor = new Color(0.62f, 0.70f, 0.78f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            var faderQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            faderQuad.name = "Fader";
            Object.DestroyImmediate(faderQuad.GetComponent<Collider>());
            faderQuad.transform.SetParent(cam.transform, false);
            faderQuad.transform.localPosition = new Vector3(0, 0, 0.08f);
            faderQuad.transform.localScale = new Vector3(0.6f, 0.6f, 1f);
            var fr = faderQuad.GetComponent<MeshRenderer>();
            fr.sharedMaterial = A.Fader; fr.shadowCastingMode = ShadowCastingMode.Off; fr.enabled = false;
            var fader = cam.gameObject.AddComponent<ScreenFader>();
            fader.Quad = fr;

            var simPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SamplesRoot + "/XR Device Simulator/XR Device Simulator.prefab");
            if (simPrefab != null)
            {
                var sim = (GameObject)PrefabUtility.InstantiatePrefab(simPrefab);
                sim.AddComponent<EditorOnlyObject>();
            }

            // ---- App / UI ----
            var consoleRoot = new GameObject("ConsoleRoot").transform;
            var appGo = new GameObject("IMPOSSIBLE LAB App");
            var app = appGo.AddComponent<LabApp>();
            app.Assets = A;
            app.DisplayPoint = displayPoint;
            app.WorkshopPlayerSpot = playerSpot;
            app.WorkshopConsoleSpot = lab.Find("WorkshopConsoleSpot");
            app.RobotArmJoints = arms;
            app.RingLight = ring;
            app.Hologram = hologram;
            app.GateDoorLeft = gateL; app.GateDoorRight = gateR;
            app.RainLab = rain;
            app.Origin = origin;
            app.Fader = fader;
            app.ConsoleRoot = consoleRoot;
            var ui = appGo.AddComponent<LabUI>();
            ui.App = app; ui.ConsoleRoot = consoleRoot;

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("ILAB_SCENE_BUILT " + ScenePath);
        }

        // ================= CENTRAL LAB =================
        static Transform BuildCentralLab(Transform env, out Transform displayPoint, out Renderer ring, out GameObject hologram,
            out Transform[] arms, out Transform gateL, out Transform gateR)
        {
            var lab = new GameObject("CENTRAL LAB").transform;
            lab.SetParent(env, false);
            // 床（約12m×12m＋周辺）・壁・天井梁
            var floor = Box("Floor", lab, new Vector3(0, -0.05f, 0), new Vector3(16, 0.1f, 16), A.Concrete);
            AddTeleport(floor);
            Cyl("FloorPlate", lab, new Vector3(0, 0.004f, 0), new Vector3(5.2f, 0.004f, 5.2f), A.DarkMetal);
            Cyl("FloorPlateInner", lab, new Vector3(0, 0.008f, 0), new Vector3(4.9f, 0.004f, 4.9f), A.Concrete);
            Box("Wall_Back", lab, new Vector3(0, 2.5f, 8), new Vector3(16, 5, 0.2f), A.Concrete);
            Box("Wall_Left", lab, new Vector3(-8, 2.5f, 0), new Vector3(0.2f, 5, 16), A.Concrete);
            Box("Wall_Right_A", lab, new Vector3(8, 2.5f, -4.75f), new Vector3(0.2f, 5, 6.5f), A.Concrete);
            Box("Wall_Right_B", lab, new Vector3(8, 2.5f, 4.75f), new Vector3(0.2f, 5, 6.5f), A.Concrete);
            Box("Wall_Right_Top", lab, new Vector3(8, 4.25f, 0), new Vector3(0.2f, 1.5f, 3f), A.Concrete);
            Box("Wall_Front", lab, new Vector3(0, 2.5f, -8), new Vector3(16, 5, 0.2f), A.Concrete);
            // 窓（ガラス帯）と柱
            Box("Window_Back", lab, new Vector3(0, 3.2f, 7.88f), new Vector3(13, 1.6f, 0.05f), A.Glass);
            for (int i = -3; i <= 3; i++) Box("Pillar_" + i, lab, new Vector3(i * 2.2f, 2.5f, 7.75f), new Vector3(0.25f, 5, 0.25f), A.MatteMetal);
            Box("CeilingBeam_X", lab, new Vector3(0, 4.9f, 0), new Vector3(16, 0.25f, 0.4f), A.MatteMetal);
            Box("CeilingBeam_Z", lab, new Vector3(0, 4.9f, 0), new Vector3(0.4f, 0.25f, 16), A.MatteMetal);

            // 中央発明台（直径3m）
            Cyl("InventionTable_Base", lab, new Vector3(0, 0.42f, 0), new Vector3(3.0f, 0.42f, 3.0f), A.RobotWhite);
            Cyl("InventionTable_Rim", lab, new Vector3(0, 0.86f, 0), new Vector3(3.1f, 0.03f, 3.1f), A.DarkMetal);
            Cyl("InventionTable_Top", lab, new Vector3(0, 0.9f, 0), new Vector3(2.3f, 0.02f, 2.3f), A.MatteMetal);
            for (int i = 0; i < 12; i++)
            {
                float ang = i * 30f * Mathf.Deg2Rad;
                Box("Vent_" + i, lab, new Vector3(Mathf.Sin(ang) * 1.51f, 0.45f, Mathf.Cos(ang) * 1.51f), new Vector3(0.5f, 0.25f, 0.02f), A.DarkMetal)
                    .transform.rotation = Quaternion.Euler(0, i * 30f, 0);
            }
            // 天井の円環構造と照明（イベント時のみ明るく）
            Cyl("CeilingRing", lab, new Vector3(0, 4.4f, 0), new Vector3(3.6f, 0.25f, 3.6f), A.MatteMetal);
            var ringGo = Cyl("RingLight", lab, new Vector3(0, 4.13f, 0), new Vector3(3.0f, 0.02f, 3.0f), A.AccentBlue, isStatic: false);
            ring = ringGo.GetComponent<Renderer>();
            Cyl("HoloGlass", lab, new Vector3(0, 1.7f, 0), new Vector3(1.6f, 0.78f, 1.6f), A.Glass);
            Cyl("HoloEmitter", lab, new Vector3(0, 0.93f, 0), new Vector3(1.4f, 0.02f, 1.4f), A.AccentBlue);
            // 発明品の表示位置
            displayPoint = new GameObject("DisplayPoint").transform;
            displayPoint.SetParent(lab, false);
            displayPoint.localPosition = new Vector3(0, 1.45f, 0);
            // AI INVENTOR ホログラム表示（待機時のみ）
            hologram = new GameObject("Hologram");
            hologram.transform.SetParent(lab, false);
            var holoCanvas = SignCanvas("HoloText", hologram.transform, new Vector3(0, 1.75f, -0.2f), Quaternion.identity, new Vector2(900, 300));
            var ht = UiKit.Text(holoCanvas.transform, "AI INVENTOR\n<size=60%>どんな発明をしますか？</size>", 90, LabTheme.AccentLight, bold: true, align: TextAlignmentOptions.Center);
            UiKit.Stretch(ht.rectTransform);

            // ロボットアーム（演出物・低ポリゴン）
            var joints = new List<Transform>();
            foreach (float side in new[] { -1f, 1f })
            {
                var root = new GameObject(side < 0 ? "RobotArm_L" : "RobotArm_R").transform;
                root.SetParent(lab, false);
                root.localPosition = new Vector3(2.1f * side, 0, 0.6f);
                root.localRotation = Quaternion.Euler(0, side < 0 ? 60 : -60, 0);
                Cyl("Base", root, new Vector3(0, 0.15f, 0), new Vector3(0.6f, 0.15f, 0.6f), A.RobotWhite, isStatic: false);
                Cyl("Column", root, new Vector3(0, 0.6f, 0), new Vector3(0.32f, 0.3f, 0.32f), A.RobotWhite, isStatic: false);
                var shoulder = new GameObject("Shoulder").transform;
                shoulder.SetParent(root, false); shoulder.localPosition = new Vector3(0, 0.95f, 0);
                Sphere("Joint", shoulder, Vector3.zero, Vector3.one * 0.28f, A.DarkMetal);
                var upper = Capsule("Upper", shoulder, new Vector3(0, 0.45f, 0.12f), new Vector3(0.2f, 0.42f, 0.2f), A.RobotWhite);
                upper.transform.localRotation = Quaternion.Euler(18, 0, 0);
                var elbow = new GameObject("Elbow").transform;
                elbow.SetParent(shoulder, false); elbow.localPosition = new Vector3(0, 0.88f, 0.27f);
                Sphere("Joint", elbow, Vector3.zero, Vector3.one * 0.2f, A.DarkMetal);
                var fore = Capsule("Fore", elbow, new Vector3(0, 0.05f, -0.35f), new Vector3(0.15f, 0.35f, 0.15f), A.RobotWhite);
                fore.transform.localRotation = Quaternion.Euler(-80, 0, 0);
                Sphere("Tool", elbow, new Vector3(0, 0.0f, -0.72f), Vector3.one * 0.1f, A.AccentBlue);
                joints.Add(shoulder);
            }
            arms = joints.ToArray();

            // 作業台（実務的な机・モニター）
            for (int i = 0; i < 3; i++)
            {
                Box("Bench_L" + i, lab, new Vector3(-6.6f, 0.4f, -3 + i * 3), new Vector3(1.2f, 0.8f, 2.2f), A.RobotWhite);
                Box("Monitor_L" + i, lab, new Vector3(-6.8f, 1.15f, -3 + i * 3), new Vector3(0.05f, 0.5f, 0.85f), A.DarkMetal).transform.rotation = Quaternion.Euler(0, 0, -8);
            }
            // サイン（上部ナビの代わりに研究室の固定サインで案内, 仕様21.3）
            var sign = SignCanvas("Sign_Main", lab, new Vector3(-7.85f, 2.9f, 1.5f), Quaternion.Euler(0, -90, 0), new Vector2(3200, 900));
            var st = UiKit.Text(sign.transform, "IMPOSSIBLE LAB\n<size=35%><color=#" + LabTheme.ToHex(LabTheme.TextSecondary) + ">INVENT / TEST / IMPROVE / REALIZE</color></size>", 300, LabTheme.TextPrimary, bold: true, align: TextAlignmentOptions.MidlineLeft);
            UiKit.Stretch(st.rectTransform, 80, 40, 40, 40);
            SignBackground(sign);

            // INVENTION GALLERY（展示台：簡易）
            for (int i = 0; i < 3; i++)
            {
                var p = new Vector3(-2.6f + i * 2.6f, 0, 6.6f);
                Cyl("GalleryPedestal_" + i, lab, p + new Vector3(0, 0.35f, 0), new Vector3(1.0f, 0.35f, 1.0f), A.RobotWhite);
                Cyl("GalleryGlass_" + i, lab, p + new Vector3(0, 1.3f, 0), new Vector3(0.9f, 0.6f, 0.9f), A.Glass);
            }
            var gsign = SignCanvas("Sign_Gallery", lab, new Vector3(0, 3.9f, 7.85f), Quaternion.identity, new Vector2(2600, 300));
            SignBackground(gsign);
            var gt = UiKit.Text(gsign.transform, "05  INVENTION GALLERY ｜ 発明品展示室", 150, LabTheme.TextPrimary, bold: true, align: TextAlignmentOptions.Center);
            UiKit.Stretch(gt.rectTransform);

            // EXPERIMENT GATE（可動式扉）
            var gate = new GameObject("EXPERIMENT GATE").transform;
            gate.SetParent(lab, false);
            gate.localPosition = new Vector3(7.9f, 0, 0);
            gate.localRotation = Quaternion.Euler(0, -90, 0);
            Box("Frame_L", gate, new Vector3(-1.6f, 1.75f, 0), new Vector3(0.3f, 3.5f, 0.5f), A.MatteMetal);
            Box("Frame_R", gate, new Vector3(1.6f, 1.75f, 0), new Vector3(0.3f, 3.5f, 0.5f), A.MatteMetal);
            Box("Frame_Top", gate, new Vector3(0, 3.55f, 0), new Vector3(3.5f, 0.3f, 0.5f), A.MatteMetal);
            Box("GateLight", gate, new Vector3(0, 3.38f, -0.26f), new Vector3(3.0f, 0.04f, 0.02f), A.AccentBlue);
            gateL = Box("Door_L", gate, new Vector3(-0.6f, 1.7f, 0), new Vector3(1.2f, 3.4f, 0.12f), A.DarkMetal, isStatic: false).transform;
            gateR = Box("Door_R", gate, new Vector3(0.6f, 1.7f, 0), new Vector3(1.2f, 3.4f, 0.12f), A.DarkMetal, isStatic: false).transform;
            var gs = SignCanvas("Sign_Gate", gate, new Vector3(0, 4.05f, -0.3f), Quaternion.identity, new Vector2(2400, 300));
            SignBackground(gs);
            var gst = UiKit.Text(gs.transform, "03  EXPERIMENT GATE ｜ 実験ゲート", 140, LabTheme.TextPrimary, bold: true, align: TextAlignmentOptions.Center);
            UiKit.Stretch(gst.rectTransform);

            // 立ち位置とコンソール（正面約1.2m・やや下向き）
            var spot = new GameObject("WorkshopPlayerSpot").transform;
            spot.SetParent(lab, false); spot.localPosition = new Vector3(0, 0, -3.3f); spot.localRotation = Quaternion.identity;
            var cspot = new GameObject("WorkshopConsoleSpot").transform;
            cspot.SetParent(lab, false); cspot.localPosition = new Vector3(0, 1.0f, -2.1f); cspot.localRotation = Quaternion.identity;
            return lab;
        }

        // ================= RainLab =================
        static RainLabController BuildRainLab(Transform env)
        {
            var root = new GameObject("TestZone_RainLab").transform;
            root.SetParent(env, false);
            root.position = RainLabPos;
            var ground = Box("Ground", root, new Vector3(0, -0.05f, 0), new Vector3(24, 0.1f, 24), A.Ground);
            AddTeleport(ground);
            Box("Shelter_Back", root, new Vector3(0, 1.5f, 6), new Vector3(10, 3, 0.2f), A.Concrete);
            for (int i = -2; i <= 2; i++) Box("Marker_" + i, root, new Vector3(i * 1.0f, 0.003f, 0), new Vector3(0.02f, 0.004f, 0.6f), A.AccentOrange);
            // 判定用マネキン：円筒（半径0.22m・高さ1.75m）＋簡易人体
            var man = new GameObject("Mannequin").transform;
            man.SetParent(root, false);
            Capsule("Torso", man, new Vector3(0, 1.05f, 0), new Vector3(0.36f, 0.5f, 0.24f), A.Mannequin);
            Sphere("Head", man, new Vector3(0, 1.62f, 0), Vector3.one * 0.22f, A.Mannequin);
            Capsule("Legs", man, new Vector3(0, 0.42f, 0), new Vector3(0.3f, 0.42f, 0.2f), A.Mannequin);
            var zone = Cyl("WetZone", man, new Vector3(0, ImpossibleLab.Core.RainSimulator.BodyHeight * 0.5f, 0),
                new Vector3(ImpossibleLab.Core.RainSimulator.BodyRadius * 2, ImpossibleLab.Core.RainSimulator.BodyHeight * 0.5f, ImpossibleLab.Core.RainSimulator.BodyRadius * 2), A.WetZone, isStatic: false);
            var headTop = new GameObject("HeadTop").transform;
            headTop.SetParent(man, false); headTop.localPosition = new Vector3(0, ImpossibleLab.Core.RainSimulator.BodyHeight, 0);
            var label = SignCanvas("ZoneLabel", man, new Vector3(0.75f, 1.0f, 0), Quaternion.identity, new Vector2(700, 160));
            SignBackground(label);
            var lt = UiKit.Text(label.transform, "判定用の円筒モデル\n<size=75%>半径0.22m・高さ1.75m</size>", 52, LabTheme.TextPrimary, align: TextAlignmentOptions.Center);
            UiKit.Stretch(lt.rectTransform);

            // 雨粒の可視化（計測と同じ軌跡のみ描く）
            var rainGo = new GameObject("RainParticles");
            rainGo.transform.SetParent(root, false);
            var ps = rainGo.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.duration = 60f; main.playOnAwake = false;
            main.startSpeed = 0f; main.startSize = 0.012f; main.maxParticles = 3000;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = 0f;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var pr = rainGo.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = ParticleSystemRenderMode.Stretch; pr.velocityScale = 0.03f; pr.lengthScale = 1f;
            pr.sharedMaterial = A.RainParticle; pr.shadowCastingMode = ShadowCastingMode.Off;

            var ctrl = root.gameObject.AddComponent<RainLabController>();
            ctrl.Rain = ps;
            ctrl.MannequinHeadTop = headTop;
            ctrl.WetZone = zone.GetComponent<Renderer>();
            ctrl.WetZoneNormal = A.WetZone; ctrl.WetZoneHit = A.MannequinHit;
            var spot = new GameObject("PlayerSpot").transform;
            spot.SetParent(root, false); spot.localPosition = new Vector3(0, 0, -4.5f);
            var cspot = new GameObject("ConsoleSpot").transform;
            cspot.SetParent(root, false); cspot.localPosition = new Vector3(0, 1.0f, -3.3f); cspot.localRotation = Quaternion.identity;
            ctrl.PlayerSpot = spot; ctrl.ConsoleSpot = cspot;
            var sign = SignCanvas("Sign_RainLab", root, new Vector3(0, 2.6f, 5.85f), Quaternion.identity, new Vector2(2600, 300));
            SignBackground(sign);
            var t = UiKit.Text(sign.transform, "TEST ZONE ｜ RainLab（雨・風の実験場）", 140, LabTheme.TextPrimary, bold: true, align: TextAlignmentOptions.Center);
            UiKit.Stretch(t.rectTransform);
            return ctrl;
        }

        // ================= helpers =================
        static void AddTeleport(GameObject floor)
        {
            var area = floor.AddComponent<TeleportationArea>();
            area.interactionLayers = 1 << 31; // "Teleport"
        }

        static Canvas SignCanvas(string name, Transform parent, Vector3 pos, Quaternion rot, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(parent, false);
            var c = go.GetComponent<Canvas>(); c.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size; rt.localScale = Vector3.one * UiKit.MetersPerPixel;
            rt.localPosition = pos; rt.localRotation = rot;
            return c;
        }

        static void SignBackground(Canvas c)
        {
            var img = UiKit.Image("Bg", c.transform, LabTheme.PanelAlpha);
            UiKit.Stretch(img.rectTransform);
            img.transform.SetAsFirstSibling();
        }

        static GameObject Prim(PrimitiveType t, string name, Transform parent, Vector3 pos, Vector3 scale, Material m, bool isStatic, bool keepCollider)
        {
            var go = GameObject.CreatePrimitive(t);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            if (isStatic) GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            return go;
        }

        static GameObject Box(string n, Transform p, Vector3 pos, Vector3 s, Material m, bool isStatic = true) =>
            Prim(PrimitiveType.Cube, n, p, pos, s, m, isStatic, keepCollider: n == "Floor" || n == "Ground" || n.StartsWith("Wall"));
        static GameObject Cyl(string n, Transform p, Vector3 pos, Vector3 s, Material m, bool isStatic = true) => Prim(PrimitiveType.Cylinder, n, p, pos, s, m, isStatic, n.StartsWith("InventionTable_Base"));
        static GameObject Sphere(string n, Transform p, Vector3 pos, Vector3 s, Material m) => Prim(PrimitiveType.Sphere, n, p, pos, s, m, false, false);
        static GameObject Capsule(string n, Transform p, Vector3 pos, Vector3 s, Material m) => Prim(PrimitiveType.Capsule, n, p, pos, s, m, false, false);
    }
}
