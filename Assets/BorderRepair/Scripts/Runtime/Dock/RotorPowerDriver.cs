using UnityEngine;

namespace BorderRepair.Dock
{
    /// <summary>
    /// 七号两台涡轮转子的供电表现。
    /// - 未接管时（悬停）：不干预，转子由 Animator 的 Idle_Hover 片段驱动。
    /// - 接管后（落座后）：在 LateUpdate 里覆盖两根转子骨骼的本地旋转：通电按悬停转速旋转，断电后匀减速到停。
    /// 只覆盖转子骨骼，Animator 仍然驱动身体、手臂、夹爪等其它骨骼，所以断电不会影响夹爪和其它动作。
    /// 转轴（转子骨骼本地坐标）由编辑器构建脚本从 Idle_Hover 片段实测后写入。
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class RotorPowerDriver : MonoBehaviour
    {
        [SerializeField] Transform[] rotors = new Transform[0];
        [SerializeField] Vector3[] localAxes = new Vector3[0];
        [Tooltip("Idle_Hover 中转子的转速：两秒转 720°。")]
        [SerializeField] float idleSpeedDegPerSec = 360f;
        [Tooltip("断电后转子从悬停转速减速到停止所需的时间（秒）。")]
        [SerializeField] float spinDownSeconds = 2.5f;
        [Tooltip("通电后从静止升到悬停转速所需的时间（秒）。")]
        [SerializeField] float spinUpSeconds = 1.0f;

        Quaternion[] baseRotations = new Quaternion[0];
        float angle;
        float speed;
        bool powered = true;

        public bool Driven { get; private set; }
        public bool Powered => powered;
        public float SpeedDegPerSec => Driven ? speed : (powered ? idleSpeedDegPerSec : 0f);
        public bool IsStopped => Driven && !powered && speed <= 0f;
        /// <summary>通电且已回到悬停转速（未接管时由 Animator 的 Idle_Hover 驱动，也算正常转速）。</summary>
        public bool IsAtIdleSpeed => powered && (!Driven || speed >= idleSpeedDegPerSec - 0.01f);
        public float SpinUpSeconds => spinUpSeconds;
        public float IdleSpeedDegPerSec => idleSpeedDegPerSec;
        public float SpinDownSeconds => spinDownSeconds;
        public Transform[] Rotors => rotors;
        public Vector3[] LocalAxes => localAxes;

        public void Configure(Transform[] rotorBones, Vector3[] axes, float idleSpeed)
        {
            rotors = rotorBones;
            localAxes = axes;
            idleSpeedDegPerSec = idleSpeed;
        }

        /// <summary>从 Animator 当前写入的转子姿态接管，保持当前转速，避免跳变。</summary>
        public void TakeOver()
        {
            if (Driven) return;
            baseRotations = new Quaternion[rotors.Length];
            for (int i = 0; i < rotors.Length; i++)
                baseRotations[i] = rotors[i].localRotation;
            angle = 0f;
            speed = powered ? idleSpeedDegPerSec : 0f;
            Driven = true;
        }

        /// <summary>交还给 Animator（离座后重新悬停时使用）：之后不再覆盖转子骨骼，由当前动画片段驱动。</summary>
        public void Release()
        {
            Driven = false;
        }

        public void SetPower(bool on)
        {
            powered = on;
        }

        void LateUpdate() => Step(Time.deltaTime);

        /// <summary>推进一帧（LateUpdate 调用；编辑模式测试也直接调用它，按固定步长推进）。</summary>
        public void Step(float dt)
        {
            if (!Driven) return;
            float target = powered ? idleSpeedDegPerSec : 0f;
            float rate = powered ? idleSpeedDegPerSec / Mathf.Max(spinUpSeconds, 0.01f) : idleSpeedDegPerSec / Mathf.Max(spinDownSeconds, 0.01f);
            speed = Mathf.MoveTowards(speed, target, rate * dt);
            angle = Mathf.Repeat(angle + speed * dt, 360f);
            for (int i = 0; i < rotors.Length; i++)
            {
                if (rotors[i] == null) continue;
                rotors[i].localRotation = baseRotations[i] * Quaternion.AngleAxis(angle, localAxes[i]);
            }
        }
    }
}
