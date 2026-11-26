using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using ImpossibleLab.Core;
using Newtonsoft.Json.Linq;
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
        /// <summary>見比べ用：このフォルダの AI の応答（*.json）を1件ずつ設計させ、製品の説明と試作品を撮るだけ（通しの検証はしない）。</summary>
        public string ShowcaseDir;
        LabApp _app;
        Camera _cam;
        RenderTexture _rt;
        readonly StringBuilder _log = new StringBuilder();
        int _shot;
        /// <summary>直前に撮った画面写真のファイル（見本の写真を切り出すため）。</summary>
        string _lastShot;
        string _rainRunningShot, _rainResultShot, _rainRealityShot;

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
            IEnumerator run = string.IsNullOrEmpty(ShowcaseDir) ? Run() : Showcase(ShowcaseDir);
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
        /// <summary>発明ライブラリの PC の代わり：公開を受け取り、自分の公開に採用を1件付けて返す。</summary>
        sealed class FakeLibrary : ILibraryTransport
        {
            public string InventionId, Name, LastBody;
            public readonly string Id = "p-verify000001", AdoptionId = "a-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            public bool Withdrawn;
            public System.Threading.Tasks.Task<(long code, string body, string transportError)> SendAsync(string method, string path, string json, int timeoutSec)
            {
                (long, string, string) r;
                if (method == "POST" && path == "/library")
                {
                    LastBody = json;
                    r = (201, new JObject { ["status"] = "ok", ["id"] = Id, ["created"] = true }.ToString(Newtonsoft.Json.Formatting.None), null);
                }
                else if (method == "GET" && path == "/library/mine") r = (200, Mine(), null);
                else if (method == "POST" && path == $"/library/{Id}/withdraw") { Withdrawn = true; r = (200, new JObject { ["id"] = Id, ["withdrawn"] = true }.ToString(), null); }
                else if (method == "POST" && path == "/library/pair") r = (200, new JObject { ["code"] = "042917", ["expiresAt"] = DateTime.UtcNow.AddMinutes(10).ToString("o") }.ToString(), null);
                else r = (404, new JObject { ["status"] = "error", ["message"] = "not found" }.ToString(), null);
                return System.Threading.Tasks.Task.FromResult(r);
            }

            string Mine()
            {
                if (LastBody == null) return new JObject { ["items"] = new JArray() }.ToString();
                var adopter = new JObject { ["id"] = "u-verify", ["name"] = "高瀬 真央", ["affiliation"] = "北辰工科大学 人間支援ロボティクス研究室", ["field"] = "", ["intro"] = "", ["verified"] = true };
                var adoption = new JObject
                {
                    ["id"] = AdoptionId, ["at"] = "2026-11-26T01:00:00Z", ["reason"] = "体に付ける支援機器の研究で、頭の動きに合わせて追従する制御の題材に使いたい。",
                    ["adopter"] = adopter, ["threadId"] = "t-verify", ["messageCount"] = 0, ["lastFrom"] = null,
                };
                var item = new JObject
                {
                    ["id"] = Id, ["inventionId"] = InventionId, ["revision"] = 3, ["name"] = Name, ["status"] = Withdrawn ? "withdrawn" : "adopted",
                    ["withdrawnBy"] = Withdrawn ? "creator" : null, ["wants"] = new JObject { ["vr"] = 2, ["site"] = 5 }, ["tried"] = 4, ["considering"] = 1,
                    ["adoptions"] = new JArray(adoption),
                };
                return new JObject { ["items"] = new JArray(item) }.ToString(Newtonsoft.Json.Formatting.None);
            }
        }

        /// <summary>保存の画面から公開：確認（表示名）→ 同意 → 送信 → 公開の状態 → 公開をやめる確認。</summary>
        IEnumerator PublishFlow(FakeLibrary library)
        {
            _app.SetLibraryTransportForVerify(library);
            _app.VerifyHoldsPc = true;
            _app.ProxyOnline = true;
            _app.Notify();
            yield return null;
            Check(_app.PublishProblemNow == null, "publish: allowed after an experiment");
            yield return Shot("S06_library_face", facePanel: "MainConsole");
            _app.StartPublish();
            yield return null;
            Check(_app.Publish == PublishStep.Confirm, "publish: confirm step");
            _app.SetPublishAnonymous(false);
            _app.SetPublishName("");
            _app.PublishToConsent();
            Check(_app.Publish == PublishStep.Confirm, "publish: a display name is needed when not anonymous");
            _app.SetPublishName("あおい");
            _app.Notify();
            yield return null;
            yield return Shot("S06_publish_confirm_face", facePanel: "MainConsole");
            _app.PublishToConsent();
            yield return null;
            Check(_app.Publish == PublishStep.Consent, "publish: consent step");
            yield return Shot("S06_publish_consent_face", facePanel: "MainConsole");
            _app.AgreeAndPublish();
            float t0 = Time.realtimeSinceStartup;
            while ((_app.Publish != PublishStep.None || _app.MineFor(library.InventionId) == null) && Time.realtimeSinceStartup - t0 < 20f) yield return null;
            Check(library.LastBody != null && _app.PublishError == null, "publish: sent " + _app.PublishError);
            if (library.LastBody != null)
            {
                var body = JObject.Parse(library.LastBody);
                var pub = body["publication"];
                Check((int)pub["format"] == 1 && (string)pub["creator"]["displayName"] == "あおい" && !(bool)pub["creator"]["anonymous"] && (bool)pub["consent"]["agreed"],
                    "publish: publication carries the display name and the consent");
                var glb = Convert.FromBase64String((string)body["glbBase64"]);
                Check(GlbWriter.Inspect(glb, out var gErr) != null && gErr == null && glb.Length <= ProductExport.TargetBytes, $"publish: model {glb.Length / 1024} KB {gErr}");
                var images = (JArray)body["images"];
                byte[] jpg = images.Count == 1 ? Convert.FromBase64String((string)images[0]["jpgBase64"]) : null;
                Check(jpg != null && jpg.Length > 2000 && jpg[0] == 0xff && jpg[1] == 0xd8 && jpg.Length <= LibraryProtocol.MaxJpegBytes, $"publish: one product photo {(jpg?.Length ?? 0) / 1024} KB");
                // PC 側の形で確かめるために残す（proxy の tools/check-publish.ts）
                Directory.CreateDirectory(Path.Combine(OutputDir, "library"));
                File.WriteAllText(Path.Combine(OutputDir, "library-publish-body.json"), library.LastBody);
                if (jpg != null) File.WriteAllBytes(Path.Combine(OutputDir, "library-publish-photo.jpg"), jpg);
            }
            Check(_app.Library.Published.TryGetValue(library.InventionId, out var pid) && pid == library.Id, "publish: the device remembers the publication id");
            var mine = _app.MineFor(library.InventionId);
            Check(mine != null && mine.StatusJa == "採用" && mine.Adoptions.Count == 1, "publish: status read back from the PC");
            yield return Shot("S06_published_face", facePanel: "MainConsole");
            // サイトに入る番号：PC にもらって保存の画面に出す
            _app.RequestPairCode();
            t0 = Time.realtimeSinceStartup;
            while (_app.PairCodeNow == null && _app.PairError == null && Time.realtimeSinceStartup - t0 < 5f) yield return null;
            Check(_app.PairCodeNow == "042917", "pair: the save screen shows the code for the site " + _app.PairError);
            yield return Shot("S06_pair_face", facePanel: "MainConsole");
            _app.AskWithdraw();
            yield return null;
            Check(_app.Publish == PublishStep.ConfirmWithdraw, "publish: withdraw asks first");
            yield return Shot("S06_withdraw_confirm_face", facePanel: "MainConsole");
            _app.CancelPublish();
            yield return null;
            Check(_app.Publish == PublishStep.None && !library.Withdrawn, "publish: withdraw can be cancelled");
        }

        void Check(bool cond, string what) { Log((cond ? "PASS " : "FAIL ") + what); if (!cond) _failed = true; }
        void Log(string s) { _log.AppendLine(s); Debug.Log("[ILAB_VERIFY] " + s); }

        IEnumerator WaitState(LabState s, float timeout = 30f)
        {
            float t = 0;
            while (_app.Sm.State != s && t < timeout) { t += Time.unscaledDeltaTime; yield return null; }
            Check(_app.Sm.State == s, "state reached " + s + " (now " + _app.Sm.State + ")");
            yield return null; yield return null;
            // 実験場との行き来（暗転）の途中は操作を受け付けないので、終わるまで待つ（画面の作り直しが速いと、2フレームでは暗転が終わらない）
            float tw = 0; while (_app.Transitioning && tw < 10f) { tw += Time.unscaledDeltaTime; yield return null; }
        }

        IEnumerator Shot(string name, bool rainLab = false, bool overview = false, string facePanel = null, bool device = false, string devicePart = null, Vector3? eyeAt = null, Vector3? lookAt = null, bool productView = false)
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
                float d = Mathf.Max(0.35f, b.extents.magnitude * (devicePart != null ? 3.2f : productView ? 2.6f : 1.15f));
                var dir = (devicePart != null ? new Vector3(0.6f, -0.35f, 1f) : productView ? new Vector3(0.7f, 0.55f, 1f) : new Vector3(0.35f, -0.1f, 1f)).normalized;
                // productView：製品の写真のように斜め上の前から全体を撮る（上面の細部・塗り分けの確認用）
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
                // メインのコンソールは画面いっぱいに撮る（設計図 1600×900 と見比べるため）
                float dist = facePanel == "MainConsole" ? 0.78f : 1.15f;
                _cam.transform.SetPositionAndRotation(panel.position - panel.forward * dist, Quaternion.LookRotation(panel.forward, panel.up));
            }
            else if (overview) _cam.transform.SetPositionAndRotation(new Vector3(5.5f, 3.4f, -6.8f), Quaternion.LookRotation(new Vector3(0, 1.2f, 0) - new Vector3(5.5f, 3.4f, -6.8f)));
            else
            {
                var eye = basePos + new Vector3(0, 1.6f, rainLab ? -4.5f : -3.3f);
                var look = basePos + new Vector3(0, rainLab ? 1.05f : 1.0f, rainLab ? -1.6f : -0.9f);
                _cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            }
            // 計器の輪の中の試作品は 2 フレームに 1 回描くので、撮る前に今の状態で描かせる
            var preview = FindFirstObjectByType<ProductPreview>();
            if (preview != null) preview.RenderNow();
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
            _lastShot = file;
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
            yield return Shot("S01_invention_input_face", facePanel: "MainConsole");
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
            yield return Shot("S02_error_unsupported_face", facePanel: "MainConsole");
            _app.UseOfflineSample();
            yield return new WaitForSeconds(0.4f);
            yield return Shot("S02_generating");
            yield return Shot("S02_generating_face", facePanel: "MainConsole");
            yield return WaitState(LabState.AwaitingApproval);
            Check(_app.Preview != null && _app.Preview.Category == "protection", "preview DNA valid");
            // 設計ができたら、まず製品の説明のページ（概要・しくみ・部品の役割）
            var exUi = FindFirstObjectByType<LabUI>();
            Check(exUi != null && exUi.ExplainPage, "explain page shown first after design");
            yield return Shot("S02_explain");
            yield return Shot("S02_explain_face", facePanel: "MainConsole");
            if (exUi != null)
            {
                exUi.SelectExplainPart(1);
                yield return null;
                Check(exUi.ExplainPart == 1, "explain: select part");
                Check(exUi.Product != null && exUi.Product.Focus == 1, "explain: product preview zooms to the selected part");
                yield return Shot("S02_explain_part_face", facePanel: "MainConsole");
                exUi.ShowExplain(false);
                yield return null;
            }
            yield return Shot("S02_preview");
            yield return Shot("S02_preview_face", facePanel: "MainConsole");
            _app.ApprovePreview();
            yield return new WaitForSeconds(0.6f);
            yield return Shot("S02_building");
            yield return Shot("S02_building_face", facePanel: "MainConsole");
            yield return WaitState(LabState.Editing);
            yield return Shot("S03_edit");
            yield return Shot("S03_edit_face", facePanel: "MainConsole");
            // 輪の中は試作品の立体。部品を選ぶとその部品に寄り、「全体を見る」で戻る
            if (exUi != null)
            {
                Check(exUi.Product != null && exUi.Product.Shown && exUi.Product.Focus == -1, "edit: product preview shows the whole product");
                exUi.StepPartAndZoom(+1);
                yield return null;
                Check(exUi.ProductZoomed && exUi.Product.Focus == _app.SelectedPartIndex && _app.SelectedPartIndex == 1, "edit: selecting a part zooms to it");
                yield return Shot("S03_zoom_face", facePanel: "MainConsole");
                exUi.ShowWholeProduct();
                yield return null;
                Check(!exUi.ProductZoomed && exUi.Product.Focus == -1, "edit: back to the whole product");
                // 押したまま動かすと回り、そのあとは自動で回らない
                float yaw0 = exUi.Product.Yaw;
                exUi.Product.Orbit(new Vector2(40f, -20f));
                yield return null;
                Check(exUi.Product.Manual && Mathf.Abs(Mathf.DeltaAngle(exUi.Product.Yaw, yaw0 - 18f)) < 0.01f, "edit: dragging turns the product and stops the auto turn");
                yield return Shot("S03_turned_face", facePanel: "MainConsole");
                exUi.StepPartAndZoom(-1);
                yield return null;
                exUi.ShowWholeProduct();
                yield return null;
            }
            yield return Shot("S03_device_rain", device: true);
            yield return Shot("S03_product_rain", device: true, productView: true);
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
            yield return Shot("S04_test_setup_face", facePanel: "MainConsole");
            _app.StartExperiment();
            yield return WaitState(LabState.RunningTest);
            yield return new WaitForSeconds(4f);
            yield return Shot("S05_running", rainLab: true);
            _rainRunningShot = _lastShot;
            yield return Shot("S05_running_face", facePanel: "MainConsole");
            yield return WaitState(LabState.ReviewingResult);
            var r1 = _app.LastResult;
            Log($"RESULT rev{r1.Revision} {r1.Config.Mode} wetness={r1.WetnessScore:0.00} coverage={r1.CoverageRatio:0.000} hits={r1.DeviceHits}/{r1.BaselineHits}");
            yield return Shot("S05_result_rev1", rainLab: true);
            _rainResultShot = _lastShot;
            yield return Shot("S05_result_rev1_face", facePanel: "MainConsole");

            // 改良：横からの雨 → 範囲
            _app.ImproveAndRetest();
            yield return WaitState(LabState.Editing);
            _app.PickRevision("横からの雨も防いで");
            _app.RequestRevision();
            float t = 0; while (_app.PendingRevision == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Check(_app.PendingRevision != null && _app.PendingRevision.Success, "revision proposal (side rain)");
            yield return Shot("S03_revision_diff");
            yield return Shot("S03_revision_diff_face", facePanel: "MainConsole");
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
            // 承認したあとも、編集の画面から製品の説明を開ける（戻ると編集のまま）
            if (exUi != null)
            {
                exUi.OpenExplain();
                yield return null;
                Check(exUi.ExplainPage && _app.Sm.State == LabState.Editing, "explain opens from editing");
                yield return Shot("S03_explain_face", facePanel: "MainConsole");
                exUi.ShowExplain(false);
                yield return null;
                Check(!exUi.ExplainPage && _app.Sm.State == LabState.Editing, "explain closes back to editing");
            }

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
            yield return Shot("S05_result_compare_face", facePanel: "MainConsole");
            // 結果の画面からも開ける。開いたまま次の実験に移ると閉じる
            if (exUi != null)
            {
                exUi.OpenExplain();
                yield return null;
                Check(exUi.ExplainPage, "explain opens from result");
                yield return Shot("S05_explain_face", facePanel: "MainConsole");
            }

            // REALITY で比較
            _app.RerunOtherMode();
            yield return WaitState(LabState.ReviewingResult, 40f);
            Check(exUi == null || !exUi.ExplainPage, "explain closes when the screen changes");
            var r3 = _app.LastResult;
            Log($"RESULT rev{r3.Revision} {r3.Config.Mode} wetness={r3.WetnessScore:0.00} coverage={r3.CoverageRatio:0.000} energy={r3.EnergyWh:0.000}Wh drift={r3.DriftM:0.00}");
            Check(r3.Config.Mode == LabModes.Reality && r3.EnergyWh.HasValue, "reality result has energy");
            Check(_app.OtherModeResult != null, "dream/reality comparison available");
            yield return Shot("S05_result_reality", rainLab: true);
            _rainRealityShot = _lastShot;
            yield return Shot("S05_result_reality_face", facePanel: "MainConsole");
            Check(r3.RuntimeMinutesMin.HasValue && r3.RuntimeMinutesMin <= r3.RuntimeMinutes && r3.RuntimeMinutes <= r3.RuntimeMinutesMax, "reality runtime range brackets nominal");

            // REALITY 前提係数と出典
            _app.GoToTestSetup();
            yield return WaitState(LabState.ConfiguringTest);
            Check(_app.TestConfig.Mode == LabModes.Reality, "setup keeps reality mode");
            yield return Shot("S04_reality_setup", rainLab: true);
            yield return Shot("S04_reality_setup_face", facePanel: "MainConsole");
            _app.ShowCoefficients();
            Check(_app.CoefficientPage == 0 && _app.CoefficientPageCount >= 2, "coefficient table opens");
            yield return Shot("S04_coefficients_p1", rainLab: true);
            yield return Shot("S04_coefficients_p1_face", facePanel: "MainConsole");
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
            // 依頼文と、版ごとに改良を頼んだ文が保存されている（元に戻して頼み直した Rev.3 は、頼み直した方）
            {
                var saved = _app.Repo.Load(_app.Current.Current.Id, out var loadErr);
                var n2 = saved?.NoteFor(2); var n3 = saved?.NoteFor(3);
                Check(saved != null && saved.RequestText == LabApp.DefaultRainRequest && saved.DesignSource == "offline", "saved: request text and design source " + loadErr);
                Check(n2 != null && n2.Request == "横からの雨も防いで" && n2.Source == "offline" && n2.Changes.Count > 0, "saved: rev2 note keeps the revision request");
                Check(n3 != null && n3.Request == "防ぐ範囲を広げて" && saved.RevisionNotes.Count == 2, "saved: rev3 note after undo and redo");
            }
            ExportGlb("rain_rev" + _app.Current.Current.Revision, _app.Current.Current);
            // 発明ライブラリの見本：公開データ・3D・写真（発明台に置いた最後の版、実験の様子、研究所の結果の画面）
            yield return Shot("S06_product_final_rain", device: true, productView: true);
            {
                var saved = _app.Repo.Load(_app.Current.Current.Id, out _);
                int rev = saved.Current.Revision;
                WriteSample(saved, new[]
                {
                    ("stage", $"IMPOSSIBLE LAB の発明台に置いた Rev.{rev}", rev, CropJpg(_lastShot, 300, 60, 1000, 700)),
                    ("experiment", "Rev.1 の実験。横風 4 m/s、雨量 30 mm/h の中で人型の頭が濡れた", 1, CropJpg(_rainRunningShot, 440, 0, 720, 400)),
                    ("experiment", $"Rev.{rev} を REALITY で測ると、覆いが風で流されて頭から外れた", rev, CropJpg(_rainRealityShot, 440, 0, 720, 400)),
                    ("result", "IMPOSSIBLE LAB の操作パネルで見た Rev.1 の結果", 1, CropJpg(_rainResultShot, 540, 415, 520, 485)),
                });
            }
            yield return Shot("S06_saved");
            yield return Shot("S06_saved_face", facePanel: "MainConsole");
            if (exUi != null)
            {
                exUi.OpenExplain();
                yield return null;
                Check(exUi.ExplainPage && _app.Sm.State == LabState.Saved, "explain opens from saved");
                yield return Shot("S06_explain_face", facePanel: "MainConsole");
                exUi.ShowExplain(false);
                yield return null;
            }
            // 発明ライブラリに公開する（PC の代わりに、送った中身を受け取って採用を1件返す）
            var library = new FakeLibrary { InventionId = _app.Current.Current.Id, Name = _app.Current.Current.Name };
            yield return PublishFlow(library);

            _app.OpenGallery();
            yield return WaitState(LabState.Gallery);
            Check(_app.PendingDeleteId == null, "gallery opens without delete dialog");
            Check(_app.Sm.State == LabState.Gallery && _app.MineFor(library.InventionId) != null, "gallery knows the published invention");
            yield return Shot("S06_gallery");
            yield return Shot("S06_gallery_face", facePanel: "MainConsole");
            // 7件以上ならページ送り（新しい順に6件ずつ）
            var gui = FindFirstObjectByType<LabUI>();
            if (_app.GalleryEntries.Count > LabUI.GalleryPerPage)
            {
                gui.StepGalleryPage(+1);
                yield return null;
                Check(gui.GalleryPage == 1, $"gallery moves to page 2 ({_app.GalleryEntries.Count} entries)");
                yield return Shot("S06_gallery_p2");
                yield return Shot("S06_gallery_p2_face", facePanel: "MainConsole");
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
                yield return Shot("S06_gallery_tag_face", facePanel: "MainConsole");
                gui.SetGalleryTag(null);
                yield return null;
                Check(gui.GalleryTag == null, "gallery tag filter clears");
            }
            else Log("gallery tag filter not exercised (no tags)");
            _app.AskDelete(_app.Current.Current.Id);
            yield return null; yield return null;
            yield return Shot("S06_delete_confirm");
            yield return Shot("S06_delete_confirm_face", facePanel: "MainConsole");
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

            // 採用の知らせ：最初の指示の画面に一度だけ出て、読んだら消える
            _app.NewInvention();
            yield return null; yield return null;
            Check(_app.AdoptionNotices.Count == 1 && _app.AdoptionNotices[0].adoption.Id == library.AdoptionId, "library: adoption notice waits on the request screen");
            yield return Shot("S01_adoption_notice_face", facePanel: "MainConsole");
            _app.DismissAdoptionNotices();
            yield return null;
            Check(_app.AdoptionNotices.Count == 0 && _app.Library.SeenAdoptions.Contains(library.AdoptionId), "library: notice dismissed and remembered");
            // 端末の記録から検証の分を消して、次の検証でも同じように出るようにする
            _app.Library.SeenAdoptions.Remove(library.AdoptionId);
            _app.Library.Published.Remove(library.InventionId);
            _app.Library.Save(Application.persistentDataPath);
            _app.Mine.Clear();
            _app.VerifyHoldsPc = false;
            _app.ProxyOnline = null;
            _app.Notify();

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
                yield return Shot($"S03_product_{cat}", device: true, productView: true);
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
                    yield return Shot("S03_part_edit_face", facePanel: "MainConsole");
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
                yield return Shot("S02_preview_ai_face", facePanel: "MainConsole");
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
                yield return Shot("S02_function_map_ai_p1_face", facePanel: "MainConsole");
                var ui2 = FindFirstObjectByType<LabUI>();
                ui2.StepNeedPage(+1);
                yield return null;
                Check(ui2.NeedPage == 1, "S02: function map moves to page 2");
                yield return Shot("S02_function_map_ai_p2");
                yield return Shot("S02_function_map_ai_p2_face", facePanel: "MainConsole");
                _app.ApprovePreview();
                yield return WaitState(LabState.Editing);
                _app.GoToTestSetup();
                yield return WaitState(LabState.ConfiguringTest);
                Check(_app.PlanOptions.Count(c => c.FromAi) == 3 && _app.SelectedPlan.FromAi, $"S04: AI plans listed first ({_app.PlanOptions.Count(c => c.FromAi)} AI / {_app.PlanOptions.Count})");
                yield return Shot("S04_plan_ai");
                yield return Shot("S04_plan_ai_face", facePanel: "MainConsole");
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
                yield return Shot("S05_result_plan_ai_face", facePanel: "MainConsole");
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
            yield return PlanLabFlow("cart4", @"{ ""schemaVersion"": 2, ""id"": ""verify-ramp-cart4"", ""name"": ""4輪のスロープ運搬カート"", ""purpose"": ""荷物を載せて車いす用スロープを上る"", ""tags"": [""carrier""], ""mount"": ""ground"",
  ""parts"": [ { ""id"": ""body"", ""type"": ""container"", ""position"": [0, 0.25, 0], ""scale"": [0.5, 0.25, 0.4] }, { ""id"": ""w1"", ""type"": ""wheel"", ""position"": [0.2, 0.08, 0.15], ""scale"": [0.16, 0.05, 0.16] },
             { ""id"": ""w2"", ""type"": ""wheel"", ""position"": [-0.2, 0.08, 0.15], ""scale"": [0.16, 0.05, 0.16] }, { ""id"": ""w3"", ""type"": ""wheel"", ""position"": [0.2, 0.08, -0.15], ""scale"": [0.16, 0.05, 0.16] },
             { ""id"": ""w4"", ""type"": ""wheel"", ""position"": [-0.2, 0.08, -0.15], ""scale"": [0.16, 0.05, 0.16] }, { ""id"": ""m"", ""type"": ""motor"", ""position"": [0, 0.1, 0], ""scale"": [0.08, 0.08, 0.08] } ],
  ""behaviors"": [ { ""type"": ""drive"", ""targetPart"": ""m"", ""parameters"": { ""speedLimit"": 0.8, ""maxSlopeDeg"": 8 } },
                   { ""type"": ""carry_load"", ""targetPart"": ""body"", ""parameters"": { ""capacity"": 10 } } ], ""revision"": 1, ""parentRevision"": null }",
                ExperimentCatalog.ClimbSlope, "climbedRatio", addModule: null, stepModule: "slope", stepKey: "angleDeg");
            // 2026-11-27：1日使う（品番の消費電力と電池の容量から、電池の持ちを見る）
            yield return PlanLabFlow("daily", @"{ ""schemaVersion"": 3, ""id"": ""verify-daily-lantern"", ""name"": ""背負うランタン"", ""purpose"": ""夜道で足元を照らし、1日もつ"", ""tags"": [""lighting""], ""mount"": ""back"",
  ""parts"": [ { ""id"": ""body"", ""type"": ""body"", ""position"": [0, 0, 0], ""scale"": [0.12, 0.08, 0.05] },
             { ""id"": ""lamp"", ""type"": ""light"", ""model"": ""led-spot1w"", ""position"": [0, 0.05, 0.02], ""scale"": [0.02, 0.016, 0.02] },
             { ""id"": ""bat"", ""type"": ""battery"", ""model"": ""bat-18650x1"", ""position"": [0, 0, 0], ""scale"": [0.0185, 0.065, 0.0185] } ],
  ""behaviors"": [ { ""type"": ""illuminate"", ""targetPart"": ""lamp"", ""parameters"": { ""lumens"": 100, ""beamAngleDeg"": 60, ""tiltDeg"": 50 } } ],
  ""functionMap"": [ { ""need"": ""足元を照らす"", ""blocks"": [""illuminate""], ""status"": ""simulated"" },
                     { ""need"": ""1日もつ"", ""blocks"": [], ""metrics"": [""batteryHours"", ""chargeDays""], ""status"": ""simulated"" } ], ""revision"": 1, ""parentRevision"": null }",
                ExperimentCatalog.DailyUse, "chargeDays", addModule: null, stepModule: "usage", stepKey: "hoursPerDay");

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
            yield return Shot("S02_preview_motion_bin_face", facePanel: "MainConsole");
            _app.ApprovePreview();
            yield return WaitState(LabState.Editing);
            // S03：動きの手順（オン・オフ、待つ時間の変更、元に戻す、動かしてみる）
            var gui = FindFirstObjectByType<LabUI>();
            gui.ProgramTab = true;
            yield return null;
            yield return Shot("S03_program_motion_bin");
            yield return Shot("S03_program_face", facePanel: "MainConsole");
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

            // 座ったまま試用：人型モデルを「近づける」ボタンで 1m 以内に寄せるとフタが開く（あなた＝対象は遠いまま）
            target.position = p + new Vector3(0, 0, 3.5f);
            _app.ToggleDevice();
            Check(_app.TrialHasProgram && Mathf.Abs(_app.TrialPersonDistance - LabApp.TrialPersonMax) < 1e-3f, "seated trial: a person model stands 3 m away");
            yield return new WaitForSeconds(0.5f);
            for (int k = 0; k < 5; k++) _app.StepTrialPerson(-1); // 3.0 → 0.5 m
            yield return new WaitForSeconds(2.5f);
            float openSeated = _app.View.Joints["lid"].Value;
            Check(openSeated > 80f, $"seated trial: lid opens when the person model is moved near ({openSeated:0} deg, {_app.TrialPersonDistance:0.0} m)");
            Check(_app.TrialStatus() != null && _app.TrialStatus().Contains("1m 以内"), $"seated trial: status shows the distance condition ({_app.TrialStatus()})");
            var dv = _app.View.transform.position;
            yield return Shot("S03_seated_trial", eyeAt: dv + new Vector3(1.6f, 0.9f, -1.6f), lookAt: dv + new Vector3(0f, 0.3f, 0f));
            _app.ToggleDevice();
            yield return null; // Destroy はフレームの終わりに反映される
            Check(_app.TrialPersonDistance < 0f && GameObject.Find("TrialPerson") == null, "seated trial: the person model is removed when the trial stops");
            Destroy(target.gameObject);

            // 実験：近づいて離れる（AT-13）
            _app.GoToTestSetup();
            yield return WaitState(LabState.ConfiguringTest);
            Check(_app.EditPlan != null && _app.EditPlan.Scenario == ExperimentCatalog.ApproachDevice, $"motion: first plan is approach_device ({_app.EditPlan?.Scenario})");
            yield return Shot("S04_motion_bin");
            yield return Shot("S04_motion_bin_face", facePanel: "MainConsole");
            float tw = 0; while (_app.Transitioning && tw < 10f) { tw += Time.unscaledDeltaTime; yield return null; }
            _app.StartExperiment();
            yield return WaitState(LabState.RunningTest);
            yield return new WaitForSeconds(3.2f);
            yield return Shot("S05_running_motion_bin", rainLab: true);
            yield return Shot("S05_running_motion_bin_face", facePanel: "MainConsole");
            yield return WaitState(LabState.ReviewingResult, 90f);
            var res = _app.LastResult;
            Check(res != null && res.Metric("ruleFiredCount") != null && res.Metric("reactionTime") != null && res.Metric("expectationRate") != null,
                $"motion: experiment measured firing, reaction time and expectation ({res?.Criteria})");
            Check(res != null && res.Passed == true, $"motion: experiment passed ({string.Join(" / ", res?.Limits ?? new System.Collections.Generic.List<string>())})");
            Log($"RESULT motion-bin fired={res?.Metric("ruleFiredCount")?.Value} reaction={res?.Metric("reactionTime")?.Value:0.00} expectation={res?.Metric("expectationRate")?.Value:0.00}");
            yield return Shot("S05_result_motion_bin", rainLab: true);
            yield return Shot("S05_result_motion_bin_face", facePanel: "MainConsole");
            _app.Save();
            yield return WaitState(LabState.Saved, 20f); // 次の検証は保存完了の画面から新しい発明を始める
        }

        /// <summary>AI の評価セットの依頼の原文（無ければ null）。</summary>
        static string EvalRequest(string id)
        {
            try
            {
                var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "proxy", "eval", "requests.json"));
                var token = JToken.Parse(File.ReadAllText(path));
                var list = token is JArray a ? a : (JArray)token["requests"];
                return list.OfType<JObject>().FirstOrDefault(o => (string)o["id"] == id)?.Value<string>("request");
            }
            catch (Exception) { return null; }
        }

        /// <summary>画面写真（1600×900 の PNG）の一部を切り出し、幅 720 px までの JPEG にする。y は画像の上から。</summary>
        static byte[] CropJpg(string pngFile, int x, int yTop, int w, int h)
        {
            if (pngFile == null || !File.Exists(pngFile)) return null;
            var src = new Texture2D(2, 2);
            src.LoadImage(File.ReadAllBytes(pngFile));
            x = Mathf.Clamp(x, 0, src.width - 1); w = Mathf.Min(w, src.width - x);
            int y = Mathf.Clamp(src.height - yTop - h, 0, src.height - 1); h = Mathf.Min(h, src.height - y);
            var crop = new Texture2D(w, h, TextureFormat.RGB24, false);
            crop.SetPixels(src.GetPixels(x, y, w, h));
            crop.Apply();
            Texture2D outTex = crop;
            if (w > 720)
            {
                int ow = 720, oh = Mathf.RoundToInt(h * 720f / w);
                var rt = RenderTexture.GetTemporary(ow, oh);
                Graphics.Blit(crop, rt);
                var prev = RenderTexture.active; RenderTexture.active = rt;
                outTex = new Texture2D(ow, oh, TextureFormat.RGB24, false);
                outTex.ReadPixels(new Rect(0, 0, ow, oh), 0, 0); outTex.Apply();
                RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                Destroy(crop);
            }
            var jpg = outTex.EncodeToJPG(84);
            Destroy(outTex); Destroy(src);
            return jpg;
        }

        /// <summary>
        /// 発明ライブラリの見本を書き出す：OutputDir/library/&lt;発明 ID&gt;/ に公開データ（publication.json）・3D（model.glb）・写真（imageN.jpg と images.json）。
        /// PC 側の proxy/tools/load-samples.ts がこれをライブラリに入れる。
        /// </summary>
        void WriteSample(SavedInvention inv, (string kind, string caption, int revision, byte[] jpg)[] images)
        {
            var pub = PublicationBuilder.Build(inv, new PublishOptions { Anonymous = true, AgreedAt = LabClock.NowIso() });
            var glb = ProductExport.Export(inv.Current, _app.Assets);
            string dir = Path.Combine(OutputDir, "library", inv.Current.Id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "publication.json"), pub.ToString(), new UTF8Encoding(false));
            if (glb.Glb != null) File.WriteAllBytes(Path.Combine(dir, "model.glb"), glb.Glb);
            var meta = new JArray();
            foreach (var im in images.Where(i => i.jpg != null))
            {
                string file = $"image{meta.Count}.jpg";
                File.WriteAllBytes(Path.Combine(dir, file), im.jpg);
                meta.Add(new JObject { ["file"] = file, ["kind"] = im.kind, ["caption"] = im.caption, ["revision"] = im.revision });
            }
            File.WriteAllText(Path.Combine(dir, "images.json"), meta.ToString(), new UTF8Encoding(false));
            var invj = pub["invention"];
            Log($"SAMPLE {inv.Current.Id} rev={invj["revision"]} experiments={((JArray)invj["experiments"]).Count} revisions={((JArray)invj["revisions"]).Count} images={meta.Count} glb={glb.Glb?.Length ?? 0}");
            Check(glb.Glb != null && meta.Count == images.Length && !string.IsNullOrEmpty((string)invj["request"]) && ((JArray)invj["experiments"]).Count > 0,
                $"sample-{inv.Current.Id}: publication, 3D and {meta.Count} photo(s) written");
        }

        /// <summary>
        /// 公開用の 3D（GLB）を書き出し、形が正しいこと・部品がすべてノードにあること・大きさの目安に収まることを確かめる。
        /// 書き出したファイルは OutputDir/glb/ に残す（ブラウザ・Blender で開いて見た目を確かめる）。
        /// </summary>
        void ExportGlb(string name, InventionDna dna)
        {
            var r = ProductExport.Export(dna, _app.Assets);
            var s = r.Glb != null ? GlbWriter.Inspect(r.Glb, out var err) : null;
            if (s == null) { Check(false, $"glb-{name}: readable ({r.Warnings.FirstOrDefault()})"); return; }
            string dir = Path.Combine(OutputDir, "glb");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".glb"), r.Glb);
            var missing = dna.Parts.Where(p => !s.NodeNames.Contains(p.Id)).Select(p => p.Id).ToList();
            Log($"GLB {name} bytes={r.Glb.Length} parts={r.Parts} placed={r.Instances} meshes={s.Meshes} prims={r.Primitives} verts={r.Vertices} tris={r.Triangles} mats={r.Materials} warnings={r.Warnings.Count}");
            Check(missing.Count == 0 && r.Warnings.Count == 0 && r.Triangles > 0, $"glb-{name}: every part is a node ({string.Join(",", missing)}{r.Warnings.FirstOrDefault()})");
            Check(r.Glb.Length <= ProductExport.TargetBytes, $"glb-{name}: {r.Glb.Length / 1024} KB <= {ProductExport.TargetBytes / 1024} KB");
        }

        /// <summary>見比べ用：記録した AI の応答を1件ずつ設計させ、製品の説明のページと試作品を撮る（通しの検証はしない）。</summary>
        IEnumerator Showcase(string dir)
        {
            yield return new WaitForSeconds(1f);
            var ui = FindFirstObjectByType<LabUI>();
            foreach (var path in Directory.GetFiles(dir, "*.json").OrderBy(p => p, StringComparer.Ordinal))
            {
                string id = Path.GetFileNameWithoutExtension(path);
                _app.SetDesignServiceForVerify(new FixtureDesignService(File.ReadAllText(path)));
                if (_app.Sm.State == LabState.Saved) _app.ContinueEditing();
                _app.NewInvention();
                _app.SetRequestText("見比べ " + id);
                float w = 0;
                while (_app.Sm.State == LabState.Drafting && w < 10f) { _app.SubmitRequest(); w += Time.unscaledDeltaTime; yield return null; }
                yield return WaitState(LabState.AwaitingApproval);
                if (_app.Preview == null) { Check(false, $"showcase-{id}: design ({_app.LastOutcome?.Message})"); continue; }
                Log($"SHOWCASE {id} {_app.LastOutcome?.Message}");
                yield return Shot($"{id}_explain", facePanel: "MainConsole");
                if (ui != null && _app.Preview.Parts.Count > 1) { ui.SelectExplainPart(1); yield return null; yield return Shot($"{id}_explain_part2", facePanel: "MainConsole"); }
                _app.ApprovePreview();
                yield return WaitState(LabState.Editing);
                yield return Shot($"{id}_product", device: true, productView: true);
                yield return Shot($"{id}_device", device: true);
            }
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
            // 見本として公開するので、AI の評価で実際に使った依頼の原文を入れる（proxy/eval/requests.json）
            _app.SetRequestText(EvalRequest(id) ?? "代表 " + id);
            float w = 0;
            while (_app.Sm.State == LabState.Drafting && w < 10f) { _app.SubmitRequest(); w += Time.unscaledDeltaTime; yield return null; }
            yield return WaitState(LabState.AwaitingApproval);
            if (_app.Preview == null) { Check(false, $"rep-{id}: design"); done(false); yield break; }
            yield return Shot($"S02_explain_rep_{id}_face", facePanel: "MainConsole");
            _app.ApprovePreview();
            yield return WaitState(LabState.Editing);
            var dna = _app.Current.Current;
            ExportGlb("rep_" + id, dna);
            yield return Shot($"S03_device_rep_{id}", device: true);
            yield return Shot($"S03_product_rep_{id}", device: true, productView: true);
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
            var note = saved?.NoteFor(saved.Current.Revision);
            Check(saved != null && saved.RequestText == (EvalRequest(id) ?? "代表 " + id) && saved.DesignSource == "ai" && note != null && note.Request == req && note.Changes.Count > 0,
                $"rep-{id}: saved the design request and the revision request");
            if (saved != null)
            {
                yield return Shot($"S06_product_final_rep_{id}", device: true, productView: true);
                WriteSample(saved, new[] { ("stage", $"IMPOSSIBLE LAB の発明台に置いた Rev.{saved.Current.Revision}", saved.Current.Revision, CropJpg(_lastShot, 300, 60, 1000, 700)) });
            }
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
            if (scenario == ExperimentCatalog.ClimbSlope)
            {
                // 坂を上る向き：前（+Z）が坂の上（+x）を向き、坂に合わせて上を向いている
                var course = FindFirstObjectByType<CourseLabController>();
                var dv = course != null ? course.DeviceView : null;
                var fwd = dv != null ? course.transform.InverseTransformDirection(dv.forward) : Vector3.zero;
                // 前の車輪だけの台車（cart）は重心が後ろの接地より後ろにあるので後ろに倒れ、4輪の台車（cart4）は上る
                var fr = course != null ? course.CurrentFrame : null;
                bool tipped = fr.HasValue && fr.Value.TipDeg > 1f;
                Check(tipped == (tag == "cart"), $"plan-{tag}: tipped={tipped} as the center of mass says");
                if (tipped) Check(fwd.y > 0.3f && Mathf.Abs(fwd.z) < 0.05f, $"plan-{tag}: device tips back on the slope (forward={fwd})");
                else Check(fwd.x > 0.9f && fwd.y > 0.03f && Mathf.Abs(fwd.z) < 0.05f, $"plan-{tag}: device faces up the slope (forward={fwd})");
                if (dv != null)
                    yield return Shot($"S05_running_plan_{tag}_side", eyeAt: course.transform.TransformPoint(new Vector3(-0.6f, 0.9f, ClimbSlopeScenario.LaneZ - 2.4f)), lookAt: dv.position);
            }
            yield return WaitState(LabState.ReviewingResult, 60f);
            var r = _app.LastResult;
            Check(r != null && r.IsScenarioLab && r.Scenario == scenario && r.Metric(metric) != null && r.Passed.HasValue,
                $"plan-{tag}: result has {metric} (passed={r?.Passed})");
            Check(r != null && PlanCandidates.ConditionKey(r.Plan) == key, $"plan-{tag}: result keeps the edited plan");
            Log($"RESULT plan-{tag} {r?.Plan?.Mode} passed={r?.Passed} {string.Join(" ", r?.Metrics.Select(m => m.Key + "=" + m.Display) ?? new string[0])} | {r?.Criteria}");
            yield return Shot($"S05_result_plan_{tag}", rainLab: true);
            // 判定は左の板、要望の確認は右の板の2つ目の切り替えにある
            var texts = FindObjectsByType<TMPro.TMP_Text>(FindObjectsSortMode.None).Select(t => t.text).ToList();
            bool verdicts = texts.Any(t => t == "達成" || t == "未達");
            var resultUi = FindFirstObjectByType<LabUI>();
            resultUi.SetConsoleTabs(0, 1);
            yield return null;
            bool coverage = FindObjectsByType<TMPro.TMP_Text>(FindObjectsSortMode.None).Any(t => t.text.StartsWith("この実験で確かめた", StringComparison.Ordinal));
            CheckFits($"S05_result_plan_{tag}_requests");
            resultUi.SetConsoleTabs(1, 0);
            yield return null;
            CheckFits($"S05_result_plan_{tag}_method");
            resultUi.SetConsoleTabs(0, 0);
            yield return null;
            Check(verdicts && coverage, $"plan-{tag}: S05 shows per-criterion verdicts and request coverage");
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
