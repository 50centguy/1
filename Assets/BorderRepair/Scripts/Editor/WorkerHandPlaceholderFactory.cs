using System;
using BorderRepair.Inspection;
using UnityEditor;
using UnityEngine;
using C = BorderRepair.EditorTools.WorkerHandCaseContent;

namespace BorderRepair.EditorTools
{
    /// <summary>
    /// 低面数占位义手：Unity 基本几何体 + 项目已有的占位材质。不追求最终美术。
    /// 坐标（米）：前臂沿 X 轴，手指朝 +X；背侧（盖板、封条、螺丝）朝 -Z，即检查台镜头方向；手腕顶部（+Y）是数据接口。
    /// 下方有一个零件盘，螺丝和盖板拆下后吸附到盘上的固定位置。
    /// </summary>
    internal static class WorkerHandPlaceholderFactory
    {
        const string MaterialDir = "Assets/BorderRepair/Art/Materials";

        static Material M(string key)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialDir}/M_{key}.mat");
            if (mat == null) throw new InvalidOperationException($"找不到占位材质 M_{key}，请先运行 Border Repair > Build Prototype。");
            return mat;
        }

        static GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, string mat, Vector3 euler = default) =>
            PlaceholderItemFactory.Part(parent, type, name, pos, scale, M(mat), euler);

        static Transform Node(Transform parent, string name, Vector3 localPos, Vector3 localEuler = default)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            t.localEulerAngles = localEuler;
            return t;
        }

        static GameObject Group(Transform parent, string name)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            return g;
        }

        static RepairPart Point(Transform parent, string id, Vector3 localPos, Vector3 localEuler = default)
        {
            var t = Node(parent, "Point_" + id, localPos, localEuler);
            t.gameObject.AddComponent<InspectionPoint>().Configure(id, null, null);
            return t.gameObject.AddComponent<RepairPart>();
        }

        public const string SealLabelName = "SealLabel";

        /// <summary>
        /// 封条上可读的印字：贴着封条正面的小面片，材质为 URP Lit + 预先渲染的文字贴图（见 SealLabelBaker），
        /// 接受光照、被其他物体正常遮挡。撕开后左半边只显示贴图左侧对应的部分。
        /// 已有同名子物体且是面片时不重复添加；旧版的 TextMesh 印字会被替换。返回是否有改动。
        /// </summary>
        internal static bool AddSealLabels(Transform intact, Transform torn)
        {
            var (intactMat, tornMat) = SealLabelBaker.EnsureMaterials();
            bool changed = false;
            // 完整封条 36×9 mm，正面在 z = -0.4 mm；面片略小一圈，贴在正面前 0.05 mm
            changed |= SealLabel(intact, intactMat, new Vector3(0f, 0f, -0.00045f), new Vector2(0.0345f, 0.0082f));
            // 撕开后的左半边 14×9 mm，中心 x = -11 mm
            changed |= SealLabel(torn, tornMat, new Vector3(-0.011f, 0f, -0.00045f), new Vector2(0.0135f, 0.0082f));
            return changed;
        }

        static bool SealLabel(Transform parent, Material mat, Vector3 localPos, Vector2 size)
        {
            if (parent == null) return false;
            var existing = parent.Find(SealLabelName);
            if (existing != null && existing.GetComponent<TextMesh>() == null) return false;
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = SealLabelName;
            UnityEngine.Object.DestroyImmediate(quad.GetComponent<Collider>());   // 拾取仍由封条本身的碰撞体负责
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = localPos;
            quad.transform.localRotation = Quaternion.identity;        // 面片正面朝 -Z，与封条正面一致
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);
            var mr = quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return true;
        }

        public static GameObject Build()
        {
            var root = new GameObject("Item_WorkerProsthetic_Placeholder").transform;

            // ---------- 机体（不可检查） ----------
            var body = Group(root, "Body").transform;
            Part(body, PrimitiveType.Cube, "ForearmCore", new Vector3(-0.05f, 0f, 0.005f), new Vector3(0.20f, 0.06f, 0.05f), "plastic_grey");
            Part(body, PrimitiveType.Cylinder, "SocketCuff", new Vector3(-0.162f, 0f, 0.005f), new Vector3(0.078f, 0.02f, 0.078f), "rubber", new Vector3(0, 0, 90));
            // 盖板下的舱口边框
            Part(body, PrimitiveType.Cube, "BayRimTop", new Vector3(-0.05f, 0.027f, -0.026f), new Vector3(0.17f, 0.006f, 0.012f), "metal_dark");
            Part(body, PrimitiveType.Cube, "BayRimBottom", new Vector3(-0.05f, -0.027f, -0.026f), new Vector3(0.17f, 0.006f, 0.012f), "metal_dark");
            Part(body, PrimitiveType.Cube, "BayRimLeft", new Vector3(-0.135f, 0f, -0.026f), new Vector3(0.006f, 0.06f, 0.012f), "metal_dark");
            Part(body, PrimitiveType.Cube, "BayRimRight", new Vector3(0.035f, 0f, -0.026f), new Vector3(0.006f, 0.06f, 0.012f), "metal_dark");
            Part(body, PrimitiveType.Sphere, "Wrist", new Vector3(0.065f, 0f, 0.005f), new Vector3(0.05f, 0.05f, 0.05f), "metal_dark");
            Part(body, PrimitiveType.Cube, "Palm", new Vector3(0.12f, 0f, 0.005f), new Vector3(0.08f, 0.075f, 0.028f), "plastic_grey");
            Part(body, PrimitiveType.Cube, "KnuckleGuard", new Vector3(0.125f, 0f, -0.0105f), new Vector3(0.07f, 0.07f, 0.004f), "scratched_metal");

            // 半攥紧的手指（暗示“会突然攥紧”）：向掌心（+Z）弯曲
            float[] fingerY = { 0.027f, 0.009f, -0.009f, -0.027f };
            float[] lengths = { 0.028f, 0.022f, 0.018f };
            float[] curl = { 18f, 48f, 80f };
            for (int f = 0; f < 4; f++)
            {
                var p = new Vector3(0.16f, fingerY[f], 0.0f);
                for (int s = 0; s < 3; s++)
                {
                    float a = curl[s] * Mathf.Deg2Rad;
                    var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    var seg = Part(body, PrimitiveType.Cube, $"Finger{f}_{s}", p + d * lengths[s] * 0.5f,
                                   new Vector3(lengths[s], 0.015f, 0.017f), s == 0 ? "plastic_dark" : "metal_dark");
                    seg.transform.localRotation = Quaternion.FromToRotation(Vector3.right, d);
                    p += d * lengths[s];
                }
            }
            var thumbDir = new Vector3(0.5f, 0.75f, 0.35f).normalized;
            var tp = new Vector3(0.10f, 0.035f, 0.0f);
            for (int s = 0; s < 2; s++)
            {
                var seg = Part(body, PrimitiveType.Cube, $"Thumb_{s}", tp + thumbDir * 0.012f, new Vector3(0.024f, 0.016f, 0.017f), "plastic_dark");
                seg.transform.localRotation = Quaternion.FromToRotation(Vector3.right, thumbDir);
                tp += thumbDir * 0.024f;
                thumbDir = (thumbDir + new Vector3(0.3f, -0.3f, 0.4f)).normalized;
            }

            // 零件盘（吸附位置都在盘上）
            Part(body, PrimitiveType.Cube, "PartsTray", new Vector3(-0.06f, -0.075f, -0.03f), new Vector3(0.21f, 0.006f, 0.1f), "rubber");
            Part(body, PrimitiveType.Cube, "PartsTrayLip", new Vector3(-0.06f, -0.069f, -0.079f), new Vector3(0.21f, 0.008f, 0.004f), "env_accent");
            var snaps = Node(root, "SnapTargets", Vector3.zero);
            var snapCover = Node(snaps, "Snap_ShellOnTray", new Vector3(-0.045f, -0.0685f, -0.022f), new Vector3(90f, 0f, 0f));
            // 螺丝横躺在盘上（轴线沿 X），螺头半径 4.5 mm 贴着盘面
            var snapScrewA = Node(snaps, "Snap_FastenerA_OnTray", new Vector3(-0.148f, -0.0675f, -0.062f), new Vector3(0f, 0f, 90f));
            var snapScrewB = Node(snaps, "Snap_FastenerB_OnTray", new Vector3(-0.148f, -0.0675f, -0.040f), new Vector3(0f, 0f, 90f));

            // ---------- 外壳盖板（shell）：打开后吸附到零件盘 ----------
            var shell = Point(root, C.Shell, new Vector3(-0.05f, 0f, -0.035f));
            Part(shell.transform, PrimitiveType.Cube, "CoverPlate", Vector3.zero, new Vector3(0.16f, 0.052f, 0.006f), "metal_light");
            // 过度磨损：边缘发亮的磨痕 + 几道刮擦
            Part(shell.transform, PrimitiveType.Cube, "WearEdgeTop", new Vector3(0f, 0.024f, -0.0032f), new Vector3(0.15f, 0.004f, 0.001f), "scratched_metal");
            Part(shell.transform, PrimitiveType.Cube, "WearEdgeBottom", new Vector3(0f, -0.024f, -0.0032f), new Vector3(0.15f, 0.004f, 0.001f), "scratched_metal");
            for (int i = 0; i < 4; i++)
                Part(shell.transform, PrimitiveType.Cube, $"Scuff{i}", new Vector3(-0.03f + i * 0.025f, -0.006f + (i % 2) * 0.01f, -0.0032f),
                     new Vector3(0.022f, 0.0015f, 0.0008f), "scratched_metal", new Vector3(0f, 0f, 25f + i * 12f));
            shell.Configure(C.Shell, snapCover, null, null, null, null, null);

            // 租赁封条（挂在盖板上，随盖板移动）：压住螺丝 A 的上半边
            var seal = Point(shell.transform, C.LeaseSeal, new Vector3(-0.06f, 0.0205f, -0.0072f));
            var intact = Group(seal.transform, "Intact");
            Part(intact.transform, PrimitiveType.Cube, "SealStrip", Vector3.zero, new Vector3(0.036f, 0.009f, 0.0008f), "seal_red");
            var torn = Group(seal.transform, "Torn");
            Part(torn.transform, PrimitiveType.Cube, "SealLeftHalf", new Vector3(-0.011f, 0f, 0f), new Vector3(0.014f, 0.009f, 0.0008f), "seal_red");
            Part(torn.transform, PrimitiveType.Cube, "SealRightFlap", new Vector3(0.014f, -0.004f, -0.003f), new Vector3(0.013f, 0.009f, 0.0008f), "seal_red", new Vector3(35f, 0f, -20f));
            AddSealLabels(intact.transform, torn.transform);
            torn.SetActive(false);
            seal.Configure(C.LeaseSeal, null, null, null, intact, torn, null);

            // 固定螺丝：根节点下（不随盖板走），卸下后吸附到零件盘
            foreach (var (id, pos, snap) in new[] { (C.FastenerA, new Vector3(-0.11f, 0.016f, -0.0395f), snapScrewA),
                                                    (C.FastenerB, new Vector3(0.012f, -0.016f, -0.0395f), snapScrewB) })
            {
                var screw = Point(root, id, pos, new Vector3(-90f, 0f, 0f));
                Part(screw.transform, PrimitiveType.Cylinder, "Head", Vector3.zero, new Vector3(0.009f, 0.002f, 0.009f), "metal_dark");
                Part(screw.transform, PrimitiveType.Cube, "HexSocket", new Vector3(0f, 0.0021f, 0f), new Vector3(0.0035f, 0.0006f, 0.0035f), "plastic_dark");
                Part(screw.transform, PrimitiveType.Cylinder, "Thread", new Vector3(0f, -0.006f, 0f), new Vector3(0.004f, 0.005f, 0.004f), "metal_light");
                screw.Configure(id, snap, null, null, null, null, null);
            }

            // ---------- 盖板下：传动机构 ----------
            var drive = Point(root, C.Drive, new Vector3(-0.03f, 0.004f, -0.026f));
            var worn = Group(drive.transform, "WornGears");
            Part(worn.transform, PrimitiveType.Cylinder, "Gear1", new Vector3(-0.02f, 0f, 0f), new Vector3(0.024f, 0.003f, 0.024f), "damaged", new Vector3(90f, 0f, 0f));
            Part(worn.transform, PrimitiveType.Cylinder, "Gear2", new Vector3(0.006f, 0.006f, 0f), new Vector3(0.016f, 0.003f, 0.016f), "damaged", new Vector3(90f, 0f, 0f));
            var fresh = Group(drive.transform, "NewGears");
            Part(fresh.transform, PrimitiveType.Cylinder, "Gear1", new Vector3(-0.02f, 0f, 0f), new Vector3(0.024f, 0.003f, 0.024f), "metal_light", new Vector3(90f, 0f, 0f));
            Part(fresh.transform, PrimitiveType.Cylinder, "Gear2", new Vector3(0.006f, 0.006f, 0f), new Vector3(0.016f, 0.003f, 0.016f), "metal_light", new Vector3(90f, 0f, 0f));
            Part(fresh.transform, PrimitiveType.Cube, "NewPartTag", new Vector3(-0.02f, 0f, -0.0035f), new Vector3(0.006f, 0.006f, 0.001f), "env_accent");
            fresh.SetActive(false);
            Part(drive.transform, PrimitiveType.Cube, "TendonUpper", new Vector3(0.04f, 0.006f, 0f), new Vector3(0.05f, 0.003f, 0.003f), "metal_light");
            Part(drive.transform, PrimitiveType.Cube, "TendonLower", new Vector3(0.04f, -0.004f, 0f), new Vector3(0.05f, 0.003f, 0.003f), "metal_light");
            var driveTested = Group(drive.transform, "TestClip");
            Part(driveTested.transform, PrimitiveType.Cube, "Clip", new Vector3(-0.02f, 0.016f, -0.002f), new Vector3(0.008f, 0.004f, 0.004f), "hazard_yellow");
            driveTested.SetActive(false);
            drive.Configure(C.Drive, null, worn, fresh, null, null, driveTested.gameObject);

            // 限力传感器：检测后亮红灯（旁路）
            var limiter = Point(root, C.ForceLimiter, new Vector3(0.018f, -0.013f, -0.026f));
            Part(limiter.transform, PrimitiveType.Cube, "SensorBody", Vector3.zero, new Vector3(0.022f, 0.016f, 0.008f), "plastic_dark");
            Part(limiter.transform, PrimitiveType.Sphere, "LedOff", new Vector3(0.006f, 0.004f, -0.0045f), new Vector3(0.005f, 0.005f, 0.005f), "metal_dark");
            var bypass = Group(limiter.transform, "BypassIndicator");
            Part(bypass.transform, PrimitiveType.Sphere, "LedRed", new Vector3(0.006f, 0.004f, -0.0048f), new Vector3(0.0056f, 0.0056f, 0.0056f), "seal_red");
            Part(bypass.transform, PrimitiveType.Cube, "BypassTag", new Vector3(-0.004f, -0.004f, -0.0045f), new Vector3(0.011f, 0.004f, 0.001f), "hazard_yellow");
            bypass.SetActive(false);
            limiter.Configure(C.ForceLimiter, null, null, null, null, null, bypass.gameObject);

            // 控制板：检测后亮绿灯（固件签名正常）
            var board = Point(root, C.ControlBoard, new Vector3(-0.098f, -0.006f, -0.0225f));
            Part(board.transform, PrimitiveType.Cube, "Pcb", Vector3.zero, new Vector3(0.05f, 0.036f, 0.002f), "pcb_green");
            Part(board.transform, PrimitiveType.Cube, "Mcu", new Vector3(0.006f, 0.003f, -0.0018f), new Vector3(0.012f, 0.012f, 0.002f), "plastic_dark");
            Part(board.transform, PrimitiveType.Cube, "LogChip", new Vector3(-0.014f, -0.008f, -0.0016f), new Vector3(0.008f, 0.006f, 0.0016f), "plastic_dark");
            var boardOk = Group(board.transform, "SignatureOk");
            Part(boardOk.transform, PrimitiveType.Sphere, "LedGreen", new Vector3(0.018f, 0.012f, -0.002f), new Vector3(0.004f, 0.004f, 0.004f), "screen");
            boardOk.SetActive(false);
            board.Configure(C.ControlBoard, null, null, null, null, null, boardOk.gameObject);

            // 数据接口（手腕顶部，外部可达）：读取日志时插上检测线
            var port = Point(root, C.DataPort, new Vector3(0.062f, 0.029f, 0.005f));
            Part(port.transform, PrimitiveType.Cube, "PortHousing", Vector3.zero, new Vector3(0.022f, 0.008f, 0.016f), "metal_dark");
            Part(port.transform, PrimitiveType.Cube, "PortSlot", new Vector3(0f, 0.0042f, 0f), new Vector3(0.012f, 0.0006f, 0.004f), "plastic_dark");
            var plug = Group(port.transform, "TesterPlug");
            Part(plug.transform, PrimitiveType.Cube, "Plug", new Vector3(0f, 0.011f, 0f), new Vector3(0.013f, 0.014f, 0.008f), "env_accent");
            Part(plug.transform, PrimitiveType.Cylinder, "Cable", new Vector3(0f, 0.03f, 0f), new Vector3(0.004f, 0.012f, 0.004f), "rubber");
            plug.SetActive(false);
            port.Configure(C.DataPort, null, null, null, null, null, plug.gameObject);

            return root.gameObject;
        }
    }
}
