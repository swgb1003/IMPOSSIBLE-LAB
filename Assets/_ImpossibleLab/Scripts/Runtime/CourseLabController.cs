using System;
using System.Collections.Generic;
using ImpossibleLab.Core;
using TMPro;
using UnityEngine;

namespace ImpossibleLab
{
    /// <summary>
    /// ObstacleCourse / FlightDeck（実験場 TestZone 内）。CourseSimulator が計算した軌跡をそのまま再生する（見た目と計測を一致させる）。
    /// 障害物・案内役・人型モデルは実行時にプリミティブで作り、終わったら片付ける。雨の判定用マネキンは実験中だけ隠す。
    /// </summary>
    public sealed class CourseLabController : MonoBehaviour
    {
        LabAssets _assets;
        RainLabController _rain;
        Transform _props, _leader, _pilot;
        readonly List<Renderer> _obstacleRenderers = new List<Renderer>();
        CourseRun _run;
        InventionView _view;
        float _elapsed, _flash;
        int _frame, _collisionsShown;
        TextMeshProUGUI _sign;
        string _signDefault;

        public bool Running => _run != null && _frame < _run.Frames.Count;
        public CourseRun Run => _run;
        public float Elapsed => _elapsed;
        public int CollisionsSoFar => _collisionsShown;
        public CourseFrame? CurrentFrame => _run != null && _run.Frames.Count > 0 ? _run.Frames[Mathf.Min(_frame, _run.Frames.Count - 1)] : (CourseFrame?)null;
        public event Action<RainExperimentResult> Completed;
        public event Action Collided;

        public void Init(LabAssets assets, RainLabController rain)
        {
            _assets = assets; _rain = rain;
            var sign = transform.Find("Sign_RainLab");
            if (sign != null) { _sign = sign.GetComponentInChildren<TextMeshProUGUI>(true); _signDefault = _sign != null ? _sign.text : null; }
        }

        /// <summary>組み立て式の実験（ScenarioLab）で、発明品を人型モデルのどこに見せるか（取り付け位置から決める見た目だけのずれ）。</summary>
        Vector3 _mountOffset;

        public void Begin(CourseRun run, InventionView view, string mount = null)
        {
            Cleanup();
            _run = run; _view = view; _elapsed = 0f; _frame = 0; _collisionsShown = 0;
            // 地面を走る・置く発明は、発明の中心より下にある部品（車輪など）が床に埋まらないよう、いちばん下を床に合わせる
            _floorLift = mount == "ground" || mount == "fixed" || (mount == null && run.Result.Environment == LabEnvironments.ObstacleCourse) ? FloorLift(view) : 0f;
            SetMannequin(false);
            _props = new GameObject("CourseProps").transform;
            _props.SetParent(transform, false);
            if (run.Result.IsScenarioLab) { BeginScenarioLab(run, mount); return; }
            bool carrier = run.Result.Environment == LabEnvironments.ObstacleCourse;
            if (_sign != null) _sign.text = carrier ? "TEST ZONE ｜ 障害物コース" : "TEST ZONE ｜ 飛行デッキ（観察）";
            var L = run.Layout;
            Pad("StartPad", L.Start, _assets != null ? _assets.AccentBlue : null);
            Pad("GoalPad", L.Goal, _assets != null ? _assets.AccentOrange : null);
            for (int i = 1; i < L.Route.Count; i++)
            {
                var a = L.Route[i - 1]; var b = L.Route[i];
                int n = Mathf.Max(1, Mathf.FloorToInt((b - a).magnitude / 0.5f));
                for (int k = 0; k < n; k++)
                {
                    var p = Vector2.Lerp(a, b, (float)k / n);
                    Prim(PrimitiveType.Cube, "RouteDot", new Vector3(p.x, 0.004f, p.y), new Vector3(0.06f, 0.006f, 0.06f), _assets != null ? _assets.DarkMetal : null);
                }
            }
            _obstacleRenderers.Clear();
            foreach (var o in L.Obstacles)
            {
                var go = Prim(PrimitiveType.Cylinder, "Obstacle", new Vector3(o.x, 0.35f, o.y), new Vector3(o.z * 2f, 0.35f, o.z * 2f), _assets != null ? _assets.AccentOrange : null);
                _obstacleRenderers.Add(go.GetComponent<Renderer>());
            }
            if (carrier) _leader = Figure("Leader", _assets != null ? _assets.Mannequin : null);
            else _pilot = Figure("Pilot", _assets != null ? _assets.Mannequin : null);
            if (_view != null)
            {
                _view.SetActive(false); // 試用の追従・浮遊は止め、軌跡で動かす
                _view.ExtraOffset = Vector3.zero;
            }
            Apply(run.Frames.Count > 0 ? run.Frames[0] : default);
        }

        /// <summary>
        /// 組み立て式の実験の見た目（プリミティブのみ。明るさ・日差しの見た目は7週目）。計測は開始時に計算済みで、ここでは記録を再生するだけ。
        /// </summary>
        float _floorLift;

        /// <summary>発明の原点から、いちばん下の見た目までの高さ（下にはみ出していなければ 0）。</summary>
        static float FloorLift(InventionView view)
        {
            if (view == null) return 0f;
            float min = float.MaxValue;
            foreach (var r in view.GetComponentsInChildren<Renderer>())
                if (r.enabled && r.gameObject.activeInHierarchy) min = Mathf.Min(min, r.bounds.min.y);
            return min == float.MaxValue ? 0f : Mathf.Max(0f, view.transform.position.y - min);
        }

        void BeginScenarioLab(CourseRun run, string mount)
        {
            var plan = run.Result.Plan;
            string scenario = plan?.Scenario ?? run.Result.Scenario;
            if (_sign != null) _sign.text = "TEST ZONE ｜ " + PlanCandidates.ScenarioLabel(scenario);
            // 人型モデル（雨の判定用マネキン）は、物を落とす実験以外では立たせておく。近づいて離れる実験では、動く人型モデルを別に置く
            SetMannequin(scenario != ExperimentCatalog.DropTest && scenario != ExperimentCatalog.ApproachDevice);
            if (scenario == ExperimentCatalog.ApproachDevice) _leader = Figure("Approacher", _assets != null ? _assets.Mannequin : null);
            _mountOffset = Vector3.zero;
            if (scenario == ExperimentCatalog.Stand || scenario == ExperimentCatalog.SitStill)
                _mountOffset = mount == "back" ? new Vector3(0f, 0f, -0.22f) : mount == "head" || mount == "air" ? Vector3.zero : new Vector3(0f, 0f, 0.28f);
            else if (scenario == ExperimentCatalog.LiftTask) _mountOffset = new Vector3(0.35f, 0f, 0.35f); // 荷物と一緒に動く
            var L = run.Layout;
            if (L != null)
            {
                foreach (var t in L.Targets)
                    Prim(PrimitiveType.Cylinder, "Target", new Vector3(t.x, 0.5f, t.y), new Vector3(0.24f, 0.5f, 0.24f), _assets != null ? _assets.AccentBlue : null);
                _obstacleRenderers.Clear();
                foreach (var o in L.Obstacles)
                {
                    var go = Prim(PrimitiveType.Cylinder, "Obstacle", new Vector3(o.x, 0.6f, o.y), new Vector3(o.z * 2f, 0.6f, o.z * 2f), _assets != null ? _assets.AccentOrange : null);
                    _obstacleRenderers.Add(go.GetComponent<Renderer>());
                }
            }
            if (scenario == ExperimentCatalog.LiftTask && plan != null)
            {
                float h = plan.Get("shelf", "heightM");
                Prim(PrimitiveType.Cube, "Shelf", new Vector3(0.9f, h * 0.5f, 0.6f), new Vector3(0.8f, h, 0.5f), _assets != null ? _assets.DarkMetal : null);
                float size = plan.Get("payload", "sizeM");
                _payload = Prim(PrimitiveType.Cube, "Payload", new Vector3(0.9f, size * 0.5f, 0.6f), Vector3.one * size, _assets != null ? _assets.AccentOrange : null).transform;
            }
            if (scenario == ExperimentCatalog.ClimbSlope && plan != null)
            {
                // 坂：中央を x = 0 に置き、z 軸まわりに傾けた板（計算と同じ寸法）
                float a = plan.Get("slope", "angleDeg"), len = plan.Get("slope", "lengthM"), th = a * Mathf.Deg2Rad;
                var ramp = Prim(PrimitiveType.Cube, "Slope", new Vector3(0f, 0.5f * len * Mathf.Sin(th) - 0.02f, ClimbSlopeScenario.LaneZ), new Vector3(len, 0.04f, 1.0f), _assets != null ? _assets.DarkMetal : null);
                ramp.transform.localRotation = Quaternion.Euler(0f, 0f, a);
                if (plan.Has("payload"))
                {
                    float size = plan.Get("payload", "sizeM");
                    _payload = Prim(PrimitiveType.Cube, "Payload", Vector3.zero, Vector3.one * size, _assets != null ? _assets.AccentOrange : null).transform;
                }
            }
            if (scenario == ExperimentCatalog.DropTest)
                Prim(PrimitiveType.Cylinder, "DropPad", new Vector3(0f, 0.005f, 0f), new Vector3(0.9f, 0.005f, 0.9f), _assets != null ? _assets.AccentOrange : null);
            if (_view != null) { _view.SetActive(false); _view.ExtraOffset = Vector3.zero; }
            Apply(run.Frames.Count > 0 ? run.Frames[0] : default);
        }

        Transform _payload;

        public void Abort() { Cleanup(); }

        public void Cleanup()
        {
            _run = null;
            if (_props != null) Destroy(_props.gameObject);
            _props = null; _leader = null; _pilot = null; _payload = null;
            _obstacleRenderers.Clear();
            SetMannequin(true);
            if (_sign != null && _signDefault != null) _sign.text = _signDefault;
        }

        void Update()
        {
            if (_flash > 0f) { _flash -= Time.deltaTime; if (_flash <= 0f) Flash(false); }
            if (_run == null || _frame >= _run.Frames.Count) return;
            _elapsed += Time.deltaTime;
            while (_frame < _run.Frames.Count - 1 && _run.Frames[_frame + 1].Time <= _elapsed)
            {
                _frame++;
                if (_run.Frames[_frame].Collision) { _collisionsShown++; _flash = 0.3f; Flash(true); Collided?.Invoke(); }
            }
            Apply(Interpolated());
            if (_elapsed >= _run.Duration)
            {
                _frame = _run.Frames.Count;
                Completed?.Invoke(_run.Result);
            }
        }

        CourseFrame Interpolated()
        {
            var a = _run.Frames[_frame];
            if (_frame + 1 >= _run.Frames.Count) return a;
            var b = _run.Frames[_frame + 1];
            float k = Mathf.Clamp01((_elapsed - a.Time) / Mathf.Max(1e-4f, b.Time - a.Time));
            float[] joints = a.Joints;
            if (a.Joints != null && b.Joints != null && a.Joints.Length == b.Joints.Length)
            {
                joints = new float[a.Joints.Length];
                for (int i = 0; i < joints.Length; i++) joints[i] = Mathf.Lerp(a.Joints[i], b.Joints[i], k);
            }
            return new CourseFrame { Time = _elapsed, Device = Vector3.Lerp(a.Device, b.Device, k), Leader = Vector3.Lerp(a.Leader, b.Leader, k), YawDeg = Mathf.LerpAngle(a.YawDeg, b.YawDeg, k), Joints = joints };
        }

        void Apply(CourseFrame f)
        {
            if (_run == null) return;
            if (_run.Result.IsScenarioLab)
            {
                if (_payload != null && _run.Result.Scenario == ExperimentCatalog.ClimbSlope)
                    _payload.localPosition = f.Device + Vector3.up * (0.25f + _payload.localScale.y * 0.5f); // 発明品の上に載せる
                else if (_payload != null)
                {
                    // 持ち上げ：荷物は記録した高さ（床から棚の高さまで）。棚の前で持ち上げ、上がりきったら棚に載せる
                    var plan = _run.Result.Plan;
                    float shelf = plan != null ? plan.Get("shelf", "heightM") : 0.8f, size = _payload.localScale.y;
                    bool onShelf = f.Device.y >= shelf - 1e-3f;
                    _payload.localPosition = new Vector3(onShelf ? 0.9f : 0.35f, f.Device.y + size * 0.5f, onShelf ? 0.6f : 0.35f);
                }
                if (_view != null) _view.transform.SetPositionAndRotation(transform.TransformPoint(f.Device + _mountOffset + Vector3.up * _floorLift), transform.rotation);
                if (_leader != null && _run.Result.Scenario == ExperimentCatalog.ApproachDevice)
                {
                    _leader.localPosition = f.Leader;
                    var toDevice = new Vector3(-f.Leader.x, 0f, -f.Leader.z); // 発明（原点）の方を向く
                    _leader.localRotation = Quaternion.LookRotation(toDevice.sqrMagnitude > 1e-6f ? toDevice : Vector3.back);
                }
                // 動作プログラムの関節（記録の並びは Result.JointIds）
                if (_view != null && f.Joints != null)
                    for (int i = 0; i < f.Joints.Length && i < _run.Result.JointIds.Count; i++) _view.SetJoint(_run.Result.JointIds[i], f.Joints[i]);
                return;
            }
            if (_run.Result.Environment == LabEnvironments.ObstacleCourse)
            {
                if (_view != null) _view.transform.SetPositionAndRotation(transform.TransformPoint(f.Device + Vector3.up * _floorLift), transform.rotation * Quaternion.Euler(0f, f.YawDeg, 0f));
                if (_leader != null)
                {
                    var dir = f.Leader - (_leader.localPosition);
                    _leader.localPosition = f.Leader;
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 1e-6f) _leader.localRotation = Quaternion.LookRotation(dir);
                }
            }
            else
            {
                // 人型モデルの足元に靴を置く。進行方向（+x）を向き、横揺れで傾ける
                var rot = transform.rotation * Quaternion.Euler(0f, 90f, 0f) * Quaternion.Euler(0f, 0f, -f.YawDeg);
                if (_pilot != null) { _pilot.localPosition = f.Device; _pilot.rotation = rot; }
                if (_view != null) _view.transform.SetPositionAndRotation(transform.TransformPoint(f.Device), rot);
            }
        }

        void SetMannequin(bool on)
        {
            if (_rain != null && _rain.MannequinHeadTop != null && _rain.MannequinHeadTop.parent != null)
                _rain.MannequinHeadTop.parent.gameObject.SetActive(on);
        }

        void Flash(bool on)
        {
            if (_assets == null) return;
            foreach (var r in _obstacleRenderers) if (r != null) r.sharedMaterial = on ? _assets.MannequinHit : _assets.AccentOrange;
        }

        void Pad(string name, Vector2 p, Material m) =>
            Prim(PrimitiveType.Cylinder, name, new Vector3(p.x, 0.005f, p.y), new Vector3(0.9f, 0.005f, 0.9f), m);

        /// <summary>簡易人型（高さ約1.6m、足元が原点）。</summary>
        Transform Figure(string name, Material m)
        {
            var root = new GameObject(name).transform;
            root.SetParent(_props, false);
            Prim(PrimitiveType.Capsule, "Torso", new Vector3(0, 1.0f, 0), new Vector3(0.34f, 0.42f, 0.22f), m, root);
            Prim(PrimitiveType.Sphere, "Head", new Vector3(0, 1.52f, 0), Vector3.one * 0.2f, m, root);
            Prim(PrimitiveType.Capsule, "Legs", new Vector3(0, 0.42f, 0), new Vector3(0.28f, 0.4f, 0.18f), m, root);
            return root;
        }

        GameObject Prim(PrimitiveType type, string name, Vector3 localPos, Vector3 scale, Material m, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent != null ? parent : _props, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            if (m != null) r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }
    }
}
