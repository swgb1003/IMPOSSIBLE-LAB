using System.Collections.Generic;
using ImpossibleLab.Core;
using UnityEngine;

namespace ImpossibleLab
{
    public interface IInventionBehavior
    {
        string BehaviorType { get; }
        void Configure(BehaviorSpec spec, InventionView view);
        void Activate();
        void Deactivate();
    }

    public sealed class BuildResult
    {
        public InventionView View;
        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>検証済みDNAから、登録済みの部品・機能モジュールのみで試作品を組み立てる（仕様4, 24.2）。</summary>
    public sealed class InventionBuilder
    {
        readonly LabAssets _assets;
        public InventionBuilder(LabAssets assets) { _assets = assets; }

        public BuildResult Build(InventionDna dna, Transform parent)
        {
            var result = new BuildResult();
            // 防御的再検証：DnaJson を通っていないDNAは組み立てない
            var report = DnaJson.Parse(DnaJson.ToJson(dna));
            if (!report.IsValid) { result.Warnings.AddRange(report.Errors); return result; }

            var root = new GameObject("Invention_" + dna.Id);
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<InventionView>();
            view.Dna = report.Dna;

            foreach (var part in report.Dna.Parts)
            {
                var go = CreatePrimitive(part, report.Dna);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = part.Position;
                view.Parts[part.Id] = go.transform;
            }

            foreach (var b in report.Dna.Behaviors)
            {
                IInventionBehavior behavior = null;
                switch (b.Type)
                {
                    case "follow_head": behavior = root.AddComponent<FollowHeadBehavior>(); break;
                    case "rain_shield": behavior = root.AddComponent<RainShieldBehavior>(); break;
                    case "stabilize": behavior = root.AddComponent<StabilizeBehavior>(); break;
                    default: result.Warnings.Add($"機能 {b.Type} はこの版では見た目のみ（動作モジュール未実装）"); break;
                }
                if (behavior == null) continue;
                behavior.Configure(b, view);
                view.Behaviors.Add(behavior);
            }

            // 掴む用のコライダーは本体のまとまり全体に1つ
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            foreach (var r in root.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(root.transform.InverseTransformPoint(r.bounds.center));
            var col = root.AddComponent<BoxCollider>();
            float rad = Mathf.Max(0.25f, report.Dna.GetParameter("rain_shield", "radius", 0.3f));
            col.center = new Vector3(0, -0.05f, 0);
            col.size = new Vector3(rad * 2f, 0.25f, rad * 2f);
            result.View = view;
            return result;
        }

        GameObject CreatePrimitive(PartSpec part, InventionDna dna)
        {
            PrimitiveType type;
            Material mat = _assets.RobotWhite;
            Vector3 scale = part.Scale;
            Quaternion rot = Quaternion.identity;
            switch (part.Type)
            {
                case "canopy": type = PrimitiveType.Cylinder; mat = _assets.Canopy; scale.y *= 0.5f; break;
                case "body": type = PrimitiveType.Cylinder; scale.y *= 0.5f; break;
                case "wheel": type = PrimitiveType.Cylinder; rot = Quaternion.Euler(0, 0, 90); scale.y *= 0.5f; mat = _assets.DarkMetal; break;
                case "sensor": type = PrimitiveType.Sphere; mat = _assets.AccentBlue; break;
                case "stabilizer": type = PrimitiveType.Cube; mat = _assets.AccentOrange; break;
                case "thruster": type = PrimitiveType.Cylinder; scale.y *= 0.5f; mat = _assets.DarkMetal; break;
                case "arm": type = PrimitiveType.Capsule; scale.y *= 0.5f; mat = _assets.MatteMetal; break;
                case "container": type = PrimitiveType.Cube; mat = _assets.MatteMetal; break;
                default: type = PrimitiveType.Cylinder; scale.y *= 0.5f; mat = _assets.MatteMetal; break;
            }
            var go = GameObject.CreatePrimitive(type);
            go.name = $"Part_{part.Id}_{part.Type}";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        public void Clear(InventionView view)
        {
            if (view != null) Object.Destroy(view.gameObject);
        }
    }

    /// <summary>組み立てた試作品の参照。部品と機能モジュールを保持する。</summary>
    public sealed class InventionView : MonoBehaviour
    {
        public InventionDna Dna;
        public readonly Dictionary<string, Transform> Parts = new Dictionary<string, Transform>();
        public readonly List<IInventionBehavior> Behaviors = new List<IInventionBehavior>();
        public bool Active { get; private set; }
        /// <summary>REALITY の風による流れ（実験場のみ）。</summary>
        public Vector3 ExtraOffset;

        public T Get<T>() where T : class, IInventionBehavior => Behaviors.Find(b => b is T) as T;

        public void SetActive(bool on)
        {
            if (Active == on) return;
            Active = on;
            foreach (var b in Behaviors) { if (on) b.Activate(); else b.Deactivate(); }
        }

        /// <summary>組み立て演出：部品を順番に表示する。</summary>
        public void SetVisiblePartCount(int n)
        {
            int i = 0;
            foreach (var p in Parts.Values) p.gameObject.SetActive(i++ < n);
            if (n >= Parts.Count) foreach (var b in Behaviors) if (b is RainShieldBehavior rs) rs.RefreshVisual();
        }
    }

    /// <summary>follow_head：対象（ユーザーの頭／判定用マネキンの頭頂）の上に追従する。</summary>
    public sealed class FollowHeadBehavior : MonoBehaviour, IInventionBehavior
    {
        public string BehaviorType => "follow_head";
        public float HeightOffset;
        public Transform Target;
        /// <summary>目標高さ = Target.y + TargetHeadTop + HeightOffset。XRカメラは目の高さのため +0.1m。</summary>
        public float TargetHeadTop = 0.1f;
        InventionView _view;
        bool _active;
        float _stab;

        public void Configure(BehaviorSpec spec, InventionView view)
        {
            _view = view;
            HeightOffset = spec.Parameters["heightOffset"];
            _stab = view.Dna.GetParameter("stabilize", "stabilizationStrength", 0f);
        }

        public void Activate() { _active = true; }
        public void Deactivate() { _active = false; }

        void LateUpdate()
        {
            if (!_active || Target == null) return;
            var flatFwd = Vector3.ProjectOnPlane(Target.forward, Vector3.up);
            var goal = new Vector3(Target.position.x, Target.position.y + TargetHeadTop + HeightOffset, Target.position.z) + _view.ExtraOffset;
            // 安定化が弱いほど揺れる（見た目のみ。計測は RainSimulator が行う）
            float wobble = (1f - _stab) * 0.015f;
            goal += new Vector3(Mathf.Sin(Time.time * 2.1f), Mathf.Sin(Time.time * 3.3f) * 0.5f, Mathf.Cos(Time.time * 1.7f)) * wobble;
            transform.position = Vector3.Lerp(transform.position, goal, 1f - Mathf.Exp(-8f * Time.deltaTime));
            if (flatFwd.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flatFwd), 1f - Mathf.Exp(-4f * Time.deltaTime));
        }
    }

    /// <summary>rain_shield：キャノピー半径と側面カバーを見た目に反映し、起動時に展開する。判定は RainSimulator。</summary>
    public sealed class RainShieldBehavior : MonoBehaviour, IInventionBehavior
    {
        public string BehaviorType => "rain_shield";
        public float Radius, Effectiveness, SkirtLength;
        Transform _canopy, _skirt;
        float _deploy = 1f, _deployTarget = 1f;

        public void Configure(BehaviorSpec spec, InventionView view)
        {
            Radius = spec.Parameters["radius"];
            Effectiveness = spec.Parameters["effectiveness"];
            SkirtLength = spec.Parameters["skirtLength"];
            view.Parts.TryGetValue(spec.TargetPart, out _canopy);
            if (_canopy != null && SkirtLength > 0f)
            {
                var skirt = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                skirt.name = "Skirt";
                Destroy(skirt.GetComponent<Collider>());
                skirt.GetComponent<Renderer>().sharedMaterial = _canopy.GetComponent<Renderer>().sharedMaterial;
                skirt.transform.SetParent(transform, false);
                _skirt = skirt.transform;
            }
            RefreshVisual();
        }

        public void RefreshVisual()
        {
            if (_canopy == null) return;
            float r = Radius * Mathf.Lerp(0.35f, 1f, _deploy);
            var s = _canopy.localScale;
            _canopy.localScale = new Vector3(r * 2f, s.y, r * 2f);
            if (_skirt != null)
            {
                _skirt.gameObject.SetActive(_canopy.gameObject.activeSelf && _deploy > 0.5f);
                _skirt.localScale = new Vector3(r * 2f + 0.01f, SkirtLength * 0.5f * _deploy, r * 2f + 0.01f);
                _skirt.localPosition = _canopy.localPosition - new Vector3(0, SkirtLength * 0.5f * _deploy, 0);
            }
        }

        public void Activate() { _deployTarget = 1f; }
        public void Deactivate() { _deployTarget = 0.55f; }

        void Update()
        {
            if (Mathf.Approximately(_deploy, _deployTarget)) return;
            _deploy = Mathf.MoveTowards(_deploy, _deployTarget, Time.deltaTime * 1.5f);
            RefreshVisual();
        }
    }

    /// <summary>stabilize：姿勢安定ユニットの回転表示。REALITY の流され量は ModeRules で計算。</summary>
    public sealed class StabilizeBehavior : MonoBehaviour, IInventionBehavior
    {
        public string BehaviorType => "stabilize";
        public float Strength;
        Transform _gyro;
        bool _active;

        public void Configure(BehaviorSpec spec, InventionView view)
        {
            Strength = spec.Parameters["stabilizationStrength"];
            if (spec.TargetPart != null) view.Parts.TryGetValue(spec.TargetPart, out _gyro);
        }

        public void Activate() { _active = true; }
        public void Deactivate() { _active = false; }

        void Update()
        {
            if (_active && _gyro != null) _gyro.Rotate(0f, (90f + 360f * Strength) * Time.deltaTime, 0f, Space.Self);
        }
    }
}
