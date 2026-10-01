using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WorkbenchArea
{
    /// <summary>
    /// 【占位交互，仅用于展示和路径验证，不是正式维修流程】
    /// - 托盘取放：托盘先竖直抬起 trayLift，再水平拉到台沿外（世界 z = trayPullZ，即玩家手边）；再次触发时原路放回。
    /// - 义肢占位件拆装演示：4 颗螺钉逐颗 抬起 → 平移到螺钉托盘落点上方 → 放下；盖板 抬起 → 平移到操作垫 COVER 停放框中心 → 放下；可原路装回。
    /// - 工具箱上层移开 / 放回：上层竖直抬起 tierLift 让出下层；上层在原位时，下层的检查对象标为“被挡住：先移开上层才能取用”。
    /// 路径关键点与 Blender 检查脚本（build_workbench_area.py 的 tray / screws / cover 路径检查）一致，坐标已换成 Unity 坐标。
    /// 测试场景：1 / 2 或左键点托盘 = 取放托盘；U 或左键点工具箱上层 = 移开 / 放回上层；D = 拆下 / 装回演示。
    /// </summary>
    public class WbPlaceholderDemo : MonoBehaviour
    {
        [SerializeField] Transform trayScrews;
        [SerializeField] Transform trayOldParts;
        [SerializeField] Transform cover;
        [SerializeField] Transform[] screws = new Transform[0];
        [SerializeField] Transform toolboxUpper;
        [SerializeField] WbInspectable toolboxLower;
        [SerializeField] Vector3 screwTraySlot = new Vector3(-0.47f, 0.912f, -0.30f);
        [SerializeField] Vector3 coverPark = new Vector3(-0.25f, 0.904f, -0.30f);
        [SerializeField] float trayLift = 0.08f;
        [SerializeField] float trayPullZ = 0.25f;
        [SerializeField] float screwLift = 0.05f;
        [SerializeField] float coverLift = 0.05f;
        [SerializeField] float tierLift = 0.15f;
        [Tooltip("每段移动的时长与运动曲线（配置资产）。为空时用默认值：每段 0.35 s 缓入缓出。")]
        [SerializeField] WbPlaceholderMotionConfig motion;
        WbPlaceholderMotionConfig defaults;
        public WbPlaceholderMotionConfig Motion => motion != null ? motion : (defaults != null ? defaults : defaults = WbPlaceholderMotionConfig.CreateDefault());
        public void SetMotionConfig(WbPlaceholderMotionConfig config) => motion = config;
        public float TrayLift => trayLift;

        readonly Dictionary<Transform, Vector3> home = new Dictionary<Transform, Vector3>();
        readonly Dictionary<Transform, float> originAboveBottom = new Dictionary<Transform, float>();
        readonly HashSet<Transform> taken = new HashSet<Transform>();
        public bool Busy { get; private set; }
        public bool Disassembled { get; private set; }
        public bool UpperTierMoved { get; private set; }
        public bool IsTaken(Transform tray) => taken.Contains(tray);
        public Transform TrayScrews => trayScrews;
        public Transform TrayOldParts => trayOldParts;
        public Transform Cover => cover;
        public Transform[] Screws => screws;
        public Transform ToolboxUpper => toolboxUpper;
        public WbInspectable ToolboxLower => toolboxLower;
        public string LastMessage { get; private set; } = "";

        public void Configure(Transform tScrews, Transform tOld, Transform coverT, Transform[] screwTs, Vector3 slot, Vector3 park,
                              Transform upper, WbInspectable lower)
        {
            trayScrews = tScrews; trayOldParts = tOld; cover = coverT; screws = screwTs; screwTraySlot = slot; coverPark = park;
            toolboxUpper = upper; toolboxLower = lower;
        }

        void Awake()
        {
            var all = new List<Transform>(screws) { trayScrews, trayOldParts, cover, toolboxUpper };
            foreach (var t in all)
            {
                if (t == null) continue;
                home[t] = t.position;
                var r = t.GetComponent<Renderer>();
                originAboveBottom[t] = r != null ? t.position.y - r.bounds.min.y : 0f;
            }
            foreach (var w in FindObjectsByType<WbInspectable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (w.blockedBy != null) w.IsBlocked = true;   // 挡板都在原位：被挡对象先标为“被挡住”
        }

        void OnEnable() => WbCameraRig.Clicked += OnClicked;
        void OnDisable() => WbCameraRig.Clicked -= OnClicked;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || Busy) return;
            if (kb.digit1Key.wasPressedThisFrame) StartCoroutine(ToggleTray(trayScrews));
            if (kb.digit2Key.wasPressedThisFrame) StartCoroutine(ToggleTray(trayOldParts));
            if (kb.uKey.wasPressedThisFrame) StartCoroutine(ToggleUpperTier());
            if (kb.dKey.wasPressedThisFrame) StartCoroutine(Disassembled ? Reassemble() : Disassemble());
        }

        /// <summary>左键点击（来自 WbCameraRig）：只对托盘、工具箱上 / 下层做占位动作，其余只给出说明。</summary>
        void OnClicked(WbCameraRig rig, WbInspectable w)
        {
            if (w == null) { Say(rig, "【占位】这里没有可检查对象"); return; }
            if (w.IsBlocked) { Say(rig, $"【占位】{w.HudText}"); return; }
            if (Busy) { Say(rig, "【占位】上一个演示还没结束"); return; }
            if (w.target == trayScrews || w.target == trayOldParts) { StartCoroutine(ToggleTray(w.target)); return; }
            if (w.target == toolboxUpper) { StartCoroutine(ToggleUpperTier()); return; }
            if (toolboxLower != null && w == toolboxLower) { Say(rig, "【占位】工具箱下层：上层已移开，可以取用"); return; }
            if (w.role.StartsWith("placeholder")) { Say(rig, $"【占位】{w.displayName}：按 D 演示拆下 / 装回（不是正式流程）"); return; }
            Say(rig, $"【占位】已选中 {w.displayName}");
        }

        void Say(WbCameraRig rig, string s) { LastMessage = s; if (rig != null) rig.Status = s; }

        /// <summary>挡板 blocker 回到原位 / 被移开时，切换所有被它挡住的检查对象。</summary>
        static void SetBlocked(Transform blocker, bool blocked)
        {
            foreach (var w in FindObjectsByType<WbInspectable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (w.blockedBy != null && w.blockedBy.target == blocker) w.IsBlocked = blocked;
        }

        /// <summary>托盘路径（Unity 世界坐标）：原位 → 抬起 → 拉到台沿外。</summary>
        public Vector3[] TrayPath(Transform tray)
        {
            var start = home[tray];
            var up = start + Vector3.up * trayLift;
            return new[] { start, up, new Vector3(up.x, up.y, trayPullZ) };
        }

        /// <summary>螺钉路径：原位 → 抬起 → 托盘落点上方 → 落到托盘底（螺钉原点在钉头顶面，落点处抬高螺钉自身高度；4 颗在落点周围错开 8 mm）。</summary>
        public Vector3[] ScrewPath(int i)
        {
            var start = home[screws[i]];
            var up = start + Vector3.up * screwLift;
            var spread = new Vector3((i % 2 == 0 ? -1 : 1) * 0.008f, 0f, (i < 2 ? -1 : 1) * 0.008f);
            var land = screwTraySlot + spread + Vector3.up * originAboveBottom[screws[i]];
            return new[] { start, up, new Vector3(land.x, up.y, land.z), land };
        }

        /// <summary>盖板路径：原位 → 抬起 → 停放框中心上方 → 平放在操作垫上。</summary>
        public Vector3[] CoverPath()
        {
            var start = home[cover];
            var up = start + Vector3.up * coverLift;
            var land = coverPark + Vector3.up * originAboveBottom[cover];
            return new[] { start, up, new Vector3(land.x, up.y, land.z), land };
        }

        /// <summary>工具箱上层路径：原位 → 竖直抬起 tierLift。</summary>
        public Vector3[] UpperTierPath() => new[] { home[toolboxUpper], home[toolboxUpper] + Vector3.up * tierLift };

        public IEnumerator ToggleTray(Transform tray)
        {
            if (tray == null || Busy) yield break;
            Busy = true;
            var p = TrayPath(tray);
            if (!IsTaken(tray))
            {
                for (int k = 0; k < p.Length - 1; k++) yield return Move(tray, p[k], p[k + 1]);
                taken.Add(tray);
                LastMessage = $"【占位】已取出 {tray.name}";
            }
            else
            {
                for (int k = p.Length - 1; k > 0; k--) yield return Move(tray, p[k], p[k - 1]);
                taken.Remove(tray);
                LastMessage = $"【占位】已放回 {tray.name}";
            }
            Busy = false;
        }

        public IEnumerator ToggleUpperTier()
        {
            if (toolboxUpper == null || Busy) yield break;
            Busy = true;
            var p = UpperTierPath();
            if (!UpperTierMoved)
            {
                yield return Move(toolboxUpper, p[0], p[1]);
                UpperTierMoved = true;
                SetBlocked(toolboxUpper, false);
                LastMessage = "【占位】已移开工具箱上层：下层可以取用";
            }
            else
            {
                SetBlocked(toolboxUpper, true);
                yield return Move(toolboxUpper, p[1], p[0]);
                UpperTierMoved = false;
                LastMessage = "【占位】已放回工具箱上层：下层又被挡住";
            }
            Busy = false;
        }

        public IEnumerator Disassemble()
        {
            if (Busy || Disassembled) yield break;
            Busy = true;
            for (int i = 0; i < screws.Length; i++)
            {
                var p = ScrewPath(i);
                for (int k = 0; k < p.Length - 1; k++) yield return Move(screws[i], p[k], p[k + 1]);
            }
            var c = CoverPath();
            for (int k = 0; k < c.Length - 1; k++) yield return Move(cover, c[k], c[k + 1]);
            Disassembled = true;
            SetBlocked(cover, false);
            LastMessage = "【占位】拆下演示完成：4 颗螺钉在托盘，盖板在停放框";
            Busy = false;
        }

        public IEnumerator Reassemble()
        {
            if (Busy || !Disassembled) yield break;
            Busy = true;
            SetBlocked(cover, true);
            var c = CoverPath();
            for (int k = c.Length - 1; k > 0; k--) yield return Move(cover, c[k], c[k - 1]);
            for (int i = screws.Length - 1; i >= 0; i--)
            {
                var p = ScrewPath(i);
                for (int k = p.Length - 1; k > 0; k--) yield return Move(screws[i], p[k], p[k - 1]);
            }
            Disassembled = false;
            LastMessage = "【占位】装回演示完成";
            Busy = false;
        }

        IEnumerator Move(Transform t, Vector3 a, Vector3 b)
        {
            var m = Motion.step;
            float time = 0f;
            while (time < m.Seconds)
            {
                time += Time.deltaTime;
                PoseSegment(t, a, b, time / m.Seconds);
                yield return null;
            }
            t.position = b;
        }

        /// <summary>一段直线移动：时间进度 progress（0–1）按曲线换算，写对象位置。运行和预览共用。</summary>
        public void PoseSegment(Transform t, Vector3 a, Vector3 b, float progress) =>
            t.position = Vector3.Lerp(a, b, Motion.step.Evaluate(progress));

        void OnGUI()
        {
            GUI.Label(new Rect(22, 100, 760, 22), "【占位】1 / 2 或点托盘 = 取放托盘   U 或点工具箱上层 = 移开 / 放回上层   D = 义肢占位件拆下 / 装回");
            if (LastMessage.Length > 0) GUI.Label(new Rect(22, 116, 760, 22), LastMessage);
        }
    }
}
