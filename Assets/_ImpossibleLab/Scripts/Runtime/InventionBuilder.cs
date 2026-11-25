using System.Collections.Generic;
using System.Linq;
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
        /// <summary>下を濃い色に塗り分ける外装の部品 id（ProductLook）。</summary>
        string _lowerShell;
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
            _lowerShell = ProductLook.Enabled ? ProductLook.LowerShell(report.Dna) : null;

            foreach (var part in report.Dna.Parts)
            {
                var go = CreatePrimitive(part, report.Dna);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = part.Position;
                view.Parts[part.Id] = go.transform;
            }
            if (ProductLook.Enabled) ProductLook.AddDetails(view, report.Dna, _assets);
            BuildJoints(report.Dna, view);

            // 動作プログラム（仕様 28）：試用で同じインタプリタを動かす
            if (Features.MotionProgram && report.Dna.Program != null)
            {
                var runner = root.AddComponent<ProgramRunner>();
                runner.Configure(null, view);
                view.Behaviors.Add(runner);
            }

            foreach (var b in report.Dna.Behaviors)
            {
                IInventionBehavior behavior = null;
                switch (b.Type)
                {
                    case "follow_head": behavior = root.AddComponent<FollowHeadBehavior>(); break;
                    case "rain_shield": behavior = root.AddComponent<RainShieldBehavior>(); break;
                    case "stabilize": behavior = root.AddComponent<StabilizeBehavior>(); break;
                    case "follow_user": behavior = root.AddComponent<FollowUserBehavior>(); break;
                    case "hover": behavior = root.AddComponent<HoverBehavior>(); break;
                    case "avoid_obstacle":
                    case "carry_load":
                    case "move_forward":
                        // 発明台での試用では見た目の変化なし。性能は実験場（CourseSimulator）で計測する
                        behavior = root.AddComponent<PassiveBehavior>(); ((PassiveBehavior)behavior).Type = b.Type; break;
                    default: result.Warnings.Add($"「{LabCatalog.Behavior(b.Type)?.LabelJa ?? b.Type}」は試用では動きません（実験では計算します）"); break;
                }
                if (behavior == null) continue;
                behavior.Configure(b, view);
                view.Behaviors.Add(behavior);
            }

            // 掴む用のコライダーは本体のまとまり全体に1つ
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            foreach (var r in root.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(root.transform.InverseTransformPoint(r.bounds.center));
            var col = root.AddComponent<BoxCollider>();
            if (report.Dna.FindBehavior("rain_shield") != null)
            {
                float rad = Mathf.Max(0.25f, report.Dna.GetParameter("rain_shield", "radius", 0.3f));
                col.center = new Vector3(0, -0.05f, 0);
                col.size = new Vector3(rad * 2f, 0.25f, rad * 2f);
            }
            else
            {
                // 部品の位置・大きさから掴む範囲を決める（最小 0.25 m 角）
                var pb = new Bounds(report.Dna.Parts[0].Position, report.Dna.Parts[0].Scale);
                foreach (var part in report.Dna.Parts) pb.Encapsulate(new Bounds(part.Position, part.Scale));
                col.center = pb.center;
                col.size = Vector3.Max(pb.size, Vector3.one * 0.25f);
            }
            result.View = view;
            return result;
        }

        /// <summary>骨組みの箱で、1辺がほかより極端に薄いもの（持ち手・枠など）。中の詰まった板ではなく、細い棒4本の枠で描く。</summary>
        public static bool IsThinFrame(PartSpec part)
        {
            if (part.Type != "frame" || part.ShapeOrDefault != "box") return false;
            var d = new[] { Mathf.Abs(part.Scale.x), Mathf.Abs(part.Scale.y), Mathf.Abs(part.Scale.z) };
            System.Array.Sort(d);
            return d[0] <= 0.25f * d[1] && d[1] >= 0.08f;
        }

        /// <summary>
        /// 薄い骨組みを枠（棒4本）で作る。見た目だけで、質量などの計算は DNA の箱のまま。
        /// 部品の大きさは親に掛け（ほかの部品と同じ）、棒は 1 を基準にした大きさで置く。棒の太さは薄い辺の長さ（1.5〜4cm）。
        /// </summary>
        GameObject CreateThinFrame(PartSpec part, InventionDna dna)
        {
            var go = new GameObject($"Part_{part.Id}_{part.Type}");
            Vector3 size = new Vector3(Mathf.Abs(part.Scale.x), Mathf.Abs(part.Scale.y), Mathf.Abs(part.Scale.z));
            go.transform.localScale = part.Scale;
            int thin = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
            int a = (thin + 1) % 3, b = (thin + 2) % 3; // 枠の面の2辺
            float t = Mathf.Clamp(size[thin], 0.015f, 0.04f);
            t = Mathf.Min(t, 0.3f * Mathf.Min(size[a], size[b]));
            Material mat = _assets.For(PartLook.Surface(part));
            int? rgb = PartLook.Tint(part, dna.Appearance);
            void Bar(string name, int along, float offset)
            {
                // along の向きに伸びる棒。もう一方の辺の端に寄せる
                int across = along == a ? b : a;
                var scale = Vector3.one;
                scale[across] = t / Mathf.Max(1e-4f, size[across]);
                var pos = Vector3.zero;
                pos[across] = offset * (0.5f - scale[across] * 0.5f);
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bar.name = name;
                Object.Destroy(bar.GetComponent<Collider>());
                bar.transform.SetParent(go.transform, false);
                bar.transform.localPosition = pos;
                bar.transform.localScale = scale;
                var rend = bar.GetComponent<Renderer>();
                rend.sharedMaterial = mat;
                if (rgb.HasValue)
                {
                    var mpb = new MaterialPropertyBlock();
                    mpb.SetColor("_BaseColor", RgbColor(rgb.Value));
                    rend.SetPropertyBlock(mpb);
                }
            }
            Bar("BarA1", a, +1f); Bar("BarA2", a, -1f);
            Bar("BarB1", b, +1f); Bar("BarB2", b, -1f);
            return go;
        }

        GameObject CreatePrimitive(PartSpec part, InventionDna dna)
        {
            if (IsThinFrame(part)) return CreateThinFrame(part, dna);
            var kit = PartKitLibrary.Find(_assets, part.Type, part.ShapeOrDefault);
            if (kit != null) return CreateFromKit(part, dna, kit);
            // 見た目は DNA の素材（機能部品は機能の色）、形は DNA の shape（未指定なら部品タイプの既定）で決める（PartLook）
            Material mat = _assets.For(PartLook.Surface(part));
            Vector3 scale = part.Scale;
            Quaternion rot = Quaternion.identity;
            PrimitiveType type;
            switch (part.ShapeOrDefault)
            {
                case "box": type = PrimitiveType.Cube; break;
                case "sphere": type = PrimitiveType.Sphere; break;
                case "capsule": type = PrimitiveType.Capsule; scale.y *= 0.5f; break; // Unity のカプセル・円柱は高さ2
                default: type = PrimitiveType.Cylinder; scale.y *= 0.5f; break;
            }
            // 車輪の円柱は横倒し（scale は [直径, 幅, 直径]）
            if (part.Type == "wheel" && type == PrimitiveType.Cylinder) rot = Quaternion.Euler(0, 0, 90);
            var go = GameObject.CreatePrimitive(type);
            go.name = $"Part_{part.Id}_{part.Type}";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            var rend = go.GetComponent<Renderer>();
            rend.sharedMaterial = mat;
            // 外観プリセットの色（マテリアルは共有のまま色だけ変える）
            int? rgb = PartLook.Tint(part, dna.Appearance);
            if (rgb.HasValue)
            {
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_BaseColor", RgbColor(rgb.Value));
                rend.SetPropertyBlock(mpb);
            }
            return go;
        }

        /// <summary>部品キットのメッシュで作る。大きさの基準は基本形状と同じ（円柱・カプセルは高さ2）。飾りは子にして同じ大きさを掛ける。</summary>
        GameObject CreateFromKit(PartSpec part, InventionDna dna, PartKitLibrary.Entry kit)
        {
            Vector3 scale = part.Scale;
            string shape = part.ShapeOrDefault;
            if (shape == "cylinder" || shape == "capsule") scale.y *= 0.5f;
            var go = new GameObject($"Part_{part.Id}_{part.Type}");
            go.transform.localRotation = part.Type == "wheel" && shape == "cylinder" ? Quaternion.Euler(0, 0, 90) : Quaternion.identity;
            go.transform.localScale = scale;
            // 機能の色を飾りが持つキットでは、筐体は素材の色にする
            var surface = kit.HasAccent ? PartLook.Housing(part) : PartLook.Surface(part);
            // 角の丸みを実寸で一定にする（飾りも同じ変形で筐体の面に沿わせる）。センサーのレンズは真円にする
            bool fit = ProductLook.Enabled && ProductLook.Fits(kit.Key, shape);
            bool cylinder = shape == "cylinder";
            int? tint = PartLook.Tint(surface, part, dna.Appearance);
            if (tint.HasValue && part.Id == _lowerShell) tint = ProductLook.Darker(tint.Value);
            AddMesh(go, fit ? ProductLook.Fit(kit.Mesh, scale, cylinder) : kit.Mesh, _assets.For(surface), tint);
            var fn = PartLook.Surface(part);
            bool roundLens = ProductLook.Enabled && part.Type == "sensor" && shape == "sphere";
            foreach (var x in kit.Extras)
            {
                var child = new GameObject(x.Name);
                child.transform.SetParent(go.transform, false);
                bool functionColor = x.IsAccent && (fn == PartSurface.AccentBlue || fn == PartSurface.AccentOrange);
                var mesh = fit ? ProductLook.Fit(x.Mesh, scale, cylinder) : roundLens && x.IsAccent ? ProductLook.Lens(x.Mesh, scale) : x.Mesh;
                AddMesh(child, mesh, functionColor ? _assets.For(fn) : x.Material, functionColor ? PartLook.Tint(fn, part, dna.Appearance) : null);
            }
            if (part.Type == "sensor" && shape == "sphere" && kit.HasAccent && _assets.LensGlow != null) AddLensGlow(go.transform, scale);
            return go;
        }

        /// <summary>
        /// センサーのレンズ（キットでは前面 z=0.3、半径0.36）に光のにじみの板を重ねる。
        /// 部品は縦横で違う大きさを掛けているので、にじみが楕円にならないよう子の大きさで打ち消す。
        /// </summary>
        void AddLensGlow(Transform part, Vector3 scale)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "LensGlow";
            Object.Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(part, false);
            q.transform.localPosition = new Vector3(0, 0, 0.34f);
            float d = 0.36f * 2f * 3.6f * Mathf.Min(Mathf.Abs(scale.x), Mathf.Abs(scale.y)); // レンズの直径の約3.6倍
            q.transform.localScale = new Vector3(d / Mathf.Max(1e-4f, Mathf.Abs(scale.x)), d / Mathf.Max(1e-4f, Mathf.Abs(scale.y)), 1f);
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = _assets.LensGlow;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        static void AddMesh(GameObject go, Mesh mesh, Material mat, int? rgb)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var rend = go.AddComponent<MeshRenderer>();
            rend.sharedMaterial = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (rgb.HasValue)
            {
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_BaseColor", RgbColor(rgb.Value));
                rend.SetPropertyBlock(mpb);
            }
        }

        static Color RgbColor(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        /// <summary>
        /// 関節（仕様 28.2）：回転・移動の中心に空の親を置き、部品をその子にする。親の関節があればその下に入れる（親を先に作る）。
        /// hinge は正の値で、軸の負の向きまわりに回す（x 軸なら前 +Z 側が持ち上がる）。slider は軸の向きに動かす。
        /// </summary>
        static void BuildJoints(InventionDna dna, InventionView view)
        {
            int Depth(PartSpec p) { int d = 0; var cur = p; while (cur?.Joint != null && !string.IsNullOrEmpty(cur.Joint.Parent) && d < 8) { cur = dna.FindPart(cur.Joint.Parent); d++; } return d; }
            foreach (var p in dna.Parts.Where(x => x.Joint != null).OrderBy(Depth))
            {
                if (!view.Parts.TryGetValue(p.Id, out var partT)) continue;
                var pivot = new GameObject("Joint_" + p.Id).transform;
                Vector3 at = p.Position + p.Joint.Pivot; // 発明の座標での中心
                if (!string.IsNullOrEmpty(p.Joint.Parent) && view.Joints.TryGetValue(p.Joint.Parent, out var parentJoint))
                {
                    pivot.SetParent(parentJoint.Pivot, false);
                    pivot.localPosition = at - parentJoint.RootPosition;
                }
                else
                {
                    pivot.SetParent(view.transform, false);
                    pivot.localPosition = at;
                }
                partT.SetParent(pivot, false);
                partT.localPosition = p.Position - at;
                view.Joints[p.Id] = new InventionView.JointView { Spec = p.Joint, Pivot = pivot, Origin = pivot.localPosition, RootPosition = at };
                view.SetJoint(p.Id, p.Joint.Home);
            }
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

        /// <summary>関節の表示（回転・移動の中心の Transform）。</summary>
        public sealed class JointView
        {
            public JointSpec Spec;
            public Transform Pivot;
            public Vector3 Origin;
            /// <summary>中心の位置（発明の座標）。子の関節の位置を求めるのに使う。</summary>
            public Vector3 RootPosition;
            public float Value;
        }
        public readonly Dictionary<string, JointView> Joints = new Dictionary<string, JointView>();

        static Vector3 AxisOf(string a) => a == "x" ? Vector3.right : a == "z" ? Vector3.forward : Vector3.up;

        /// <summary>関節を値（hinge は度、slider は m）にする。範囲の外は丸める。</summary>
        public void SetJoint(string id, float value)
        {
            if (!Joints.TryGetValue(id, out var j)) return;
            j.Value = Mathf.Clamp(value, j.Spec.Min, j.Spec.Max);
            var axis = AxisOf(j.Spec.Axis);
            if (j.Spec.IsHinge) j.Pivot.localRotation = Quaternion.AngleAxis(-j.Value, axis);
            else j.Pivot.localPosition = j.Origin + axis * j.Value;
        }

        public void ResetJoints() { foreach (var kv in Joints) SetJoint(kv.Key, kv.Value.Spec.Home); }
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

    /// <summary>follow_user：起動すると床の上を移動してユーザーの後ろについてくる（試用。計測は CourseSimulator）。</summary>
    public sealed class FollowUserBehavior : MonoBehaviour, IInventionBehavior
    {
        public string BehaviorType => "follow_user";
        public float Speed, FollowDistance;
        public Transform Target;
        public float FloorY;
        bool _active;

        public void Configure(BehaviorSpec spec, InventionView view)
        {
            Speed = Mathf.Min(spec.Parameters["speed"], view.Dna.Constraints.MaxSpeed);
            FollowDistance = spec.Parameters["followDistance"];
        }

        public void Activate() { _active = true; }
        public void Deactivate() { _active = false; }

        void Update()
        {
            if (!_active || Target == null) return;
            var p = transform.position;
            var goal = new Vector3(Target.position.x, FloorY, Target.position.z);
            var flat = goal - new Vector3(p.x, FloorY, p.z);
            float d = flat.magnitude;
            var np = new Vector3(p.x, Mathf.MoveTowards(p.y, FloorY, 1.5f * Time.deltaTime), p.z);
            if (d > FollowDistance + 0.05f)
            {
                var step = flat / d * Mathf.Min(Speed * Time.deltaTime, d - FollowDistance);
                np += step;
            }
            transform.position = np;
            if (d > 0.05f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(new Vector3(flat.x, 0f, flat.z)), 1f - Mathf.Exp(-5f * Time.deltaTime));
        }
    }

    /// <summary>hover：起動するとその場で浮き上がり、ゆっくり上下する（試用。飛行の計測は CourseSimulator）。</summary>
    public sealed class HoverBehavior : MonoBehaviour, IInventionBehavior
    {
        public string BehaviorType => "hover";
        public float Lift, MaxHeight;
        float _stab, _base;
        bool _active;

        public void Configure(BehaviorSpec spec, InventionView view)
        {
            Lift = spec.Parameters["lift"];
            MaxHeight = spec.Parameters["maxHeight"];
            _stab = view.Dna.GetParameter("stabilize", "stabilizationStrength", 0f);
        }

        public void Activate() { _active = true; _base = transform.position.y; }
        public void Deactivate() { _active = false; }

        void Update()
        {
            if (!_active) return;
            // 発明台の上なので高さは控えめ（最大 0.4 m）。揚力が弱いと低く、安定化が弱いと揺れる（見た目のみ）
            float h = Lift < 0.15f ? 0.02f : Mathf.Min(0.4f, MaxHeight * 0.3f) * Mathf.Lerp(0.5f, 1f, Lift);
            float wobble = (1f - _stab) * 0.03f;
            var p = transform.position;
            float y = _base + h + Mathf.Sin(Time.time * 2f) * 0.02f;
            transform.position = new Vector3(p.x, Mathf.Lerp(p.y, y, 1f - Mathf.Exp(-3f * Time.deltaTime)), p.z);
            transform.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 2.7f) * wobble * 100f, transform.localEulerAngles.y, Mathf.Cos(Time.time * 2.1f) * wobble * 100f);
        }
    }

    /// <summary>試用時に見た目の変化がない機能（avoid_obstacle・carry_load・move_forward）。性能は実験場で計測する。</summary>
    public sealed class PassiveBehavior : MonoBehaviour, IInventionBehavior
    {
        public string Type;
        public string BehaviorType => Type;
        public void Configure(BehaviorSpec spec, InventionView view) { }
        public void Activate() { }
        public void Deactivate() { }
    }
}
