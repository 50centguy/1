using System.Collections.Generic;
using UnityEngine;

namespace BorderRepair.FirstOrder
{
    /// <summary>
    /// 首单原型的镜头。两种模式：
    /// - 固定机位（原样）：每一步自动切到合适的镜头，玩家也可以按 1–9 / 镜头栏手动切换，切换时短暂平滑过渡；
    /// - 第一人称（场景里配了 <see cref="FirstPersonWalker"/> 时）：镜头跟着玩家的眼睛走（<see cref="FirstPerson"/>）。
    ///   这时流程的自动切镜头（<see cref="Auto"/>）不再拉走镜头；固定机位仍可用 <see cref="Go"/> 进入（“近看”），<see cref="Walk"/> 回到行走。
    /// 程序验收和自检照旧用 Go(机位) 进入固定机位。
    /// </summary>
    public class FirstOrderCameraRig : MonoBehaviour
    {
        public const string Dock = "Dock", EngineL = "EngineL", EngineRear = "EngineRear", Bench = "Bench", Overview = "Overview", EngineR = "EngineR",
                            Record = "Record", Compare = "Compare", EngineClose = "EngineClose", FirstPerson = "FirstPerson";
        public static readonly string[] Order = { Dock, EngineL, EngineRear, Bench, Overview, EngineR, Record, Compare, EngineClose };
        public static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
        {
            [Dock] = "维修座", [EngineL] = "左引擎", [EngineRear] = "左引擎背面", [Bench] = "工作台", [Overview] = "总览", [EngineR] = "右引擎（对照）", [Record] = "保养记录（翻盖内侧）", [Compare] = "新旧轴承对比", [EngineClose] = "左引擎近看（进气口 / 轴承位）",
            [FirstPerson] = "行走（第一人称）",
        };

        [System.Serializable] public struct Shot { public string id; public Transform pose; public float fov; }

        [SerializeField] Camera cam;
        [SerializeField] List<Shot> shots = new List<Shot>();
        [SerializeField] float blendSeconds = 0.35f;
        [Tooltip("第一人称行走（可空：为空时只有固定机位，和原来一样）")]
        [SerializeField] FirstPersonWalker walker;

        public Camera Cam => cam;
        public string Current { get; private set; } = Dock;
        public FirstPersonWalker Walker => walker;
        /// <summary>场景支持第一人称行走。</summary>
        public bool FirstPersonEnabled => walker != null && walker.isActiveAndEnabled;
        /// <summary>现在镜头在玩家眼睛上（不在固定机位）。</summary>
        public bool Walking => FirstPersonEnabled && Current == FirstPerson;
        Vector3 fromPos; Quaternion fromRot; float fromFov; float blend = 1f;

        public void Configure(Camera c, List<Shot> s) { cam = c; shots = s; }
        public void ConfigureWalker(FirstPersonWalker w) => walker = w;

        public Shot Get(string id) => shots.Find(s => s.id == id);

        /// <summary>进入固定机位（玩家点镜头栏、数字键、观察面板的“切到…”，以及程序验收）。第一人称场景里这就是“近看”，玩家的身体留在原地。</summary>
        public void Go(string id, bool instant = false)
        {
            if (id == FirstPerson) { Walk(instant); return; }
            var s = Get(id);
            if (s.pose == null) return;
            Begin(id, instant);
        }

        /// <summary>流程推进时的自动切镜头：只有固定机位模式才切；第一人称场景里不把玩家的视角拉走。</summary>
        public void Auto(string id)
        {
            if (FirstPersonEnabled) return;
            Go(id);
        }

        /// <summary>回到第一人称行走（镜头从当前位置平滑回到眼睛）。</summary>
        public void Walk(bool instant = false)
        {
            if (!FirstPersonEnabled) return;
            Begin(FirstPerson, instant);
        }

        void Begin(string id, bool instant)
        {
            Current = id;
            fromPos = cam.transform.position; fromRot = cam.transform.rotation; fromFov = cam.fieldOfView;
            blend = instant ? 1f : 0f;
            if (instant) Apply(1f);
        }

        bool Target(out Vector3 p, out Quaternion r, out float fov)
        {
            if (Current == FirstPerson && walker != null) { p = walker.Eye.position; r = walker.Eye.rotation; fov = walker.Fov; return true; }
            var s = Get(Current);
            if (s.pose == null) { p = default; r = default; fov = 0f; return false; }
            p = s.pose.position; r = s.pose.rotation; fov = s.fov; return true;
        }

        void Apply(float k)
        {
            if (!Target(out var p, out var r, out var fov)) return;
            if (k >= 1f) { cam.transform.SetPositionAndRotation(p, r); cam.fieldOfView = fov; return; }
            float e = Mathf.SmoothStep(0f, 1f, k);
            cam.transform.SetPositionAndRotation(Vector3.Lerp(fromPos, p, e), Quaternion.Slerp(fromRot, r, e));
            cam.fieldOfView = Mathf.Lerp(fromFov, fov, e);
        }

        void LateUpdate()
        {
            if (blend >= 1f) { Apply(1f); return; }
            blend = Mathf.Min(1f, blend + Time.deltaTime / Mathf.Max(0.01f, blendSeconds));
            Apply(blend);
        }
    }
}
