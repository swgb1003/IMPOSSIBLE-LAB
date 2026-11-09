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

        IEnumerator Shot(string name, bool rainLab = false, bool overview = false)
        {
            yield return null; // batchmode では WaitForEndOfFrame が呼ばれないため
            Vector3 basePos = rainLab ? _app.RainLab.transform.position : Vector3.zero;
            if (overview) _cam.transform.SetPositionAndRotation(new Vector3(5.5f, 3.4f, -6.8f), Quaternion.LookRotation(new Vector3(0, 1.2f, 0) - new Vector3(5.5f, 3.4f, -6.8f)));
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

            // 試用：起動して追従（Editorではカメラが頭）
            _app.ToggleDevice();
            Check(_app.DeviceRunning, "device activates");
            _app.ToggleDevice();

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
            _app.AskDelete(_app.Current.Current.Id);
            yield return null; yield return null;
            yield return Shot("S06_delete_confirm");
            _app.CancelDelete();

            // 再起動相当：新しいリポジトリから読み込み、同じDNAか
            var repo = new InventionRepository(Application.persistentDataPath);
            var loaded = repo.Load(_app.Current.Current.Id, out var err);
            Check(loaded != null && DnaJson.ToJson(loaded.Current) == DnaJson.ToJson(_app.Current.Current), "saved DNA restores identically " + err);
            Check(loaded != null && loaded.Experiments.Count >= 3, "experiment records persisted");
            _app.LoadFromGallery(_app.Current.Current.Id);
            yield return WaitState(LabState.Editing);
            yield return Shot("S03_loaded_from_gallery");

            // 手元メニュー：表示・収まり・効果音の切り替え
            var hm = _app.GetComponent<HandMenu>();
            Check(hm != null && hm.Canvas != null, "hand menu created (Left Controller found)");
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
                if (cat == "carrier")
                {
                    int rev = _app.Current.Current.Revision;
                    _app.StepAppearance(+1);
                    yield return null;
                    Check(_app.Current.Current.Appearance == "safety_orange" && _app.Current.Current.Revision == rev, "appearance preset changes without a new revision");
                    yield return Shot("S03_appearance_carrier");
                }
                _app.ToggleDevice();
                yield return new WaitForSeconds(1.5f);
                Check(_app.DeviceRunning, $"{cat}: device activates in workshop");
                yield return Shot($"S03_trial_{cat}");
                _app.ToggleDevice();
                _app.GoToTestSetup();
                yield return WaitState(LabState.ConfiguringTest);
                Check(_app.TestConfig.Environment == LabEnvironments.ForCategory(cat), $"{cat}: environment = {_app.TestConfig.Environment}");
                if (cat == "mobility") { _app.TestConfig.WindDirectionDeg = 0f; _app.TestConfig.WindSpeed = 4f; }
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
            Time.timeScale = 1f;
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
            public System.Threading.Tasks.Task<RevisionOutcome> ReviseAsync(InventionDna current, RainExperimentResult lastExperiment, string requestText, System.Threading.CancellationToken ct)
                => System.Threading.Tasks.Task.FromResult(new RevisionOutcome { Status = DesignStatus.Failed, Message = "検証用の設計元は改良に対応しません" });
        }
    }
}
