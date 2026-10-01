using System.Collections.Generic;
using UnityEngine;

namespace BorderRepair.FirstOrder
{
    /// <summary>首单原型的固定镜头：每一步自动切到合适的镜头，玩家也可以按 1–6 手动切换。切换时短暂平滑过渡。</summary>
    public class FirstOrderCameraRig : MonoBehaviour
    {
        public const string Dock = "Dock", EngineL = "EngineL", EngineRear = "EngineRear", Bench = "Bench", Overview = "Overview", EngineR = "EngineR";
        public static readonly string[] Order = { Dock, EngineL, EngineRear, Bench, Overview, EngineR };
        public static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
        {
            [Dock] = "维修座", [EngineL] = "左引擎", [EngineRear] = "左引擎背面", [Bench] = "工作台", [Overview] = "总览", [EngineR] = "右引擎（对照）",
        };

        [System.Serializable] public struct Shot { public string id; public Transform pose; public float fov; }

        [SerializeField] Camera cam;
        [SerializeField] List<Shot> shots = new List<Shot>();
        [SerializeField] float blendSeconds = 0.35f;

        public Camera Cam => cam;
        public string Current { get; private set; } = Dock;
        Vector3 fromPos; Quaternion fromRot; float fromFov; float blend = 1f;

        public void Configure(Camera c, List<Shot> s) { cam = c; shots = s; }

        public Shot Get(string id) => shots.Find(s => s.id == id);

        public void Go(string id, bool instant = false)
        {
            var s = Get(id);
            if (s.pose == null) return;
            Current = id;
            fromPos = cam.transform.position; fromRot = cam.transform.rotation; fromFov = cam.fieldOfView;
            blend = instant ? 1f : 0f;
            if (instant) Apply(1f);
        }

        void Apply(float k)
        {
            var s = Get(Current);
            float e = Mathf.SmoothStep(0f, 1f, k);
            cam.transform.SetPositionAndRotation(Vector3.Lerp(fromPos, s.pose.position, e), Quaternion.Slerp(fromRot, s.pose.rotation, e));
            cam.fieldOfView = Mathf.Lerp(fromFov, s.fov, e);
        }

        void LateUpdate()
        {
            if (blend >= 1f) { Apply(1f); return; }
            blend = Mathf.Min(1f, blend + Time.deltaTime / Mathf.Max(0.01f, blendSeconds));
            Apply(blend);
        }
    }
}
