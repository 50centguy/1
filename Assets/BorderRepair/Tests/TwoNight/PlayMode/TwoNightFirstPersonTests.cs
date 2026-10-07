using System;
using System.Collections;
using System.IO;
using System.Text;
using BorderRepair.FirstOrder;
using BorderRepair.FirstOrder.Slice;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace BorderRepair.TwoNight.Tests
{
    /// <summary>
    /// 第二晚第一人称（程序给转头、移动和鼠标 / 键盘事件，不是真人试玩）：
    /// 手册展开时不走、不瞄；伸手够不着时左键不操作；准星点左上盖开始检查；沿通道走到七号背面扳开后侧锁扣；
    /// 走不出房间、走不进七号；Tab 放开 / 点画面锁回（那一下不算操作）；固定机位与行走互切。
    /// 结果写 Docs/Integration/TwoNightSlice/first_person_check.md。
    /// </summary>
    public class TwoNightFirstPersonTests
    {
        Mouse mouse; Keyboard keyboard; Action mouseCleanup;
        readonly StringBuilder log = new StringBuilder();
        string dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "TwoNightFp_" + Guid.NewGuid().ToString("N"));
            TwoNightSave.OverrideDirectory = dir;
            keyboard = InputSystem.AddDevice<Keyboard>("TwoNightFp_VirtualKeyboard");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            mouseCleanup?.Invoke();
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            TwoNightSave.OverrideDirectory = null; TwoNightRun.Clear();
            try { Directory.Delete(dir, true); } catch { }
            yield return null;
        }

        IEnumerator LeftClick()
        {
            var c = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            mouse.MakeCurrent();
            InputSystem.QueueStateEvent(mouse, new MouseState { position = c }.WithButton(MouseButton.Left, true)); yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = c }.WithButton(MouseButton.Left, false)); yield return null; yield return null;
        }

        IEnumerator Tap(Key key)
        {
            keyboard.MakeCurrent();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
        }

        static Vector2 Xz(Vector3 v) => new Vector2(v.x, v.z);

        /// <summary>朝目标点（地面坐标 x, z）一直往前走，直到 6 cm 以内；卡住（1 秒内前进不到 2 cm）或超时就停下。返回是否到达。</summary>
        IEnumerator WalkTo(FirstPersonWalker w, float x, float z, Action<bool> done)
        {
            var target = new Vector2(x, z);
            float t = 0f, lastCheck = 0f; var lastPos = Xz(w.transform.position);
            while (t < 8f)
            {
                var p = Xz(w.transform.position);
                var d = target - p;
                if (d.magnitude < 0.06f) break;
                w.Look(Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg, w.Pitch);
                w.ScriptedMove = new Vector2(0f, Mathf.Clamp01(d.magnitude / 0.15f) * 0.7f + 0.3f);
                yield return null;
                t += Time.deltaTime;
                if (t - lastCheck >= 1f)
                {
                    if ((Xz(w.transform.position) - lastPos).magnitude < 0.02f) break;
                    lastCheck = t; lastPos = Xz(w.transform.position);
                }
            }
            w.ScriptedMove = null;
            yield return null;
            var end = Xz(w.transform.position);
            bool ok = (end - target).magnitude < 0.08f;
            log.AppendLine($"  - 走向 ({x:F2}, {z:F2})：停在 ({end.x:F2}, {end.y:F2})，{(ok ? "到达" : "没到")}");
            done(ok);
        }

        IEnumerator Hold(FirstPersonWalker w, Vector2 move, float seconds)
        {
            w.ScriptedMove = move;
            for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
            w.ScriptedMove = null;
            yield return null;
        }

        static Vector3 Center(Component c) => c.GetComponentInChildren<Renderer>().bounds.center;

        /// <summary>检查开始前，左引擎的点选入口是包住整台引擎的检查代理（点它或上盖都算“开始检查”）；开始后代理关掉，指到的是上盖本身。</summary>
        static bool IsLeftEngine(FirstOrderFlow flow, Component c) => c == flow.Cover || c is BorderRepair.Dock.DockInteractable d && d.action == BorderRepair.Dock.DockAction.EngineLeft;

        [UnityTest]
        public IEnumerator Night2_FirstPerson_Walk_Reach_Aim_Bounds()
        {
            var s = TwoNightRun.NewGame(null);
            TwoNightRun.SettleCommunicator(s, true, "case_n1_collector_communicator", null);
            TwoNightRun.ConfirmLedger(s); TwoNightRun.FinishIncident(s);
            Assert.IsTrue(TwoNightRun.RegisterUnit07(s, new Unit07SafeState { seated = true, clamped = true, powerOff = true, rotorsStopped = true, trayStowed = true, robotUpright = true }, out var why), why);
            Assert.IsTrue(TwoNightRun.BeginNight2(s));
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/BorderRepair/Scenes/Slice/" + TwoNightScenes.Robot + ".unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null; yield return null; yield return null;
            mouse = FirstOrderAcceptanceDriver.CreateVirtualMouse(out mouseCleanup);
            if (!keyboard.enabled) InputSystem.EnableDevice(keyboard);   // 场景加载前建的虚拟键盘会被“窗口不在前台”停用；建虚拟鼠标时已设为忽略焦点，这里重新启用
            yield return null;
            var view = UnityEngine.Object.FindFirstObjectByType<SliceView>();
            var input = UnityEngine.Object.FindFirstObjectByType<FirstOrderInput>();
            var flow = UnityEngine.Object.FindFirstObjectByType<FirstOrderFlow>();
            var w = flow.Rig.Walker;
            Assert.IsNotNull(w, "发布场景配了第一人称行走");
            w.LeaveSystemCursor = true;
            log.AppendLine($"# 第二晚第一人称检查（PlayMode，程序输入，不是真人试玩）\n\n- {DateTime.Now:yyyy-MM-dd HH:mm}，Game 视图 {Screen.width}×{Screen.height}；人物半径 {w.GetComponent<CharacterController>().radius:F2} m，伸手 {w.Reach:F1} m。\n");

            // ---- 1) 开场：行走中，但手册展开 → 不瞄、不走
            Assert.IsTrue(flow.Rig.Walking, "第二晚开场是第一人称行走，镜头 " + flow.Rig.Current);
            Assert.IsTrue(view.ManualOpen);
            var p0 = w.transform.position;
            Assert.IsFalse(w.Aiming, "手册展开：鼠标放开");
            yield return Hold(w, new Vector2(0f, 1f), 0.5f);
            Assert.Less(Vector3.Distance(p0, w.transform.position), 0.01f, "手册展开时不移动");
            Assert.IsFalse(view.CrosshairVisible);
            log.AppendLine($"1. 开场出生点 ({p0.x:F2}, {p0.z:F2})；手册展开时不瞄准、按前进不移动。");
            view.ToggleManual();
            yield return null; yield return null;
            Assert.IsTrue(w.Aiming && view.CrosshairVisible, "关上手册：准星出现");

            // ---- 2) 伸手够不着：不操作
            w.PlaceAt(new Vector3(-1.2f, 0.02f, 0.6f), 90f);
            yield return null;
            w.LookAt(Center(flow.Cover));
            yield return null; yield return null;
            Assert.IsTrue(IsLeftEngine(flow, input.Hovered), "远处准星对准左引擎：" + FirstOrderInput.NameOf(input.Hovered));
            Assert.IsTrue(input.HoverOutOfReach, $"{input.HoverDistance:F2} m 超出伸手距离");
            var step = flow.Step;
            yield return LeftClick();
            Assert.AreEqual(step, flow.Step, "够不着时左键不推进流程");
            StringAssert.Contains("够不着", view.FeedbackText.text);
            StringAssert.Contains("够不着", view.TooltipText);
            log.AppendLine($"2. 站在 (-1.20, 0.60) 看左引擎（{input.HoverDistance:F2} m > 伸手 {w.Reach:F1} m，左键不操作，提示“{view.FeedbackText.text}”。");

            // ---- 3) 走近，准星点左上盖开始检查
            bool ok = false;
            yield return WalkTo(w, -0.40f, 0.45f, r => ok = r);
            Assert.IsTrue(ok, "走到左引擎前：" + log + $" | 行走 {flow.Rig.Walking} 瞄准 {w.Aiming} 镜头 {flow.Rig.Current} 着地 {w.GetComponent<CharacterController>().isGrounded} y {w.transform.position.y:F2}");
            w.LookAt(Center(flow.Cover));
            yield return null; yield return null;
            Assert.IsTrue(IsLeftEngine(flow, input.Hovered), "走近后准星对准左引擎：" + FirstOrderInput.NameOf(input.Hovered));
            Assert.IsFalse(input.HoverOutOfReach, $"{input.HoverDistance:F2} m 在伸手距离内");
            yield return LeftClick();
            Assert.AreEqual(FoStep.ReleaseLatches, flow.Step, "准星左键：开始检查 → " + flow.Message);
            log.AppendLine($"3. 走到 (-0.40, 0.45)，准星对准左引擎（{FirstOrderInput.NameOf(input.Hovered)}）{input.HoverDistance:F2} m，左键开始检查（步骤 {flow.Step}）。");

            // ---- 4) 沿工作台和维修座之间的通道走到七号背面，扳开后侧锁扣
            log.AppendLine("4. 去七号背面（工作台与维修座之间的通道）：");
            foreach (var (x, z) in new[] { (-0.25f, 0.12f), (0.30f, 0.14f), (0.58f, 0.27f), (0.95f, 0.30f), (1.15f, 0.42f) })
            {
                yield return WalkTo(w, x, z, r => ok = r);
                Assert.IsTrue(ok, $"走到 ({x:F2}, {z:F2})，实际停在 ({w.transform.position.x:F2}, {w.transform.position.z:F2})");
            }
            w.LookAt(Center(flow.LatchRear));
            yield return null; yield return null;
            Assert.AreEqual(flow.LatchRear, input.Hovered, "背面准星对准后侧锁扣：" + FirstOrderInput.NameOf(input.Hovered));
            Assert.IsFalse(input.HoverOutOfReach, $"{input.HoverDistance:F2} m");
            yield return LeftClick();
            float until = Time.realtimeSinceStartup + 10f; while (flow.Busy && Time.realtimeSinceStartup < until) yield return null;
            Assert.AreEqual(PartLocation.Released, flow.LatchRear.Location, "后侧锁扣扳开：" + flow.Message);
            log.AppendLine($"   在 ({w.transform.position.x:F2}, {w.transform.position.z:F2}) 准星对准后侧锁扣 {input.HoverDistance:F2} m，左键扳开。");

            // ---- 5) Tab 放开鼠标：不悬停；点画面锁回，这一下不算操作
            yield return Tap(Key.Tab);
            Assert.IsFalse(w.Aiming, "Tab 放开鼠标");
            yield return null;
            Assert.IsNull(input.Hovered, "放开鼠标时不悬停 3D");
            w.LookAt(Center(flow.LatchOuter));
            var outer = flow.LatchOuter.Location;
            yield return LeftClick();
            Assert.IsTrue(w.Aiming, "点画面锁回准星");
            Assert.AreEqual(outer, flow.LatchOuter.Location, "锁回的那一下不当作操作");
            log.AppendLine("5. Tab 放开鼠标后不悬停 3D；点一下画面锁回准星，这一下没有操作任何东西。");

            // ---- 6) 边界：走不进七号、走不出房间
            w.PlaceAt(new Vector3(-0.45f, 0.02f, 0.85f), 90f);
            yield return Hold(w, new Vector2(0f, 1f), 2.5f);
            float intoRobot = w.transform.position.x;
            Assert.Less(intoRobot, 0.12f, "朝七号一直走，被挡在机身外");
            w.PlaceAt(new Vector3(-1.0f, 0.02f, 0.9f), 0f);
            yield return Hold(w, new Vector2(0f, 1f), 2.5f);
            float front = w.transform.position.z;
            Assert.Less(front, 1.66f, "往房间正面走，被看不见的边界挡住");
            w.PlaceAt(new Vector3(-1.0f, 0.02f, 0.9f), -90f);
            yield return Hold(w, new Vector2(0f, 1f), 2.5f);
            float side = w.transform.position.x;
            Assert.Greater(side, -1.62f, "往右前方走，被挡住");
            Assert.Greater(w.transform.position.y, -0.05f, "没有掉出地面");
            log.AppendLine($"6. 朝七号走 2.5 s 停在 x = {intoRobot:F2}（机身最外 0.11）；往正面走停在 z = {front:F2}（地面边缘 1.70）；往右前走停在 x = {side:F2}（墙 -1.60）。");

            // ---- 7) 固定机位（近看）与行走互切
            flow.Rig.Go(FirstOrderCameraRig.EngineClose);
            yield return null;
            Assert.IsFalse(flow.Rig.Walking);
            var back = view.GetButton("back");
            Assert.IsTrue(back.interactable && back.GetComponentInChildren<UnityEngine.UI.Text>().text.Contains("行走"));
            back.onClick.Invoke();
            yield return null;
            Assert.IsTrue(flow.Rig.Walking, "「← 行走」回到行走");
            flow.Rig.Go(FirstOrderCameraRig.Bench);
            yield return null;
            yield return Hold(w, new Vector2(1f, 0f), 0.1f);
            Assert.IsTrue(flow.Rig.Walking, "固定机位里按移动键回到行走");
            log.AppendLine("7. 镜头栏进固定机位后，「← 行走」按钮或移动键都能回到行走。");

            Directory.CreateDirectory("Docs/Integration/TwoNightSlice");
            File.WriteAllText("Docs/Integration/TwoNightSlice/first_person_check.md", log.ToString(), new UTF8Encoding(false));
        }
    }
}
