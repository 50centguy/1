using UnityEngine;
using UnityEngine.InputSystem;

namespace WorkbenchArea
{
    /// <summary>
    /// 两个固定镜头：游戏镜头（站在台前）与近距维修镜头（俯看操作垫）。Tab 切换；鼠标悬停显示对象名（占位提示）。
    /// 镜头位置与竖直视场与 Blender 检查脚本中的 CAMERAS 一致。
    /// </summary>
    public class WbCameraRig : MonoBehaviour
    {
        public enum View { Game = 0, CloseUp = 1 }

        [SerializeField] Camera cam;
        [SerializeField] Transform gamePose;
        [SerializeField] Transform closeUpPose;
        [SerializeField] float gameFov = 50f;
        [SerializeField] float closeUpFov = 38f;
        [SerializeField] bool showHud = true;

        public View Current { get; private set; } = View.Game;
        public Camera Cam => cam;
        public WbInspectable Hovered { get; private set; }

        public void Configure(Camera c, Transform game, Transform closeUp, float gFov, float cFov)
        {
            cam = c; gamePose = game; closeUpPose = closeUp; gameFov = gFov; closeUpFov = cFov;
        }

        void Start() => SetView(View.Game);

        public void SetView(View v)
        {
            Current = v;
            var p = v == View.Game ? gamePose : closeUpPose;
            cam.transform.SetPositionAndRotation(p.position, p.rotation);
            cam.fieldOfView = v == View.Game ? gameFov : closeUpFov;
        }

        public WbInspectable Pick(Vector2 screen) => Pick(cam.ScreenPointToRay(screen));

        /// <summary>
        /// 点选规则：沿射线收集检查区（触发器），忽略被实体表面挡住的；在最先命中的检查区之后 3 cm 内的候选里取体积最小的，
        /// 这样螺钉、旋钮、指示灯这类小件不会被它们所在的盖板 / 机身检查区吞掉。没有检查区时，命中的实体对象若带检查区也算。
        /// </summary>
        public static WbInspectable Pick(Ray ray)
        {
            const float window = 0.03f;
            var hits = Physics.RaycastAll(ray, 5f, ~0, QueryTriggerInteraction.Collide);
            if (hits.Length == 0) return null;
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            float solid = float.MaxValue;
            Collider solidCol = null;
            foreach (var h in hits) if (!h.collider.isTrigger) { solid = h.distance; solidCol = h.collider; break; }
            WbInspectable best = null;
            float first = -1f, bestVol = float.MaxValue;
            foreach (var h in hits)
            {
                if (!h.collider.isTrigger || h.distance > solid + 0.004f) continue;
                var wi = h.collider.GetComponent<WbInspectable>();
                if (wi == null) continue;
                if (first < 0f) first = h.distance;
                if (h.distance > first + window) break;
                var s = h.collider.bounds.size;
                float vol = s.x * s.y * s.z;
                if (vol < bestVol) { bestVol = vol; best = wi; }
            }
            if (best != null) return best;
            return solidCol != null ? solidCol.GetComponentInChildren<WbInspectable>() : null;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.tabKey.wasPressedThisFrame) SetView(Current == View.Game ? View.CloseUp : View.Game);
            var mouse = Mouse.current;
            if (mouse != null) Hovered = Pick(mouse.position.ReadValue());
        }

        void OnGUI()
        {
            if (!showHud) return;
            GUI.Box(new Rect(12, 12, 720, 124), GUIContent.none);
            GUI.Label(new Rect(22, 18, 500, 22), $"工作台区域测试 · 镜头：{(Current == View.Game ? "游戏镜头" : "近距维修镜头")}（Tab 切换）");
            GUI.Label(new Rect(22, 40, 500, 22), Hovered != null ? $"指向：{Hovered.displayName}（{Hovered.role}）" : "指向：—");
            GUI.Label(new Rect(22, 60, 500, 22), "占位交互：仅用于展示和路径验证，不是正式维修流程");
        }
    }
}
