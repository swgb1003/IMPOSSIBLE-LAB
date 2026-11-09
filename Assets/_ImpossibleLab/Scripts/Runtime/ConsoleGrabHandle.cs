using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ImpossibleLab
{
    /// <summary>
    /// メイン画面の下にある移動バー。コントローラーのレーザーを当ててグリップを握っている間、
    /// 3枚のパネル（ConsoleRoot）をレーザーの先に追従させる。パネルは常に水平を保ち、頭の方を向く。
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class ConsoleGrabHandle : XRSimpleInteractable
    {
        public Transform Target;          // 動かす対象（ConsoleRoot）
        public Transform Head;            // 向きの基準（XRカメラ）
        public UnityEngine.UI.Image Visual;
        public TMPro.TMP_Text Label;

        public const float MinDistance = 0.5f, MaxDistance = 3.0f;
        public const float MinHeight = 0.1f, MaxHeight = 2.4f;

        Transform _hand;
        float _distance;
        Vector3 _offsetLocal; // 握った点 → Target 原点（Target の回転基準）
        public bool Dragging => _hand != null;
        public event System.Action Released;

        protected override void OnHoverEntered(HoverEnterEventArgs args) { base.OnHoverEntered(args); Refresh(); }
        protected override void OnHoverExited(HoverExitEventArgs args) { base.OnHoverExited(args); Refresh(); }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            if (Target == null) return;
            _hand = args.interactorObject.transform;
            // 握った点＝バー中心をレーザー上に投影した点
            Vector3 toHandle = transform.position - _hand.position;
            _distance = Mathf.Clamp(Vector3.Dot(toHandle, _hand.forward), MinDistance, MaxDistance);
            Vector3 grabPoint = _hand.position + _hand.forward * _distance;
            _offsetLocal = Quaternion.Inverse(Target.rotation) * (Target.position - grabPoint);
            Debug.Log($"[ILAB] grab start by {_hand.name} dist={_distance:F2} target={Target.position:F2}");
            Refresh();
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
            bool was = _hand != null;
            _hand = null;
            Refresh();
            if (was)
            {
                Debug.Log($"[ILAB] grab end target={(Target != null ? Target.position : Vector3.zero):F2}");
                Released?.Invoke();
            }
        }

        void LateUpdate()
        {
            if (_hand == null || Target == null) return;
            Vector3 grabPoint = _hand.position + _hand.forward * _distance;
            grabPoint.y = Mathf.Clamp(grabPoint.y, MinHeight, MaxHeight);
            // 水平を保ったまま頭の方を向く（Target の +Z が利用者から見て奥）
            Vector3 eye = Head != null ? Head.position : _hand.position;
            Vector3 fwd = Vector3.ProjectOnPlane(grabPoint - eye, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Target.forward;
            var rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            Target.SetPositionAndRotation(grabPoint + rot * _offsetLocal, rot);
        }

        void Refresh()
        {
            Color c = Dragging ? LabTheme.AccentLight : isHovered ? LabTheme.AccentPrimary : LabTheme.TextSecondary;
            if (Visual != null) Visual.color = c;
            if (Label != null) Label.text = Dragging ? "離すと固定" : isHovered ? "グリップを握って移動" : "パネルを移動";
        }

        public void Init(Transform target, Transform head, UnityEngine.UI.Image visual, TMPro.TMP_Text label)
        {
            Target = target; Head = head; Visual = visual; Label = label;
            Refresh();
        }
    }
}
