using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImpossibleLab.Core;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ImpossibleLab
{
    public enum StageStatus { Waiting, Running, Done, Failed }

    /// <summary>
    /// 縦切りの統括。状態遷移（AppStateMachine）を唯一の画面状態とし、UIはここを読むだけ。
    /// 発明指示 → 設計 → 承認 → 組み立て → 編集/改良 → 実験 → 結果 → 保存/ギャラリー。
    /// </summary>
    public sealed class LabApp : MonoBehaviour
    {
        [Header("Assets")] public LabAssets Assets;
        [Header("Workshop")] public Transform DisplayPoint;
        public Transform WorkshopPlayerSpot, WorkshopConsoleSpot;
        public Transform[] RobotArmJoints;
        public Renderer RingLight;
        public GameObject Hologram;
        public Transform GateDoorLeft, GateDoorRight;
        [Header("Test Zone")] public RainLabController RainLab;
        /// <summary>ObstacleCourse / FlightDeck（RainLab と同じ実験場に実行時に組み立てる）。</summary>
        [NonSerialized] public CourseLabController Course;
        [NonSerialized] public LabFx Fx;
        public bool ExperimentRunning => (RainLab != null && RainLab.Running) || (Course != null && Course.Running);
        [Header("XR")] public XROrigin Origin;
        public ScreenFader Fader;
        public Transform ConsoleRoot;

        public readonly AppStateMachine Sm = new AppStateMachine();
        public IInventionDesignService Design { get; private set; }
        /// <summary>Editor 検証（DemoAutopilot）専用：記録したAI応答で画面を確かめるために設計元を差し替える。</summary>
        internal void SetDesignServiceForVerify(IInventionDesignService s) { if (s != null) Design = s; }
        /// <summary>サンプル設計図を明示的に選んだときに使うオフライン設計（AIは呼ばない）。</summary>
        public IInventionDesignService OfflineDesign { get; private set; }
        public ProxyConfig Proxy { get; private set; }
        bool _forceOfflineOnce;
        public InventionRepository Repo { get; private set; }
        InventionBuilder _builder;

        // ---- 画面が参照するデータ（実行時状態。Unityのシリアライズで null が "" に化けないよう NonSerialized） ----
        [NonSerialized] public string RequestText = "";
        [NonSerialized] public string InputWarning;
        public readonly Dictionary<DesignStage, StageStatus> Stages = new Dictionary<DesignStage, StageStatus>();
        [NonSerialized] public DesignOutcome LastOutcome;
        [NonSerialized] public InventionDna Preview;              // 承認待ちの設計
        [NonSerialized] public SavedInvention Current;            // 承認・組み立て済みの発明
        [NonSerialized] public InventionView View;
        [NonSerialized] public RevisionProposal PendingProposal;
        [NonSerialized] public RevisionResult PendingRevision;
        [NonSerialized] public string RevisionText = "";
        [NonSerialized] public bool RevisionBusy;
        [NonSerialized] public string RevisionMessage;
        [NonSerialized] public RainExperimentConfig TestConfig = new RainExperimentConfig();
        [NonSerialized] public RainExperimentResult LastResult;
        [NonSerialized] public RainExperimentResult BaselineResult;   // 同一条件の前回（改良前）結果
        [NonSerialized] public RainExperimentResult OtherModeResult;  // 同一版・同一条件・別モード
        [NonSerialized] public string ErrorMessage;
        [NonSerialized] public List<string> ErrorSuggestions = new List<string>();
        [NonSerialized] public string Toast;
        [NonSerialized] public List<GalleryEntry> GalleryEntries = new List<GalleryEntry>();
        [NonSerialized] public string PendingDeleteId;
        public bool InRainLab { get; private set; }
        public bool DeviceRunning => View != null && View.Active;
        public bool Seated { get; private set; }
        [NonSerialized] public string LastSavedPath;
        /// <summary>REALITY 前提係数一覧のページ（-1＝非表示）。</summary>
        [NonSerialized] public int CoefficientPage = -1;

        public event Action Changed;
        CancellationTokenSource _cts;
        float _ringPulse;
        bool _transitioning;

        public static readonly string DefaultRainRequest = "手を使わず雨を防ぐ装置を作って";

        void Awake()
        {
            if (Assets != null) { LabTheme.Font = Assets.Font; LabTheme.FontBold = Assets.FontBold; }
            _builder = new InventionBuilder(Assets);
            var samples = new Dictionary<string, string>();
            if (Assets != null && Assets.RainSample != null) samples["protection"] = Assets.RainSample.text;
            foreach (var (cat, file) in new[] { ("carrier", "carrier_robot"), ("mobility", "flying_shoes") })
            {
                var ta = Resources.Load<TextAsset>("ImpossibleLabSamples/" + file);
                if (ta != null) samples[cat] = ta.text; else Debug.LogWarning("[ILAB] sample missing: " + file);
            }
            // 段階表示は実処理に紐づける。オフライン処理は一瞬で終わるため、各段階を読める最小時間だけ表示する。
            OfflineDesign = new OfflineDesignService(samples, stageDelayMs: 450);
            Proxy = ProxyConfig.Load(Application.persistentDataPath);
            // 自動検証（-ilabVerify）は決定的なオフライン設計で通す
            bool verify = Array.IndexOf(Environment.GetCommandLineArgs(), "-ilabVerify") >= 0;
            Design = Proxy.Enabled && !verify ? new FallbackDesignService(new ProxyDesignService(Proxy), OfflineDesign) : OfflineDesign;
            Debug.Log($"[ILAB] design service = {Design.DisplayName} url={Proxy.Url}");
            Repo = new InventionRepository(Application.persistentDataPath);
            Sm.Changed += (a, b) => Notify();
            ResetStages();
            if (RainLab != null)
            {
                RainLab.Completed += OnExperimentCompleted;
                Course = RainLab.gameObject.AddComponent<CourseLabController>();
                Course.Init(Assets, RainLab);
                Course.Completed += OnExperimentCompleted;
            }
            if (GetComponent<PerfMonitor>() == null) gameObject.AddComponent<PerfMonitor>();
            Fx = gameObject.AddComponent<LabFx>();
            Fx.Init(this);
            if (Course != null) Course.Collided += Fx.Bump;
            gameObject.AddComponent<HandMenu>().Init(this);
        }

        void Start()
        {
            RecenterConsole(WorkshopConsoleSpot);
            StartCoroutine(AlignViewOnStartup());
            var subsystems = new List<UnityEngine.XR.XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);
            foreach (var s in subsystems) s.trackingOriginUpdated += OnTrackingOriginUpdated;
            Notify();
        }

        void OnDestroy()
        {
            var subsystems = new List<UnityEngine.XR.XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);
            foreach (var s in subsystems) s.trackingOriginUpdated -= OnTrackingOriginUpdated;
            CancelPending();
        }

        /// <summary>起動時、頭の向きに関係なくメイン画面の正面に立つよう視点を合わせる（頭部トラッキングが始まるまで待つ）。</summary>
        IEnumerator AlignViewOnStartup()
        {
            if (Origin == null) yield break;
            var cam = Origin.Camera.transform;
            // ヘッドセットを装着して頭の姿勢が届くまで待つ（未装着で起動すると数秒〜数分来ないことがある）
            while (cam.localPosition.sqrMagnitude < 1e-6f) yield return null;
            for (int i = 0; i < 10; i++) yield return null; // 最初の姿勢が安定するまで数フレーム待つ
            AlignToCurrentArea();
            _consolePlaced = true;
            _settleUntil = Time.realtimeSinceStartup + 6f; // 直後に頭の高さが落ち着くまでは小さなずれも直す
        }

        /// <summary>Quest の再センタリング（メタボタン長押し）後も正面に合わせ直す。</summary>
        float _lastOriginAlign = -999f;
        void OnTrackingOriginUpdated(UnityEngine.XR.XRInputSubsystem _)
        {
            Debug.Log($"[ILAB] trackingOriginUpdated t={Time.realtimeSinceStartup:F1}");
            // 移動バーで操作中や直後は合わせ直さない（連続で届いてもパネルを引き戻さない）
            if (_transitioning || ConsoleBeingDragged()) return;
            if (Time.realtimeSinceStartup - _lastOriginAlign < 3f) return;
            _lastOriginAlign = Time.realtimeSinceStartup;
            StartCoroutine(AlignNextFrame());
        }

        IEnumerator AlignNextFrame() { yield return null; AlignToCurrentArea(); }

        void AlignToCurrentArea()
        {
            if (_transitioning) return;
            TeleportRig(InRainLab ? RainLab.PlayerSpot : WorkshopPlayerSpot);
            RecenterConsole(InRainLab ? RainLab.ConsoleSpot : WorkshopConsoleSpot);
            if (Origin != null && Origin.Camera != null)
                Debug.Log($"[ILAB] align mode={Origin.CurrentTrackingOriginMode} eye={Origin.Camera.transform.position:F2} camLocal={Origin.Camera.transform.localPosition:F2} offset={Origin.CameraYOffset:F2} console={(ConsoleRoot != null ? ConsoleRoot.position : Vector3.zero):F2}");
        }

        public void Notify() => Changed?.Invoke();

        void ResetStages() { foreach (var s in DesignStageText.All) Stages[s] = StageStatus.Waiting; }

        // ================= S01 発明指示 =================
        public void SetRequestText(string text)
        {
            RequestText = text ?? "";
            InputWarning = null;
            if (Sm.State == LabState.Idle) Sm.Transition(LabState.Drafting);
        }

        /// <summary>クイック発明は入力欄へ反映するだけ（即時に設計処理しない, 仕様23 S01）。</summary>
        public void PickQuickInvention(string text)
        {
            SetRequestText(text);
            Notify();
        }

        public void SubmitRequest()
        {
            if (Sm.Busy || _transitioning) return; // 二重送信防止
            InputWarning = RequestRules.Check(RequestText);
            if (InputWarning != null) { if (Sm.State == LabState.Idle) Sm.Transition(LabState.Drafting); Notify(); return; }
            if (Sm.State == LabState.Idle) Sm.Transition(LabState.Drafting);
            if (Sm.State == LabState.Error || Sm.State == LabState.Cancelled) Sm.Transition(LabState.Drafting);
            if (!Sm.Transition(LabState.GeneratingDesign)) return;
            _ = GenerateAsync(RequestText);
        }

        async Task GenerateAsync(string text)
        {
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            ResetStages();
            LastOutcome = null; Preview = null;
            var progress = new Progress<DesignStage>(stage =>
            {
                foreach (var s in DesignStageText.All)
                {
                    if (s == stage) { Stages[s] = StageStatus.Running; break; }
                    Stages[s] = StageStatus.Done;
                }
                SetHologram(true, "AIが設計を行っています…\n" + DesignStageText.Label(stage));
                Notify();
            });
            DesignOutcome outcome;
            try
            {
                var service = _forceOfflineOnce ? OfflineDesign : Design;
                _forceOfflineOnce = false;
                var task = service.GenerateAsync(text, progress, _cts.Token);
                // AI は1回あたり最大約100秒（プロキシ側の再試行込み）。全体の上限はその少し上
                var timeout = Task.Delay(TimeSpan.FromSeconds(service == OfflineDesign ? 20 : 130));
                if (await Task.WhenAny(task, timeout) == timeout)
                {
                    _cts.Cancel();
                    outcome = new DesignOutcome { Status = DesignStatus.Offline, Message = "設計サービスが応答しません（タイムアウト）" };
                }
                else outcome = await task;
            }
            catch (OperationCanceledException) { outcome = new DesignOutcome { Status = DesignStatus.Cancelled, Message = "設計をキャンセルしました" }; }
            catch (Exception e)
            {
                Debug.LogWarning("[ImpossibleLab] design failed: " + e.Message);
                outcome = new DesignOutcome { Status = DesignStatus.Failed, Message = "設計処理でエラーが発生しました" };
            }
            if (this == null) return;
            LastOutcome = outcome;
            if (outcome.Status == DesignStatus.Cancelled) { Sm.Transition(LabState.Cancelled); Sm.Transition(LabState.Drafting); SetHologram(true, null); return; }
            if (outcome.Status != DesignStatus.Success)
            {
                foreach (var s in DesignStageText.All) if (Stages[s] == StageStatus.Running) Stages[s] = StageStatus.Failed;
                ShowError(outcome.Message, outcome.Suggestions.Count > 0 ? outcome.Suggestions : new List<string> { "再試行するか、サンプル設計図を使ってください" });
                SetHologram(true, null);
                return;
            }
            Stages[DesignStage.PartDesign] = StageStatus.Done;
            Preview = outcome.Dna;
            SetHologram(false, null);
            Sm.Transition(LabState.AwaitingApproval);
        }

        public void CancelGeneration() { if (Sm.State == LabState.GeneratingDesign) _cts?.Cancel(); }

        public void UseOfflineSample()
        {
            RequestText = DefaultRainRequest;
            if (Sm.State != LabState.Drafting) Sm.Transition(LabState.Drafting);
            _forceOfflineOnce = true;
            SubmitRequest();
        }

        public void BackToDraft()
        {
            if (Sm.Busy) return;
            Preview = null;
            if (Sm.Transition(LabState.Drafting)) SetHologram(true, null);
        }

        public void Regenerate()
        {
            if (Sm.State != LabState.AwaitingApproval) return;
            if (Sm.Transition(LabState.GeneratingDesign)) _ = GenerateAsync(RequestText);
        }

        // ================= S02 承認 → 組み立て =================
        public void ApprovePreview()
        {
            if (Sm.State != LabState.AwaitingApproval || Preview == null) return;
            if (!Sm.Transition(LabState.Building)) return;
            StartCoroutine(BuildRoutine(Preview, animate: true));
        }

        IEnumerator BuildRoutine(InventionDna dna, bool animate)
        {
            Stages[DesignStage.Assembly] = StageStatus.Running;
            Notify();
            ClearView();
            var result = _builder.Build(dna, DisplayPoint);
            if (result.View == null)
            {
                ShowError("組み立てに失敗しました: " + string.Join(" / ", result.Warnings.Take(2)), new List<string> { "サンプル設計図を使う" });
                yield break;
            }
            View = result.View;
            SetupGrab(View);
            if (animate)
            {
                View.SetVisiblePartCount(0);
                _ringPulse = 1f;
                var parts = View.Parts.Values.ToList();
                Fx.BuildScan(DisplayPoint, 0.45f * parts.Count + 0.3f, Assets != null ? Assets.Canopy : null);
                for (int i = 1; i <= parts.Count; i++)
                {
                    yield return new WaitForSeconds(0.45f);
                    View.SetVisiblePartCount(i);
                    Fx.PartPlaced(parts[i - 1]);
                    PulseArms(i);
                }
                yield return new WaitForSeconds(0.3f);
            }
            View.SetVisiblePartCount(int.MaxValue);
            Stages[DesignStage.Assembly] = StageStatus.Done;
            Stages[DesignStage.Complete] = StageStatus.Done;
            Current = new SavedInvention { Current = dna };
            Preview = null;
            LastResult = BaselineResult = OtherModeResult = null;
            TestConfig.Mode = dna.Mode;
            Sm.Transition(LabState.Editing);
            if (result.Warnings.Count > 0) ShowToast(string.Join(" / ", result.Warnings.Take(2)));
        }

        void SetupGrab(InventionView view)
        {
            var rb = view.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false;
            var grab = view.gameObject.AddComponent<XRGrabInteractable>();
            grab.throwOnDetach = false;
            grab.movementType = XRBaseInteractable.MovementType.Kinematic;
            grab.useDynamicAttach = true;
            // 掴んだままトリガー → 起動/停止（仕様 MUST-5）
            grab.activated.AddListener(_ => ToggleDevice());
        }

        void RebuildView(InventionDna dna)
        {
            bool wasRunning = DeviceRunning;
            Vector3 pos = View != null ? View.transform.position : DisplayPoint.position;
            ClearView();
            var r = _builder.Build(dna, DisplayPoint);
            View = r.View;
            if (View == null) return;
            View.transform.position = pos;
            SetupGrab(View);
            if (wasRunning) ToggleDevice();
        }

        void ClearView()
        {
            if (View != null) { View.SetActive(false); _builder.Clear(View); View = null; }
        }

        // ================= S03 編集・改良 =================
        public void ToggleDevice()
        {
            if (View == null || InRainLab) return;
            bool on = !View.Active;
            var follow = View.Get<FollowHeadBehavior>();
            if (follow != null) { follow.Target = on ? Origin.Camera.transform : null; follow.TargetHeadTop = 0.1f; }
            var followUser = View.Get<FollowUserBehavior>();
            if (followUser != null) { followUser.Target = on ? Origin.Camera.transform : null; followUser.FloorY = WorkshopPlayerSpot != null ? WorkshopPlayerSpot.position.y : 0f; }
            View.SetActive(on);
            if (!on) View.transform.SetPositionAndRotation(DisplayPoint.position, DisplayPoint.rotation);
            string what = follow != null ? "頭上に追従します" : followUser != null ? "床に降りて、あなたの後ろをついてきます" : View.Get<HoverBehavior>() != null ? "その場で浮き上がります" : "動作します";
            ShowToast(on ? $"起動しました：{what}（試用）" : "停止して発明台に戻しました");
        }

        public void StepParameter(string behavior, string param, int direction)
        {
            if (Current == null || Sm.State != LabState.Editing || RevisionBusy) return;
            var info = LabCatalog.Behavior(behavior)?.Param(param);
            if (info == null) return;
            float step = (info.Max - info.Min) / 20f;
            float now = Current.Current.GetParameter(behavior, param, info.Default);
            var proposal = new RevisionProposal { Source = "manual", Summary = "手動調整" };
            proposal.Operations.Add(new RevisionOperation { Op = RevisionOps.UpdateParameter, BehaviorType = behavior, Parameter = param, Value = now + step * direction });
            var res = RevisionEngine.Apply(Current.Current, proposal);
            if (!res.Success) { ShowToast(string.Join(" / ", res.Errors.Take(1))); return; }
            Commit(res.NewDna);
            ShowToast($"{info.LabelJa}：{res.Changes[0].Before} → {res.Changes[0].After}");
        }

        public void RequestRevision()
        {
            if (Current == null || RevisionBusy || Sm.State != LabState.Editing) return;
            _ = ReviseAsync(RevisionText);
        }

        public void PickRevision(string text) { RevisionText = text; RevisionMessage = null; Notify(); }

        async Task ReviseAsync(string text)
        {
            RevisionBusy = true; RevisionMessage = "改良案を作成中…"; PendingProposal = null; PendingRevision = null;
            Notify();
            RevisionOutcome outcome;
            try { outcome = await Design.ReviseAsync(Current.Current, LastResult, text, CancellationToken.None); }
            catch (Exception) { outcome = new RevisionOutcome { Status = DesignStatus.Failed, Message = "改良案の作成に失敗しました" }; }
            if (this == null) return;
            RevisionBusy = false;
            if (outcome.Status != DesignStatus.Success)
            {
                RevisionMessage = outcome.Message + (outcome.Proposal != null && outcome.Proposal.Notes.Count > 0 ? "\n" + string.Join("\n", outcome.Proposal.Notes) : "");
                Notify();
                return;
            }
            var res = RevisionEngine.Apply(Current.Current, outcome.Proposal);
            if (!res.Success) { RevisionMessage = "改良案を適用できません: " + string.Join(" / ", res.Errors.Take(2)); Notify(); return; }
            PendingProposal = outcome.Proposal;
            PendingRevision = res;
            RevisionMessage = null;
            Notify();
        }

        public void ApproveRevision()
        {
            if (PendingRevision == null || !PendingRevision.Success) return;
            var dna = PendingRevision.NewDna;
            var summary = PendingProposal?.Summary;
            PendingRevision = null; PendingProposal = null; RevisionText = "";
            Commit(dna);
            ShowToast("改良を適用しました（Rev." + dna.Revision + "）：" + summary);
        }

        public void RejectRevision() { PendingRevision = null; PendingProposal = null; RevisionMessage = "改良案を却下しました"; Notify(); }

        void Commit(InventionDna next)
        {
            Current.History.Add(Current.Current);
            Current.Current = next;
            RebuildView(next);
            Notify();
        }

        /// <summary>外観プリセットを切り替える（性能に影響しないため改良の版は増やさない）。</summary>
        public void StepAppearance(int dir)
        {
            if (Current == null || Sm.State != LabState.Editing || RevisionBusy) return;
            var list = LabCatalog.Appearances;
            int i = Array.FindIndex(list, a => a.Id == Current.Current.Appearance);
            var next = list[((i < 0 ? 0 : i) + dir + list.Length) % list.Length];
            Current.Current.Appearance = next.Id;
            RebuildView(Current.Current);
            ShowToast($"外観：{next.LabelJa}（性能には影響しません）");
        }

        public bool CanUndo => Current != null && Current.History.Count > 0 && Sm.State == LabState.Editing;

        public void Undo()
        {
            if (!CanUndo) return;
            var prev = Current.History[Current.History.Count - 1];
            Current.History.RemoveAt(Current.History.Count - 1);
            Current.Current = prev;
            PendingRevision = null; PendingProposal = null;
            RebuildView(prev);
            ShowToast("元に戻しました（Rev." + prev.Revision + "）");
        }

        // ================= S04 実験設定 =================
        public void GoToTestSetup()
        {
            if (Current == null) return;
            if (LabCatalog.Category(Current.Current.Category)?.ExperimentAvailable != true)
            { ShowToast("この発明カテゴリの実験場はこの版では未対応です"); return; }
            if (DeviceRunning) ToggleDevice();
            CoefficientPage = -1;
            TestConfig.Environment = LabEnvironments.ForCategory(Current.Current.Category);
            Sm.Transition(LabState.ConfiguringTest);
        }

        public void SetMode(string mode) { if (LabModes.IsValid(mode)) { TestConfig.Mode = mode; if (mode != LabModes.Reality) CoefficientPage = -1; Notify(); } }

        public void StepCondition(string key, int dir)
        {
            switch (key)
            {
                case "rain": TestConfig.RainIntensity = Mathf.Clamp(TestConfig.RainIntensity + 5f * dir, RainExperimentConfig.MinIntensity, RainExperimentConfig.MaxIntensity); break;
                case "wind": TestConfig.WindSpeed = Mathf.Clamp(TestConfig.WindSpeed + 1f * dir, 0f, RainExperimentConfig.MaxWind); break;
                case "dir": TestConfig.WindDirectionDeg = Mathf.Repeat(TestConfig.WindDirectionDeg + 45f * dir, 360f); break;
                case "flightwind": TestConfig.WindSpeed = Mathf.Clamp(TestConfig.WindSpeed + 1f * dir, 0f, RainExperimentConfig.MaxFlightWind); break;
                case "obstacles": TestConfig.ObstacleCount = Mathf.Clamp(TestConfig.ObstacleCount + dir, 0, RainExperimentConfig.MaxObstacles); break;
                case "payload": TestConfig.PayloadKg = Mathf.Clamp(TestConfig.PayloadKg + 5f * dir, 0f, RainExperimentConfig.MaxPayload); break;
                case "distance": TestConfig.TargetDistance = Mathf.Clamp(TestConfig.TargetDistance + 1f * dir, RainExperimentConfig.MinTargetDistance, RainExperimentConfig.MaxTargetDistance); break;
            }
            Notify();
        }

        public const int CoefficientsPerPage = 6;
        public int CoefficientPageCount => RealityModel.Default == null ? 0 : (RealityModel.Default.Coefficients.Count + CoefficientsPerPage - 1) / CoefficientsPerPage;
        public void ShowCoefficients() { if (RealityModel.Default == null) { ShowToast(RealityModel.DefaultError); return; } CoefficientPage = 0; Notify(); }
        public void StepCoefficientPage(int dir) { if (CoefficientPage < 0) return; CoefficientPage = Mathf.Clamp(CoefficientPage + dir, 0, Mathf.Max(0, CoefficientPageCount - 1)); Notify(); }
        public void HideCoefficients() { CoefficientPage = -1; Notify(); }

        public void NewSeed() { TestConfig.Seed = UnityEngine.Random.Range(1, 99999); Notify(); }

        public void BackToEditing()
        {
            if (Sm.State == LabState.ConfiguringTest || Sm.State == LabState.ReviewingResult)
            {
                if (InRainLab) StartCoroutine(MoveArea(toRainLab: false, then: () => Sm.Transition(LabState.Editing)));
                else Sm.Transition(LabState.Editing);
            }
        }

        // ================= S05 実験実行・結果 =================
        public void StartExperiment()
        {
            if (_transitioning || Current == null) return;
            if (Sm.State == LabState.ReviewingResult) Sm.Transition(LabState.ConfiguringTest);
            if (Sm.State != LabState.ConfiguringTest) return;
            CoefficientPage = -1;
            var snapshot = TestConfig.Clamped(); // 設定スナップショット
            var dna = Current.Current;
            BaselineResult = FindComparable(snapshot, dna.Revision, sameRevision: false);
            OtherModeResult = null;
            if (!InRainLab) StartCoroutine(MoveArea(toRainLab: true, then: () => BeginRun(dna, snapshot)));
            else BeginRun(dna, snapshot);
        }

        void BeginRun(InventionDna dna, RainExperimentConfig cfg)
        {
            if (!Sm.Transition(LabState.RunningTest)) return;
            string expId = "exp-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            // 実験はすべて「実験計画」として扱う（画面の設定 → 計画 → 実行）
            var plan = ExperimentPlan.FromConfig(cfg);
            Debug.Log($"[ILAB] experiment plan scenario={plan.Scenario} modules={string.Join(",", plan.Modules.Select(m => m.Type))} mode={plan.Mode} seed={plan.Seed}");
            if (plan.Scenario != ExperimentCatalog.Stand)
            {
                LastResult = null;
                var run = CourseSimulator.Run(dna, cfg, expId, LabClock.NowIso());
                Course.Begin(run, View);
                Fx.StartPulse(RainLab.transform.position, Assets != null ? Assets.Canopy : null);
                Debug.Log($"[ILAB] course run env={cfg.Environment} mode={cfg.Mode} frames={run.Frames.Count} dur={run.Duration:F1}s passed={run.Result.Passed}");
                Notify();
                return;
            }
            var sim = new RainSimulator(dna, plan.ToConfig(), expId, LabClock.NowIso());
            // 発明品を判定用マネキンの頭上へ（REALITY は風で流された位置）
            View.SetActive(false);
            var follow = View.Get<FollowHeadBehavior>();
            View.ExtraOffset = new Vector3(sim.Device.Drift.x, 0, sim.Device.Drift.y);
            if (follow != null) { follow.Target = RainLab.MannequinHeadTop; follow.TargetHeadTop = 0f; }
            View.transform.position = RainLab.MannequinHeadTop.position + Vector3.up * dna.GetParameter("follow_head", "heightOffset", 0.35f) + View.ExtraOffset;
            View.SetActive(true);
            LastResult = null;
            RainLab.Begin(sim, cfg);
            Fx.StartPulse(RainLab.transform.position, Assets != null ? Assets.Canopy : null);
            Notify();
        }

        public void StopExperiment()
        {
            if (Sm.State != LabState.RunningTest) return;
            RainLab.Abort();
            if (Course != null) Course.Abort();
            ResetDeviceInRainLab();
            Sm.Transition(LabState.Cancelled);
            Sm.Transition(LabState.ConfiguringTest);
            ShowToast("実験を停止し、初期状態に戻しました（記録なし）");
        }

        void OnExperimentCompleted(RainExperimentResult r)
        {
            if (Sm.State != LabState.RunningTest) return;
            LastResult = r;
            Current.Experiments.Add(r);
            OtherModeResult = Current.Experiments.LastOrDefault(e => e != r && e.Revision == r.Revision && e.Config.Mode != r.Config.Mode && e.Config.SameExceptMode(r.Config));
            try { Repo.Save(Current); LastSavedPath = Repo.DirectoryPath; }
            catch (Exception e) { Debug.LogWarning("[ImpossibleLab] auto save failed: " + e.Message); ShowToast("実験記録の自動保存に失敗しました"); }
            Sm.Transition(LabState.ReviewingResult);
        }

        RainExperimentResult FindComparable(RainExperimentConfig cfg, int revision, bool sameRevision) =>
            Current.Experiments.LastOrDefault(e => cfg.SameConditions(e.Config) && (sameRevision ? e.Revision == revision : e.Revision != revision));


        public void RerunSameConditions() { if (Sm.State == LabState.ReviewingResult) StartExperiment(); }

        public void RerunOtherMode()
        {
            if (Sm.State != LabState.ReviewingResult) return;
            TestConfig.Mode = TestConfig.Mode == LabModes.Dream ? LabModes.Reality : LabModes.Dream;
            StartExperiment();
        }

        public void ImproveAndRetest()
        {
            if (Sm.State != LabState.ReviewingResult) return;
            ResetDeviceInRainLab();
            StartCoroutine(MoveArea(toRainLab: false, then: () => Sm.Transition(LabState.Editing)));
        }

        void ResetDeviceInRainLab()
        {
            if (Course != null) Course.Cleanup();
            if (View == null) return;
            View.SetActive(false);
            View.ExtraOffset = Vector3.zero;
            var follow = View.Get<FollowHeadBehavior>();
            if (follow != null) follow.Target = null;
        }

        /// <summary>実験からの改善案（実測値に基づく単純な規則。AIではない）。</summary>
        public List<string> ImprovementHints(RainExperimentResult r)
        {
            var hints = new List<string>();
            if (r == null) return hints;
            if (!r.IsRain) return CourseSimulator.Hints(r);
            int total = Mathf.Max(1, r.DeviceHits);
            if (r.HeadHits > total * 0.3f) hints.Add("頭部に雨が当たっています →「防ぐ範囲を広げて」");
            if (r.LowerHits + r.UpperHits > total * 0.5f && r.Config.WindSpeed > 1f) hints.Add("横からの雨が多い →「横からの雨も防いで」");
            if (r.DriftM > 0.15f) hints.Add($"風で装置が {r.DriftM:0.00} m 流されています →「風に流されないようにして」");
            if (r.Limits.Count > 0) hints.Add("REALITYの制約で性能が制限されています（制限内容を確認）");
            if (hints.Count == 0) hints.Add("大きな問題は見つかりませんでした。風速や雨量を上げて試してください");
            return hints;
        }

        // ================= S06 保存・ギャラリー =================
        public void Save()
        {
            if (Current == null) return;
            if (Sm.State != LabState.Editing && Sm.State != LabState.ReviewingResult) return;
            // 承認待ちの改良案があるまま保存すると、改良が反映されていないことに気づきにくい（実機で発生）
            if (PendingRevision != null)
            {
                ShowToast("未適用の改良案があります。「承認して適用」か「却下」を選んでから保存してください");
                Notify();
                return;
            }
            try
            {
                Repo.Save(Current);
                LastSavedPath = Repo.DirectoryPath;
                if (InRainLab) StartCoroutine(MoveArea(false, () => Sm.Transition(LabState.Saved)));
                else Sm.Transition(LabState.Saved);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ImpossibleLab] save failed: " + e.Message);
                ShowToast("保存に失敗しました。ストレージの空き容量を確認してください");
            }
        }

        public void OpenGallery()
        {
            if (Sm.Busy) return;
            if (DeviceRunning) ToggleDevice();
            if (Sm.State == LabState.Editing || Sm.State == LabState.Saved || Sm.State == LabState.Drafting || Sm.State == LabState.Idle || Sm.State == LabState.Error)
            {
                RefreshGallery();
                Sm.Transition(LabState.Gallery);
            }
        }

        public void RefreshGallery()
        {
            try { GalleryEntries = Repo.List(); }
            catch (Exception e) { GalleryEntries = new List<GalleryEntry>(); ShowToast("保存データの一覧を取得できません: " + e.Message); }
            Notify();
        }

        public void LoadFromGallery(string id)
        {
            var inv = Repo.Load(id, out var err);
            if (inv == null) { ShowToast("読み込めません：" + err); return; }
            Current = inv;
            LastResult = inv.Experiments.LastOrDefault();
            BaselineResult = OtherModeResult = null;
            PendingRevision = null; PendingProposal = null;
            ClearView();
            var r = _builder.Build(inv.Current, DisplayPoint);
            View = r.View;
            if (View != null) SetupGrab(View);
            SetHologram(false, null);
            Sm.Transition(LabState.Editing);
            ShowToast($"「{inv.Current.Name}」Rev.{inv.Current.Revision} を読み込みました");
        }

        public void Duplicate(string id)
        {
            var inv = Repo.Load(id, out var err);
            if (inv == null) { ShowToast("複製できません：" + err); return; }
            var copy = inv.Current.Clone();
            copy.Id = Repo.NewCopyId(inv.Current.Id);
            copy.Name = (inv.Current.Name.Length > 34 ? inv.Current.Name.Substring(0, 34) : inv.Current.Name) + "（複製）";
            Repo.Save(new SavedInvention { Current = copy });
            RefreshGallery();
            ShowToast("複製しました");
        }

        public void AskDelete(string id) { PendingDeleteId = id; Notify(); }
        public void CancelDelete() { PendingDeleteId = null; Notify(); }
        public void ConfirmDelete()
        {
            if (PendingDeleteId == null) return;
            bool ok = Repo.Delete(PendingDeleteId);
            if (Current != null && Current.Current.Id == PendingDeleteId) { Current = null; ClearView(); }
            PendingDeleteId = null;
            RefreshGallery();
            ShowToast(ok ? "削除しました" : "削除できませんでした");
        }

        public void CloseGallery()
        {
            if (Sm.State != LabState.Gallery) return;
            PendingDeleteId = null;
            if (Current != null) Sm.Transition(LabState.Editing);
            else { Sm.Transition(LabState.Drafting); SetHologram(true, null); }
        }

        public void ContinueEditing() { if (Sm.State == LabState.Saved) Sm.Transition(LabState.Editing); }

        public void NewInvention()
        {
            if (Sm.Busy) return;
            if (Sm.State != LabState.Saved && Sm.State != LabState.Editing && Sm.State != LabState.Gallery) return;
            ClearView();
            Current = null; LastResult = null; RequestText = "";
            ResetStages();
            Sm.Transition(LabState.Drafting);
            SetHologram(true, null);
        }

        // ================= エラー =================
        void ShowError(string message, List<string> suggestions)
        {
            ErrorMessage = message;
            ErrorSuggestions = suggestions ?? new List<string>();
            Sm.Transition(LabState.Error);
        }

        public void DismissError()
        {
            if (Sm.State != LabState.Error) return;
            Sm.Transition(Current != null ? LabState.Editing : LabState.Drafting);
        }

        public void ShowToast(string msg) { Toast = msg; _toastUntil = Time.time + 4f; Notify(); }
        float _toastUntil;

        // ================= 空間・演出 =================
        IEnumerator MoveArea(bool toRainLab, Action then)
        {
            _transitioning = true;
            if (toRainLab) yield return AnimateGate(1f);
            if (Fader != null) yield return Fader.FadeTo(1f);
            var spot = toRainLab ? RainLab.PlayerSpot : WorkshopPlayerSpot;
            TeleportRig(spot);
            if (!toRainLab && Course != null) Course.Cleanup();
            if (!toRainLab && View != null)
            {
                View.SetActive(false);
                View.transform.SetPositionAndRotation(DisplayPoint.position, DisplayPoint.rotation);
            }
            InRainLab = toRainLab;
            RecenterConsole(toRainLab ? RainLab.ConsoleSpot : WorkshopConsoleSpot);
            then?.Invoke();
            if (Fader != null) yield return Fader.FadeTo(0f);
            if (!toRainLab) yield return AnimateGate(0f);
            _transitioning = false;
            Notify();
        }

        IEnumerator AnimateGate(float open)
        {
            if (GateDoorLeft == null || GateDoorRight == null) yield break;
            float t = 0f;
            var l0 = GateDoorLeft.localPosition; var r0 = GateDoorRight.localPosition;
            var l1 = new Vector3(-0.6f - 0.9f * open, l0.y, l0.z); var r1 = new Vector3(0.6f + 0.9f * open, r0.y, r0.z);
            while (t < 0.6f)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0, 1, t / 0.6f);
                GateDoorLeft.localPosition = Vector3.Lerp(l0, l1, k);
                GateDoorRight.localPosition = Vector3.Lerp(r0, r1, k);
                yield return null;
            }
        }

        void TeleportRig(Transform spot)
        {
            if (Origin == null || spot == null) return;
            Origin.MatchOriginUpCameraForward(Vector3.up, spot.forward);
            var cam = Origin.Camera.transform.position;
            Origin.MoveCameraToWorldLocation(new Vector3(spot.position.x, cam.y, spot.position.z));
        }

        /// <summary>
        /// メイン画面中心の高さは常に「今の目の高さ − ConsoleBelowEye + 利用者の好み」。床の高さ・座位・身長に依存しない。
        /// 好みは上げても画面中心が目より下に残る範囲に制限する（頭上に出ない）。
        /// </summary>
        public const float ConsoleBelowEye = 0.5f, HeightStep = 0.1f;
        public const float MaxRaise = 0.35f, MaxLower = 0.7f;
        const string HeightPrefKey = "ilab.consoleHeightOffset.v2"; // 旧キーの値（頭上の原因になり得る）は使わない

        /// <summary>利用者が調整した高さ（目の高さ基準からの差, m）。PlayerPrefs に保存して次回も使う。</summary>
        public float ConsoleHeightOffset
        {
            get { try { return Mathf.Clamp(PlayerPrefs.GetFloat(HeightPrefKey, 0f), -MaxLower, MaxRaise); } catch { return 0f; } }
            private set { try { PlayerPrefs.SetFloat(HeightPrefKey, Mathf.Clamp(value, -MaxLower, MaxRaise)); PlayerPrefs.Save(); } catch { } }
        }

        float ConsoleYForEye(float eyeY) => eyeY - ConsoleBelowEye + ConsoleHeightOffset;

        /// <summary>フッターの「下げる／上げる」、右コントローラーの A／B。</summary>
        public void NudgeConsoleHeight(int dir)
        {
            if (ConsoleRoot == null) return;
            var p = ConsoleRoot.position;
            if (HeadTracked(out float eyeY))
            {
                float before = ConsoleHeightOffset;
                ConsoleHeightOffset = before + HeightStep * dir;
                ConsoleRoot.position = new Vector3(p.x, ConsoleYForEye(eyeY), p.z);
                bool atLimit = Mathf.Approximately(before, ConsoleHeightOffset);
                ShowToast(atLimit ? (dir < 0 ? "これ以上は下げられません" : "これ以上は上げられません") : dir < 0 ? "パネルを下げました" : "パネルを上げました");
            }
            else
            {
                ConsoleRoot.position = p + Vector3.up * (HeightStep * dir);
                ShowToast(dir < 0 ? "パネルを下げました" : "パネルを上げました");
            }
            Debug.Log($"[ILAB] nudge dir={dir} console={ConsoleRoot.position.y:F2} offset={ConsoleHeightOffset:F2}");
        }

        /// <summary>移動バーを離したとき：その高さを好みとして保存（上限を超えていたら上限の高さに直す）。</summary>
        public void RememberConsoleHeight()
        {
            if (ConsoleRoot == null || !HeadTracked(out float eyeY)) return;
            ConsoleHeightOffset = ConsoleRoot.position.y - (eyeY - ConsoleBelowEye);
            var p = ConsoleRoot.position;
            ConsoleRoot.position = new Vector3(p.x, ConsoleYForEye(eyeY), p.z);
            Debug.Log($"[ILAB] console height saved eye={eyeY:F2} console={ConsoleRoot.position.y:F2} offset={ConsoleHeightOffset:F2}");
        }

        void RecenterConsole(Transform spot)
        {
            if (ConsoleRoot == null || spot == null) return;
            var pos = spot.position;
            if (HeadTracked(out float eyeY)) pos.y = ConsoleYForEye(eyeY);
            else if (Seated) pos += Vector3.down * 0.35f;
            ConsoleRoot.SetPositionAndRotation(pos, spot.rotation);
        }

        // ---- 高さの見張り：起動直後・ヘッドセットを付け直した直後・座る/立つで目の高さが変わったとき、画面の高さを目に合わせ直す ----
        bool _consolePlaced;
        float _settleUntil, _offFor, _nextHeartbeat;

        bool ConsoleBeingDragged()
        {
            var grab = ConsoleRoot != null ? ConsoleRoot.GetComponentInChildren<ConsoleGrabHandle>() : null;
            return grab != null && grab.Dragging;
        }

        void KeepConsoleAtEyeLevel()
        {
            if (!_consolePlaced || ConsoleRoot == null || _transitioning || ConsoleBeingDragged() || !HeadTracked(out float eyeY)) return;
            float now = Time.realtimeSinceStartup;
            float diff = ConsoleRoot.position.y - ConsoleYForEye(eyeY);
            bool settling = now < _settleUntil;
            // 直後はすぐ直す。普段は頭の上下動では動かさず、座る・立つなどで大きくずれたままのときだけ直す
            if (Mathf.Abs(diff) > (settling ? 0.12f : 0.3f)) _offFor += Time.unscaledDeltaTime; else _offFor = 0f;
            if (_offFor > (settling ? 0.2f : 1.5f))
            {
                _offFor = 0f;
                var p = ConsoleRoot.position;
                ConsoleRoot.position = new Vector3(p.x, ConsoleYForEye(eyeY), p.z);
                Debug.Log($"[ILAB] height fix eye={eyeY:F2} from={p.y:F2} to={ConsoleRoot.position.y:F2} settling={settling}");
            }
            if (now > _nextHeartbeat && now < 60f)
            {
                _nextHeartbeat = now + 5f;
                Debug.Log($"[ILAB] heartbeat eye={eyeY:F2} camLocal={Origin.Camera.transform.localPosition:F2} console={ConsoleRoot.position:F2} offset={ConsoleHeightOffset:F2} mode={Origin.CurrentTrackingOriginMode}");
            }
        }

        // ヘッドセットを外して付け直したとき（アプリが一時停止→再開）も、もう一度合わせる
        void OnApplicationPause(bool paused) { if (!paused) _settleUntil = Time.realtimeSinceStartup + 5f; }
        void OnApplicationFocus(bool focus) { if (focus) _settleUntil = Time.realtimeSinceStartup + 5f; }

        // [ILAB] ログを persistentDataPath/ilab_log.txt にも残す（logcat が流れても後から確認できるように）
        string _logPath;
        void OnEnable()
        {
            try
            {
                _logPath = System.IO.Path.Combine(Application.persistentDataPath, "ilab_log.txt");
                if (System.IO.File.Exists(_logPath)) System.IO.File.Copy(_logPath, _logPath + ".prev", true);
                System.IO.File.WriteAllText(_logPath, $"start {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
            }
            catch { _logPath = null; }
            Application.logMessageReceived += WriteIlabLog;
        }
        void OnDisable() { Application.logMessageReceived -= WriteIlabLog; }
        void WriteIlabLog(string msg, string stack, LogType type)
        {
            if (_logPath == null || msg == null || !msg.StartsWith("[ILAB]")) return;
            try { System.IO.File.AppendAllText(_logPath, $"{Time.realtimeSinceStartup:F1} {msg}\n"); } catch { }
        }

        bool HeadTracked(out float eyeY)
        {
            eyeY = 0f;
            if (Origin == null || Origin.Camera == null) return false;
            var cam = Origin.Camera.transform;
            if (cam.localPosition.sqrMagnitude < 1e-6f) return false; // まだ頭の姿勢が来ていない
            eyeY = cam.position.y;
            return true;
        }

        /// <summary>現在の頭の位置に合わせてパネルを正面約1.2mへ再配置（身長・座位対応, 仕様21.3）。</summary>
        public void RecenterToHead()
        {
            if (Origin == null || ConsoleRoot == null) return;
            var head = Origin.Camera.transform;
            var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            var pos = head.position + fwd * 1.2f;
            pos.y = ConsoleYForEye(head.position.y);
            ConsoleRoot.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd));
            Debug.Log($"[ILAB] recenter to head eye={head.position:F2} console={pos:F2}");
            ShowToast("パネルを正面に合わせました");
        }

        public void ToggleSeated()
        {
            Seated = !Seated;
            if (Origin != null && Origin.CameraFloorOffsetObject != null)
            {
                var t = Origin.CameraFloorOffsetObject.transform;
                t.localPosition += Vector3.up * (Seated ? 0.4f : -0.4f);
            }
            RecenterConsole(InRainLab ? RainLab.ConsoleSpot : WorkshopConsoleSpot);
            ShowToast(Seated ? "座位モード：視点を40cm上げました" : "立位モード");
        }

        void SetHologram(bool on, string text)
        {
            if (Hologram == null) return;
            Hologram.SetActive(on);
            var t = Hologram.GetComponentInChildren<TMPro.TMP_Text>();
            if (t != null) t.text = text ?? "AI INVENTOR\n<size=60%>どんな発明をしますか？</size>";
        }

        void PulseArms(int i)
        {
            if (RobotArmJoints == null) return;
            foreach (var j in RobotArmJoints) if (j != null) j.localRotation = Quaternion.Euler(-25f + 12f * (i % 3), 0, 0);
        }

        void Update()
        {
            if (Toast != null && Time.time > _toastUntil) { Toast = null; Notify(); }
            KeepConsoleAtEyeLevel();
            // 円環照明はイベント時（生成・組み立て・実験）だけ明るくする
            bool evt = Sm.State == LabState.GeneratingDesign || Sm.State == LabState.Building;
            _ringPulse = Mathf.MoveTowards(_ringPulse, evt ? 1f : 0f, Time.deltaTime * 1.5f);
            if (RingLight != null && RingLight.sharedMaterial != null)
            {
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_EmissionColor", LabTheme.AccentPrimary * Mathf.Lerp(0.25f, 2.2f, _ringPulse));
                RingLight.SetPropertyBlock(mpb);
            }
            if (RobotArmJoints != null && !evt)
                foreach (var j in RobotArmJoints) if (j != null) j.localRotation = Quaternion.Slerp(j.localRotation, Quaternion.identity, Time.deltaTime * 2f);
        }

        void CancelPending() { _cts?.Cancel(); _cts?.Dispose(); }
    }
}
