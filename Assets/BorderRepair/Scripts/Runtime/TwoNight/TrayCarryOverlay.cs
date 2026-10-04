using UnityEngine;

namespace BorderRepair.TwoNight
{
    /// <summary>
    /// 七号手臂姿态覆盖层（与 RotorPowerDriver 同一种做法）：Animator 照常每帧播放现有片段（身体浮动、引擎摆动、转子、表情都不变），
    /// 本组件在 LateUpdate 里只覆盖两组骨骼，权重为 0 时完全不写，控制权交还 Animator：
    /// - 右臂（根环 / 肩 / 肘 / 腕 + 上下爪）：端盘姿态，按美术审计 tray_handoff.md 第 3 节；
    /// - 左臂（根环 / 肩 / 肘 / 腕）：收纳姿态，取自现有片段 Arm_Deploy_L 第 0 帧（“上臂后折、前臂回折”），右手放盘时让开托盘架；
    /// - 放盘那几秒（steadyWeight）：Root、Body 两根骨骼按 Idle_Hover 第 0 帧稳住（Idle_Hover 的身体浮动约 ±6 mm，会吃掉放盘时 8 mm 的余量；路径就是按第 0 帧算的）。
    /// 不改动画片段、不全局停 Animator。同一时间这些骨骼只有本组件一个最终写入者（Animator 先写、本组件后写），不会互相写回抖动。
    /// </summary>
    [DefaultExecutionOrder(150)]
    public class TrayCarryOverlay : MonoBehaviour
    {
        [SerializeField] Transform[] rightBones = new Transform[6];      // 根环、肩、肘、腕、上爪、下爪
        [SerializeField] Quaternion[] rightCarry = new Quaternion[6];    // 端盘姿态，夹爪夹住 10 mm 横杆
        [SerializeField] Quaternion[] rightOpenJaw = new Quaternion[2];  // 夹爪半开（上 / 下）
        [SerializeField] Transform[] leftBones = new Transform[4];       // 根环、肩、肘、腕
        [SerializeField] Quaternion[] leftStow = new Quaternion[4];      // Arm_Deploy_L 第 0 帧
        [Range(0, 1)] [SerializeField] float rightWeight;
        [Range(0, 1)] [SerializeField] float leftWeight;
        [Range(0, 1)] [SerializeField] float jawOpen = 1f;
        [Range(0, 1)] [SerializeField] float upperJawExtra;   // 只张开上爪（放盘时下爪还托着横杆）
        [SerializeField] Transform[] steadyBones = new Transform[0];   // Root、Body
        [SerializeField] Vector3[] steadyPos = new Vector3[0];
        [SerializeField] Quaternion[] steadyRot = new Quaternion[0];   // Idle_Hover 第 0 帧
        [Range(0, 1)] [SerializeField] float steadyWeight;

        public float RightWeight { get => rightWeight; set => rightWeight = Mathf.Clamp01(value); }
        public float LeftWeight { get => leftWeight; set => leftWeight = Mathf.Clamp01(value); }
        /// <summary>0 = 夹住横杆，1 = 半开（Pose_Gripper_Half，接近 / 放开横杆用）。</summary>
        public float JawOpen { get => jawOpen; set => jawOpen = Mathf.Clamp01(value); }
        /// <summary>只让上爪再张开（0..1，叠加在 JawOpen 上）：放盘时上爪离开横杆、下爪仍托着横杆。</summary>
        public float UpperJawExtra { get => upperJawExtra; set => upperJawExtra = Mathf.Clamp01(value); }
        /// <summary>0 = 身体照常随 Idle_Hover 浮动；1 = Root / Body 稳在 Idle_Hover 第 0 帧。</summary>
        public float SteadyWeight { get => steadyWeight; set => steadyWeight = Mathf.Clamp01(value); }
        public Transform[] RightBones => rightBones;
        public Transform[] LeftBones => leftBones;
        public bool Active => rightWeight > 0f || leftWeight > 0f || steadyWeight > 0f;

        public void Configure(Transform[] rb, Quaternion[] carry, Quaternion[] openJaw, Transform[] lb, Quaternion[] stow)
        { rightBones = rb; rightCarry = carry; rightOpenJaw = openJaw; leftBones = lb; leftStow = stow; }

        public void ConfigureSteady(Transform[] bones, Vector3[] pos, Quaternion[] rot) { steadyBones = bones; steadyPos = pos; steadyRot = rot; }

        public Quaternion RightTarget(int i) => i < 4 ? rightCarry[i] : Quaternion.Slerp(rightCarry[i], rightOpenJaw[i - 4], i == 4 ? Mathf.Max(jawOpen, upperJawExtra) : jawOpen);
        public Quaternion LeftTarget(int i) => leftStow[i];

        void LateUpdate()
        {
            if (steadyWeight > 0f)
                for (int i = 0; i < steadyBones.Length; i++)
                    if (steadyBones[i] != null)
                    {
                        steadyBones[i].localPosition = Vector3.Lerp(steadyBones[i].localPosition, steadyPos[i], steadyWeight);
                        steadyBones[i].localRotation = Quaternion.Slerp(steadyBones[i].localRotation, steadyRot[i], steadyWeight);
                    }
            if (rightWeight > 0f)
                for (int i = 0; i < rightBones.Length; i++)
                    if (rightBones[i] != null) rightBones[i].localRotation = Quaternion.Slerp(rightBones[i].localRotation, RightTarget(i), rightWeight);
            if (leftWeight > 0f)
                for (int i = 0; i < leftBones.Length; i++)
                    if (leftBones[i] != null) leftBones[i].localRotation = Quaternion.Slerp(leftBones[i].localRotation, LeftTarget(i), leftWeight);
        }

        /// <summary>编辑器 / 测试：不等 LateUpdate，立即按当前权重写一次。</summary>
        public void ApplyNow() => LateUpdate();
    }
}
