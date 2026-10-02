using System.IO;
using System.Linq;
using System.Text;
using BorderRepair.FirstOrder.EditorTools.LayoutAB;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BorderRepair.FirstOrder.EditorTools.FaultArtQA
{
    /// <summary>
    /// 在布局 B（主）和 A（对照）测试副本里加“故障美术观感调整”（FaultArtLookFix），只改测试副本，不改正式场景、美术源材质、RobotV4 和流程：
    /// 1) 两个盒投影反射探针（维修架 / 七号一带、工作台一带），开场实时渲染一次（烘焙探针只拍“反射探针静态”的物体，七号、维修架、工作台都不是，烘出来是空的）——金属轴承有东西可反射，不再是死黑；
    /// 2) 上盖内侧保养标记换成场景验收用的材质副本：旧纸色（不再是纯白）、哑光、关掉高光和环境反射——台灯直射下不再过曝；
    /// 3) 维修架近处一盏很弱、不投影的暖色检修补光（可选，看实测）；
    /// 4) 游戏镜头抗锯齿（可选，看细纤维闪烁实测）。
    /// 每一项的数值写在这里，改动清单见 Docs/Integration/Unit07FirstOrder/FaultArtQA/README.md。
    /// </summary>
    public static class FaultArtLookSetup
    {
        public const string ArtDir = FirstOrderSceneBuilder.Root + "/Art/FaultArtQA";
        public const string FixName = "FaultArtLookFix (场景验收观感调整：仅测试副本)";

        // ---- 调整参数（实测后定）
        public static float BayFillIntensity = 0.6f;                        // 0 = 不加补光
        public static float TrayFillIntensity = 0.8f;
        public static AntialiasingMode CameraAA = AntialiasingMode.FastApproximateAntialiasing;   // 实测：FXAA 让纤维闪烁降低约 30%，SMAA 只降约 20%

        public static void Run()
        {
            Directory.CreateDirectory(ArtDir);
            var log = new StringBuilder();
            foreach (var (id, scene) in new[] { ("B", LayoutABScenes.SceneB), ("A", LayoutABScenes.SceneA) })
                Setup(id, scene, log);
            Directory.CreateDirectory(FaultArtAudit.OutDir);
            File.WriteAllText(Path.Combine(FaultArtAudit.OutDir, "setup_log.txt"), log.ToString(), new UTF8Encoding(false));
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static void Setup(string id, string scenePath, StringBuilder log)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            foreach (var old in scene.GetRootGameObjects().Where(g => g.name == FixName)) Object.DestroyImmediate(old);
            var flow = Object.FindFirstObjectByType<FirstOrderFlow>();
            var root = new GameObject(FixName);
            var fix = root.AddComponent<FaultArtLookFix>();
            fix.gameCamera = flow.Rig.Cam;
            fix.originalAA = fix.gameCamera.GetUniversalAdditionalCameraData().antialiasing;
            fix.fixedAA = CameraAA;

            // 1) 反射探针
            var robot = GameObject.Find("UNIT07_RobotV4_DockReady");
            var dock = GameObject.Find("Unit07ServiceDock");
            var bay = robot.GetComponentsInChildren<Renderer>().Concat(dock.GetComponentsInChildren<Renderer>()).Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
            bay.Expand(new Vector3(0.6f, 0.5f, 0.6f));
            var benchTop = GameObject.Find("Bench_Top").GetComponent<Renderer>().bounds;
            var benchBox = new Bounds(new Vector3(benchTop.center.x, benchTop.max.y + 0.45f, benchTop.center.z), new Vector3(benchTop.size.x + 0.1f, 1.0f, benchTop.size.z + 0.3f));
            foreach (var (name, b, cap) in new[] { ("RP_EngineBay", bay, flow.Bearing.WorldBounds().center + Vector3.up * 0.40f), ("RP_Bench", benchBox, flow.MatZone.landing.position + Vector3.up * 0.30f) })
            {
                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                go.transform.position = cap;
                var p = go.AddComponent<ReflectionProbe>();
                p.mode = ReflectionProbeMode.Custom;
                p.boxProjection = true;
                p.center = b.center - cap; p.size = b.size;
                p.resolution = 128; p.hdr = true; p.importance = 1; p.blendDistance = 0.1f;
                // 自己从探针位置拍一张立方体贴图（烘焙探针只拍“反射探针静态”的物体，七号、维修架、工作台都不是，烘出来是空的；
                // 实时探针在这个项目的画质设置下不渲染）——拍的是场景本身，存成资源，运行时零开销
                var camGo = new GameObject("TmpProbeCam");
                var pc = camGo.AddComponent<Camera>();
                camGo.transform.position = cap;
                pc.nearClipPlane = 0.03f; pc.farClipPlane = 20f; pc.allowHDR = true; pc.allowMSAA = false;
                pc.clearFlags = CameraClearFlags.SolidColor; pc.backgroundColor = new Color(0.03f, 0.035f, 0.03f);
                var cube = new Cubemap(128, TextureFormat.RGBAHalf, true);   // 盒投影的粗糙反射，128 足够（256 每张 8 MB）
                bool ok = pc.RenderToCubemap(cube);
                cube.Apply(true);
                Object.DestroyImmediate(camGo);
                string tex = $"{ArtDir}/RP_{id}_{name}.asset";
                AssetDatabase.DeleteAsset(tex);
                AssetDatabase.CreateAsset(cube, tex);
                p.customBakedTexture = AssetDatabase.LoadAssetAtPath<Cubemap>(tex);
                // 平均亮度（检查不是空图）
                float lum = 0f; int n = 0;
                foreach (CubemapFace f in new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ })
                    foreach (var c in cube.GetPixels(f, 3)) { lum += 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b; n++; }
                p.enabled = false;
                fix.probes.Add(p);
                log.AppendLine($"[{id}] 反射探针 {name}：拍摄点 {cap:F2}，盒投影 中心 {b.center:F2} 尺寸 {b.size:F2}，128 HDR 立方体贴图（RenderToCubemap {(ok ? "成功" : "失败")}，平均亮度 {lum / Mathf.Max(1, n):F3}）存到 {tex}");
            }

            // 2) 保养标记：已在 art/unit07-fault-kit-dust-fibers 同步回故障包源（materials.json → M_FK_CoverLabel），场景里不再换材质
            log.AppendLine($"[{id}] 保养标记：用故障包源材质 {AssetDatabase.GetAssetPath(flow.CoverLabel.sharedMaterial)}（已同步验收过的旧纸色 / 哑光 / 关高光和环境反射），不再换场景材质副本");

            // 3) 检修补光（可选）
            if (BayFillIntensity > 0f)
            {
                var l = new GameObject("Dock_InspectionFill (warm, no shadow)").AddComponent<Light>();
                l.transform.SetParent(root.transform, false);
                var seat = flow.Bearing.WorldBounds().center;
                var (stand, _) = LayoutABScenes.Stand(id);
                var from = Vector3.Lerp(new Vector3(stand.x, 1.75f, stand.z), seat, 0.35f) + Vector3.up * 0.25f;
                l.type = LightType.Spot; l.color = new Color(1.0f, 0.88f, 0.72f); l.intensity = BayFillIntensity; l.range = 1.2f;
                l.spotAngle = 55f; l.innerSpotAngle = 25f; l.shadows = LightShadows.None;
                l.transform.SetPositionAndRotation(from, Quaternion.LookRotation(seat - from));
                l.enabled = false;
                fix.addedLights.Add(l);
                log.AppendLine($"[{id}] 检修补光：暖色聚光，强度 {BayFillIntensity}，不投影，位置 {from:F2} 对准轴承位");
            }
            // 4) 托盘 / 轴承盒一带的层板下补光（可选）：像层板底下装的一条暖色小灯，往下照托盘和轴承盒；不投影
            if (TrayFillIntensity > 0f)
            {
                var trays = GameObject.Find("WB_Trays").GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
                var l = new GameObject("Bench_UnderShelfFill (warm, no shadow)").AddComponent<Light>();
                l.transform.SetParent(root.transform, false);
                var target = new Vector3(trays.center.x, trays.max.y, trays.center.z);
                var from = new Vector3(trays.center.x, 1.62f, -0.80f);                // 上层板（y 1.70）下沿、靠墙一侧
                l.type = LightType.Spot; l.color = new Color(1.0f, 0.84f, 0.66f); l.intensity = TrayFillIntensity; l.range = 1.2f;
                l.spotAngle = 70f; l.innerSpotAngle = 30f; l.shadows = LightShadows.None;
                l.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from));
                l.enabled = false;
                fix.addedLights.Add(l);
                log.AppendLine($"[{id}] 层板下补光：暖色聚光，强度 {TrayFillIntensity}，不投影，位置 {from:F2} 对准托盘 / 轴承盒 {target:F2}");
            }
            log.AppendLine($"[{id}] 游戏镜头抗锯齿：{fix.originalAA} → {fix.fixedAA}");
            EditorUtility.SetDirty(fix);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
