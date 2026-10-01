using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WorkbenchArea
{
    /// <summary>
    /// 【占位交互，仅用于展示和路径验证，不是正式维修流程】
    /// - 托盘取放：托盘先竖直抬起 trayLift，再水平拉到台沿外（世界 z = trayPullZ，即玩家手边）；再次触发时原路放回。
    /// - 义肢占位件拆装演示：4 颗螺钉逐颗 抬起 → 平移到螺钉托盘落点上方 → 放下；盖板 抬起 → 平移到操作垫 COVER 停放框 → 放下；可原路装回。
    /// 路径关键点与 Blender 检查脚本（build_workbench_area.py 的 tray / screws / cover 路径检查）一致，坐标已换成 Unity 坐标。
    /// 测试场景键盘：1 = 取放螺钉托盘，2 = 取放旧件托盘，D = 拆下 / 装回演示。
    /// </summary>
    public class WbPlaceholderDemo : MonoBehaviour
    {
        [SerializeField] Transform trayScrews;
        [SerializeField] Transform trayOldParts;
        [SerializeField] Transform cover;
        [SerializeField] Transform[] screws = new Transform[0];
        [SerializeField] Vector3 screwTraySlot = new Vector3(-0.47f, 0.912f, -0.30f);
        [SerializeField] Vector3 coverPark = new Vector3(-0.26f, 0.904f, -0.30f);
        [SerializeField] float trayLift = 0.08f;
        [SerializeField] float trayPullZ = 0.25f;
        [SerializeField] float screwLift = 0.05f;
        [SerializeField] float coverLift = 0.05f;
        [SerializeField] float stepSeconds = 0.35f;

        readonly Dictionary<Transform, Vector3> home = new Dictionary<Transform, Vector3>();
        readonly Dictionary<Transform, float> originAboveBottom = new Dictionary<Transform, float>();
        readonly HashSet<Transform> taken = new HashSet<Transform>();
        public bool Busy { get; private set; }
        public bool Disassembled { get; private set; }
        public bool IsTaken(Transform tray) => taken.Contains(tray);
        public Transform TrayScrews => trayScrews;
        public Transform TrayOldParts => trayOldParts;
        public Transform Cover => cover;
        public Transform[] Screws => screws;
        public string LastMessage { get; private set; } = "";

        public void Configure(Transform tScrews, Transform tOld, Transform coverT, Transform[] screwTs, Vector3 slot, Vector3 park)
        {
            trayScrews = tScrews; trayOldParts = tOld; cover = coverT; screws = screwTs; screwTraySlot = slot; coverPark = park;
        }

        void Awake()
        {
            var all = new List<Transform>(screws) { trayScrews, trayOldParts, cover };
            foreach (var t in all)
            {
                if (t == null) continue;
                home[t] = t.position;
                var r = t.GetComponent<Renderer>();
                originAboveBottom[t] = r != null ? t.position.y - r.bounds.min.y : 0f;
            }
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || Busy) return;
            if (kb.digit1Key.wasPressedThisFrame) StartCoroutine(ToggleTray(trayScrews));
            if (kb.digit2Key.wasPressedThisFrame) StartCoroutine(ToggleTray(trayOldParts));
            if (kb.dKey.wasPressedThisFrame) StartCoroutine(Disassembled ? Reassemble() : Disassemble());
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

        /// <summary>盖板路径：原位 → 抬起 → 停放框上方 → 平放在操作垫上。</summary>
        public Vector3[] CoverPath()
        {
            var start = home[cover];
            var up = start + Vector3.up * coverLift;
            var land = coverPark + Vector3.up * originAboveBottom[cover];
            return new[] { start, up, new Vector3(land.x, up.y, land.z), land };
        }

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
            LastMessage = "【占位】拆下演示完成：4 颗螺钉在托盘，盖板在停放框";
            Busy = false;
        }

        public IEnumerator Reassemble()
        {
            if (Busy || !Disassembled) yield break;
            Busy = true;
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
            float time = 0f;
            while (time < stepSeconds)
            {
                time += Time.deltaTime;
                t.position = Vector3.Lerp(a, b, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / stepSeconds)));
                yield return null;
            }
            t.position = b;
        }

        void OnGUI()
        {
            GUI.Label(new Rect(22, 92, 700, 22), "【占位】1 = 取放螺钉托盘   2 = 取放旧件托盘   D = 义肢占位件 拆下 / 装回演示");
            if (LastMessage.Length > 0) GUI.Label(new Rect(22, 112, 700, 22), LastMessage);
        }
    }
}
