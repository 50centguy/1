using BorderRepair.Data;
using UnityEngine;

namespace BorderRepair.Inspection
{
    /// <summary>
    /// 可维修零件的外观状态（与 InspectionPoint 挂在同一物体上，partId 与 pointId 相同）。
    /// 状态是明确的枚举，由 RepairSession 决定；本组件只负责表现：
    /// Removed / Open 时平滑移动到 snapTarget（吸附位置），Replaced / Torn / Tested 时切换外观。
    /// 不使用物理模拟。以后接 VR 手柄时，抓取、放入吸附区同样只需要调用会话的 PerformAction，再由这里表现。
    /// </summary>
    public class RepairPart : MonoBehaviour
    {
        [SerializeField] string partId;
        [Tooltip("Removed / Open 状态下零件要去的吸附位置（物品 prefab 内的空物体）")]
        [SerializeField] Transform snapTarget;
        [SerializeField] GameObject installedVisual;
        [SerializeField] GameObject replacedVisual;
        [SerializeField] GameObject intactVisual;
        [SerializeField] GameObject tornVisual;
        [SerializeField] GameObject testedIndicator;
        [SerializeField] float moveSeconds = 0.3f;

        Vector3 homeLocalPosition;
        Quaternion homeLocalRotation;
        bool initialized;

        // 可选的“离位动作”（由反馈表现组件触发，默认不播放）：先沿自身轴线退出 / 旋转 / 翘起，再移到吸附位置
        Vector3 exitAxis;
        float exitDistance, exitTurns, exitTilt, exitSeconds, exitElapsed = -1f;

        public string PartId => partId;
        public RepairPartState State { get; private set; } = RepairPartState.Installed;
        public Transform SnapTarget => snapTarget;
        public bool IsAtTarget { get; private set; } = true;
        /// <summary>检测后显示的指示物（可空），供反馈表现做闪烁。</summary>
        public GameObject TestedIndicator => testedIndicator;
        public bool IsPlayingExit => exitElapsed >= 0f;

        /// <summary>
        /// 从原位开始的离位动作：沿零件自身的 localAxis 退出 distance 米，同时绕该轴转 turns 圈、绕自身 X 轴翘起 tiltDegrees，
        /// 用时 seconds；结束后照常平滑移到吸附位置。只影响表现，状态仍由会话决定。
        /// </summary>
        public void PlayExit(Vector3 localAxis, float distance, float turns, float tiltDegrees, float seconds)
        {
            Init();
            if (!MovesToSnap || seconds <= 0f) return;
            exitAxis = localAxis.sqrMagnitude > 1e-8f ? localAxis.normalized : Vector3.up;
            exitDistance = distance;
            exitTurns = turns;
            exitTilt = tiltDegrees;
            exitSeconds = seconds;
            exitElapsed = 0f;
            transform.localPosition = homeLocalPosition;
            transform.localRotation = homeLocalRotation;
            IsAtTarget = false;
        }

        /// <summary>供编辑器生成工具配置占位 prefab。</summary>
        public void Configure(string id, Transform snap, GameObject installed, GameObject replaced,
                              GameObject intact, GameObject torn, GameObject tested)
        {
            partId = id;
            snapTarget = snap;
            installedVisual = installed;
            replacedVisual = replaced;
            intactVisual = intact;
            tornVisual = torn;
            testedIndicator = tested;
        }

        void Awake() => Init();

        void Init()
        {
            if (initialized) return;
            initialized = true;
            homeLocalPosition = transform.localPosition;
            homeLocalRotation = transform.localRotation;
            ApplyVisuals();
        }

        public void SetState(RepairPartState state, bool instant = false)
        {
            Init();
            State = state;
            ApplyVisuals();
            IsAtTarget = false;
            if (instant) SnapNow();
        }

        public void ResetPart()
        {
            Init();
            exitElapsed = -1f;
            State = RepairPartState.Installed;
            ApplyVisuals();
            SnapNow();
        }

        bool MovesToSnap => (State == RepairPartState.Removed || State == RepairPartState.Open) && snapTarget != null;

        void TargetPose(out Vector3 localPos, out Quaternion localRot)
        {
            if (MovesToSnap && transform.parent != null)
            {
                localPos = transform.parent.InverseTransformPoint(snapTarget.position);
                localRot = Quaternion.Inverse(transform.parent.rotation) * snapTarget.rotation;
            }
            else if (MovesToSnap)
            {
                localPos = snapTarget.position;
                localRot = snapTarget.rotation;
            }
            else
            {
                localPos = homeLocalPosition;
                localRot = homeLocalRotation;
            }
        }

        public void SnapNow()
        {
            exitElapsed = -1f;
            TargetPose(out var p, out var r);
            transform.localPosition = p;
            transform.localRotation = r;
            IsAtTarget = true;
        }

        void Update()
        {
            if (IsAtTarget) return;
            if (exitElapsed >= 0f)
            {
                if (!MovesToSnap) exitElapsed = -1f;
                else
                {
                    exitElapsed += Time.deltaTime;
                    float k = Mathf.Clamp01(exitElapsed / exitSeconds);
                    float ease = 1f - (1f - k) * (1f - k);
                    var local = Quaternion.AngleAxis(exitTurns * 360f * k, exitAxis) * Quaternion.AngleAxis(exitTilt * ease, Vector3.right);
                    transform.localPosition = homeLocalPosition + homeLocalRotation * exitAxis * (exitDistance * ease);
                    transform.localRotation = homeLocalRotation * local;
                    if (k >= 1f) exitElapsed = -1f;
                    return;
                }
            }
            TargetPose(out var p, out var r);
            float t = moveSeconds <= 0f ? 1f : 1f - Mathf.Exp(-Time.deltaTime * 5f / moveSeconds);
            transform.localPosition = Vector3.Lerp(transform.localPosition, p, t);
            transform.localRotation = Quaternion.Slerp(transform.localRotation, r, t);
            if ((transform.localPosition - p).sqrMagnitude < 1e-8f && Quaternion.Angle(transform.localRotation, r) < 0.1f)
                SnapNow();
        }

        void ApplyVisuals()
        {
            bool replaced = State == RepairPartState.Replaced;
            if (installedVisual != null) installedVisual.SetActive(!replaced || replacedVisual == null);
            if (replacedVisual != null) replacedVisual.SetActive(replaced);
            bool torn = State == RepairPartState.Torn;
            if (intactVisual != null) intactVisual.SetActive(!torn || tornVisual == null);
            if (tornVisual != null) tornVisual.SetActive(torn);
            if (testedIndicator != null) testedIndicator.SetActive(State == RepairPartState.Tested);
        }
    }
}
