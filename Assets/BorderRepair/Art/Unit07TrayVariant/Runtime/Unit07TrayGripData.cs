using UnityEngine;

namespace BorderRepair.Art.Unit07TrayVariant
{
    /// <summary>
    /// 零件盘提手抬高变体的握持交接数据（程序接入用；本身不驱动任何东西）。
    /// 坐标约定：托盘本地 = 变体预制体根的本地坐标（X 长轴、Y 前后、Z 向上，原点盘底中心，米），与原 Dock_PartsTray 的本地坐标一致；
    /// 腕骨本地 = UNIT07_RobotV4_DockReady/…/Arm_R_Wrist 的本地坐标。缩放均为 1。
    /// </summary>
    [CreateAssetMenu(menuName = "Border Repair/Unit07 Tray Grip Data")]
    public class Unit07TrayGripData : ScriptableObject
    {
        [Header("托盘")]
        public GameObject trayPrefab;
        public string trayPrefabPath;
        [Tooltip("握杆中心（托盘本地）")] public Vector3 gripBarCenterLocal;
        [Tooltip("握杆方向（托盘本地，单位向量）")] public Vector3 gripBarAxisLocal = Vector3.up;
        [Tooltip("右手咬合中心落在握杆上的点（托盘本地）")] public Vector3 gripPointLocal;
        [Tooltip("允许与爪齿接触的握杆直段（沿握杆方向，离握杆中心的范围，米）")] public Vector2 allowedContactSpan;
        public float gripBarRadius = 0.005f;

        [Header("右手挂点（托盘相对右腕骨）")]
        public string wristBonePath;
        public Vector3 holdLocalPosition;
        public Quaternion holdLocalRotation = Quaternion.identity;
        [Tooltip("参考：72838e9 的旧挂点（不要再用）")] public Vector3 previousHoldLocalPosition;
        public Quaternion previousHoldLocalRotation = Quaternion.identity;

        [Header("手臂姿态（相对 Idle_Hover 第 0 帧，绕骨骼本地 X，度）")]
        public float[] rightArmJointsDeg = new float[4];
        [Tooltip("夹爪开度参数（Closed→Half 之间）")] public float jawT = 0.32071f;

        [Header("原落架位置（父对象与本地位姿，与原托盘相同）")]
        public string homeParentPath;
        public Vector3 homeLocalPosition;
        public Quaternion homeLocalRotation = Quaternion.identity;

        [Header("建议的演出路线参数（隔离验证场景里算出；程序接入时按发布场景重算）")]
        [TextArea(3, 12)] public string routeNotes;
    }
}
