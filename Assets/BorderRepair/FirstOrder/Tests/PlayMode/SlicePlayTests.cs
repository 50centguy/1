using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BorderRepair.Dock;
using BorderRepair.FirstOrder.Slice;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace BorderRepair.FirstOrder.Tests
{
    /// <summary>
    /// 两晚切片：只用鼠标（Input System 虚拟鼠标设备 → InputSystemUIInputModule / FirstOrderInput）走完维修核心。
    /// 镜头只通过界面镜头栏切换（不直接调 Rig.Go），3D 点击只点没有被界面盖住的点。
    /// 另测：界面点击不穿透到 3D；文字输入时数字键 / F3 不生效；观察右侧和误拆右侧反馈不同；观察有有效距离；未停转时拒绝并说明原因。
    /// 这些都是程序生成的鼠标 / 键盘事件，不是真人试玩。
    /// </summary>
    public class SlicePlayTests
    {
        const string ScenePath = "Assets/BorderRepair/FirstOrder/Scenes/Slice/TwoNightSlice.unity";
        const string ReportDir = "Docs/Integration/TwoNightSlice";

        FirstOrderFlow flow;
        FirstOrderInput input;
        SliceView view;
        Mouse mouse;
        Keyboard keyboard;
        Action cleanup;
        readonly List<string> log = new List<string>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
#endif
            yield return null;
            flow = UnityEngine.Object.FindFirstObjectByType<FirstOrderFlow>();
            input = UnityEngine.Object.FindFirstObjectByType<FirstOrderInput>();
            view = UnityEngine.Object.FindFirstObjectByType<SliceView>();
            mouse = FirstOrderAcceptanceDriver.CreateVirtualMouse(out var c1);
            keyboard = InputSystem.AddDevice<Keyboard>("Slice_VirtualKeyboard");
            cleanup = () => { try { if (keyboard.added) InputSystem.RemoveDevice(keyboard); } catch { } c1(); };
            log.Clear();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            cleanup?.Invoke();
            yield return null;
        }

        void Log(string s) => log.Add(s);   // 只写进报告；不进控制台（测试末尾核对控制台没有意外日志）

        // ------------------------------------------------------------------ 鼠标 / 键盘

        IEnumerator MoveTo(Vector2 p)
        {
            mouse.MakeCurrent();
            InputSystem.QueueStateEvent(mouse, new MouseState { position = p });
            yield return null; yield return null;
        }

        IEnumerator Press(Vector2 p, MouseButton b)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = p }.WithButton(b, true));
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = p }.WithButton(b, false));
            yield return null; yield return null;
        }

        IEnumerator Key(Key k)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(k));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }

        static Vector2 Center(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return (c[0] + c[2]) * 0.5f;    // 屏幕空间覆盖画布：世界坐标就是屏幕坐标
        }

        IEnumerator ClickUI(string id)
        {
            var b = view.GetButton(id);
            Assert.IsNotNull(b, "界面按钮 " + id);
            Assert.IsTrue(b.gameObject.activeInHierarchy && b.interactable, "界面按钮可点：" + id);
            int clicks = 0;
            void Count() => clicks++;
            b.onClick.AddListener(Count);
            var p = Center((RectTransform)b.transform);
            yield return MoveTo(p);
            Assert.IsTrue(input.PointerOverUI, $"鼠标在按钮 {id} 上时算在界面上");
            Assert.IsNull(input.Hovered, "鼠标在界面上时不悬停 3D 对象");
            yield return Press(p, MouseButton.Left);
            b.onClick.RemoveListener(Count);
            Assert.AreEqual(1, clicks, "鼠标点到了界面按钮 " + id);
        }

        IEnumerator Go(string shot)
        {
            yield return ClickUI("cam:" + shot);
            yield return new WaitForSeconds(0.5f);    // 镜头过渡 0.35 s
            Assert.AreEqual(shot, flow.Rig.Current);
        }

        /// <summary>目标碰撞范围里找一个：真实点选会选中它、并且没被界面盖住的屏幕点。</summary>
        bool FindPoint(Component target, out Vector2 screen)
        {
            screen = default;
            var cam = flow.Rig.Cam;
            var cols = target.GetComponents<Collider>().Where(c => c.enabled).ToList();
            if (target is FirstOrderPart fp) cols.AddRange(fp.members.SelectMany(m => m.GetComponents<Collider>()));
            Physics.SyncTransforms();
            foreach (var col in cols)
            {
                var bo = col.bounds;
                for (int i = 0; i < 125; i++)
                {
                    var f = new Vector3(i % 5, (i / 5) % 5, i / 25) / 4f;
                    var w = bo.min + Vector3.Scale(bo.size, Vector3.one * 0.5f + (f - Vector3.one * 0.5f) * 0.9f);
                    var sp = cam.WorldToScreenPoint(w);
                    if (sp.z <= 0 || sp.x < 1 || sp.y < 1 || sp.x > cam.pixelWidth - 1 || sp.y > cam.pixelHeight - 1) continue;
                    if (FirstOrderInput.IsOverUI(sp)) continue;
                    if (FirstOrderInput.Pick(cam.ScreenPointToRay(sp)) == target) { screen = sp; return true; }
                }
            }
            return false;
        }

        /// <summary>左键操作（或右键观察）一个 3D 目标。返回是否被流程接受（观察时返回 true）。</summary>
        IEnumerator Click3D(string label, Component target, bool expectAccept, string expectText = null, bool observe = false)
        {
            Assert.IsTrue(FindPoint(target, out var p), $"{label}：镜头「{flow.Rig.Current}」下找不到能点到 {FirstOrderInput.NameOf(target)} 且没被界面盖住的点");
            yield return MoveTo(p);
            Assert.AreEqual(target, input.Hovered, label + "：悬停对象");
            bool? accepted = null;
            void OnActed(string t, bool ok, string msg) { if (accepted == null) accepted = ok; }
            flow.Acted += OnActed;
            yield return Press(p, observe ? MouseButton.Right : MouseButton.Left);
            flow.Acted -= OnActed;
            if (observe)
            {
                Assert.IsNull(accepted, label + "：观察不该触发维修动作");
                Assert.AreEqual(target, input.LastObserveHit, label);
                Log($"{label}：观察「{view.LastObservation.title}」{(view.LastObservation.seen ? "看清" : "看不清")}（{view.LastObservation.distance:F2} m）—— {view.LastObservation.body}");
            }
            else
            {
                Assert.IsNotNull(accepted, label + "：游戏没有收到点击");
                Assert.AreEqual(expectAccept, accepted.Value, $"{label}：{flow.Message}");
                if (expectText != null) StringAssert.Contains(expectText, flow.Message, label);
                Assert.AreEqual(!expectAccept, view.LastFeedbackWasRefusal, label + "：拒绝时界面显示“不行”");
                Log($"{label}：{(accepted.Value ? "接受" : "拒绝")} —— {flow.Message}");
            }
            float until = Time.realtimeSinceStartup + 60f;
            while (flow.Busy && Time.realtimeSinceStartup < until) yield return null;
        }

        IEnumerator Wait(string label, Func<bool> cond, float seconds = 60f)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!cond() && Time.realtimeSinceStartup < until) yield return null;
            Assert.IsTrue(cond(), "等待超时：" + label);
            Log("等待：" + label);
        }

        static Component GripAny(FirstOrderFlow f, Func<Component, bool> ok)
        {
            var gs = new[] { "Dock_Clamp_L_Grip", "Dock_Clamp_R_Grip" }.Select(n => (Component)GameObject.Find(n).GetComponent<DockInteractable>()).ToList();
            return gs.FirstOrDefault(ok) ?? gs[0];
        }
        static Component Lever() => GameObject.Find("Dock_PowerSwitch_LeverGrip").GetComponent<DockInteractable>();

        void WriteReport(string file, string title)
        {
            Directory.CreateDirectory(ReportDir);
            var sb = new StringBuilder();
            sb.AppendLine("# " + title);
            sb.AppendLine();
            sb.AppendLine($"- {DateTime.Now:yyyy-MM-dd HH:mm}，Unity {Application.unityVersion}，屏幕 {Screen.width}×{Screen.height}，场景 `{ScenePath}`");
            sb.AppendLine("- Input System 虚拟鼠标设备的事件经 InputSystemUIInputModule（界面）和 FirstOrderInput（3D）处理；镜头只经界面镜头栏切换。**程序生成的鼠标事件，不是真人试玩。**");
            sb.AppendLine();
            for (int i = 0; i < log.Count; i++) sb.AppendLine($"{i + 1}. {log[i]}");
            File.WriteAllText(Path.Combine(ReportDir, file), sb.ToString(), new UTF8Encoding(false));
        }

        // ------------------------------------------------------------------ 测试

        [UnityTest]
        public IEnumerator MouseOnly_FullRepair_WithObservation()
        {
            const string D = FirstOrderCameraRig.Dock, E = FirstOrderCameraRig.EngineL, R = FirstOrderCameraRig.EngineRear, ER = FirstOrderCameraRig.EngineR,
                         CL = FirstOrderCameraRig.EngineClose, B = FirstOrderCameraRig.Bench, O = FirstOrderCameraRig.Overview, REC = FirstOrderCameraRig.Record, CMP = FirstOrderCameraRig.Compare;
            var dock = flow.Dock;
            Assert.IsFalse(input.ShowHud, "调试 HUD 默认关");

            // 未停转：观察可以，检查 / 拆卸被拒绝并说明原因
            yield return Go(E);
            yield return Click3D("通电悬停时碰左上盖", flow.Cover, false, "供电");
            yield return Go(ER);
            yield return Click3D("通电时观察右引擎", flow.RightEngine, true, observe: true);
            Assert.IsTrue(view.LastObservation.seen && view.LastObservation.key == SliceObservation.EngineR);
            StringAssert.Contains("转动中只能看外观", view.LastObservation.body);

            // 落座 → 夹紧 → 断电
            yield return Go(D);
            var grip = GripAny(flow, g => FindPoint(g, out _));
            yield return Click3D("点夹具握把：张开、落座", grip, true);
            yield return Wait("七号落座", () => dock.State == DockState.SeatedOpen);
            yield return Click3D("点夹具握把：夹紧", grip, true);
            yield return Wait("已夹紧", () => dock.State == DockState.Clamped);
            yield return Click3D("点断电开关", Lever(), true);
            yield return Go(E);
            if (dock.State == DockState.SpinningDown)
                yield return Click3D("叶轮减速中碰外侧锁扣", flow.LatchOuter, false, "叶轮");
            else Log("（叶轮在切镜头期间已停稳，这次没测到减速中的拒绝；FirstOrderPlayTests 里有）");
            yield return Wait("叶轮停稳", () => dock.State == DockState.RotorsStopped && dock.Rotors.SpeedDegPerSec <= 0f);

            // 右引擎：观察（基准）和误拆（拒绝）反馈不同
            yield return Go(ER);
            yield return Click3D("停转后观察右引擎（对照）", flow.RightEngine, true, observe: true);
            Assert.IsTrue(view.LastObservation.seen);
            StringAssert.Contains("手转叶轮顺滑", view.LastObservation.body);
            var baseline = view.LastObservation.body;
            yield return Click3D("误拆右引擎", flow.RightEngine, false, "误拆右侧");
            Assert.AreNotEqual(baseline, flow.Message, "观察和误拆的反馈不同");

            // 左引擎：看进气口、检查、清理
            yield return Go(E);
            yield return Click3D("点左上盖：开始检查", flow.Cover, true);
            yield return Click3D("观察左进气口（检查开始后维修座的整块左引擎代理关掉，进气口能单独点到）", flow.Clog, true, observe: true);
            Assert.IsTrue(view.LastObservation.seen && view.LastObservation.key == SliceObservation.IntakeL);
            yield return Click3D("点进气口：清理", flow.Clog, true);
            yield return Wait("进气口清理完", () => flow.ClogCleared && !flow.Busy);

            // 拆上盖
            yield return Click3D("扳开外侧锁扣", flow.LatchOuter, true);
            yield return Go(R);
            yield return Click3D("扳开后侧锁扣（背面镜头）", flow.LatchRear, true);
            yield return Go(E);
            yield return Click3D("取下上盖总成", flow.Cover, true);
            yield return Go(O);
            yield return Click3D("上盖翻面放到操作垫", flow.MatZone, true);
            yield return Go(REC);
            yield return Click3D("观察上盖内侧保养记录", flow.Cover, true, observe: true);
            Assert.IsTrue(view.LastObservation.seen && view.LastObservation.key == SliceObservation.Label);
            StringAssert.Contains("BRG NOISE", view.LastObservation.body);

            // 轴承：太远看不清 → 观察面板的“切到近看” → 看清 → “返回”
            yield return Go(E);
            yield return Click3D("左引擎镜头观察左上轴承", flow.Bearing, true, observe: true);
            Assert.IsFalse(view.LastObservation.seen, "左引擎镜头离轴承太远：" + view.LastObservation.body);
            Assert.AreEqual(CL, view.LastObservation.suggestShot);
            yield return ClickUI("obs:go");
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(CL, flow.Rig.Current, "观察面板切到近看");
            yield return Click3D("近看观察左上轴承", flow.Bearing, true, observe: true);
            Assert.IsTrue(view.LastObservation.seen && view.LastObservation.key == SliceObservation.BearingWorn, view.LastObservation.body);
            yield return ClickUI("back");
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(E, flow.Rig.Current, "“返回”回到左引擎镜头");
            Log("近看 → 返回：回到左引擎镜头");

            yield return Click3D("点左上轴承：定位", flow.Bearing, true);
            yield return Click3D("点左上轴承：取下", flow.Bearing, true);
            yield return Go(O);
            yield return Click3D("旧轴承放进托盘", flow.OldTrayZone, true);
            yield return Go(CMP);
            yield return Click3D("观察新旧轴承", flow.NewBearing, true, observe: true);
            Assert.IsTrue(view.LastObservation.seen && view.LastObservation.key == SliceObservation.Compare, view.LastObservation.body);
            yield return Go(B);
            yield return Click3D("从轴承盒取新轴承装上", flow.NewBearing, true);
            yield return Click3D("上盖翻回来装回", flow.Cover, true);
            yield return Go(E);
            yield return Click3D("扣回外侧锁扣", flow.LatchOuter, true);
            yield return Go(R);
            yield return Click3D("扣回后侧锁扣", flow.LatchRear, true);

            // 通电、离座复测
            yield return Go(D);
            yield return Click3D("点断电开关：通电", Lever(), true);
            yield return Wait("转子恢复转动", () => dock.Rotors.SpeedDegPerSec > 1f);
            grip = GripAny(flow, g => FindPoint(g, out _));
            yield return Click3D("点夹具握把：松开离座", grip, true);
            yield return Wait("离座复测结束", () => flow.Step == FoStep.Done);
            Assert.IsTrue(flow.RetestPassed, flow.RetestDetail);
            Log("复测（占位判定）：" + flow.RetestDetail);

            yield return ClickUI("manual");
            yield return null;
            Assert.IsTrue(view.ManualOpen);
            foreach (var (key, label) in SliceObservation.DiagnosisItems) Assert.IsTrue(view.Observation.HasSeen(key), "诊断记录：" + label);
            Log("诊断记录：" + view.DiagnosisText().Replace("\n", " "));
            WriteReport("mouse_path.md", "两晚切片 · 只用鼠标走完维修核心（程序鼠标事件）");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator UiClick_DoesNotReach3D_AndTextInputBlocksShortcuts()
        {
            yield return Go(FirstOrderCameraRig.Dock);
            yield return ClickUI("manual");
            Assert.IsTrue(view.ManualOpen);
            yield return null;

            // 手册面板盖着的地方，后面正好有可操作的 3D 对象：点下去不能穿透
            var rt = view.ManualPanelRect;
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            Vector2? under = null;
            Component behind = null;
            for (int i = 1; i < 30 && under == null; i++)
                for (int j = 1; j < 30 && under == null; j++)
                {
                    var p = new Vector2(Mathf.Lerp(c[0].x, c[2].x, i / 30f), Mathf.Lerp(c[0].y, c[2].y, j / 30f));
                    if (!FirstOrderInput.IsOverUI(p)) continue;
                    var hit = FirstOrderInput.Pick(flow.Rig.Cam.ScreenPointToRay(p));
                    if (hit != null) { under = p; behind = hit; }
                }
            Assert.IsNotNull(under, "手册面板后面要有可点的 3D 对象，才能测穿透");
            var step = flow.Step; var state = flow.Dock.State; int rejected = flow.RejectedCount;
            bool acted = false;
            void OnActed(string t, bool ok, string m) => acted = true;
            flow.Acted += OnActed;
            yield return MoveTo(under.Value);
            Assert.IsTrue(input.PointerOverUI);
            Assert.IsNull(input.Hovered, "界面盖住的地方不悬停 3D 对象");
            yield return Press(under.Value, MouseButton.Left);
            yield return Press(under.Value, MouseButton.Right);
            flow.Acted -= OnActed;
            Assert.IsFalse(acted, "点手册面板没有点到后面的 " + FirstOrderInput.NameOf(behind));
            Assert.IsNull(input.LastObserveHit, "右键也没有观察到后面的对象");
            Assert.AreEqual(step, flow.Step); Assert.AreEqual(state, flow.Dock.State); Assert.AreEqual(rejected, flow.RejectedCount);
            Log($"手册面板盖住 {FirstOrderInput.NameOf(behind)}：左键、右键都没穿透");

            // 文字输入：焦点在备注框时，数字键和 F3 不生效
            var field = view.NotesField;
            yield return MoveTo(Center((RectTransform)field.transform));
            yield return Press(Center((RectTransform)field.transform), MouseButton.Left);
            yield return null;
            Assert.IsTrue(field.isFocused, "点备注框后获得焦点");
            Assert.IsTrue(FirstOrderInput.TextInputFocused);
            var shot = flow.Rig.Current;
            yield return Key(UnityEngine.InputSystem.Key.Digit2);
            yield return Key(UnityEngine.InputSystem.Key.F3);
            Assert.AreEqual(shot, flow.Rig.Current, "输入文字时数字键不切镜头");
            Assert.IsFalse(input.ShowHud, "输入文字时 F3 不开调试面板");
            Log("备注框有焦点：数字键 2、F3 都没有生效");

            // 关掉手册（鼠标点“关闭”）后，数字键作为可选调试操作仍可用；调试按钮能开关 HUD
            yield return ClickUI("manual:close");
            Assert.IsFalse(view.ManualOpen);
            Assert.IsFalse(FirstOrderInput.TextInputFocused);
            yield return Key(UnityEngine.InputSystem.Key.Digit2);
            Assert.AreEqual(FirstOrderCameraRig.EngineL, flow.Rig.Current, "没有文字焦点时数字键 2 切到左引擎（可选调试）");
            yield return ClickUI("debug");
            Assert.IsTrue(input.ShowHud, "调试按钮打开 HUD");
            yield return ClickUI("debug");
            Assert.IsFalse(input.ShowHud, "调试按钮关闭 HUD");
            Log("关闭手册后：数字键 2 切到左引擎；调试按钮开 / 关 HUD");
            WriteReport("ui_input_guard.md", "两晚切片 · 界面点击不穿透 3D、文字输入时快捷键不生效（程序鼠标 / 键盘事件）");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
