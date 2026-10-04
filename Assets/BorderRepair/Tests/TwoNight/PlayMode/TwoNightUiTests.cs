using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BorderRepair.FirstOrder;
using BorderRepair.FirstOrder.Slice;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace BorderRepair.TwoNight.Tests
{
    /// <summary>
    /// 第二晚界面（程序鼠标事件，虚拟鼠标设备经 InputSystemUIInputModule / FirstOrderInput 处理，不是真人试玩）：
    /// 1) 手册展开时：后方 3D 不悬停、不点、悬停提示不显示；备注框能获得焦点；点“关闭”后恢复悬停；
    /// 2) 悬停提示不压到镜头栏 / 模式按钮 / 状态栏，也不出画布；
    /// 3) 诊断记录：开场列“左上轴承（原位）”，不写“磨损”；“新旧轴承对比”在实际拆下旧轴承后才出现。
    /// 结果写 Docs/Integration/TwoNightSlice/night2_ui_check.md。
    /// </summary>
    public class TwoNightUiTests
    {
        Mouse mouse; Keyboard keyboard; Action mouseCleanup;
        readonly StringBuilder log = new StringBuilder();
        string dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "TwoNightUi_" + Guid.NewGuid().ToString("N"));
            TwoNightSave.OverrideDirectory = dir;
            keyboard = InputSystem.AddDevice<Keyboard>("TwoNightUi_VirtualKeyboard");
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

        IEnumerator MoveTo(Vector2 p) { mouse.MakeCurrent(); InputSystem.QueueStateEvent(mouse, new MouseState { position = p }); yield return null; yield return null; }

        IEnumerator Press(Vector2 p)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = p }.WithButton(MouseButton.Left, true)); yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = p }.WithButton(MouseButton.Left, false)); yield return null; yield return null;
        }

        static Vector2 Center(RectTransform rt) { var c = new Vector3[4]; rt.GetWorldCorners(c); return (c[0] + c[2]) / 2f; }

        [UnityTest]
        public IEnumerator Night2_ManualModal_TooltipLayout_StagedDiagnosis()
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
            mouse = FirstOrderAcceptanceDriver.CreateVirtualMouse(out mouseCleanup);   // 场景加载后再建（与核心切片测试相同），界面输入模块才会接这个设备
            yield return null;
            var view = UnityEngine.Object.FindFirstObjectByType<SliceView>();
            var input = UnityEngine.Object.FindFirstObjectByType<FirstOrderInput>();
            var flow = UnityEngine.Object.FindFirstObjectByType<FirstOrderFlow>();
            Assert.IsTrue(view.ManualOpen, "第二晚开场手册展开");
            log.AppendLine($"# 第二晚界面检查（PlayMode，程序鼠标事件）\n\n- {DateTime.Now:yyyy-MM-dd HH:mm}，Game 视图 {Screen.width}×{Screen.height}。\n");

            // ---- 1) 手册展开：后方 3D
            var cam = flow.Rig.Cam;
            Vector2? behind = null; Component target = null;
            for (int i = 2; i < 40 && behind == null; i++)
                for (int j = 2; j < 24 && behind == null; j++)
                {
                    var p = new Vector2(Screen.width * i / 41f, Screen.height * j / 25f);
                    if (FirstOrderInput.IsOverUI(p)) continue;
                    var hit = FirstOrderInput.Pick(cam.ScreenPointToRay(p));
                    if (hit != null) { behind = p; target = hit; }
                }
            Assert.IsNotNull(behind, "手册外面要有能指到的 3D 对象，才能测“后方不点”");
            bool acted = false; void OnActed(string t, bool ok, string m) => acted = true;
            flow.Acted += OnActed;
            var step = flow.Step;
            yield return MoveTo(behind.Value);
            Assert.IsTrue(input.ModalBlocked, "手册展开时 3D 输入被挡");
            Assert.IsNull(input.Hovered, "手册展开时不悬停 3D：" + FirstOrderInput.NameOf(target));
            Assert.IsFalse(view.TooltipVisible, "手册展开时不显示悬停提示");
            yield return Press(behind.Value);
            Assert.IsFalse(acted, "手册展开时点不到后方 3D");
            Assert.AreEqual(step, flow.Step);
            log.AppendLine($"- 手册展开：指向手册外的 3D 对象 {FirstOrderInput.NameOf(target)}（{behind.Value.x:F0}, {behind.Value.y:F0}）——不悬停、无提示、点击无效。");
            // 备注框可用
            var field = view.NotesField;
            yield return MoveTo(Center((RectTransform)field.transform));
            yield return Press(Center((RectTransform)field.transform));
            yield return null;
            Assert.IsTrue(field.isFocused, "备注框能获得焦点");
            log.AppendLine("- 备注框：点后获得焦点。");
            // 关闭
            var close = view.GetButton("manual:close");
            yield return MoveTo(Center((RectTransform)close.transform));
            yield return Press(Center((RectTransform)close.transform));
            Assert.IsFalse(view.ManualOpen, "点“关闭”收起手册");
            Assert.IsFalse(input.ModalBlocked, "手册关上后 3D 输入恢复");
            yield return MoveTo(behind.Value + new Vector2(1, 0)); yield return MoveTo(behind.Value);
            Assert.IsNotNull(input.Hovered, "手册关上后恢复悬停");
            Assert.IsTrue(view.TooltipVisible, "手册关上后显示悬停提示");
            log.AppendLine($"- 关闭手册后：悬停恢复（{FirstOrderInput.NameOf(input.Hovered)}），提示出现。");
            flow.Acted -= OnActed;

            // ---- 2) 悬停提示布局：扫整个画面里能悬停的点，凡是显示了提示，都不压按钮、不出画布
            var canvas = (RectTransform)view.transform;
            Rect TipRect()
            {
                var c = new Vector3[4]; view.TooltipRect.GetWorldCorners(c);
                var size = canvas.rect.size;
                Vector2 a = (Vector2)canvas.InverseTransformPoint(c[0]) + size * 0.5f, b = (Vector2)canvas.InverseTransformPoint(c[2]) + size * 0.5f;
                return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
            }
            int shown = 0, hidden = 0, overlaps = 0, outside = 0;
            for (int i = 1; i < 30; i++)
                for (int j = 1; j < 18; j++)
                {
                    var p = new Vector2(Screen.width * i / 30f, Screen.height * j / 18f);
                    if (FirstOrderInput.IsOverUI(p) || FirstOrderInput.Pick(cam.ScreenPointToRay(p)) == null) continue;
                    yield return MoveTo(p);
                    if (input.Hovered == null) continue;
                    if (!view.TooltipVisible) { hidden++; continue; }
                    shown++;
                    var r = TipRect(); var size = canvas.rect.size;
                    if (r.xMin < 0 || r.yMin < 0 || r.xMax > size.x || r.yMax > size.y) outside++;
                    if (view.TooltipAvoidRects().Any(a => a.Overlaps(r))) overlaps++;
                }
            log.AppendLine($"- 悬停提示扫点：显示 {shown} 次、因四个方向都会压按钮或出界而不显示 {hidden} 次；压到按钮 / 状态栏 {overlaps} 次，出画布 {outside} 次。");
            Assert.Greater(shown, 0);
            Assert.AreEqual(0, overlaps, "悬停提示不压按钮"); Assert.AreEqual(0, outside, "悬停提示不出画布");

            // ---- 3) 诊断记录分阶段
            string diag = view.DiagnosisText();
            StringAssert.Contains("左上轴承（原位）", diag);
            StringAssert.DoesNotContain("新旧轴承对比", diag, "开场不列“新旧轴承对比”");
            StringAssert.DoesNotContain("磨损", diag, "开场不写“磨损”");
            StringAssert.DoesNotContain("磨损", view.ManualBodyText + view.ManualTitleText);
            log.AppendLine($"- 开场诊断记录：{diag.Replace("\n", " ")}");
            IEnumerator Do(Component c, string label)
            {
                Assert.IsTrue(flow.Click(c), label + "：" + flow.Message);
                float until = Time.realtimeSinceStartup + 20f; while (flow.Busy && Time.realtimeSinceStartup < until) yield return null;
                yield return null;
            }
            yield return Do(flow.Cover, "检查左引擎");
            yield return Do(flow.LatchOuter, "扳开外侧锁扣");
            yield return Do(flow.LatchRear, "扳开后侧锁扣");
            yield return Do(flow.Cover, "取下上盖");
            yield return Do(flow.MatZone, "上盖放到操作垫");
            StringAssert.DoesNotContain("新旧轴承对比", view.DiagnosisText(), "上盖拆下、旧轴承还在原位：仍不列对比");
            yield return Do(flow.Bearing, "定位轴承");
            yield return Do(flow.Bearing, "取下旧轴承");
            Assert.AreNotEqual(PartLocation.Installed, flow.Bearing.Location);
            StringAssert.Contains("新旧轴承对比", view.DiagnosisText(), "旧轴承拆下后出现“新旧轴承对比”");
            log.AppendLine($"- 取下旧轴承后：{view.DiagnosisText().Replace("\n", " ")}");
            Directory.CreateDirectory("Docs/Integration/TwoNightSlice");
            File.WriteAllText("Docs/Integration/TwoNightSlice/night2_ui_check.md", log.ToString(), new UTF8Encoding(false));
        }
    }
}
