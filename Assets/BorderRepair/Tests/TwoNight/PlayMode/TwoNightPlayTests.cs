using System;
using System.Collections;
using System.IO;
using System.Linq;
using BorderRepair.Core;
using BorderRepair.Data;
using BorderRepair.Dock;
using BorderRepair.FirstOrder;
using BorderRepair.FirstOrder.Slice;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace BorderRepair.TwoNight.Tests
{
    /// <summary>
    /// 两晚切片串联：柜台结单与账本、七号端盘失衡、停靠登记与夜末存档、继续进入第二晚、损坏存档。
    /// 维修和停靠操作都是程序调用（会话 API、FirstOrderInput.ClickAt 按屏幕坐标点），不是真人试玩。
    /// 存档放在临时目录（TwoNightSave.OverrideDirectory），不碰玩家的真存档。
    /// </summary>
    public class TwoNightPlayTests
    {
        const string SceneDir = "Assets/BorderRepair/Scenes/Slice/";
        string dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "TwoNightPlay_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            TwoNightSave.OverrideDirectory = dir;
            TwoNightRun.Clear();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.captureDeltaTime = 0f;
            TwoNightSave.OverrideDirectory = null;
            TwoNightRun.Clear();
            try { Directory.Delete(dir, true); } catch { }
            yield return null;
        }

        static IEnumerator Load(string name)
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(SceneDir + name + ".unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null; yield return null;
        }

        /// <summary>等场景切换（导演调用 SceneManager.LoadScene 之后）。</summary>
        static IEnumerator WaitScene(string name, float seconds = 20f)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (SceneManager.GetActiveScene().name != name && Time.realtimeSinceStartup < until) yield return null;
            Assert.AreEqual(name, SceneManager.GetActiveScene().name, "场景切换");
            yield return null; yield return null;
        }

        static void AssertSingles()
        {
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length, "只有一个 EventSystem");
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(a => a.enabled), "只有一个启用的 AudioListener（一个主摄像机）");
            Assert.LessOrEqual(UnityEngine.Object.FindObjectsByType<FirstOrderInput>(FindObjectsSortMode.None).Length, 1, "最多一个七号点选输入");
            Assert.LessOrEqual(UnityEngine.Object.FindObjectsByType<RepairStationController>(FindObjectsSortMode.None).Length, 1, "最多一个柜台控制器");
        }

        /// <summary>按原型营业流程处理当前送修物（会话 API，与按钮同一入口）：接收 → 扫描关键点 → 诊断 → 决定 → 结单。</summary>
        static void DriveCommunicator(RepairStationController st, RepairDecision decision)
        {
            var s = st.Session;
            Assert.IsTrue(s.AcceptItem());
            Assert.IsTrue(s.SetScanMode(true));
            foreach (var p in s.CurrentCase.inspectionPoints.Where(p => p.requiredForDiagnosis))
                Assert.AreEqual(ScanOutcome.NewFinding, st.ScanPoint(p.pointId), p.pointId);
            Assert.IsTrue(s.TryBeginDiagnosis());
            Assert.IsTrue(s.SubmitDiagnosis(s.CurrentCase.correctDiagnosisId));
            Assert.IsTrue(s.SubmitDecision(decision));
            Assert.IsTrue(s.NextCase());   // 结单 → 营业总结
        }

        static void PlayNight1Logic()
        {
            var s = TwoNightRun.NewGame(null);
            TwoNightRun.SettleCommunicator(s, true, "case_n1_collector_communicator", null);
            TwoNightRun.ConfirmLedger(s);
        }

        // ------------------------------------------------------------------ 柜台

        [UnityTest]
        public IEnumerator Counter_CorrectRepair_PostsOnce_ConfirmGoesToUnit07()
        {
            TwoNightRun.NewGame(null);
            yield return Load(TwoNightScenes.Counter);
            AssertSingles();
            var st = UnityEngine.Object.FindFirstObjectByType<RepairStationController>();
            var dir2 = UnityEngine.Object.FindFirstObjectByType<TwoNightCounterDirector>();
            Assert.AreEqual(1, st.Session.CaseCount, "第一晚只有一单");
            StringAssert.Contains("通讯器", st.Session.CurrentCase.itemName);
            Assert.AreEqual("收藏家", st.Session.CurrentCase.customerName);
            Assert.IsTrue(dir2.DialogueVisible, "收藏家对话");
            for (int i = 0; i < 3; i++) dir2.DialogueButton.onClick.Invoke();
            Assert.IsFalse(dir2.DialogueVisible);

            DriveCommunicator(st, RepairDecision.Repair);
            yield return null;
            Assert.IsTrue(dir2.LedgerVisible, "正确结单后显示账本");
            Assert.AreEqual(800, TwoNightRun.Current.Cash);
            Assert.AreEqual(400, TwoNightRun.Current.RentShortfall);
            StringAssert.Contains("800", dir2.LedgerText.text);
            StringAssert.Contains("还差 400", dir2.LedgerText.text);

            // 重复结单：重开会话、再修一次、再结单 → 不再入账
            st.Session.Restart();
            DriveCommunicator(st, RepairDecision.Repair);
            yield return null;
            Assert.AreEqual(800, TwoNightRun.Current.Cash, "重复结单不入账");
            Assert.AreEqual(2, TwoNightRun.Current.transactions.Count);

            dir2.ConfirmButton.onClick.Invoke();
            dir2.ConfirmButton.onClick.Invoke();   // 连点
            Assert.AreEqual(TwoNightPhase.Night1Incident, TwoNightRun.Current.phase);
            yield return WaitScene(TwoNightScenes.Robot);
            AssertSingles();
            Assert.AreEqual(800, TwoNightRun.Current.Cash);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Counter_WrongDecision_NoIncome_RetryThenCorrect()
        {
            TwoNightRun.NewGame(null);
            yield return Load(TwoNightScenes.Counter);
            var st = UnityEngine.Object.FindFirstObjectByType<RepairStationController>();
            var d = UnityEngine.Object.FindFirstObjectByType<TwoNightCounterDirector>();
            DriveCommunicator(st, RepairDecision.Refuse);
            yield return null;
            Assert.IsTrue(d.RetryVisible, "错误 / 拒绝结果：提示重新处理");
            Assert.IsFalse(d.LedgerVisible);
            Assert.AreEqual(500, TwoNightRun.Current.Cash, "错误决定不入账");
            Assert.AreEqual(TwoNightPhase.Night1Counter, TwoNightRun.Current.phase);
            d.RetryButton.onClick.Invoke();
            yield return null;
            Assert.AreEqual(RepairStage.Intake, st.Session.Stage);
            DriveCommunicator(st, RepairDecision.RecommendReplacement);
            yield return null;
            Assert.AreEqual(500, TwoNightRun.Current.Cash, "建议更换也不是有效结单");
            d.RetryButton.onClick.Invoke();
            DriveCommunicator(st, RepairDecision.Repair);
            yield return null;
            Assert.IsTrue(d.LedgerVisible);
            Assert.AreEqual(800, TwoNightRun.Current.Cash);
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ 七号：第一晚

        [UnityTest]
        public IEnumerator Unit07_WithoutConfirmedLedger_NoIncident()
        {
            TwoNightRun.NewGame(null);   // 还在柜台阶段
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("七号场景不能从阶段"));
            yield return Load(TwoNightScenes.Robot);
            var inc = UnityEngine.Object.FindFirstObjectByType<TrayIncident>();
            for (int i = 0; i < 60; i++) yield return null;
            Assert.IsFalse(inc.Playing);
            Assert.AreEqual("idle", inc.Phase, "没确认账本，不触发七号事故");
            Assert.AreEqual(500, TwoNightRun.Current.Cash);
        }

        Component Grip(FirstOrderAcceptanceDriver drv, FirstOrderFlow flow)
        {
            flow.Rig.Go(FirstOrderCameraRig.Dock, true);
            var gs = new[] { "Dock_Clamp_L_Grip", "Dock_Clamp_R_Grip" }.Select(n => (Component)GameObject.Find(n).GetComponent<DockInteractable>()).ToList();
            return gs.FirstOrDefault(g => drv.FindClickPoint(g, out _)) ?? gs[0];
        }

        IEnumerator Click(FirstOrderAcceptanceDriver drv, FirstOrderFlow flow, FirstOrderInput input, string cam, Component target, bool expect, string text = null)
        {
            flow.Rig.Go(cam, true);
            yield return null;
            Assert.IsTrue(drv.FindClickPoint(target, out var sp), "点得到 " + target.name);
            var (hit, ok) = input.ClickAt(sp);
            Assert.AreEqual(target, hit);
            Assert.AreEqual(expect, ok, flow.Message);
            if (text != null) StringAssert.Contains(text, flow.Message);
            float until = Time.realtimeSinceStartup + 20f;
            while (flow.Busy && Time.realtimeSinceStartup < until) yield return null;
        }

        [UnityTest]
        public IEnumerator Unit07_Night1_IncidentThenDock_RegisterSaves_SafeState()
        {
            PlayNight1Logic();
            yield return Load(TwoNightScenes.Robot);
            AssertSingles();
            var director = UnityEngine.Object.FindFirstObjectByType<TwoNightRobotDirector>();
            var inc = director.Incident;
            var flow = director.Flow;
            var input = UnityEngine.Object.FindFirstObjectByType<FirstOrderInput>();
            var view = UnityEngine.Object.FindFirstObjectByType<SliceView>();
            Assert.IsTrue(inc.Playing, "账本确认后七号端盘失衡");
            Assert.IsFalse(input.enabled, "演出期间不接受 3D 点击");
            Assert.IsTrue(flow.InspectionLocked);
            StringAssert.DoesNotContain("磨损", view.ManualTitleText + view.ManualBodyText, "入口手册不写诊断答案");
            bool sawTaken = false, sawLean = false, sawStatus = false;
            float until = Time.realtimeSinceStartup + 90f;
            while (!inc.Done && Time.realtimeSinceStartup < until)
            {
                sawTaken |= inc.Tray.Taken;
                sawLean |= inc.LeanNow > inc.LeanDegrees * 0.9f;
                sawStatus |= GameObject.Find("TwoNight_ScreenStatus（屏幕状态文字）") != null;
                Assert.IsFalse(flow.Cover.Location != PartLocation.Installed, "第一晚不拆上盖");
                yield return null;
            }
            Assert.IsTrue(inc.Done, "演出播完");
            Assert.IsTrue(sawTaken, "七号取过盘");
            Assert.IsTrue(sawLean, "机身左倾到设定角度");
            Assert.IsTrue(sawStatus, "屏幕 TRAY UNSTABLE");
            yield return null;
            var root = inc.RobotRoot;
            Assert.Less((root.position - inc.HoverPosition).magnitude, 1e-5f, "回到原悬停位");
            Assert.Less(Vector3.Angle(root.up, Vector3.up), 0.01f, "机身回正");
            Assert.IsFalse(inc.Tray.Taken, "托盘已放回");
            Assert.Less((inc.Tray.transform.position - director.TrayHomePosition).magnitude, 1e-5f, "托盘在原位");
            Assert.AreEqual(0f, inc.Overlay.RightWeight); Assert.AreEqual(0f, inc.Overlay.LeftWeight);
            Assert.AreEqual(TwoNightPhase.Night1Docking, TwoNightRun.Current.phase);
            Assert.IsFalse(TwoNightRun.FinishIncident(TwoNightRun.Current), "事故只触发一次");
            Assert.IsTrue(input.enabled);

            // 停靠、夹紧、断电（程序点击，与鼠标同一入口）
            var drv = new FirstOrderAcceptanceDriver(flow, input);
            var dock = flow.Dock;
            Assert.IsFalse(director.RegisterVisible, "没停靠不能登记");
            director.Register();
            Assert.AreEqual(TwoNightPhase.Night1Docking, TwoNightRun.Current.phase, "没停靠时点登记：阶段不变");
            Assert.IsFalse(string.IsNullOrEmpty(director.LastRegisterMessage) || director.LastRegisterMessage.StartsWith("已登记"), "给出不能登记的原因：" + director.LastRegisterMessage);
            Assert.AreEqual(SaveStatus.None, TwoNightSave.Read().status, "不安全时不写存档");
            var grip = Grip(drv, flow);
            yield return Click(drv, flow, input, FirstOrderCameraRig.Dock, grip, true);
            until = Time.realtimeSinceStartup + 20f; while (dock.State != DockState.SeatedOpen && Time.realtimeSinceStartup < until) yield return null;
            yield return Click(drv, flow, input, FirstOrderCameraRig.Dock, grip, true);
            until = Time.realtimeSinceStartup + 20f; while (dock.State != DockState.Clamped && Time.realtimeSinceStartup < until) yield return null;
            var lever = GameObject.Find("Dock_PowerSwitch_LeverGrip").GetComponent<DockInteractable>();
            yield return Click(drv, flow, input, FirstOrderCameraRig.Dock, lever, true);
            if (dock.State == DockState.SpinningDown)
                yield return Click(drv, flow, input, FirstOrderCameraRig.EngineL, flow.LatchOuter, false, "叶轮");
            Assert.IsFalse(director.RegisterVisible, "叶轮没停稳不能登记");
            until = Time.realtimeSinceStartup + 20f; while (dock.State != DockState.RotorsStopped && Time.realtimeSinceStartup < until) yield return null;
            yield return null;
            Assert.AreEqual(FoStep.InspectLeftEngine, flow.Step);
            yield return Click(drv, flow, input, FirstOrderCameraRig.EngineL, flow.Cover, false, "今晚不拆");
            Assert.AreEqual(PartLocation.Installed, flow.Cover.Location);
            Assert.IsTrue(director.RegisterVisible, "停稳后可以登记");
            Assert.IsTrue(director.CollectSafeState().IsSafe, director.CollectSafeState().WhyUnsafe());
            director.RegisterButton.onClick.Invoke();
            yield return null;
            Assert.IsTrue(director.EndPanelVisible);
            Assert.AreEqual(TwoNightPhase.Night1Ended, TwoNightRun.Current.phase);
            var r = TwoNightSave.Read();
            Assert.AreEqual(SaveStatus.Ok, r.status, r.message + " / " + director.LastSaveMessage);
            Assert.AreEqual(800, r.state.Cash);
            Assert.IsTrue(r.state.unit07.IsSafe);
            Assert.IsFalse(r.state.unit07Repaired);
            Assert.AreEqual(TwoNightRun.Unit07WorkOrderId, r.state.unit07WorkOrderId);
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ 第二晚与菜单

        static void WriteNight1Save()
        {
            var s = TwoNightRun.NewGame(null);
            TwoNightRun.SettleCommunicator(s, true, "case_n1_collector_communicator", null);
            TwoNightRun.ConfirmLedger(s);
            TwoNightRun.FinishIncident(s);
            TwoNightRun.RegisterUnit07(s, new Unit07SafeState { seated = true, clamped = true, powerOff = true, rotorsStopped = true, trayStowed = true, robotUpright = true }, out _);
            Assert.IsTrue(TwoNightSave.Write(s, out var m), m);
            TwoNightRun.Clear();
        }

        [UnityTest]
        public IEnumerator Menu_ContinueTwice_Night2SafeStart_NoDoubleMoney()
        {
            WriteNight1Save();
            var before = File.ReadAllText(TwoNightSave.FilePath);
            for (int round = 0; round < 2; round++)
            {
                yield return Load(TwoNightScenes.Menu);
                AssertSingles();
                var menu = UnityEngine.Object.FindFirstObjectByType<TwoNightMenu>();
                Assert.IsTrue(menu.ContinueButton.interactable, menu.StatusText.text);
                menu.ContinueButton.onClick.Invoke();
                yield return WaitScene(TwoNightScenes.Robot);
                for (int i = 0; i < 5; i++) yield return null;
                AssertSingles();
                var director = UnityEngine.Object.FindFirstObjectByType<TwoNightRobotDirector>();
                var flow = director.Flow; var dock = flow.Dock;
                var s = TwoNightRun.Current;
                Assert.AreEqual(2, s.night);
                Assert.AreEqual(TwoNightPhase.Night2Open, s.phase);
                Assert.AreEqual(800, s.Cash, "继续不加钱（第 " + (round + 1) + " 次）");
                StringAssert.Contains("现金 <b>800</b>", director.HudText.text);
                StringAssert.Contains("还差 400", director.HudText.text);
                Assert.AreEqual(DockState.RotorsStopped, dock.State, "已停靠夹紧、断电、停稳");
                Assert.IsFalse(dock.PowerOn);
                Assert.AreEqual(0f, dock.Rotors.SpeedDegPerSec);
                Assert.Less((dock.RobotRoot.position - dock.RobotAnchor.position).magnitude, 1e-5f);
                Assert.AreEqual(0f, dock.ClampOpenFraction);
                Assert.AreEqual(FoStep.InspectLeftEngine, flow.Step, "从安全检查阶段开始");
                Assert.IsFalse(flow.InspectionLocked);
                Assert.IsFalse(flow.ClogCleared, "进气口还堵着");
                Assert.AreEqual(PartLocation.Installed, flow.Bearing.Location, "旧轴承还在");
                Assert.IsFalse(flow.BearingReplaced);
                Assert.IsFalse(director.Incident.Playing, "第二晚不重放事故");
                var view = UnityEngine.Object.FindFirstObjectByType<SliceView>();
                Assert.IsTrue(view.ManualOpen, "第二晚开场打开入口手册");
                StringAssert.DoesNotContain("磨损", view.ManualTitleText + view.ManualBodyText);
                // 可以开始检查：点左上盖 = 开始检查（与鼠标同一入口）
                var input = UnityEngine.Object.FindFirstObjectByType<FirstOrderInput>();
                var drv = new FirstOrderAcceptanceDriver(flow, input);
                view.ToggleManual();
                yield return Click(drv, flow, input, FirstOrderCameraRig.EngineL, flow.Cover, true);
                Assert.AreEqual(FoStep.ReleaseLatches, flow.Step);
                Assert.AreEqual(before, File.ReadAllText(TwoNightSave.FilePath), "继续不改存档");
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Menu_CorruptSave_ContinueDisabled_RestartDeletesOnlyCheckpoint()
        {
            File.WriteAllText(TwoNightSave.FilePath, "{ broken");
            var other = Path.Combine(dir, "keep_me.txt");
            File.WriteAllText(other, "x");
            yield return Load(TwoNightScenes.Menu);
            var menu = UnityEngine.Object.FindFirstObjectByType<TwoNightMenu>();
            Assert.IsFalse(menu.ContinueButton.interactable);
            StringAssert.Contains("存档不能用", menu.StatusText.text);
            menu.RestartButton.onClick.Invoke();
            Assert.IsTrue(File.Exists(TwoNightSave.FilePath), "确认前不删");
            menu.ConfirmYes.onClick.Invoke();
            Assert.IsFalse(File.Exists(TwoNightSave.FilePath));
            Assert.IsTrue(File.Exists(other), "不删其它文件");
            yield return WaitScene(TwoNightScenes.Counter);
            Assert.AreEqual(500, TwoNightRun.Current.Cash);
            Assert.AreEqual(TwoNightPhase.Night1Counter, TwoNightRun.Current.phase);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Menu_NoSave_ContinueDisabled_NewGameStartsCounter()
        {
            yield return Load(TwoNightScenes.Menu);
            var menu = UnityEngine.Object.FindFirstObjectByType<TwoNightMenu>();
            Assert.IsFalse(menu.ContinueButton.interactable);
            StringAssert.Contains("没有存档", menu.StatusText.text);
            menu.NewButton.onClick.Invoke();
            yield return WaitScene(TwoNightScenes.Counter);
            Assert.AreEqual(TwoNightPhase.Night1Counter, TwoNightRun.Current.phase);
            AssertSingles();
            LogAssert.NoUnexpectedReceived();
        }
    }
}
