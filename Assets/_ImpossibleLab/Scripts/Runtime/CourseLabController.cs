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

        public void Begin(CourseRun run, InventionView view)
        {
            Cleanup();
            _run = run; _view = view; _elapsed = 0f; _frame = 0; _collisionsShown = 0;
            SetMannequin(false);
            _props = new GameObject("CourseProps").transform;
            _props.SetParent(transform, false);
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

        public void Abort() { Cleanup(); }

        public void Cleanup()
        {
            _run = null;
            if (_props != null) Destroy(_props.gameObject);
            _props = null; _leader = null; _pilot = null;
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
            return new CourseFrame { Time = _elapsed, Device = Vector3.Lerp(a.Device, b.Device, k), Leader = Vector3.Lerp(a.Leader, b.Leader, k), YawDeg = Mathf.LerpAngle(a.YawDeg, b.YawDeg, k) };
        }

        void Apply(CourseFrame f)
        {
            if (_run == null) return;
            if (_run.Result.Environment == LabEnvironments.ObstacleCourse)
            {
                if (_view != null) _view.transform.SetPositionAndRotation(transform.TransformPoint(f.Device), transform.rotation * Quaternion.Euler(0f, f.YawDeg, 0f));
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
