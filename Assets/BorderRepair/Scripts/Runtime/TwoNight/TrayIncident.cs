using System;
using System.Collections;
using BorderRepair.Dock;
using UnityEngine;

namespace BorderRepair.TwoNight
{
    /// <summary>
    /// 第一晚“七号端盘失衡”演出（状态驱动，不做物理）。路径点由编辑器构建脚本算好、做过静态间隙检查（incident_motion.md），真实播放另有逐帧探针：
    /// 0. 准备（第一帧渲染之前）：七号在维修座上方的搬运高度 T1，右手已经端着零件盘（审计 tray_handoff.md 第 3 节的姿态），左臂收纳；
    /// 1. 端着盘停一会儿 → 机身轻微左倾（托盘随机身一起偏斜）、屏幕 TRAY UNSTABLE → 回正；
    /// 2. 移到托盘架上方 → 把盘降到离原位 8 mm（再低下爪会压进托盘架边沿）→ 上爪张开 → 盘交还托盘架（DockPickable.PutBack 恢复父对象 / 本地位置 / 本地旋转），
    ///    但先停在下爪上不动 → 手水平滑出（方向由构建脚本选，见 incident_motion.md）→ 贴着横杆的爪都分开后，托盘落下 8 mm 回到原位；
    /// 3. 升回 T1 → 两臂交还 Animator → 竖直落回悬停位 H，旋转完全回正。
    /// 不从托盘架上“夹起”托盘：实测七号的下爪在托盘原位时会压进托盘架 3.3 mm（grip_vs_shelf.md），真正的夹取 / 放下要等托盘架或托盘补件（见 README）。
    /// 演出期间预制体根的位置 / 旋转只由本组件写（维修座在悬停状态下不写根）。
    /// </summary>
    public class TrayIncident : MonoBehaviour
    {
        [SerializeField] Transform robotRoot;
        [SerializeField] Transform body;
        [SerializeField] TrayCarryOverlay overlay;
        [SerializeField] DockPickable tray;
        [SerializeField] Transform holdAnchor;
        [SerializeField] GameObject screenStatus;
        [Header("路径（世界坐标，预制体根的位置；旋转始终为悬停旋转）")]
        [SerializeField] Vector3 hover;
        [SerializeField] Quaternion hoverRot = Quaternion.identity;
        [SerializeField] Vector3 transitOverDock, transitOverShelf, place, release, withdraw, withdrawUp;
        [Header("节奏")]
        [SerializeField] float leanDegrees = 4f;
        [SerializeField] float speed = 0.22f;
        [SerializeField] float armBlendSeconds = 0.9f;
        [SerializeField] float jawSeconds = 0.35f;
        [SerializeField] float dropSeconds = 0.12f;

        public bool Playing { get; private set; }
        public bool Done { get; private set; }
        public bool Prepared { get; private set; }
        public string Phase { get; private set; } = "idle";
        public float LeanNow { get; private set; }
        public Transform RobotRoot => robotRoot;
        public DockPickable Tray => tray;
        public Vector3 HoverPosition => hover;
        public Quaternion HoverRotation => hoverRot;
        public TrayCarryOverlay Overlay => overlay;
        public float LeanDegrees => leanDegrees;
        public event Action<string> Caption;
        Vector3 leanPivot, leanBase;

        public void Configure(Transform root, Transform bodyBone, TrayCarryOverlay o, DockPickable t, Transform anchor, GameObject status,
                              Vector3 h, Quaternion hr, Vector3 overDock, Vector3 overShelf, Vector3 placeAt, Vector3 releaseAt, Vector3 withdrawTo, Vector3 withdrawUpTo, float leanDeg)
        {
            robotRoot = root; body = bodyBone; overlay = o; tray = t; holdAnchor = anchor; screenStatus = status;
            hover = h; hoverRot = hr; transitOverDock = overDock; transitOverShelf = overShelf; place = placeAt; release = releaseAt; withdraw = withdrawTo; withdrawUp = withdrawUpTo; leanDegrees = leanDeg;
        }

        /// <summary>第一帧渲染之前调用：七号在 T1、右手端着盘、左臂收纳。</summary>
        public void Prepare()
        {
            robotRoot.SetPositionAndRotation(transitOverDock, hoverRot);
            overlay.RightWeight = 1f; overlay.LeftWeight = 1f; overlay.JawOpen = 0f; overlay.UpperJawExtra = 0f;
            tray.HoldPoint = holdAnchor;
            tray.Take();
            tray.transform.localPosition = Vector3.zero;
            tray.transform.localRotation = Quaternion.identity;
            Prepared = true;
            Phase = "carry";
        }

        public Coroutine Play() => StartCoroutine(Run());

        IEnumerator Move(Vector3 to)
        {
            var from = robotRoot.position;
            float dur = Mathf.Max(0.3f, Vector3.Distance(from, to) / speed);
            for (float t = 0; t < dur; t += Time.deltaTime)
            {
                robotRoot.SetPositionAndRotation(Vector3.Lerp(from, to, Mathf.SmoothStep(0, 1, t / dur)), hoverRot);
                yield return null;
            }
            robotRoot.SetPositionAndRotation(to, hoverRot);
        }

        IEnumerator Blend(Action<float> set, float from, float to, float seconds)
        {
            for (float t = 0; t < seconds; t += Time.deltaTime) { set(Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, t / seconds))); yield return null; }
            set(to);
        }

        void Lean(float deg)
        {
            LeanNow = deg;
            var q = Quaternion.AngleAxis(deg, hoverRot * Vector3.forward);
            robotRoot.SetPositionAndRotation(leanPivot + q * (leanBase - leanPivot), q * hoverRot);
        }

        IEnumerator Run()
        {
            if (!Prepared) Prepare();
            Playing = true; Done = false;
            Phase = "carry";
            Caption?.Invoke("打烊了。七号右手端着零件盘，在维修座上方慢慢飘着。");
            yield return new WaitForSeconds(1.2f);

            Phase = "lean";
            leanBase = robotRoot.position; leanPivot = body.position;
            Caption?.Invoke("七号的机身慢慢往左边沉，零件盘跟着斜了……");
            if (screenStatus != null) screenStatus.SetActive(true);
            float[] keys = { leanDegrees * 0.6f, leanDegrees, leanDegrees * 0.75f, leanDegrees, 0f };   // 克制：慢慢倾、两次小幅回摆、回正
            float[] secs = { 1.2f, 0.9f, 0.6f, 0.6f, 1.4f };
            for (int k = 0; k < keys.Length; k++)
            {
                float a = LeanNow, b = keys[k];
                for (float t = 0; t < secs[k]; t += Time.deltaTime) { Lean(Mathf.Lerp(a, b, Mathf.SmoothStep(0, 1, t / secs[k]))); yield return null; }
                Lean(b);
                if (k == 1) yield return new WaitForSeconds(0.8f);
            }
            Lean(0f);
            robotRoot.SetPositionAndRotation(leanBase, hoverRot);
            Caption?.Invoke("我：盘先放回去。今晚先停着，明天开盖看看。");

            Phase = "return";
            yield return Blend(v => overlay.SteadyWeight = v, 0f, 1f, 0.5f);   // 放盘前先稳住身体（停掉浮动，路径按 Idle_Hover 第 0 帧算）
            yield return Move(transitOverShelf);
            yield return Move(place);                                         // 盘离原位 8 mm
            yield return Blend(v => overlay.UpperJawExtra = v, 0f, 1f, jawSeconds);   // 上爪张开，下爪还托着横杆
            Phase = "release";
            var held = (tray.transform.position, tray.transform.rotation);
            tray.PutBack();                                                   // 恢复父对象、本地位置、本地旋转（原位）……
            var home = (tray.transform.position, tray.transform.rotation);
            tray.transform.SetPositionAndRotation(held.position, held.rotation);   // ……但盘还搁在下爪上、离原位 8 mm，不跟手走
            yield return Move(release);                                       // 手水平滑出，贴着横杆的爪都分开
            for (float t = 0; t < dropSeconds; t += Time.deltaTime)           // 失去支撑，落下 8 mm
            {
                float u = (t / dropSeconds) * (t / dropSeconds);
                tray.transform.SetPositionAndRotation(Vector3.Lerp(held.position, home.position, u), Quaternion.Slerp(held.rotation, home.rotation, u));
                yield return null;
            }
            tray.transform.SetPositionAndRotation(home.position, home.rotation);
            Phase = "away";
            yield return Move(withdraw);
            yield return Move(withdrawUp);
            yield return Move(transitOverDock);
            if (screenStatus != null) screenStatus.SetActive(false);
            Phase = "arms_out";
            yield return Blend(v => { overlay.RightWeight = v; overlay.LeftWeight = v; overlay.SteadyWeight = v; }, 1f, 0f, armBlendSeconds);
            overlay.UpperJawExtra = 0f; overlay.JawOpen = 1f;
            Phase = "descend";
            yield return Move(hover);
            robotRoot.SetPositionAndRotation(hover, hoverRot);
            Phase = "done";
            Playing = false; Done = true;
        }
    }
}
