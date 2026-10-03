using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using BorderRepair.FirstOrder;
using UnityEngine.Rendering.Universal;
using static BorderRepair.ArtAudit.AuditCommon;

namespace BorderRepair.ArtAudit
{
    /// <summary>
    /// 第 4 步（只读，场景不保存）：参考图。只渲染 Unity 相机画面（RenderTexture），不截桌面。
    /// 姿态和位置全部从 tray_measure.json / engine_measure.json 读回（与实测同一组数），标注用无光照小球 / 细杆 / 编号文字，图例写在 renders.md。
    /// 同一主题的图用同一机位，方便前后对比。
    /// </summary>
    public static class AuditRender
    {
        const string ImgDir = OutDir + "/img";
        static Material Unlit(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", c);
            return m;
        }

        static readonly List<GameObject> marks = new List<GameObject>();
        static Font font;
        static readonly StringBuilder legend = new StringBuilder();

        static void Clear() { foreach (var g in marks) if (g) Object.DestroyImmediate(g); marks.Clear(); }

        static void Dot(Vector3 p, Color c, float r = 0.008f)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.position = p; g.transform.localScale = Vector3.one * r * 2f;
            g.GetComponent<Renderer>().sharedMaterial = Unlit(c);
            marks.Add(g);
        }

        static void Rod(Vector3 a, Vector3 b, Color c, float w = 0.003f)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.position = (a + b) / 2f;
            g.transform.up = (b - a).normalized;
            g.transform.localScale = new Vector3(w, (b - a).magnitude / 2f, w);
            g.GetComponent<Renderer>().sharedMaterial = Unlit(c);
            marks.Add(g);
        }

        static void Tag(Vector3 p, string text, Color c, Camera cam, float size = 0.012f)
        {
            var g = new GameObject("tag");
            var tm = g.AddComponent<TextMesh>();
            tm.font = font; tm.text = text; tm.fontSize = 64; tm.characterSize = size; tm.color = c; tm.anchor = TextAnchor.LowerLeft;
            g.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            g.transform.position = p;
            g.transform.rotation = Quaternion.LookRotation(p - cam.transform.position, cam.transform.up);
            marks.Add(g);
        }

        static Camera MakeCam(Vector3 pos, Vector3 look, float fov)
        {
            var go = new GameObject("AuditCam");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = fov; cam.nearClipPlane = 0.02f; cam.farClipPlane = 30f;
            go.transform.position = pos; go.transform.rotation = Quaternion.LookRotation(look - pos, Vector3.up);
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            marks.Add(go);
            return cam;
        }

        static void Save(Camera cam, string file, string caption)
        {
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply();
            RenderTexture.active = null; cam.targetTexture = null;
            Directory.CreateDirectory(ImgDir);
            File.WriteAllBytes(Path.Combine(ImgDir, file), tex.EncodeToPNG());
            Object.DestroyImmediate(tex); rt.Release();
            legend.AppendLine($"\n### `img/{file}`\n\n{caption}\n");
        }

        // ------------------------------------------------------------------ 读回实测 JSON（只取需要的数）

        static string J(string file) => File.ReadAllText(Path.Combine(OutDir, file));
        static float[] Arr(string src, string key)
        {
            var m = Regex.Match(src, "\"" + Regex.Escape(key) + "\":\\[([^\\]]*)\\]");
            return m.Success ? m.Groups[1].Value.Split(',').Where(s => s.Length > 0).Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray() : null;
        }
        static string Obj(string src, string key)
        {
            int i = src.IndexOf("\"" + key + "\":{"); if (i < 0) return null;
            int depth = 0; int start = src.IndexOf('{', i);
            for (int k = start; k < src.Length; k++) { if (src[k] == '{') depth++; else if (src[k] == '}') { depth--; if (depth == 0) return src.Substring(start, k - start + 1); } }
            return null;
        }
        static Vector3 V3(float[] a) => new Vector3(a[0], a[1], a[2]);
        static Quaternion Q4(float[] a) => new Quaternion(a[0], a[1], a[2], a[3]);

        public static void Run()
        {
            AuditTray.Setup();
            AuditTray.RestPose();
            // 夹爪三个姿态的旋转、夹 10 mm 横杆的开度和咬合中心：与实测同一套计算
            AuditTray.MeasureJaw(AuditTray.L, new StringBuilder(), new StringBuilder());
            AuditTray.MeasureJaw(AuditTray.R, new StringBuilder(), new StringBuilder());
            AuditTray.RestPose();
            font =AssetDatabase.LoadAssetAtPath<Font>("Assets/BorderRepair/Art/Fonts/NotoSansSC/NotoSansSC-Regular.otf");
            legend.AppendLine("# 参考图与标注图例\n\n全部是 Unity 相机渲染（布局 B 场景只读打开，未保存；后处理、FXAA 与切片相同），1600×900。位置 / 姿态取自 `tray_measure.json` 与 `engine_measure.json`。标注颜色：");
            legend.AppendLine("- 黄：提手横杆中心 / 线缆端点；绿：夹爪咬合中心；红：引擎铰轴（Engine_L_Hinge 本地 X）；橙：转子轴；青：引擎与机身的接触点；白：尺寸线；紫：搬运路线。");
            Tray();
            Engine();
            Write("renders.md", legend.ToString());
            Debug.Log("[ArtAudit] 参考图已渲染");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // ------------------------------------------------------------------ 托盘

        static void Tray()
        {
            var robot = AuditTray.Robot;
            var body = FindUnder(robot, "Body");
            var tray = AuditTray.TrayT;
            var anchor = Find("Dock_RobotAnchor");
            var js = J("tray_measure.json");
            var ti = AuditTray.MeasureTray();
            var rec = Obj(js, "recommended");
            var trayHome = (tray.position, tray.rotation);
            var rootHome = (robot.position, robot.rotation);
            // 固定机位：七号左前下方，看夹爪与托盘区
            Vector3 camPos = body.TransformPoint(new Vector3(-0.55f, -0.10f, 0.95f)), camLook = body.TransformPoint(new Vector3(0.05f, -0.38f, 0.15f));

            // T01 托盘在维修座托盘架上（原位），标横杆中心、间距、原点
            {
                Clear(); robot.SetPositionAndRotation(rootHome.position, rootHome.rotation); AuditTray.RestPose();
                var cam = MakeCam(tray.position + tray.forward * 0.45f - tray.up * 0.55f + Vector3.up * 0.0f, tray.position, 40f);
                cam.transform.position = tray.position + new Vector3(0, 0.45f, 0) + (tray.position - body.position).normalized * 0.35f;
                cam.transform.rotation = Quaternion.LookRotation(tray.position - cam.transform.position, Vector3.up);
                var b0 = tray.TransformPoint(ti.barCenterL[0]); var b1 = tray.TransformPoint(ti.barCenterL[1]);
                Dot(b0, Color.yellow); Dot(b1, Color.yellow); Rod(b0, b1, Color.white, 0.002f); Dot(tray.position, Color.magenta, 0.006f);
                Tag((b0 + b1) / 2f + Vector3.up * 0.02f, $"{Mm(Vector3.Distance(b0, b1))} mm", Color.white, cam, 0.006f);
                Tag(b0 + Vector3.up * 0.015f, "1", Color.yellow, cam, 0.006f); Tag(b1 + Vector3.up * 0.015f, "2", Color.yellow, cam, 0.006f); Tag(tray.position + Vector3.up * 0.01f, "3", Color.magenta, cam, 0.006f);
                Save(cam, "T01_tray_on_shelf.png", $"`Unit07ServiceDock/Dock_PartsTray` 在托盘架上（原位）。1、2 = 两端提手横杆中心（间距 {Mm(Vector3.Distance(b0, b1))} mm，横杆沿前后、管径约 10 mm）；3 = 网格原点（盘底中心）。");
            }

            // T02 静止姿态两手咬合中心 vs 提手间距（为什么双手端不起来）
            {
                Clear(); robot.SetPositionAndRotation(anchor.position + Vector3.up * 0.12f, rootHome.rotation); AuditTray.RestPose();
                AuditTray.Jaw(AuditTray.L, AuditTray.L.jawT); AuditTray.Jaw(AuditTray.R, AuditTray.R.jawT);
                var two = Obj(js, "bounded_two_hand_best");
                var tbl = V3(Arr(two, "tray_body_local"));
                AuditTray.PlaceTray(body, new Vector3(0, tbl[1], tbl[2]), 0f);
                Pose(AuditTray.L, Arr(two, "L")); Pose(AuditTray.R, Arr(two, "R"));
                var cam = MakeCam(body.TransformPoint(new Vector3(0f, -0.25f, 1.05f)), body.TransformPoint(new Vector3(0f, -0.33f, 0.15f)), 38f);
                var gl = AuditTray.GripWorld(AuditTray.L); var gr = AuditTray.GripWorld(AuditTray.R);
                var (bl, br, _) = AuditTray.Bars(ti);
                Dot(gl, Color.green); Dot(gr, Color.green); Dot(bl, Color.yellow); Dot(br, Color.yellow);
                Rod(gl, bl, Color.red, 0.002f); Rod(gr, br, Color.red, 0.002f);
                Tag(gl + Vector3.up * 0.02f, $"左手差 {Mm(Vector3.Distance(gl, bl))} mm", Color.green, cam, 0.006f);
                Tag(gr + Vector3.up * 0.02f, $"右手差 {Mm(Vector3.Distance(gr, br))} mm", Color.green, cam, 0.006f);
                Save(cam, "T02_two_hand_not_reachable.png", "正面看：在已有动画用过的关节角（±5°）内，两手咬合中心（绿）最接近提手横杆（黄）的一组。红线是还差的距离（两侧各约 41 mm、握持轴偏 15°）。现有托盘的提手太近（303 mm），两手够不到。");
            }

            // T03–T06 推荐的单手端盘姿态（悬停高度），同一机位
            if (rec != null)
            {
                var hand = Regex.Match(rec, "\"hand\":\"(L|R)\"").Groups[1].Value;
                var arm = hand == "L" ? AuditTray.L : AuditTray.R;
                var poses = Obj(rec, "poses");
                var relPos = V3(Arr(rec, "tray_local_pos")); var relRot = Q4(Arr(rec, "tray_local_rot"));
                var list = new[] { ("normal", "T03_one_hand_normal.png", "单手端盘 · 正常：盘面水平"), ("robot_roll_6", "T04_one_hand_left_sink_6deg.png", "单手端盘 · 左侧下沉 6°：预制体根绕 Body 原点、七号前向轴转 6°，关节不变"),
                                   ("wrist_roll_p4", "T05_one_hand_tray_tilt_4deg.png", "单手端盘 · 盘面倾斜 4°：只转握盘手的腕骨 4°") };
                foreach (var (id, file, cap) in list)
                {
                    var p = Obj(poses, id); if (p == null) continue;
                    Clear();
                    var rp = V3(Arr(p, "root_pos")); var rr = Q4(Arr(p, "root_rot"));
                    robot.SetPositionAndRotation(rp + Vector3.up * 0.12f, rr);   // 实测在落座高度；参考图放到悬停高度（+120 mm，刚性整体上移）
                    AuditTray.RestPose(); AuditTray.Jaw(arm, arm.jawT);
                    Pose(arm, Arr(p, "joints"));
                    tray.SetPositionAndRotation(arm.wrist.TransformPoint(relPos), arm.wrist.rotation * relRot);
                    var cam = MakeCam(camPos + Vector3.up * 0.12f, camLook + Vector3.up * 0.12f, 42f);
                    if (id == "normal") cam.transform.SetPositionAndRotation(cam.transform.position, cam.transform.rotation);
                    Dot(AuditTray.GripWorld(arm), Color.green, 0.006f);
                    Save(cam, file, cap + $"（{hand} 手纵握靠近自己这侧的提手横杆，握点在横杆中心前方 30 mm；托盘挂在 `{PathOf(arm.wrist)}` 下）。绿点 = 咬合中心。");
                }
                // 维修座镜头、玩家站位（推荐姿态、悬停高度）
                var pn = Obj(poses, "normal");
                if (pn != null)
                {
                    robot.SetPositionAndRotation(V3(Arr(pn, "root_pos")) + Vector3.up * 0.12f, Q4(Arr(pn, "root_rot")));
                    AuditTray.RestPose(); AuditTray.Jaw(arm, arm.jawT); Pose(arm, Arr(pn, "joints"));
                    tray.SetPositionAndRotation(arm.wrist.TransformPoint(relPos), arm.wrist.rotation * relRot);
                    var rig = Object.FindFirstObjectByType<FirstOrderCameraRig>();
                    foreach (var shot in new[] { FirstOrderCameraRig.Dock, FirstOrderCameraRig.Overview })
                    {
                        Clear();
                        var s = rig.Get(shot);
                        var cam = MakeCam(s.pose.position, s.pose.position + s.pose.forward, s.fov);
                        cam.transform.rotation = s.pose.rotation;
                        var vp = cam.WorldToViewportPoint(tray.GetComponent<Renderer>().bounds.center);
                        var b = tray.GetComponent<Renderer>().bounds;
                        float px = ScreenSize(cam, b);
                        Save(cam, $"T06_view_{shot}.png", $"游戏镜头 `{shot}`（FirstOrderCameraRig 原机位、原 FOV {s.fov:F0}°），七号悬停、单手端盘。托盘中心在画面 ({vp.x:F2}, {vp.y:F2})，包围盒在 1600×900 画面里约 {px:F0} px 宽。");
                    }
                    // 玩家站位（布局 B 站点，眼高 1.60 m，看七号机身）
                    {
                        Clear();
                        var (sp, yaw) = BorderRepair.FirstOrder.EditorTools.LayoutAB.LayoutABScenes.Stand("B");
                        var eye = sp + Vector3.up * 1.60f;
                        var cam = MakeCam(eye, body.position + Vector3.down * 0.25f, 55f);
                        float px = ScreenSize(cam, tray.GetComponent<Renderer>().bounds);
                        Save(cam, "T07_view_player_eye_B.png", $"布局 B 玩家站位 {sp:F2}，眼高 1.60 m，FOV 55°，看七号机身下方。托盘包围盒约 {px:F0} px 宽。");
                    }
                }
            }
            robot.SetPositionAndRotation(rootHome.position, rootHome.rotation);
            tray.SetPositionAndRotation(trayHome.position, trayHome.rotation);
            AuditTray.RestPose();
        }

        static void Pose(AuditTray.Arm a, float[] ang) => AuditTray.Pose(a, ang);

        static float ScreenSize(Camera cam, Bounds b)
        {
            var pts = new List<Vector3>();
            for (int i = 0; i < 8; i++) pts.Add(cam.WorldToViewportPoint(new Vector3(i % 2 == 0 ? b.min.x : b.max.x, (i / 2) % 2 == 0 ? b.min.y : b.max.y, i / 4 == 0 ? b.min.z : b.max.z)));
            return (pts.Max(p => p.x) - pts.Min(p => p.x)) * 1600f;
        }

        // ------------------------------------------------------------------ 引擎

        static void Engine()
        {
            var robot = AuditTray.Robot;
            var body = FindUnder(robot, "Body");
            var hinge = FindUnder(robot, "Engine_L_Hinge");
            var rotor = FindUnder(robot, "Engine_L_Rotor");
            var js = J("engine_measure.json");
            var left = -body.right;
            var take = hinge.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).Select(r => r.transform).ToList();
            var homes = take.Select(t => (t, t.position, t.rotation)).ToList();
            var hingeHome = hinge.position;
            // 固定机位：七号左前方，能同时看到原位和侧拔 400 mm 的位置
            var camPos = hinge.position + left * 0.55f + body.forward * 0.75f + Vector3.up * 0.22f;
            var camLook = hinge.position + left * 0.18f + Vector3.down * 0.05f;

            // E01a / E01b 原位标注：透视（隐藏引擎外壳与故障包覆盖件）+ 从后上方看引擎与机身之间的接口。图上只写编号，含义见图例
            {
                var shellNames = new[] { "Engine_UpperCover_L", "Engine_LowerCover_L", "Engine_NozzleRing_L", "Engine_IntakeLip_L", "Engine_IntakeGuard_L", "Engine_IntakeDuct_L", "Engine_RearCap_L", "Engine_Badge_L" };
                var shell = hinge.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && (shellNames.Contains(r.name) || r.name.StartsWith("UNIT07_FK_IntakeClog") || r.name.StartsWith("UNIT07_FK_CoverLabel"))).ToList();
                var key = FindUnder(robot, "ConnectionKey_L").GetComponent<Renderer>();
                var bolt = FindUnder(robot, "ConnectionKey_Bolt_L").GetComponent<Renderer>();
                var socket = FindUnder(robot, "BodySocket_L").GetComponent<Renderer>();
                var anchorR = FindUnder(robot, "EngineAnchor_L").GetComponent<Renderer>();
                var pts = new List<(Vector3 p, Color c, string label)>
                {
                    (hinge.position, Color.red, "铰轴原点（Engine_L_Hinge，红线 = 本地 X 转轴）"),
                    (rotor.position, new Color(1f, 0.55f, 0f), "转子轴原点（Engine_L_Rotor，橙线 = 本地 X）"),
                    (key.bounds.center, Color.magenta, "ConnectionKey_L（连接键，随引擎骨骼，插在机身 BodySocket_L 里）"),
                    (bolt.bounds.center, new Color(1f, 0.4f, 0.8f), "ConnectionKey_Bolt_L（连接键螺栓）"),
                    (socket.bounds.center, Color.cyan, "BodySocket_L（机身侧插座，留在机身）"),
                    (anchorR.bounds.center, new Color(0.4f, 0.8f, 1f), "EngineAnchor_L（引擎侧连接座，随引擎）"),
                };
                foreach (Match m in Regex.Matches(js, "\"path\":\"[^\"]*?/(ExternalCable_L|AuxCable_L)\"[^{}]*?\"end_body\":\\[([^\\]]*)\\],\"end_engine\":\\[([^\\]]*)\\]"))
                {
                    var eb = V3(m.Groups[2].Value.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray());
                    var ee = V3(m.Groups[3].Value.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray());
                    var c = m.Groups[1].Value.StartsWith("External") ? Color.yellow : new Color(0.85f, 0.85f, 0.85f);
                    pts.Add((eb, c, $"{m.Groups[1].Value} 机身端（插头 {m.Groups[1].Value}_PlugBody，权重全在 Body）"));
                    pts.Add((ee, c, $"{m.Groups[1].Value} 引擎端（插头 {m.Groups[1].Value}_PlugEngine，权重全在 Engine_L_Hinge）"));
                }
                var views = new[]
                {
                    ("E01a_engine_xray_front.png", hinge.position + left * 0.42f + body.forward * 0.42f + Vector3.up * 0.18f, hinge.position + left * 0.06f, 40f, "透视（隐藏外壳、进气件与故障包覆盖件），七号左前方"),
                    ("E01b_interface_rear_top.png", socket.bounds.center + left * 0.22f - body.forward * 0.30f + Vector3.up * 0.30f, socket.bounds.center + left * 0.03f, 38f, "引擎与机身之间的接口，七号左后上方（外壳照常显示）"),
                };
                foreach (var (file, pos, look, fov, what) in views)
                {
                    Clear();
                    bool xray = file.Contains("xray");
                    foreach (var r in shell) r.enabled = !xray;
                    var cam = MakeCam(pos, look, fov);
                    Rod(hinge.position - hinge.right * 0.10f, hinge.position + hinge.right * 0.10f, Color.red, 0.0025f);
                    Rod(rotor.position - rotor.right * 0.09f, rotor.position + rotor.right * 0.09f, new Color(1f, 0.55f, 0f), 0.0025f);
                    var sb = new StringBuilder();
                    for (int i = 0; i < pts.Count; i++)
                    {
                        Dot(pts[i].p, pts[i].c, 0.0045f);
                        // 编号沿相机上方向错开，避免互相压住
                        var tagPos = pts[i].p + cam.transform.up * (0.012f + 0.004f * (i % 3)) + cam.transform.right * 0.004f;
                        Tag(tagPos, (i + 1).ToString(), pts[i].c, cam, 0.0035f);
                        sb.Append($"{i + 1} = {pts[i].label}；");
                    }
                    foreach (Match m in Regex.Matches(js, "\"at\":\\[([^\\]]*)\\]"))
                        Dot(V3(m.Groups[1].Value.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray()), Color.white, 0.003f);
                    Save(cam, file, $"左引擎原位（落座、夹具闭合），{what}。{sb}白点 = 引擎件与机身件真实网格 ≤ 2 mm 的接触点（只有连接键 ↔ 插座一处）。");
                    foreach (var r in shell) r.enabled = true;
                }
            }
            // E02 同机位：移动铰骨 400 mm（线缆被直线拉长）
            {
                Clear();
                hinge.position = hingeHome + left * 0.40f;
                var cam = MakeCam(camPos, camLook, 45f);
                Rod(hingeHome, hinge.position, Color.white, 0.002f);
                Tag((hingeHome + hinge.position) / 2f + Vector3.up * 0.02f, "400 mm", Color.white, cam, 0.008f);
                Save(cam, "E02_hinge_moved_400mm_cables_stretch.png", "同机位：直接把 `Engine_L_Hinge` 骨骼沿七号左向外移 400 mm。整台引擎跟着走，两根蒙皮线缆被拉成直线（只有 Body / Hinge 两根骨骼，不会下垂、不会断开）。程序不能用移动骨骼来做取出（Idle_Hover 每帧也会写回这根骨骼）。");
                hinge.position = hingeHome;
            }
            // E03 工作台镜头：旧引擎落点；E04 两台（旧件 + 用同一套网格复制的新件）；E05 总览：搬运路线（engine_path.json）
            var pathJs = File.Exists(Path.Combine(OutDir, "engine_path.json")) ? J("engine_path.json") : null;
            var path = pathJs != null ? Obj(pathJs, "path") : null;
            if (path != null)
            {
                var keyNames = new[] { "ConnectionKey_L", "ConnectionKey_Bolt_L" };
                var moving = take.Where(t => !keyNames.Contains(t.name)).ToList();
                var gb = new Bounds(moving[0].GetComponent<Renderer>().bounds.center, Vector3.zero);
                foreach (var t in moving) gb.Encapsulate(t.GetComponent<Renderer>().bounds);
                Matrix4x4 Place(Vector3 delta, float yaw) => Matrix4x4.Translate(delta) * Matrix4x4.TRS(gb.center, Quaternion.AngleAxis(yaw, Vector3.up), Vector3.one) * Matrix4x4.Translate(-gb.center);
                void Apply(Matrix4x4 M, float yaw) { foreach (var t in moving) { var h = homes.First(x => x.t == t); t.SetPositionAndRotation(M.MultiplyPoint3x4(h.position), Quaternion.AngleAxis(yaw, Vector3.up) * h.rotation); } }
                var delta = V3(Arr(path, "land_delta"));
                var yaw = float.Parse(Regex.Match(path, "\"land_yaw\":([-0-9.]+)").Groups[1].Value, CultureInfo.InvariantCulture);
                var h0 = float.Parse(Regex.Match(path, "\"h\":([-0-9.]+)").Groups[1].Value, CultureInfo.InvariantCulture);
                var rig = Object.FindFirstObjectByType<FirstOrderCameraRig>();
                var bench = rig.Get(FirstOrderCameraRig.Bench);

                Apply(Place(delta, yaw), yaw);
                {
                    Clear();
                    var cam = MakeCam(bench.pose.position, bench.pose.position + bench.pose.forward, bench.fov); cam.transform.rotation = bench.pose.rotation;
                    Save(cam, "E03_old_engine_on_mat_benchcam.png", $"游戏镜头 `Bench`（原机位、原 FOV）：旧左引擎（随引擎走的全部网格，不含连接键 / 螺栓、不含蒙皮线缆）直立放在操作垫上，绕竖直轴转 {yaw:F0}°（engine_path 选出的落点）。黄色圆片是首单上盖落点标记（编辑模式下可见，运行时隐藏）。");
                }
                // E04 两台：旧件在 two_d1，新件（复制 Engine_L_Hinge 下的网格，去掉故障包件，恢复原轴承渲染器）在 two_d2
                var em = J("engine_measure.json");
                var d1 = Arr(em, "two_d1"); var d2 = Arr(em, "two_d2");
                if (d1 != null && d2 != null)
                {
                    float y1 = float.Parse(Regex.Match(em, "\"two_y1\":([-0-9.]+)").Groups[1].Value, CultureInfo.InvariantCulture);
                    float y2 = float.Parse(Regex.Match(em, "\"two_y2\":([-0-9.]+)").Groups[1].Value, CultureInfo.InvariantCulture);
                    Clear();
                    foreach (var (t, p, r) in homes) t.SetPositionAndRotation(p, r);   // 先在原位复制，再摆旧件
                    var clone = Object.Instantiate(hinge.gameObject);
                    Apply(Place(V3(d1), y1), y1);
                    marks.Add(clone);
                    foreach (var fk in clone.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("UNIT07_FK_") || t.name.StartsWith("FO_") || t.name.Contains("InspectProxy")).ToList())
                        if (fk) Object.DestroyImmediate(fk.gameObject);
                    var cb = clone.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == "Engine_BearingTop_L"); if (cb) cb.enabled = true;
                    foreach (var kn in keyNames) { var k = clone.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == kn); if (k) k.gameObject.SetActive(false); }
                    // 复制件与原件同位姿；整体按新件落点摆
                    var M2 = Place(V3(d2), y2);
                    clone.transform.SetPositionAndRotation(M2.MultiplyPoint3x4(hingeHome), Quaternion.AngleAxis(y2, Vector3.up) * hinge.rotation);
                    var cam = MakeCam(bench.pose.position, bench.pose.position + bench.pose.forward, bench.fov); cam.transform.rotation = bench.pose.rotation;
                    Save(cam, "E04_two_engines_on_mat_benchcam.png", $"游戏镜头 `Bench`：旧件（有积尘、磨损轴承、保养贴纸）与新件同时直立放在操作垫上（engine_measure 找到的一对不重叠落点）。新件只是把 `Engine_L_Hinge` 下的网格复制一份、去掉故障包件、恢复原轴承渲染器——外观与旧件除故障包外完全相同（同材质、同 L-03 字样），用来说明可复用范围，不是补件。");
                    Object.DestroyImmediate(clone); marks.Remove(clone);
                }
                // E05 总览：搬运路线
                Apply(Place(delta, yaw), yaw);
                {
                    Clear();
                    var s = rig.Get(FirstOrderCameraRig.Overview);
                    var cam = MakeCam(s.pose.position, s.pose.position + s.pose.forward, s.fov); cam.transform.rotation = s.pose.rotation;
                    var p0 = gb.center; var p1 = p0 + left * 0.40f;
                    var p2 = new Vector3(p1.x, p1.y + (h0 - gb.min.y), p1.z);
                    var landC = gb.center + delta; var p3 = new Vector3(landC.x, p2.y, landC.z);
                    var purple = new Color(0.7f, 0.3f, 1f);
                    Rod(p0, p1, purple, 0.006f); Rod(p1, p2, purple, 0.006f); Rod(p2, p3, purple, 0.006f); Rod(p3, landC, purple, 0.006f);
                    foreach (var p in new[] { p0, p1, p2, p3, landC }) Dot(p, purple, 0.012f);
                    Save(cam, "E05_carry_path_overview.png", $"游戏镜头 `Overview`：搬运路线（紫）= 原位 → 侧拔 400 mm → 升 / 降到引擎最低点 {h0:F2} m → 水平移到操作垫上方 → 落下（engine_path.json）。旧引擎显示在落点。");
                }
                foreach (var (t, p, r) in homes) t.SetPositionAndRotation(p, r);
            }
            else
            {
                foreach (var (t, p, r) in homes) t.SetPositionAndRotation(p, r);
            }
        }
    }
}
