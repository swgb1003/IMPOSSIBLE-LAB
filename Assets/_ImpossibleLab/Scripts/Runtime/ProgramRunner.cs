using System.Collections.Generic;
using System.Linq;
using ImpossibleLab.Core;
using UnityEngine;

namespace ImpossibleLab
{
    /// <summary>
    /// 研究室での試用（仕様 28.6）：実験と同じ MotionInterpreter を固定刻み（0.02 秒）で動かし、関節の値を試作品に反映する。
    /// センサー値は研究室内の簡易な値：対象までの距離＝プレイヤー（カメラ）までの水平距離、明るさ・気温は研究室の決まった値。
    /// 起動（試用）している間だけ動く。止めると関節を初期値に戻す。
    /// </summary>
    public sealed class ProgramRunner : MonoBehaviour, IInventionBehavior
    {
        public string BehaviorType => "program";
        InventionView _view;
        MotionInterpreter _it;
        LabSensors _sensors;
        float _acc;
        int _shownEvents;
        public bool Running { get; private set; }
        public MotionInterpreter Interpreter => _it;
        /// <summary>試用中に新しく起きた発火の説明（LabApp が通知に出す）。</summary>
        public event System.Action<ProgramEvent> Fired;
        /// <summary>検証用：カメラの代わりに使う「対象」の位置（null ならメインカメラ）。</summary>
        public Transform TargetOverride;

        public void Configure(BehaviorSpec spec, InventionView view)
        {
            _view = view;
        }

        public void Activate()
        {
            if (_view == null || _view.Dna?.Program == null) return;
            _it = new MotionInterpreter(_view.Dna);
            _sensors = new LabSensors { Runner = this };
            _acc = 0f; _shownEvents = 0;
            Running = true;
        }

        public void Deactivate()
        {
            Running = false;
            _view?.ResetJoints();
        }

        // ---- S03 の「動かしてみる」：条件に関係なく1つのルールを起動し、終わったら初期値に戻す ----
        MotionInterpreter _preview;
        float _previewHold;
        public bool Previewing => _preview != null;

        public bool Preview(string ruleId)
        {
            if (Running || _view?.Dna?.Program == null) return false;
            _preview = new MotionInterpreter(_view.Dna);
            if (!_preview.ForceStart(ruleId)) { _preview = null; return false; }
            _acc = 0f; _previewHold = 0.6f;
            return true;
        }

        sealed class NoSensors : IProgramSensors { public bool TryGet(string s, out float v) { v = 0f; return false; } }
        static readonly NoSensors None = new NoSensors();

        void UpdatePreview()
        {
            _acc += Mathf.Min(Time.deltaTime, 0.1f);
            while (_acc >= MotionInterpreter.Step) { _acc -= MotionInterpreter.Step; _preview.Tick(None); }
            foreach (var kv in _preview.JointValues) _view.SetJoint(kv.Key, kv.Value);
            bool done = !_preview.Program_AnyRunning() && _preview.JointValues.Keys.All(k => _preview.JointAtTarget(k));
            if (done && (_previewHold -= Time.deltaTime) <= 0f) { _preview = null; _view.ResetJoints(); }
        }

        void Update()
        {
            if (_preview != null && !Running) { UpdatePreview(); return; }
            if (!Running || _it == null) return;
            _acc += Mathf.Min(Time.deltaTime, 0.1f); // 引っかかったときに一気に進めない
            while (_acc >= MotionInterpreter.Step && !_it.Stopped)
            {
                _acc -= MotionInterpreter.Step;
                _it.Tick(_sensors);
            }
            foreach (var kv in _it.JointValues) _view.SetJoint(kv.Key, kv.Value);
            for (; _shownEvents < _it.Events.Count; _shownEvents++)
            {
                var e = _it.Events[_shownEvents];
                if (e.Kind == "start" || e.Kind == "stopped") Fired?.Invoke(e);
            }
            if (_it.Stopped) Running = false;
        }

        /// <summary>研究室内の簡易なセンサー。</summary>
        sealed class LabSensors : IProgramSensors
        {
            public ProgramRunner Runner;

            public bool TryGet(string sensor, out float value)
            {
                var dna = Runner._view.Dna;
                switch (sensor)
                {
                    case "target_distance":
                    case "target_detected":
                    {
                        var target = Runner.TargetOverride != null ? Runner.TargetOverride : (Camera.main != null ? Camera.main.transform : null);
                        if (target == null) { value = 0; return false; }
                        var from = Runner._view.transform.position;
                        var d = new Vector2(target.position.x - from.x, target.position.z - from.z).magnitude;
                        if (sensor == "target_distance") { value = d; return true; }
                        value = d <= dna.GetParameter("detect", "rangeM", 0f) ? 1f : 0f; return true;
                    }
                    case "ambient_light": value = 500f; return true;   // 研究室の照明（屋内のおよその明るさ）
                    case "temperature": value = 22f; return true;
                    case "rain": value = 0f; return true;
                    case "user_moving": value = 0f; return true;
                    case "holding": value = 0f; return true;
                    case "battery_percent": value = 100f; return true;
                    default: value = 0f; return false;
                }
            }
        }
    }
}
