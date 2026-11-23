using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using ImpossibleLab.Core;
using UnityEngine;

namespace ImpossibleLab
{
    /// <summary>
    /// 開発用：縦切りの一連操作（指示→生成→承認→実験→改良→再実験→モード比較→保存→ギャラリー→再読込）を自動実行し、
    /// 各画面を撮影して計測値をログに出す。Editor の検証用（-ilabVerify）。実機のVR操作の代替ではない。
    /// </summary>
    public sealed class DemoAutopilot : MonoBehaviour
    {
        public static bool Finished;
        public static int ExitCode;
        public string OutputDir;
        LabApp _app;
        Camera _cam;
        RenderTexture _rt;
        readonly StringBuilder _log = new StringBuilder();
        int _shot;

        IEnumerator Start()
        {
            _app = FindFirstObjectByType<LabApp>();
            Directory.CreateDirectory(OutputDir);
            var go = new GameObject("CaptureCamera");
            _cam = go.AddComponent<Camera>();
            _cam.enabled = false; _cam.fieldOfView = 62f; _cam.nearClipPlane = 0.05f;
            _cam.clearFlags = CameraClearFlags.SolidColor; _cam.backgroundColor = new Color(0.62f, 0.70f, 0.78f);
            _rt = new RenderTexture(1600, 900, 24);
            Time.timeScale = 3f;
            yield return null;
            yield return null;
            bool ok = true;
            IEnumerator run = Run();
            while (true)
            {
                object cur;
                try { if (!run.MoveNext()) break; cur = run.Current; }
                catch (Exception e) { Log("EXCEPTION " + e); ok = false; break; }
                yield return cur;
            }
            Log(ok ? "AUTOPILOT_OK" : "AUTOPILOT_FAILED");
            File.WriteAllText(Path.Combine(OutputDir, "verify_log.txt"), _log.ToString(), Encoding.UTF8);
            ExitCode = ok && !_failed ? 0 : 1;
            Finished = true;
        }

        bool _failed;
        void Check(bool cond, string what) { Log((cond ? "PASS " : "FAIL ") + what); if (!cond) _failed = true; }
        void Log(string s) { _log.AppendLine(s); Debug.Log("[ILAB_VERIFY] " + s); }

        IEnumerator WaitState(LabState s, float timeout = 30f)
        {
            float t = 0;
            while (_app.Sm.State != s && t < timeout) { t += Time.unscaledDeltaTime; yield return null; }
            Check(_app.Sm.State == s, "state reached " + s + " (now " + _app.Sm.State + ")");
            yield return null; yield return null;
        }

        IEnumerator Shot(string name, bool rainLab = false, bool overview = false, string facePanel = null, bool device = false, string devicePart = null, Vector3? eyeAt = null, Vector3? lookAt = null)
        {
            yield return null; // batchmode では WaitForEndOfFrame が呼ばれないため
            Vector3 basePos = rainLab ? _app.RainLab.transform.position : Vector3.zero;
            var panel = facePanel != null ? _app.ConsoleRoot.Find(facePanel) : null;
            bool hideConsole = false;
            Transform hiddenFrame = null;
            if (eyeAt.HasValue && lookAt.HasValue)
            {
                // 研究室の決まった場所を撮る（小物の見た目の確認用）。パネルは隠す
                _cam.transform.SetPositionAndRotation(eyeAt.Value, Quaternion.LookRotation(lookAt.Value - eyeAt.Value));
                hideConsole = _app.ConsoleRoot.gameObject.activeSelf;
                _app.ConsoleRoot.gameObject.SetActive(false);
            }
            else if (device && _app.View != null)
            {
                // 試作品に寄って撮る（部品の見た目の確認用）。パネルが手前に来るので一時的に隠す
                // devicePart を指定すると、その部品のまわりに寄る（下から見上げる）
                Transform focus = _app.View.transform;
                if (devicePart != null && _app.View.Parts.TryGetValue(devicePart, out var pt)) focus = pt;
                var rs = focus.GetComponentsInChildren<Renderer>();
                var b = rs.Length > 0 ? rs[0].bounds : new Bounds(focus.position, Vector3.one * 0.3f);
                foreach (var r in rs) b.Encapsulate(r.bounds);
                // 前（+Z）から、少し下から見上げる（参考：描き直した画像の角度）
                float d = Mathf.Max(0.35f, b.extents.magnitude * (devicePart != null ? 3.2f : 1.15f));
                var dir = (devicePart != null ? new Vector3(0.6f, -0.35f, 1f) : new Vector3(0.35f, -0.1f, 1f)).normalized;
                _cam.transform.SetPositionAndRotation(b.center + dir * d, Quaternion.LookRotation(-dir));
                hideConsole = _app.ConsoleRoot.gameObject.activeSelf;
                _app.ConsoleRoot.gameObject.SetActive(false);
                // 選択中の部品の枠は見た目の確認では隠す
                hiddenFrame = _app.View.transform.Find("PartHighlight");
                if (hiddenFrame != null) hiddenFrame.gameObject.SetActive(false);
            }
            else if (panel != null)
            {
                // 側面のパネルを正面から撮る（文字の読みやすさの確認用）
                _cam.transform.SetPositionAndRotation(panel.position - panel.forward * 1.15f, Quaternion.LookRotation(panel.forward));
            }
            else if (overview) _cam.transform.SetPositionAndRotation(new Vector3(5.5f, 3.4f, -6.8f), Quaternion.LookRotation(new Vector3(0, 1.2f, 0) - new Vector3(5.5f, 3.4f, -6.8f)));
            else
            {
                var eye = basePos + new Vector3(0, 1.6f, rainLab ? -4.5f : -3.3f);
                var look = basePos + new Vector3(0, rainLab ? 1.05f : 1.0f, rainLab ? -1.6f : -0.9f);
                _cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            }
            _cam.targetTexture = _rt;
            _cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            var tex = new Texture2D(_rt.width, _rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            _cam.targetTexture = null;
            string file = Path.Combine(OutputDir, $"{++_shot:00}_{name}.png");
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Destroy(tex);
            Log("SHOT " + file);
            if (hiddenFrame != null) hiddenFrame.gameObject.SetActive(true);
            if (hideConsole) { _app.ConsoleRoot.gameObject.SetActive(true); yield break; }
            if (!overview) CheckFits(name);
        }

        /// <summary>メイン・左右パネルの全要素（文字の実際の描画範囲を含む）がパネルの枠内に収まっているかを確かめる。</summary>
        void CheckFits(string screen, Canvas only = null)
        {
            Canvas.ForceUpdateCanvases();
            var bad = new System.Collections.Generic.List<string>();
            var corners = new Vector3[4];
            foreach (var canvas in only != null ? new[] { only } : _app.ConsoleRoot.GetComponentsInChildren<Canvas>(false))
            {
                if (!canvas.isRootCanvas || canvas.GetComponentInChildren<UnityEngine.UI.Graphic>() == null) continue;
                var root = (RectTransform)canvas.transform;
                Rect r = root.rect;
                foreach (var g in canvas.GetComponentsInChildren<UnityEngine.UI.Graphic>(false))
                {
                    if (!g.enabled || g.GetComponentInParent<UnityEngine.UI.RectMask2D>() != null) continue; // 入力欄の中身はマスクで切り取られる
                    Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
                    if (g is TMPro.TMP_Text t)
                    {
                        if (string.IsNullOrWhiteSpace(t.text)) continue;
                        t.ForceMeshUpdate();
                        var b = t.textBounds;
                        if (b.size.x <= 0.01f || b.size.y <= 0.01f) continue;
                        corners[0] = t.transform.TransformPoint(b.min); corners[1] = t.transform.TransformPoint(b.max);
                        for (int i = 0; i < 2; i++) { Vector2 p = root.InverseTransformPoint(corners[i]); min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
                    }
                    else
                    {
                        g.rectTransform.GetWorldCorners(corners);
                        foreach (var c in corners) { Vector2 p = root.InverseTransformPoint(c); min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
                    }
                    const float tol = 2f;
                    if (min.x < r.xMin - tol || min.y < r.yMin - tol || max.x > r.xMax + tol || max.y > r.yMax + tol)
                        bad.Add($"{canvas.name}/{g.transform.parent?.name}/{g.name}");
                }
            }
            Check(bad.Count == 0, $"{screen}: all UI inside panels" + (bad.Count > 0 ? $" ({bad.Count} outside: {string.Join(", ", bad.GetRange(0, Mathf.Min(6, bad.Count)))})" : ""));
        }

        IEnumerator Run()
        {
            yield return new WaitForSeconds(1f);
            yield return Shot("overview_lab", overview: true);
            yield return Shot("lab_left_wall", eyeAt: new Vector3(-4.0f, 1.7f, 1.0f), lookAt: new Vector3(-8f, 2.0f, -2.5f));
            yield return Shot("lab_gate", eyeAt: new Vector3(4.5f, 1.6f, -4.5f), lookAt: new Vector3(8f, 1.0f, -1.0f));
            yield return Shot("lab_arm_pedestal", eyeAt: new Vector3(2.8f, 1.4f, -2.6f), lookAt: new Vector3(1.4f, 1.0f, -0.6f));
            yield return Shot("lab_ceiling", eyeAt: new Vector3(-1.0f, 1.6f, -3.5f), lookAt: new Vector3(5.5f, 4.6f, 3.0f));
            yield return Shot("lab_window", eyeAt: new Vector3(-1.5f, 1.6f, 3.0f), lookAt: new Vector3(1.5f, 0.6f, 7.8f));
            var grab = UnityEngine.Object.FindFirstObjectByType<ConsoleGrabHandle>();
            Check(grab != null && grab.Target == _app.ConsoleRoot && grab.isActiveAndEnabled, "panel grab handle exists under console");
            // S01：クイック発明は入力欄に反映するだけ
            _app.PickQuickInvention(LabAppText.RainRequest);
            Check(_app.Sm.State == LabState.Drafting, "quick pick does not start generation");
            Check(_app.InputWarning == null && _app.Toast == null && _app.PendingDeleteId == null, "runtime state starts as null (not serialized as empty string)");
            // パネルの高さ：目より下に出る／頭上に置かれても戻る／下げるボタンで下がる
            var head = _app.Origin != null ? _app.Origin.Camera.transform : null;
            Vector3 headLocal0 = head != null ? head.localPosition : Vector3.zero;
            if (head != null && head.localPosition.sqrMagnitude < 1e-6f)
            {
                head.localPosition = new Vector3(0, 1.15f, 0); // エディターでは頭の姿勢が来ないため、座った人の目の高さを疑似的に与える
                yield return new WaitForSeconds(1f);           // 起動時の位置合わせ（頭の姿勢が来るのを待っている）を走らせる
            }
            if (head != null && head.localPosition.sqrMagnitude > 1e-6f)
            {
                Check(_app.ConsoleRoot.position.y < head.position.y - 0.1f, $"console center starts below eye (eye={head.position.y:F2} console={_app.ConsoleRoot.position.y:F2})");
                var cp = _app.ConsoleRoot.position;
                _app.ConsoleRoot.position = new Vector3(cp.x, head.position.y + 0.8f, cp.z); // 頭上に置かれた状態を再現
                yield return new WaitForSeconds(2.5f);
                Check(_app.ConsoleRoot.position.y < head.position.y - 0.1f, "overhead console is pulled back below eye");
                float y0 = _app.ConsoleRoot.position.y;
                _app.NudgeConsoleHeight(-1);
                Check(_app.ConsoleRoot.position.y < y0 - 0.05f, "lower button moves console down");
                _app.NudgeConsoleHeight(+1);
                // 座っている人が立ち上がった→座り直したなど、目の高さが大きく変わった後も目より下に戻る
                head.localPosition = new Vector3(0, 1.65f, 0);
                yield return new WaitForSeconds(2.5f);
                head.localPosition = new Vector3(0, 1.10f, 0);
                yield return new WaitForSeconds(2.5f);
                Check(_app.ConsoleRoot.position.y < head.position.y - 0.1f, $"console follows when user sits down (eye={head.position.y:F2} console={_app.ConsoleRoot.position.y:F2})");
                head.localPosition = headLocal0;
            }
            else Log("SKIP console height checks (head pose not available in editor)");
            yield return Shot("S01_invention_input");
            foreach (var tx in GameObject.Find("MainConsole").GetComponentsInChildren<TMPro.TMP_Text>())
            {
                var rt = tx.rectTransform;
                string txt = tx.text.Length > 24 ? tx.text.Substring(0, 24) : tx.text;
                Log($"TMP {tx.name} active={tx.isActiveAndEnabled} size={rt.rect.width:0}x{rt.rect.height:0} font={tx.font?.name} text={txt.Replace('\n', ' ')}");
            }
            // 空入力の警告
            _app.SetRequestText("  ");
            _app.SubmitRequest();
            Check(_app.InputWarning != null && _app.Sm.State == LabState.Drafting, "empty input warns: " + _app.InputWarning);
            // 未対応の発明 → エラー画面と代替案
            _app.SetRequestText("時間を止める時計を作って");
            _app.SubmitRequest();
            yield return WaitState(LabState.Error);
            yield return Shot("S02_error_unsupported");
            _app.UseOfflineSample();
            yield return new WaitForSeconds(0.4f);
            yield return Shot("S02_generating");
            yield return WaitState(LabState.AwaitingApproval);
            Check(_app.Preview != null && _app.Preview.Category == "protection", "preview DNA valid");
            yield return Shot("S02_preview");
            _app.ApprovePreview();
            yield return new WaitForSeconds(0.6f);
            yield return Shot("S02_building");
            yield return WaitState(LabState.Editing);
            yield return Shot("S03_edit");
            yield return Shot("S03_device_rain", device: true);
            yield return Shot("S03_device_rain_core", device: true, devicePart: "core");

            // 発明台の上で回す（右スティック・「向き ◀ ▶」）。試用を止めても回した向きに戻る
            _app.TurnDisplay(90f);
            float want = _app.DisplayPoint.eulerAngles.y + 90f;
            Check(Mathf.Abs(Mathf.DeltaAngle(_app.View.transform.eulerAngles.y, want)) < 0.5f, "display turns 90 degrees");
            yield return Shot("S03_device_turned", device: true);

            // 試用：起動して追従（Editorではカメラが頭）
            _app.ToggleDevice();
            Check(_app.DeviceRunning, "device activates");
            _app.ToggleDevice();
            Check(Mathf.Abs(Mathf.DeltaAngle(_app.View.transform.eulerAngles.y, want)) < 0.5f, "display keeps its turn after the trial");
            _app.TurnDisplay(-90f);

            // 実験 Rev.1（DREAM, 風3m/s）
            _app.TestConfig = new RainExperimentConfig { Mode = LabModes.Dream, RainIntensity = 30, WindSpeed = 4, WindDirectionDeg = 90, Seed = 2026, DurationSec = 10 };
            _app.GoToTestSetup();
            yield return WaitState(LabState.ConfiguringTest);
            yield return Shot("S04_test_setup");
            _app.StartExperiment();
            yield return WaitState(LabState.RunningTest);
            yield return new WaitForSeconds(4f);
            yield return Shot("S05_running", rainLab: true);
            yield return WaitState(LabState.ReviewingResult);
            var r1 = _app.LastResult;
            Log($"RESULT rev{r1.Revision} {r1.Config.Mode} wetness={r1.WetnessScore:0.00} coverage={r1.CoverageRatio:0.000} hits={r1.DeviceHits}/{r1.BaselineHits}");
            yield return Shot("S05_result_rev1", rainLab: true);

            // 改良：横からの雨 → 範囲
            _app.ImproveAndRetest();
            yield return WaitState(LabState.Editing);
            _app.PickRevision("横からの雨も防いで");
            _app.RequestRevision();
            float t = 0; while (_app.PendingRevision == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Check(_app.PendingRevision != null && _app.PendingRevision.Success, "revision proposal (side rain)");
            yield return Shot("S03_revision_diff");
            _app.Save();
            Check(_app.Sm.State == LabState.Editing && _app.PendingRevision != null && _app.Toast != null && _app.Toast.Contains("未適用"),
                "save is blocked with a notice while a revision proposal is pending");
            _app.ApproveRevision();
            _app.PickRevision("防ぐ範囲を広げて");
            _app.RequestRevision();
            t = 0; while (_app.PendingRevision == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            _app.ApproveRevision();
            Check(_app.Current.Current.Revision == 3, "revision count = 3");
            Check(Mathf.Approximately(_app.Current.Current.GetParameter("rain_shield", "radius", 0), 0.8f), "radius widened to 0.8");
            // 元に戻す → やり直し
            _app.Undo();
            Check(_app.Current.Current.Revision == 2, "undo returns to rev2");
            _app.PickRevision("防ぐ範囲を広げて");
            _app.RequestRevision();
            t = 0; while (_app.PendingRevision == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            _app.ApproveRevision();
            yield return Shot("S03_after_revision");

            // 同一条件で再実験
            _app.GoToTestSetup();
            yield return WaitState(LabState.ConfiguringTest);
            _app.StartExperiment();
            yield return WaitState(LabState.ReviewingResult, 40f);
            var r2 = _app.LastResult;
            Log($"RESULT rev{r2.Revision} {r2.Config.Mode} wetness={r2.WetnessScore:0.00} coverage={r2.CoverageRatio:0.000} hits={r2.DeviceHits}/{r2.BaselineHits}");
            Check(_app.BaselineResult != null && _app.BaselineResult.Revision == 1, "comparison baseline is rev1 under same conditions");
            Check(r2.WetnessScore < r1.WetnessScore, "improvement reduced wetness");
            yield return Shot("S05_result_compare", rainLab: true);

            // REALITY で比較
            _app.RerunOtherMode();
            yield return WaitState(LabState.ReviewingResult, 40f);
            var r3 = _app.LastResult;
            Log($"RESULT rev{r3.Revision} {r3.Config.Mode} wetness={r3.WetnessScore:0.00} coverage={r3.CoverageRatio:0.000} energy={r3.EnergyWh:0.000}Wh drift={r3.DriftM:0.00}");
            Check(r3.Config.Mode == LabModes.Reality && r3.EnergyWh.HasValue, "reality result has energy");
            Check(_app.OtherModeResult != null, "dream/reality comparison available");
            yield return Shot("S05_result_reality", rainLab: true);
            Check(r3.RuntimeMinutesMin.HasValue && r3.RuntimeMinutesMin <= r3.RuntimeMinutes && r3.RuntimeMinutes <= r3.RuntimeMinutesMax, "reality runtime range brackets nominal");

            // REALITY 前提係数と出典
            _app.GoToTestSetup();
            yield return WaitState(LabState.ConfiguringTest);
            Check(_app.TestConfig.Mode == LabModes.Reality, "setup keeps reality mode");
            yield return Shot("S04_reality_setup", rainLab: true);
            _app.ShowCoefficients();
            Check(_app.CoefficientPage == 0 && _app.CoefficientPageCount >= 2, "coefficient table opens");
            yield return Shot("S04_coefficients_p1", rainLab: true);
            for (int p = 2; p <= _app.CoefficientPageCount; p++)
            {
                _app.StepCoefficientPage(+1);
                yield return Shot("S04_coefficients_p" + p, rainLab: true);
            }
            _app.HideCoefficients();
            _app.BackToEditing();
            yield return WaitState(LabState.Editing, 20f);

            // 保存 → ギャラリー
            _app.Save();
            yield return WaitState(LabState.Saved);
            yield return Shot("S06_saved");
            _app.OpenGallery();
            yield return WaitState(LabState.Gallery);
            Check(_app.PendingDeleteId == null, "gallery opens without delete dialog");
            yield return Shot("S06_gallery");
            // 7件以上ならページ送り（新しい順に6件ずつ）
            var gui = FindFirstObjectByType<LabUI>();
            if (_app.GalleryEntries.Count > LabUI.GalleryPerPage)
            {
                gui.StepGalleryPage(+1);
                yield return null;
                Check(gui.GalleryPage == 1, $"gallery moves to page 2 ({_app.GalleryEntries.Count} entries)");
                yield return Shot("S06_gallery_p2");
                gui.StepGalleryPage(-1);
                yield return null;
            }
            else Log($"gallery paging not exercised ({_app.GalleryEntries.Count} entries)");
            // タグで絞り込み：選んだタグの発明だけになり、「すべて」で戻る
            var tagCounts = GalleryFilter.Counts(_app.GalleryEntries);
            var pick = tagCounts.Where(x => x.count < _app.GalleryEntries.Count).Select(x => x.tag.Id).FirstOrDefault() ?? tagCounts.Select(x => x.tag.Id).FirstOrDefault();
            if (pick != null)
            {
                gui.SetGalleryTag(pick);
                yield return null;
                var shown = GalleryFilter.Apply(_app.GalleryEntries, gui.GalleryTag);
                Check(gui.GalleryTag == pick && shown.Count == tagCounts.First(x => x.tag.Id == pick).count && shown.All(e => e.Tags.Contains(pick)) && gui.GalleryPage == 0,
                    $"gallery filters by tag {pick} ({shown.Count}/{_app.GalleryEntries.Count})");
                yield return Shot("S06_gallery_tag");
                yield return Shot("S06_gallery_tag_left_panel", facePanel: "LeftPanel");
                gui.SetGalleryTag(null);
                yield return null;
                Check(gui.GalleryTag == null, "gallery tag filter clears");
            }
            else Log("gallery tag filter not exercised (no tags)");
            _app.AskDelete(_app.Current.Current.Id);
            yield return null; yield return null;
            yield return Shot("S06_delete_confirm");
            _app.CancelDelete();

            // 再起動相当：新しいリポジトリから読み込み、同じDNAか
            var repo = new InventionRepository(Application.persistentDataPath);
            var loaded = repo.Load(_app.Current.Current.Id, out var err);
            Check(loaded != null && DnaJson.ToJson(loaded.Current) == DnaJson.ToJson(_app.Current.Current), "saved DNA restores identically " + err);
            Check(loaded != null && loaded.Experiments.Count >= 3, "experiment records persisted");
            string rainId = _app.Current.Current.Id;
            _app.LoadFromGallery(rainId);
            yield return WaitState(LabState.Editing);
            yield return Shot("S03_loaded_from_gallery");

            // 手元メニュー：表示・収まり・効果音の切り替え
            var hm = _app.GetComponent<HandMenu>();
            if (!LabApp.HandMenuEnabled) Check(hm == null, "hand menu disabled (not created)");
            else Check(hm != null && hm.Canvas != null, "hand menu created (Left Controller found)");
            if (hm != null && hm.Canvas != null)
            {
                var eye = new Vector3(0, 1.6f, -3.3f);
                var at = eye + new Vector3(0.12f, -0.25f, 0.45f);
                hm.PoseOverride = new Pose(at, Quaternion.LookRotation(at - eye));
                hm.ForceVisible = true;
                yield return null; yield return null;
                Check(hm.Shown, "hand menu shows");
                yield return Shot("hand_menu");
                CheckFits("hand_menu", hm.Canvas);
                bool soundBefore = _app.Fx.SoundOn;
                var soundBtn = hm.Canvas.GetComponentsInChildren<UnityEngine.UI.Button>().FirstOrDefault(b => b.GetComponentInChildren<TMPro.TMP_Text>().text.Contains("効果音"));
                soundBtn?.onClick.Invoke();
                Check(soundBtn != null && _app.Fx.SoundOn != soundBefore, "hand menu toggles sound");
                _app.Fx.SoundOn = soundBefore;
                hm.ForceVisible = false; hm.PoseOverride = null;
                yield return null;
            }

            // 運搬ロボット（障害物コース）・空飛ぶ靴（飛行デッキ）：生成 → 試用 → 実験 → 改良 → 再実験 → REALITY → 保存
            foreach (var (req, cat, fix, key) in new[] {
                ("自動でついてくる運搬ロボットを作って", "carrier", "障害物をよけて", "collisionCount"),
                ("空を飛べる靴を作って", "mobility", "風で揺れないようにして", "stabilityScore") })
            {
                _app.NewInvention();
                _app.SetRequestText(req);
                float w0 = 0;
                while (_app.Sm.State == LabState.Drafting && w0 < 10f) { _app.SubmitRequest(); w0 += Time.unscaledDeltaTime; yield return null; }
                yield return WaitState(LabState.AwaitingApproval);
                Check(_app.Preview != null && _app.Preview.Category == cat, $"{cat}: offline design generated");
                yield return Shot($"S02_preview_{cat}");
                _app.ApprovePreview();
                yield return WaitState(LabState.Editing);
                yield return Shot($"S03_edit_{cat}");
                yield return Shot($"S03_device_{cat}", device: true);
                if (cat == "carrier")
                {
                    int rev = _app.Current.Current.Revision;
                    _app.StepAppearance(+1);
                    yield return null;
                    Check(_app.Current.Current.Appearance == "safety_orange" && _app.Current.Current.Revision == rev, "appearance preset changes without a new revision");
                    yield return Shot("S03_appearance_carrier");
                    // 部品の形・素材の切り替え：改良の版が増え、総質量が変わり、元に戻せる
                    _app.StepSelectedPart(+1);
                    yield return null;
                    var part = _app.SelectedPart;
                    Check(part != null && _app.SelectedPartIndex == 1 && _app.PartHighlightVisible, "part selection moves and the part is framed");
                    float kg0 = MassModel.TotalKg(_app.Current.Current);
                    string mat0 = part.MaterialOrDefault, shape0 = part.ShapeOrDefault;
                    _app.StepPartMaterial(+1);
                    _app.StepPartShape(+1);
                    yield return null;
                    var edited = _app.Current.Current.FindPart(part.Id);
                    Check(_app.Current.Current.Revision == rev + 2 && edited.MaterialOrDefault != mat0 && edited.ShapeOrDefault != shape0, "part material and shape change as new revisions");
                    Check(!Mathf.Approximately(MassModel.TotalKg(_app.Current.Current), kg0), "total mass follows the material/shape change");
                    Check(_app.PartHighlightVisible, "frame follows the rebuilt part");
                    yield return Shot("S03_part_edit_carrier");
                    yield return Shot("S03_part_edit_left_panel", facePanel: "LeftPanel");
                    _app.Undo(); _app.Undo();
                    yield return null;
                    Check(_app.Current.Current.Revision == rev && _app.Current.Current.FindPart(part.Id).MaterialOrDefault == mat0, "undo restores part material");
                }
                _app.ToggleDevice();
                yield return new WaitForSeconds(1.5f);
                Check(_app.DeviceRunning, $"{cat}: device activates in workshop");
                yield return Shot($"S03_trial_{cat}");
                _app.ToggleDevice();
                _app.GoToTestSetup();
                yield return WaitState(LabState.ConfiguringTest);
                Check(_app.TestConfig.Environment == LabEnvironments.ForCategory(cat), $"{cat}: environment = {_app.TestConfig.Environment}");
                if (cat == "mobility")
                {
                    Check(Mathf.Approximately(_app.TestConfig.WindDirectionDeg, 0f) && Mathf.Approximately(_app.TestConfig.WindSpeed, 4f),
                        $"mobility: default wind is crosswind 4 m/s (dir={_app.TestConfig.WindDirectionDeg:0} speed={_app.TestConfig.WindSpeed:0.0})");
                    _app.TestConfig.WindDirectionDeg = 0f; _app.TestConfig.WindSpeed = 4f;
                }
                _app.Notify();
                yield return null;
                yield return Shot($"S04_setup_{cat}");
                _app.StartExperiment();
                yield return WaitState(LabState.RunningTest);
                yield return new WaitForSeconds(3f);
                yield return Shot($"S05_running_{cat}", rainLab: true);
                yield return WaitState(LabState.ReviewingResult, 60f);
                var c1 = _app.LastResult;
                Check(c1 != null && !c1.IsRain && c1.Metrics.Count > 0, $"{cat}: result has metrics");
                float before = c1?.Metric(key)?.Value ?? 0f;
                Log($"RESULT {cat} rev{c1?.Revision} {c1?.Config.Mode} passed={c1?.Passed} {string.Join(" ", c1?.Metrics.Select(m => m.Key + "=" + m.Display) ?? new string[0])}");
                yield return Shot($"S05_result_{cat}_rev1", rainLab: true);
                _app.ImproveAndRetest();
                yield return WaitState(LabState.Editing);
                _app.PickRevision(fix);
                _app.RequestRevision();
                float tw = 0; while (_app.PendingRevision == null && tw < 5f) { tw += Time.unscaledDeltaTime; yield return null; }
                Check(_app.PendingRevision != null && _app.PendingRevision.Success, $"{cat}: revision proposal ({fix})");
                _app.ApproveRevision();
                _app.GoToTestSetup();
                yield return WaitState(LabState.ConfiguringTest);
                _app.StartExperiment();
                yield return WaitState(LabState.ReviewingResult, 60f);
                var c2 = _app.LastResult;
                float after = c2?.Metric(key)?.Value ?? 0f;
                Log($"RESULT {cat} rev{c2?.Revision} {c2?.Config.Mode} passed={c2?.Passed} {string.Join(" ", c2?.Metrics.Select(m => m.Key + "=" + m.Display) ?? new string[0])}");
                Check(_app.BaselineResult != null, $"{cat}: compared with previous revision");
                Check(cat == "carrier" ? after < before : after > before, $"{cat}: {key} improved {before:0.##} -> {after:0.##}");
                yield return Shot($"S05_result_{cat}_compare", rainLab: true);
                _app.RerunOtherMode();
                yield return WaitState(LabState.ReviewingResult, 60f);
                var c3 = _app.LastResult;
                Log($"RESULT {cat} rev{c3?.Revision} {c3?.Config.Mode} passed={c3?.Passed} {string.Join(" ", c3?.Metrics.Select(m => m.Key + "=" + m.Display) ?? new string[0])} | {string.Join(" / ", c3?.Limits ?? new System.Collections.Generic.List<string>())}");
                Check(c3 != null && c3.Config.Mode == LabModes.Reality, $"{cat}: REALITY run");
                if (cat == "mobility") Check(c3 != null && c3.Limits.Any(l => l.Contains("電池切れ")), "mobility: REALITY shows power limit");
                yield return Shot($"S05_result_{cat}_reality", rainLab: true);
                _app.Save();
                yield return WaitState(LabState.Saved, 20f);
                var saved = _app.Repo.Load(_app.Current.Current.Id, out var serr);
                Check(saved != null && saved.Experiments.Count >= 3 && saved.Experiments.All(e => e.Environment == LabEnvironments.ForCategory(cat)), $"{cat}: saved with experiments {serr}");
            }

            // 動作プログラム（仕様 28、G-A2）：見本「自動で開くゴミ箱」を研究室で試用する
            yield return MotionBinFlow();

            // 2026-11-22 Quest で AI が設計したキャリーバッグ：薄い骨組み（持ち手）は枠で描き、実験場で車輪が床に埋まらない
            yield return CarryBagFlow();

            // 移動の実験のあとに雨具へ戻ると、風が雨の初期値（90°・3 m/s）に戻る（Quest 実機で 4 m/s が引き継がれた不具合）
            _app.LoadFromGallery(rainId);
            yield return WaitState(LabState.Editing);
            _app.GoToTestSetup();
            yield return WaitState(LabState.ConfiguringTest);
            Check(_app.TestConfig.Environment == LabEnvironments.RainLab && Mathf.Approximately(_app.TestConfig.WindSpeed, 3f) && Mathf.Approximately(_app.TestConfig.WindDirectionDeg, 90f),
                $"rain after mobility: wind reset (speed={_app.TestConfig.WindSpeed:0.0} dir={_app.TestConfig.WindDirectionDeg:0})");
            _app.BackToEditing();
            yield return WaitState(LabState.Editing);

            // 記録したAI応答（部品5・機能5・パラメータ9）でも、全項目がパネル内に収まる
            string fixture = Path.Combine(Application.dataPath, "_ImpossibleLab/Tests/EditMode/Fixtures/ai_design_response.json");
            Check(File.Exists(fixture), "AI design fixture exists");
            if (File.Exists(fixture))
            {
                _app.SetDesignServiceForVerify(new FixtureDesignService(File.ReadAllText(fixture)));
                _app.NewInvention();
                _app.SetRequestText("雨の日に両手が空く道具");
                // 画面移動の演出中は送信を受け付けないため、受け付けられるまで送り直す
                float wait = 0;
                while (_app.Sm.State == LabState.Drafting && wait < 10f) { _app.SubmitRequest(); wait += Time.unscaledDeltaTime; yield return null; }
                yield return WaitState(LabState.AwaitingApproval);
                yield return Shot("S02_preview_ai");
                _app.ApprovePreview();
                yield return WaitState(LabState.Editing);
                yield return Shot("S03_edit_ai_p1");
                var ui = FindFirstObjectByType<LabUI>();
                ui.StepParamPage(+1);
                yield return null;
                Check(ui.ParamPage == 1, "parameter list moves to page 2");
                yield return Shot("S03_edit_ai_p2");
                ui.StepParamPage(+1);
                yield return null;
                yield return Shot("S03_edit_ai_p3");
            }

            // 6週目 S02・S04：AI v2 の設計（記録した応答 r03：要望5件・実験計画3案）。機能対応表のページ送り → AI 案を選んで実行
            string fixtureV2 = Path.Combine(Application.dataPath, "_ImpossibleLab/Tests/EditMode/Fixtures/ai_design_v2_r03.json");
            Check(File.Exists(fixtureV2), "AI v2 design fixture exists");
            if (File.Exists(fixtureV2))
            {
                _app.SetDesignServiceForVerify(new FixtureDesignService(File.ReadAllText(fixtureV2)));
                _app.NewInvention();
                _app.SetRequestText("暑い日の通学で日陰がほしい");
                float wait2 = 0;
                while (_app.Sm.State == LabState.Drafting && wait2 < 10f) { _app.SubmitRequest(); wait2 += Time.unscaledDeltaTime; yield return null; }
                yield return WaitState(LabState.AwaitingApproval);
                var mapRows = _app.FunctionRows(_app.Preview, out bool mapDerived);
                Check(mapRows.Count == 5 && !mapDerived && mapRows.Any(r => !r.Verifiable) && mapRows.Where(r => r.Verifiable).All(r => r.PlanTitle != null),
                    $"S02: AI function map ({mapRows.Count} rows, derived={mapDerived}, plans={string.Join(",", mapRows.Select(r => r.PlanTitle ?? "-"))})");
                yield return Shot("S02_function_map_ai_p1");
                var ui2 = FindFirstObjectByType<LabUI>();
                ui2.StepNeedPage(+1);
                yield return null;
                Check(ui2.NeedPage == 1, "S02: function map moves to page 2");
                yield return Shot("S02_function_map_ai_p2");
                _app.ApprovePreview();
                yield return WaitState(LabState.Editing);
                _app.GoToTestSetup();
                yield return WaitState(LabState.ConfiguringTest);
                Check(_app.PlanOptions.Count(c => c.FromAi) == 3 && _app.SelectedPlan.FromAi, $"S04: AI plans listed first ({_app.PlanOptions.Count(c => c.FromAi)} AI / {_app.PlanOptions.Count})");
                yield return Shot("S04_plan_ai");
                _app.SelectPlan(1);
                yield return null;
                Check(_app.PlanIndex == 1 && _app.SelectedPlan.FromAi, "S04: second AI plan selected");
                float tw2 = 0; while (_app.Transitioning && tw2 < 10f) { tw2 += Time.unscaledDeltaTime; yield return null; }
                _app.StartExperiment();
                yield return WaitState(LabState.RunningTest);
                yield return WaitState(LabState.ReviewingResult, 60f);
                var ra = _app.LastResult;
                Check(ra != null && ra.IsScenarioLab && ra.Metrics.Count > 0, $"S05: AI plan ran ({ra?.Criteria})");
                Log($"RESULT plan-ai passed={ra?.Passed} {string.Join(" ", ra?.Metrics.Select(m => m.Key + "=" + m.Display) ?? new string[0])} | {ra?.Criteria}");
                yield return Shot("S05_result_plan_ai", rainLab: true);
                _app.BackToEditing();
                yield return WaitState(LabState.Editing);
            }

            // 6週目 S04：組み立て式の実験（機能ブロック）を実験計画で選び・編集して実行する
            yield return PlanLabFlow("light", @"{ ""schemaVersion"": 2, ""id"": ""verify-night-light"", ""name"": ""夜道の足元ライト"", ""purpose"": ""夜道で足元を照らし、周りに知らせる"", ""tags"": [""lighting""], ""mount"": ""back"",
  ""parts"": [ { ""id"": ""body"", ""type"": ""body"", ""position"": [0, 0, 0], ""scale"": [0.12, 0.08, 0.04] }, { ""id"": ""lamp"", ""type"": ""light"", ""position"": [0, 0.06, 0.03], ""scale"": [0.04, 0.04, 0.04] } ],
  ""behaviors"": [ { ""type"": ""illuminate"", ""targetPart"": ""lamp"", ""parameters"": { ""lumens"": 150, ""beamAngleDeg"": 60, ""tiltDeg"": 50 } },
                   { ""type"": ""alert"", ""targetPart"": ""lamp"", ""parameters"": { ""loudnessDb"": 0, ""lightLumens"": 30, ""flashHz"": 2 } } ], ""revision"": 1, ""parentRevision"": null }",
                ExperimentCatalog.Stand, "footIlluminance", addModule: "sunlight", stepModule: "darkness", stepKey: "lux");
            yield return PlanLabFlow("heater", @"{ ""schemaVersion"": 2, ""id"": ""verify-heater"", ""name"": ""背中ヒーター"", ""purpose"": ""寒い場所で座って過ごすときに背中を温める"", ""tags"": [""thermal""], ""mount"": ""back"",
  ""parts"": [ { ""id"": ""body"", ""type"": ""body"", ""position"": [0, 0, 0], ""scale"": [0.25, 0.3, 0.03] }, { ""id"": ""pad"", ""type"": ""heater"", ""position"": [0, 0, 0.02], ""scale"": [0.2, 0.25, 0.01] } ],
  ""behaviors"": [ { ""type"": ""heat"", ""targetPart"": ""pad"", ""parameters"": { ""powerW"": 10 } } ], ""revision"": 1, ""parentRevision"": null }",
                ExperimentCatalog.SitStill, "timeToTarget", addModule: null, stepModule: "temperature", stepKey: "ambientC");
            yield return PlanLabFlow("lifter", @"{ ""schemaVersion"": 2, ""id"": ""verify-lifter"", ""name"": ""棚上げアシスト"", ""purpose"": ""重い荷物を棚の高さまで持ち上げる"", ""tags"": [""daily""], ""mount"": ""ground"",
  ""parts"": [ { ""id"": ""body"", ""type"": ""body"", ""position"": [0, 0, 0], ""scale"": [0.4, 0.2, 0.4] }, { ""id"": ""arm"", ""type"": ""arm"", ""position"": [0, 0.3, 0], ""scale"": [0.05, 0.6, 0.05] } ],
  ""behaviors"": [ { ""type"": ""lift_load"", ""targetPart"": ""arm"", ""parameters"": { ""maxLoadKg"": 10, ""liftHeightM"": 1.2, ""speed"": 0.3 } } ], ""revision"": 1, ""parentRevision"": null }",
                ExperimentCatalog.LiftTask, "liftTime", addModule: null, stepModule: "shelf", stepKey: "heightM");
            yield return PlanLabFlow("cart", @"{ ""schemaVersion"": 2, ""id"": ""verify-ramp-cart"", ""name"": ""スロープ運搬カート"", ""purpose"": ""荷物を載せて車いす用スロープを上る"", ""tags"": [""carrier""], ""mount"": ""ground"",
  ""parts"": [ { ""id"": ""body"", ""type"": ""container"", ""position"": [0, 0.25, 0], ""scale"": [0.5, 0.25, 0.4] }, { ""id"": ""w1"", ""type"": ""wheel"", ""position"": [0.2, 0.08, 0.2], ""scale"": [0.16, 0.05, 0.16] },
             { ""id"": ""w2"", ""type"": ""wheel"", ""position"": [-0.2, 0.08, 0.2], ""scale"": [0.16, 0.05, 0.16] }, { ""id"": ""m"", ""type"": ""motor"", ""position"": [0, 0.1, 0], ""scale"": [0.08, 0.08, 0.08] } ],
  ""behaviors"": [ { ""type"": ""drive"", ""targetPart"": ""m"", ""parameters"": { ""speedLimit"": 0.8, ""maxSlopeDeg"": 8 } },
                   { ""type"": ""carry_load"", ""targetPart"": ""body"", ""parameters"": { ""capacity"": 10 } } ], ""revision"": 1, ""parentRevision"": null }",
                ExperimentCatalog.ClimbSlope, "climbedRatio", addModule: null, stepModule: "slope", stepKey: "angleDeg");

            // 6週目の完了条件：代表6件（評価セットの AI 設計：防護・照明・温度・検知・持ち上げ・落下）で、設計 → 実験 → 改良 → 再実験 → 比較 → 保存
            int repPassed = 0;
            foreach (var id in new[] { "r03", "r15", "r18", "r22", "r09", "r05" })
            {
                bool ok = false;
                yield return RepresentativeFlow(id, v => ok = v);
                if (ok) repPassed++;
            }
            Check(repPassed == 6, $"representative: {repPassed}/6 completed design -> test -> revise -> retest -> save");
            Time.timeScale = 1f;
        }

        /// <summary>
        /// 動作プログラムの試用：依頼文 → オフラインの見本（自動で開くゴミ箱）→ 起動 → 対象が近づくとフタが開き、離れると3秒後に閉じる。
        /// 対象はカメラの代わりの空の物体（ProgramRunner.TargetOverride）で動かす。
        /// </summary>
        IEnumerator CarryBagFlow()
        {
            string path = Path.Combine(Application.dataPath, "_ImpossibleLab/Tests/EditMode/Fixtures/ai_carry_bag.json");
            if (!File.Exists(path)) { Check(false, "carry bag: fixture exists"); yield break; }
            var prevDesign = _app.Design;
            _app.SetDesignServiceForVerify(new FixtureDesignService(File.ReadAllText(path)));
            if (_app.Sm.State == LabState.Saved) _app.ContinueEditing();
            _app.NewInvention();
            _app.SetRequestText("自動で後ろからついてくるキャリーバッグ");
            float w0 = 0;
            while (_app.Sm.State == LabState.Drafting && w0 < 10f) { _app.SubmitRequest(); w0 += Time.unscaledDeltaTime; yield return null; }
            yield return WaitState(LabState.AwaitingApproval);
            _app.ApprovePreview();
            yield return WaitState(LabState.Editing);
            Transform handle = null;
            bool found = _app.View != null && _app.View.Parts.TryGetValue("handle", out handle);
            Check(found && handle.childCount == 4 && handle.GetComponent<Renderer>() == null, "carry bag: thin frame (handle) drawn as 4 bars");
            yield return Shot("S03_device_carry_bag", device: true);
            _app.TurnDisplay(150f); // 斜め後ろから持ち手を見る
            yield return null;
            yield return Shot("S03_device_carry_bag_back", device: true);
            _app.TurnDisplay(-150f);
            _app.GoToTestSetup();
            yield return WaitState(LabState.ConfiguringTest);
            float tw = 0; while (_app.Transitioning && tw < 10f) { tw += Time.unscaledDeltaTime; yield return null; }
            _app.StartExperiment();
            yield return WaitState(LabState.RunningTest);
            yield return new WaitForSeconds(1.5f);
            float minY = float.MaxValue;
            if (_app.View != null)
                foreach (var r in _app.View.GetComponentsInChildren<Renderer>()) if (r.enabled) minY = Mathf.Min(minY, r.bounds.min.y);
            float floorY = _app.Course != null ? _app.Course.transform.position.y : 0f;
            Check(minY >= floorY - 0.02f, $"carry bag: nothing sinks below the course floor (lowest {minY - floorY:0.000} m)");
            yield return Shot("S04_carry_bag_running", rainLab: true);
            if (_app.View != null)
            {
                var at = _app.View.transform.position;
                yield return Shot("S04_carry_bag_floor", eyeAt: at + new Vector3(0.9f, 0.5f, -0.9f), lookAt: at + Vector3.up * 0.15f);
            }
            yield return WaitState(LabState.ReviewingResult, 90f);
            _app.SetDesignServiceForVerify(prevDesign);
            _app.Save();
            yield return WaitState(LabState.Saved, 20f);
        }

        IEnumerator MotionBinFlow()
        {
            _app.NewInvention();
            _app.SetRequestText("近づくと自動で開くゴミ箱が欲しい");
            float w0 = 0;
            while (_app.Sm.State == LabState.Drafting && w0 < 10f) { _app.SubmitRequest(); w0 += Time.unscaledDeltaTime; yield return null; }
            yield return WaitState(LabState.AwaitingApproval);
            Check(_app.Preview != null && _app.Preview.Program != null, "motion: offline design has a motion program");
            yield return Shot("S02_preview_motion_bin");
            _app.ApprovePreview();
            yield return WaitState(LabState.Editing);
            // S03：動きの手順（オン・オフ、待つ時間の変更、元に戻す、動かしてみる）
            var gui = FindFirstObjectByType<LabUI>();
            gui.ProgramTab = true;
            yield return null;
            yield return Shot("S03_program_motion_bin");
            int rev0 = _app.Current.Current.Revision;
            _app.ToggleRule("open_lid");
            yield return null;
            Check(_app.Current.Current.Revision == rev0 + 1 && !_app.Current.Current.Program.Rules[0].Enabled, "motion: rule off is recorded as a revision");
            _app.Undo();
            _app.StepRule("open_lid", "seconds", 2, +1);
            yield return null;
            Check(Mathf.Approximately(_app.Current.Current.Program.Rules[0].Do[2].Seconds, 3.5f) && _app.Current.Current.Revision == rev0 + 1, "motion: wait time steps by 0.5 s as a revision");
            _app.Undo();
            yield return null;
            Check(_app.Current.Current.Revision == rev0 && _app.Current.Current.Program.Rules[0].Enabled, "motion: undo restores the rule");
            _app.PreviewRule("open_lid");
            yield return new WaitForSeconds(1.2f);
            Check(_app.View.Joints["lid"].Value > 80f, $"motion: preview moves the lid ({_app.View.Joints["lid"].Value:0} deg)");
            yield return Shot("S03_program_preview", device: true);
            yield return new WaitForSeconds(4.5f);
            gui.ProgramTab = false;
            var runner = _app.View != null ? _app.View.Get<ProgramRunner>() : null;
            Check(runner != null && _app.View.Joints.ContainsKey("lid"), "motion: lid joint and program runner are built");
            if (runner == null || !_app.View.Joints.ContainsKey("lid")) yield break;
            var target = new GameObject("VerifyTarget").transform;
            runner.TargetOverride = target;
            var p = _app.View.transform.position;
            target.position = p + new Vector3(0, 0, 2.5f);
            _app.ToggleDevice();
            yield return new WaitForSeconds(0.5f);
            Check(_app.View.Joints["lid"].Value < 1f, "motion: lid stays closed while nobody is near");
            target.position = p + new Vector3(0, 0, 0.6f);
            yield return new WaitForSeconds(1.5f);
            float open = _app.View.Joints["lid"].Value;
            Check(open > 80f, $"motion: lid opens when approached ({open:0} deg)");
            Check(_app.Toast != null && _app.Toast.Contains("フタを開ける"), "motion: rule firing is shown as a notice");
            yield return Shot("S03_device_motion_bin_open", device: true);
            target.position = p + new Vector3(0, 0, 2.5f);
            yield return new WaitForSeconds(4.5f);
            float closed = _app.View.Joints["lid"].Value;
            Check(closed < 5f, $"motion: lid closes after the wait ({closed:0} deg)");
            _app.ToggleDevice();
            Destroy(target.gameObject);

            // 実験：近づいて離れる（AT-13）
            _app.GoToTestSetup();
            yield return WaitState(LabState.ConfiguringTest);
            Check(_app.EditPlan != null && _app.EditPlan.Scenario == ExperimentCatalog.ApproachDevice, $"motion: first plan is approach_device ({_app.EditPlan?.Scenario})");
            yield return Shot("S04_motion_bin");
            float tw = 0; while (_app.Transitioning && tw < 10f) { tw += Time.unscaledDeltaTime; yield return null; }
            _app.StartExperiment();
            yield return WaitState(LabState.RunningTest);
            yield return new WaitForSeconds(3.2f);
            yield return Shot("S05_running_motion_bin", rainLab: true);
            yield return WaitState(LabState.ReviewingResult, 90f);
            var res = _app.LastResult;
            Check(res != null && res.Metric("ruleFiredCount") != null && res.Metric("reactionTime") != null && res.Metric("expectationRate") != null,
                $"motion: experiment measured firing, reaction time and expectation ({res?.Criteria})");
            Check(res != null && res.Passed == true, $"motion: experiment passed ({string.Join(" / ", res?.Limits ?? new System.Collections.Generic.List<string>())})");
            Log($"RESULT motion-bin fired={res?.Metric("ruleFiredCount")?.Value} reaction={res?.Metric("reactionTime")?.Value:0.00} expectation={res?.Metric("expectationRate")?.Value:0.00}");
            yield return Shot("S05_result_motion_bin", rainLab: true);
            _app.Save();
            yield return WaitState(LabState.Saved, 20f); // 次の検証は保存完了の画面から新しい発明を始める
        }

        /// <summary>
        /// 代表1件：記録した AI 設計 → 承認 → S04（AI 案の1つ目）→ 実験 → 改善案の依頼文で改良（オフライン改良ルール）→ 再実験
        /// → 前の版と比較（判定に使った計測項目が良くなる。合格済みなら悪くならない）→ 保存して読み直す。
        /// </summary>
        IEnumerator RepresentativeFlow(string id, System.Action<bool> done)
        {
            string path = Path.Combine(Application.dataPath, $"_ImpossibleLab/Tests/EditMode/Fixtures/representative/ai_{id}.json");
            if (!File.Exists(path)) { Check(false, $"rep-{id}: fixture exists"); done(false); yield break; }
            _app.SetDesignServiceForVerify(new FixtureDesignService(File.ReadAllText(path)));
            if (_app.Sm.State == LabState.Saved) _app.ContinueEditing();
            _app.NewInvention();
            _app.SetRequestText("代表 " + id);
            float w = 0;
            while (_app.Sm.State == LabState.Drafting && w < 10f) { _app.SubmitRequest(); w += Time.unscaledDeltaTime; yield return null; }
            yield return WaitState(LabState.AwaitingApproval);
            if (_app.Preview == null) { Check(false, $"rep-{id}: design"); done(false); yield break; }
            _app.ApprovePreview();
            yield return WaitState(LabState.Editing);
            var dna = _app.Current.Current;
            yield return Shot($"S03_device_rep_{id}", device: true);
            _app.TestConfig.Mode = LabModes.Dream;
            _app.GoToTestSetup();
            yield return WaitState(LabState.ConfiguringTest);
            Check(_app.SelectedPlan != null && _app.SelectedPlan.FromAi, $"rep-{id}: AI plan selected ({_app.SelectedPlan?.Title})");
            string planTitle = _app.SelectedPlan?.Title;
            float tw = 0; while (_app.Transitioning && tw < 10f) { tw += Time.unscaledDeltaTime; yield return null; }
            _app.StartExperiment();
            yield return WaitState(LabState.RunningTest);
            yield return WaitState(LabState.ReviewingResult, 90f);
            var r1 = _app.LastResult;
            var checks1 = ResultCriteria.Evaluate(r1);
            // 依頼文がある計測項目のうち、未達のものを優先
            var target = checks1.Where(c => c.Measured).OrderBy(c => c.Passed ? 1 : 0).FirstOrDefault(c => OfflineRevisionRules.RequestForMetric(dna, c.Metric) != null);
            string req = target != null ? OfflineRevisionRules.RequestForMetric(dna, target.Metric) : null;
            req = req ?? OfflineRevisionRules.SuggestedRequestsFor(dna).First();
            yield return Shot($"S05_rep_{id}_rev1", rainLab: true);
            _app.ImproveAndRetest();
            yield return WaitState(LabState.Editing);
            _app.PickRevision(req);
            _app.RequestRevision();
            float tr = 0; while (_app.PendingRevision == null && _app.RevisionBusy && tr < 5f) { tr += Time.unscaledDeltaTime; yield return null; }
            tr = 0; while (_app.PendingRevision == null && tr < 2f) { tr += Time.unscaledDeltaTime; yield return null; }
            bool revised = _app.PendingRevision != null && _app.PendingRevision.Success;
            Check(revised, $"rep-{id}: revision proposal for 「{req}」 ({_app.RevisionMessage})");
            if (!revised) { done(false); yield break; }
            _app.ApproveRevision();
            _app.GoToTestSetup();
            yield return WaitState(LabState.ConfiguringTest);
            Check(_app.SelectedPlan?.Title == planTitle, $"rep-{id}: same plan after revision ({_app.SelectedPlan?.Title})");
            tw = 0; while (_app.Transitioning && tw < 10f) { tw += Time.unscaledDeltaTime; yield return null; }
            _app.StartExperiment();
            yield return WaitState(LabState.RunningTest);
            yield return WaitState(LabState.ReviewingResult, 90f);
            var r2 = _app.LastResult;
            Check(_app.BaselineResult == r1, $"rep-{id}: compared with Rev.{r1?.Revision}");
            bool better = false;
            string change = "-";
            if (target != null && r1 != null && r2 != null && r1.Metric(target.Metric) != null && r2.Metric(target.Metric) != null)
            {
                var info = ExperimentCatalog.Metric(target.Metric);
                float a = r1.Metric(target.Metric).Value, b = r2.Metric(target.Metric).Value;
                bool improved = info.LowerIsBetter ? b < a - 1e-4f : b > a + 1e-4f;
                bool worse = info.LowerIsBetter ? b > a + 1e-4f : b < a - 1e-4f;
                better = target.Passed ? !worse : improved;
                change = $"{info.LabelJa} {a:0.###} -> {b:0.###}";
            }
            Check(better, $"rep-{id}: {change} after 「{req}」");
            Log($"RESULT rep-{id} plan={planTitle} rev1 passed={r1?.Passed} rev2 passed={r2?.Passed} | {change} | request={req}");
            yield return Shot($"S05_rep_{id}_rev2", rainLab: true);
            _app.Save();
            yield return WaitState(LabState.Saved, 20f);
            var saved = _app.Repo.Load(dna.Id, out var err);
            bool savedOk = saved != null && saved.Experiments.Count(e => e.IsScenarioLab) >= 2 && saved.Current.Revision == r2?.Revision;
            Check(savedOk, $"rep-{id}: saved Rev.{saved?.Current.Revision} with {saved?.Experiments.Count} experiments {err}");
            _app.ContinueEditing();
            yield return WaitState(LabState.Editing);
            done(revised && better && savedOk);
        }

        /// <summary>
        /// 組み立て式の実験の通し：保存した発明を読み込み → S04 で標準の計画が選ばれる → 条件の追加・値の変更 → 実行 → 結果（ScenarioLab）
        /// → REALITY で比較 → 保存して読み直す（計画も復元される）。
        /// </summary>
        IEnumerator PlanLabFlow(string tag, string dnaJson, string scenario, string metric, string addModule, string stepModule, string stepKey)
        {
            var rep = DnaJson.Parse(dnaJson);
            Check(rep.IsValid, $"plan-{tag}: verify DNA is valid {rep.Summary}");
            if (!rep.IsValid) yield break;
            _app.Repo.Save(new SavedInvention { Current = rep.Dna });
            _app.OpenGallery();
            yield return WaitState(LabState.Gallery);
            _app.LoadFromGallery(rep.Dna.Id);
            yield return WaitState(LabState.Editing);
            _app.TestConfig.Mode = LabModes.Dream;
            _app.GoToTestSetup();
            yield return WaitState(LabState.ConfiguringTest);
            // 先頭が別の候補（例：運搬の分類なら障害物コース）のときは、目的のシナリオの候補を選ぶ
            int want = _app.PlanOptions.FindIndex(c => c.Plan.Scenario == scenario);
            if (want > 0) { _app.SelectPlan(want); yield return null; }
            Check(_app.PlanOptions.Count > 0 && !_app.PlanIsLegacy && _app.ActivePlan()?.Scenario == scenario,
                $"plan-{tag}: standard plan selected ({_app.PlanOptions.Count} options, scenario={_app.ActivePlan()?.Scenario})");
            yield return Shot($"S04_plan_{tag}");
            if (addModule != null)
            {
                _app.AddPlanModule(addModule);
                yield return null;
                Check(_app.EditPlan.Has(addModule), $"plan-{tag}: module {addModule} added");
            }
            float v0 = _app.EditPlan.Get(stepModule, stepKey);
            _app.StepPlanParam(stepModule, stepKey, +1);
            yield return null;
            float v1 = _app.EditPlan.Get(stepModule, stepKey);
            Check(v1 > v0, $"plan-{tag}: {stepModule}.{stepKey} {v0} -> {v1}");
            yield return Shot($"S04_plan_{tag}_edited");
            string key = PlanCandidates.ConditionKey(_app.ActivePlan());
            // 前の流れの画面移動（フェード）が終わるまで待つ（その間の「実験開始」は受け付けない仕様）
            float tw = 0; while (_app.Transitioning && tw < 10f) { tw += Time.unscaledDeltaTime; yield return null; }
            _app.StartExperiment();
            yield return WaitState(LabState.RunningTest);
            yield return new WaitForSeconds(2f);
            Check(!FindObjectsByType<TMPro.TMP_Text>(FindObjectsSortMode.None).Any(t => t.text.StartsWith("実験中 — RainLab", StringComparison.Ordinal) || t.text.Contains("処理した雨粒")),
                $"plan-{tag}: running screen shows the plan, not the rain lab");
            yield return Shot($"S05_running_plan_{tag}", rainLab: true);
            yield return WaitState(LabState.ReviewingResult, 60f);
            var r = _app.LastResult;
            Check(r != null && r.IsScenarioLab && r.Scenario == scenario && r.Metric(metric) != null && r.Passed.HasValue,
                $"plan-{tag}: result has {metric} (passed={r?.Passed})");
            Check(r != null && PlanCandidates.ConditionKey(r.Plan) == key, $"plan-{tag}: result keeps the edited plan");
            Log($"RESULT plan-{tag} {r?.Plan?.Mode} passed={r?.Passed} {string.Join(" ", r?.Metrics.Select(m => m.Key + "=" + m.Display) ?? new string[0])} | {r?.Criteria}");
            yield return Shot($"S05_result_plan_{tag}", rainLab: true);
            var texts = FindObjectsByType<TMPro.TMP_Text>(FindObjectsSortMode.None).Select(t => t.text).ToList();
            Check(texts.Any(t => t.StartsWith("合格条件ごとの判定", StringComparison.Ordinal)) && texts.Any(t => t == "✓ 達成" || t == "× 未達")
                && texts.Any(t => t.StartsWith("✓ この実験で確かめた", StringComparison.Ordinal)), $"plan-{tag}: S05 shows per-criterion verdicts and request coverage");
            _app.RerunOtherMode();
            yield return WaitState(LabState.ReviewingResult, 60f);
            var r2 = _app.LastResult;
            Check(r2 != null && r2 != r && r2.Plan.Mode == LabModes.Reality && _app.OtherModeResult == r, $"plan-{tag}: REALITY run compared with DREAM");
            yield return Shot($"S05_result_plan_{tag}_reality", rainLab: true);
            _app.TestConfig.Mode = LabModes.Dream;
            _app.Save();
            yield return WaitState(LabState.Saved, 20f);
            var saved = _app.Repo.Load(rep.Dna.Id, out var err);
            Check(saved != null && saved.Experiments.Count == 2 && saved.Experiments.All(e => e.IsScenarioLab && e.Plan != null && PlanCandidates.ConditionKey(e.Plan) == key),
                $"plan-{tag}: saved with plans {err}");
            // 同じ発明へ戻ったときは、選んだ計画と編集した値が残る
            _app.ContinueEditing();
            yield return WaitState(LabState.Editing);
            _app.GoToTestSetup();
            yield return WaitState(LabState.ConfiguringTest);
            Check(PlanCandidates.ConditionKey(_app.ActivePlan()) == key, $"plan-{tag}: edits kept when returning to S04");
            _app.BackToEditing();
            yield return WaitState(LabState.Editing);
        }

        /// <summary>記録したプロキシ応答をそのまま返す設計元（通信しない）。</summary>
        sealed class FixtureDesignService : IInventionDesignService
        {
            readonly string _json;
            public FixtureDesignService(string json) { _json = json; }
            public string DisplayName => "AI（記録した応答・検証用）";
            public System.Threading.Tasks.Task<DesignOutcome> GenerateAsync(string requestText, IProgress<DesignStage> progress, System.Threading.CancellationToken ct)
            {
                progress?.Report(DesignStage.IntentAnalysis);
                progress?.Report(DesignStage.FeatureSelection);
                progress?.Report(DesignStage.PartDesign);
                var o = ProxyProtocol.ParseDesign(200, _json, null);
                if (o.Status == DesignStatus.Success) progress?.Report(DesignStage.Assembly);
                return System.Threading.Tasks.Task.FromResult(o);
            }
            /// <summary>改良はオフラインの改良ルールで返す（記録した設計応答には改良が含まれないため。AI は呼ばない）。</summary>
            public System.Threading.Tasks.Task<RevisionOutcome> ReviseAsync(InventionDna current, RainExperimentResult lastExperiment, string requestText, System.Threading.CancellationToken ct)
            {
                var proposal = OfflineRevisionRules.Propose(current, requestText ?? "");
                return System.Threading.Tasks.Task.FromResult(proposal.Operations.Count == 0
                    ? new RevisionOutcome { Status = DesignStatus.Unsupported, Proposal = proposal, Message = "オフライン改良ルールでは対応できません" }
                    : new RevisionOutcome { Status = DesignStatus.Success, Proposal = proposal, Message = proposal.Summary });
            }
        }
    }
}
