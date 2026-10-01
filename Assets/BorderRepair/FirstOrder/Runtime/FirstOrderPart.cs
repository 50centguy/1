using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BorderRepair.FirstOrder
{
    public enum PartLocation
    {
        Installed = 0,      // 在七号引擎上的原位
        Released = 1,       // 锁扣：已扳开（仍装在引擎上）
        Held = 2,           // 已取下，悬在拆下位置上方，等玩家点工作台落点
        OnBench = 3,        // 放在工作台落点上
        Stored = 4,         // 备件：在工作台的备件盒里
        Cleared = 5,        // 进气口堵塞：已清理（积尘、纤维已清掉）
    }

    /// <summary>
    /// 首单原型里可点击的“真实部件”：挂在 RobotV4 / 工作台上真实存在的网格对象上（或一组同级网格的主对象上）。
    /// 记录原始父节点与本地位姿，取下时脱离骨骼层级（动画不再覆盖它），装回时按原本地位姿挂回。
    /// members：同一总成里一起移动的同级网格（例如上盖 + 进气唇口 + 护栅 + 风道）。分组是程序定义的，不是模型自带的。
    /// </summary>
    public class FirstOrderPart : MonoBehaviour
    {
        public string partId;
        [Tooltip("给 HUD 和报告用的中文名")]
        public string displayName;
        [Tooltip("RobotV4 / 工作台里的真实对象路径（相对于各自根对象）")]
        public string realPath;
        [Tooltip("是否占位：没有对应美术资产、由程序生成的对象必须标 true")]
        public bool isPlaceholder;
        public List<Transform> members = new List<Transform>();

        public PartLocation Location { get; set; } = PartLocation.Installed;
        public string LocationDetail { get; set; } = "七号左引擎原位";

        Transform homeParent;
        Vector3 homeLocalPos;
        Quaternion homeLocalRot;
        readonly List<(Transform t, Vector3 offset, Quaternion rot)> memberOffsets = new List<(Transform, Vector3, Quaternion)>();
        readonly List<(Transform t, Transform parent, Vector3 lp, Quaternion lr)> memberHomes = new List<(Transform, Transform, Vector3, Quaternion)>();
        bool captured;

        public Transform HomeParent => homeParent;

        public void CaptureHome()
        {
            if (captured) return;
            homeParent = transform.parent;
            homeLocalPos = transform.localPosition;
            homeLocalRot = transform.localRotation;
            memberHomes.Clear();
            foreach (var m in members) memberHomes.Add((m, m.parent, m.localPosition, m.localRotation));
            captured = true;
        }

        void Awake() => CaptureHome();

        /// <summary>原位（装在引擎上）的世界位姿，按父骨骼当前姿态计算。</summary>
        public Pose HomeWorldPose()
        {
            CaptureHome();
            if (homeParent == null) return new Pose(homeLocalPos, homeLocalRot);
            return new Pose(homeParent.TransformPoint(homeLocalPos), homeParent.rotation * homeLocalRot);
        }

        /// <summary>脱离原层级：总成成员挂到主对象下，一起移动。</summary>
        public void Detach(Transform carrier)
        {
            CaptureHome();
            memberOffsets.Clear();
            foreach (var m in members) m.SetParent(transform, true);
            transform.SetParent(carrier, true);
        }

        /// <summary>按原本地位姿挂回原父骨骼，总成成员各自回到原父节点。</summary>
        public void Reattach()
        {
            CaptureHome();
            transform.SetParent(homeParent, false);
            transform.localPosition = homeLocalPos;
            transform.localRotation = homeLocalRot;
            foreach (var (t, parent, lp, lr) in memberHomes)
            {
                t.SetParent(parent, false);
                t.localPosition = lp;
                t.localRotation = lr;
            }
        }

        /// <summary>
        /// 主对象 + 成员的世界包围盒，含它们下面挂的故障美术件（磨损轴承、进气口堵塞、保养标记）。
        /// 只算正在显示的渲染器（原轴承的渲染器已关掉、由磨损件代替显示；清理掉的积尘也不算）；一个都没有时退回全部。
        /// </summary>
        public Bounds WorldBounds()
        {
            var all = Renderers().ToList();
            var rs = all.Where(r => r.enabled && r.gameObject.activeInHierarchy).ToList();
            if (rs.Count == 0) rs = all;
            var b = rs.Count > 0 ? rs[0].bounds : new Bounds(transform.position, Vector3.zero);
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>主对象 + 成员及其子对象上的全部渲染器（含关掉的）。</summary>
        public IEnumerable<Renderer> Renderers()
        {
            var seen = new HashSet<Renderer>();
            foreach (var t in members.Prepend(transform))
                foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                    if (seen.Add(r)) yield return r;
        }
    }
}
