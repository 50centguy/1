using System.IO;
using System.Linq;
using BorderRepair.TwoNight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BorderRepair.Art.Unit07TrayVariant.EditorTools
{
    /// <summary>
    /// Unity 相机渲染的前后对比图（只读，不保存场景；不读桌面）：
    /// 前 = 72838e9 发布场景（原托盘、旧挂点）；后 = 隔离验证场景（变体、新挂点）。七号放在同一位置、同一端盘姿态，相机相对右下爪同一机位。
    /// 另有：沿握杆方向的剖视（相机近裁剪面放在过握点、垂直于握杆的平面上），和托盘在托盘架上的使用状态。
    /// </summary>
    public static class TrayVariantShots
    {
        const string OutDir = "ArtSource/Unit07TrayVariant/Renders/Unity";

        public static void Run()
        {
            Directory.CreateDirectory(OutDir);
            Shoot(TrayVariantBuild.SourceScene, "before");
            Shoot(TrayVariantBuild.VerifyScene, "after");
            Debug.Log("[TrayVariant] Unity shots 已写出");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static void Shoot(string scenePath, string tag)
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var inc = Object.FindFirstObjectByType<TrayIncident>();
            var so = new SerializedObject(inc);
            var hold = so.FindProperty("holdAnchor").objectReferenceValue as Transform;
            var body = so.FindProperty("body").objectReferenceValue as Transform;
            var robot = inc.RobotRoot; var ov = inc.Overlay; var tray = inc.Tray.transform;
            var homePos = tray.position; var homeRot = tray.rotation;
            var anim = robot.GetComponentInChildren<Animator>(); if (anim != null) anim.enabled = false;
            var dock = Object.FindFirstObjectByType<BorderRepair.FirstOrder.FirstOrderFlow>().Dock;

            // 1) 托盘在托盘架上（使用状态），同一机位
            var cam = new GameObject("ShotCam").AddComponent<Camera>();
            cam.fieldOfView = 35f; cam.nearClipPlane = 0.01f; cam.farClipPlane = 20f;
            var rt = new RenderTexture(1400, 900, 24); cam.targetTexture = rt;
            void Save(string name)
            {
                cam.Render(); RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(OutDir, $"{name}_{tag}.png"), tex.EncodeToPNG());
                RenderTexture.active = null; Object.DestroyImmediate(tex);
            }
            cam.transform.position = homePos + new Vector3(-0.45f, 0.35f, -0.55f); cam.transform.LookAt(homePos + Vector3.up * 0.04f); Save("U01_on_shelf");

            // 2) 端盘：七号放在维修座上方同一位置（悬停位 + 60 mm），审计端盘姿态，托盘在挂点上
            robot.position = dock.RobotAnchor.position + Vector3.up * 0.18f;
            ov.RightWeight = 1f; ov.LeftWeight = 1f; ov.JawOpen = 0f; ov.UpperJawExtra = 0f;
            for (int i = 0; i < 6; i++) ov.RightBones[i].localRotation = ov.RightTarget(i);
            for (int i = 0; i < 4; i++) ov.LeftBones[i].localRotation = ov.LeftTarget(i);
            tray.SetPositionAndRotation(hold.position, hold.rotation);
            var jaw = ov.RightBones[5];
            var t = jaw.position;
            var right = body.right; var fwd = body.forward;
            var views = new (string n, Vector3 off)[] {
                ("U02_grip_front", fwd * 0.38f + Vector3.up * 0.05f), ("U03_grip_right", right * 0.38f + Vector3.up * 0.05f),
                ("U04_grip_back_right", (right - fwd).normalized * 0.38f + Vector3.up * 0.06f), ("U05_grip_below", -Vector3.up * 0.32f + right * 0.12f + fwd * 0.08f),
                ("U06_carry_wide", (right * 0.6f - fwd * 0.9f) + Vector3.up * 0.25f) };
            foreach (var (n, off) in views)
            {
                cam.transform.position = t + off;
                cam.transform.LookAt(n == "U06_carry_wide" ? (t + tray.position) / 2f : t, Mathf.Abs(Vector3.Dot(off.normalized, Vector3.up)) > 0.9f ? fwd : Vector3.up);
                Save(n);
            }

            // 3) 剖视：过右手咬合中心、垂直于握杆（托盘本地 Y）的平面；相机在握杆 −Y 方向（夹爪尖一侧）向 +Y 看，近裁剪面就是剖切面
            var gc = ov.RightBones[3].TransformPoint(TrayVariantBuild.GripCenterWrist);
            var axis = tray.TransformDirection(Vector3.up);
            float dist = 0.30f;
            cam.orthographic = true; cam.orthographicSize = 0.075f;
            cam.transform.position = gc - axis * dist; cam.transform.rotation = Quaternion.LookRotation(axis, tray.TransformDirection(Vector3.forward));
            cam.nearClipPlane = dist - 0.0005f; Save("U07_section_at_grip");
            // 剖面往夹爪铰链方向再进 25 mm（第一版失败的位置）
            cam.transform.position = gc + axis * 0.025f - axis * dist; Save("U08_section_grip_plus25mm");
            Object.DestroyImmediate(cam.gameObject);
        }
    }
}
