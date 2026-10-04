using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BorderRepair.TwoNight.EditorTools
{
    /// <summary>只读诊断：用 Unity 相机拍审计握法的近景（托盘在右手挂点上），看下爪与托盘的实际关系。不保存场景。</summary>
    public static class TwoNightGripShots
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene(TwoNightBuilder.RobotPath, OpenSceneMode.Single);
            var inc = Object.FindFirstObjectByType<TrayIncident>();
            var so = new SerializedObject(inc);
            var robot = inc.RobotRoot; var ov = inc.Overlay;
            var hold = so.FindProperty("holdAnchor").objectReferenceValue as Transform;
            var body = so.FindProperty("body").objectReferenceValue as Transform;
            robot.position = so.FindProperty("transitOverDock").vector3Value;
            var anim = robot.GetComponentInChildren<Animator>(); if (anim != null) anim.enabled = false;
            ov.RightWeight = 1f; ov.LeftWeight = 1f; ov.JawOpen = 0f; ov.UpperJawExtra = 0f;
            for (int i = 0; i < 6; i++) ov.RightBones[i].localRotation = ov.RightTarget(i);
            for (int i = 0; i < 4; i++) ov.LeftBones[i].localRotation = ov.LeftTarget(i);
            var trayT = inc.Tray.transform;
            trayT.SetPositionAndRotation(hold.position, hold.rotation);
            var jaw = ov.RightBones[5];
            var target = jaw.position;
            var dir = Path.Combine(TwoNightBuilder.DocsDir, "img"); Directory.CreateDirectory(dir);
            var cam = new GameObject("GripShotCam").AddComponent<Camera>();
            cam.fieldOfView = 30f; cam.nearClipPlane = 0.01f; cam.farClipPlane = 10f;
            var rt = new RenderTexture(1200, 900, 24);
            cam.targetTexture = rt;
            var views = new (string n, Vector3 off)[] {
                ("front", body.forward * 0.35f + Vector3.up * 0.05f), ("right", body.right * 0.35f + Vector3.up * 0.05f),
                ("below", -Vector3.up * 0.30f + body.right * 0.12f + body.forward * 0.08f), ("above", Vector3.up * 0.35f + body.right * 0.05f + body.forward * 0.05f),
                ("back_right", (body.right - body.forward).normalized * 0.35f + Vector3.up * 0.06f) };
            foreach (var (n, off) in views)
            {
                cam.transform.position = target + off;
                cam.transform.LookAt(target, Mathf.Abs(Vector3.Dot(off.normalized, Vector3.up)) > 0.9f ? body.forward : Vector3.up);
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(dir, "grip_" + n + ".png"), tex.EncodeToPNG());
                RenderTexture.active = null;
            }
            Debug.Log("[TwoNightBuild] grip shots 已写出");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
