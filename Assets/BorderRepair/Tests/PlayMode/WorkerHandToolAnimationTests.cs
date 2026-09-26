using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BorderRepair.Core;
using BorderRepair.Data;
using BorderRepair.Inspection;
using BorderRepair.Narrative;
using BorderRepair.Tools;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Cue = BorderRepair.Narrative.RepairSoundSynth.Cue;

namespace BorderRepair.Tests
{
    static class ToolAnimUtil
    {
        public static IEnumerator WaitIdle(RepairToolAnimator anim, float timeout = 6f)
        {
            float t = 0f;
            while (anim.Busy && t < timeout) { t += Time.deltaTime; yield return null; }
            Assert.IsFalse(anim.Busy, "工具动画没有在限定时间内结束");
        }

        public static IEnumerator WaitPhase(RepairToolAnimator anim, RepairToolAnimator.Phase phase, float timeout = 4f)
        {
            float t = 0f;
            while (anim.CurrentPhase != phase && t < timeout) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(phase, anim.CurrentPhase, $"没有进入 {phase} 阶段");
        }

        /// <summary>按剧情用工具动画走到“可以读日志”之前（外壳已开、限力器已测）。</summary>
        public static IEnumerator OpenAndTestLimiter(RepairStationController c)
        {
            var anim = c.ToolAnimator;
            c.SelectTool(RepairActionType.RemoveFastener);
            Assert.IsTrue(c.ClickPoint("fastener_a"));
            yield return WaitIdle(anim);
            Assert.IsTrue(c.ClickPoint("fastener_b"));
            yield return WaitIdle(anim);
            c.SelectTool(RepairActionType.OpenHousing);
            Assert.IsTrue(c.ClickPoint("shell"));
            yield return WaitIdle(anim);
            c.SelectTool(RepairActionType.ServiceModule);
            Assert.IsTrue(c.ClickPoint("force_limiter"));
            yield return WaitIdle(anim);
        }
    }

    /// <summary>
    /// 工具动画：进入 → 对准 → 接触操作 → 退出；步骤只在接触时刻由 RepairSession 判定执行；
    /// 工具尖端对准零件上的锚点（物品旋转、缩放后仍对齐）；错误工具、重复单击、中断和重新开始时状态正确。
    /// </summary>
    public class WorkerHandToolAnimationTests
    {
        RepairStationController controller;
        RepairToolAnimator anim;
        RepairSession s;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            yield return WorkerHandTestUtil.Load(WorkerHandTestUtil.NarrativeScene, c => controller = c);
            anim = controller.ToolAnimator;
            Assert.IsNotNull(anim, "叙事场景应有工具动画");
            s = controller.Session;
            s.AcceptItem();
            yield return null;
        }

        [UnityTest]
        public IEnumerator StepIsPerformedOnlyAtContactAndPhasesRunInOrder()
        {
            var phases = new List<RepairToolAnimator.Phase>();
            anim.PhaseChanged += p => phases.Add(p);
            var fx = Object.FindFirstObjectByType<RepairFeedbackFx>();
            int cuesBefore = fx.CueLog.Count;
            bool stepDoneAtContactStart = true;
            anim.PhaseChanged += p => { if (p == RepairToolAnimator.Phase.Contact) stepDoneAtContactStart = s.IsStepDone("remove_fastener_a"); };

            controller.SelectTool(RepairActionType.RemoveFastener);
            Assert.IsTrue(controller.ClickPoint("fastener_a"), "动作成立时应播放工具动画");
            Assert.IsTrue(anim.Busy);
            Assert.IsTrue(controller.InputLocked);
            Assert.IsFalse(s.IsStepDone("remove_fastener_a"), "单击时还不能执行步骤");
            Assert.IsTrue(anim.ActiveRig.gameObject.activeSelf, "应显示螺丝刀和手套");
            Assert.AreEqual(ToolKind.Screwdriver, anim.ActiveKind);

            yield return ToolAnimUtil.WaitPhase(anim, RepairToolAnimator.Phase.Align);
            Assert.IsFalse(s.IsStepDone("remove_fastener_a"), "对准阶段还没有接触");
            Assert.AreEqual(cuesBefore, fx.CueLog.Count, "接触之前不应有螺丝声音 / 动作");
            Assert.IsTrue(controller.TryGetPart("fastener_a", out var screw));
            Assert.IsFalse(screw.IsPlayingExit, "接触之前螺丝不动");

            float t = 0f;
            while (anim.LastContactOutcome == null && t < 3f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(ActionOutcome.Performed, anim.LastContactOutcome);
            Assert.IsFalse(stepDoneAtContactStart, "进入接触阶段时（压入之前）还没有执行");
            Assert.IsTrue(s.IsStepDone("remove_fastener_a"), "接触时刻由会话执行步骤");
            Assert.IsTrue(screw.IsPlayingExit, "螺丝与螺丝刀同时开始转出");
            CollectionAssert.Contains(fx.CueLog, "Unscrew@fastener_a", "接触时刻播放拧螺丝声音");

            yield return ToolAnimUtil.WaitIdle(anim);
            CollectionAssert.AreEqual(new[] { RepairToolAnimator.Phase.Enter, RepairToolAnimator.Phase.Align, RepairToolAnimator.Phase.Contact, RepairToolAnimator.Phase.Exit, RepairToolAnimator.Phase.Idle }, phases);
            Assert.IsFalse(controller.InputLocked, "动画结束后恢复输入");
            Assert.IsFalse(anim.transform.GetComponentsInChildren<ToolRig>(false).Any(), "动画结束后收起工具");
        }

        [UnityTest]
        public IEnumerator ToolTipMeetsAnchorEvenAfterItemRotatesAndRescales()
        {
            var insp = controller.Inspector;
            var item = insp.CurrentItem.transform;
            item.localScale *= 0.8f;                                    // 模拟不同的归一化缩放
            var checks = new (RepairActionType action, string point, bool rotateMidway)[]
            {
                (RepairActionType.RemoveFastener, "fastener_a", true),
                (RepairActionType.RemoveFastener, "fastener_b", false),
                (RepairActionType.OpenHousing, "shell", true),
                (RepairActionType.ServiceModule, "force_limiter", true),
                (RepairActionType.ServiceModule, "data_port", false),
            };
            foreach (var (action, pointId, rotate) in checks)
            {
                var original = anim.FindAnchor(action, pointId);
                Assert.IsNotNull(original, $"{pointId} 没有锚点");
                Vector3 tipAtContact = default, anchorAtContact = default, axisAtContact = default, anchorUp = default;
                float scaleAtContact = 0f, rigScale = 0f;
                void OnStep(RepairStepDefinition step)
                {
                    var rig = anim.ActiveRig;
                    tipAtContact = rig.transform.position;
                    axisAtContact = rig.transform.up;
                    rigScale = rig.transform.lossyScale.x;
                    anchorAtContact = original.transform.position;
                    anchorUp = original.transform.up;
                    scaleAtContact = original.WorldScale;
                }
                s.StepPerformed += OnStep;
                controller.SelectTool(action);
                Assert.IsTrue(controller.ClickPoint(pointId), $"{pointId} 应播放动画");
                if (rotate)
                {
                    yield return ToolAnimUtil.WaitPhase(anim, RepairToolAnimator.Phase.Align);
                    insp.Rotate(new Vector2(25f, -12f));                // 动画中途玩家旋转物品
                }
                yield return ToolAnimUtil.WaitIdle(anim);
                s.StepPerformed -= OnStep;

                Assert.Greater(scaleAtContact, 0f, $"{pointId} 没有在接触时刻执行");
                float depth = 0.0045f * scaleAtContact;                   // 压入深度最大为插头 4 mm
                Assert.Less(Vector3.Distance(tipAtContact, anchorAtContact), depth + 0.0005f, $"{pointId}：工具作用端没有对准锚点");
                Assert.Greater(Vector3.Dot(axisAtContact, anchorUp), 0.999f, $"{pointId}：工具轴线没有对准");
                Assert.AreEqual(scaleAtContact, rigScale, 1e-4f, $"{pointId}：工具应与物品同比例缩放");
            }
        }

        [UnityTest]
        public IEnumerator WrongToolOrBlockedStepDoesNotAnimateOrChangeTheModel()
        {
            var fx = Object.FindFirstObjectByType<RepairFeedbackFx>();
            Assert.IsTrue(controller.TryGetPart("shell", out var shell));
            var shellPos = shell.transform.localPosition;

            controller.SelectTool(RepairActionType.OpenHousing);
            Assert.IsFalse(controller.ClickPoint("shell"), "螺丝还在：不应播放撬开动画");
            Assert.IsFalse(anim.Busy);
            Assert.AreEqual(RepairPartState.Installed, shell.State);
            Assert.AreEqual(shellPos, shell.transform.localPosition);
            Assert.AreEqual(Cue.Deny, fx.LastCue, "被拒绝时照常给出提示音");

            controller.SelectTool(RepairActionType.RemoveFastener);
            Assert.IsFalse(controller.ClickPoint("force_limiter"), "工具不对：不播放动画");
            Assert.IsFalse(anim.Busy);
            Assert.IsFalse(anim.transform.GetComponentsInChildren<ToolRig>(false).Any(), "不应显示工具");
            Assert.AreEqual(2, s.CurrentRecord.InvalidActionCount, "失误仍按原规则计数");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ClicksDuringAnimationAreIgnored()
        {
            controller.SelectTool(RepairActionType.RemoveFastener);
            Assert.IsTrue(controller.ClickPoint("fastener_a"));
            yield return null;
            Assert.IsFalse(controller.ClickPoint("fastener_a"), "动画进行中重复单击不应再次触发");
            Assert.IsFalse(controller.ClickPoint("fastener_b"));
            controller.SelectTool(RepairActionType.OpenHousing);
            Assert.AreEqual(RepairActionType.RemoveFastener, controller.ActiveTool, "动画进行中不能换工具");
            yield return ToolAnimUtil.WaitIdle(anim);
            Assert.IsTrue(s.IsStepDone("remove_fastener_a"));
            Assert.IsFalse(s.IsStepDone("remove_fastener_b"), "被忽略的单击不应执行");
            Assert.AreEqual(0, s.CurrentRecord.InvalidActionCount);
            Assert.IsTrue(controller.ClickPoint("fastener_b"), "动画结束后可以继续操作");
            yield return ToolAnimUtil.WaitIdle(anim);
            Assert.IsTrue(s.IsStepDone("remove_fastener_b"));
        }

        [UnityTest]
        public IEnumerator CancelBeforeContactChangesNothing()
        {
            Assert.IsTrue(controller.TryGetPart("fastener_a", out var screw));
            var home = screw.transform.localPosition;
            controller.SelectTool(RepairActionType.RemoveFastener);
            Assert.IsTrue(controller.ClickPoint("fastener_a"));
            yield return ToolAnimUtil.WaitPhase(anim, RepairToolAnimator.Phase.Align);
            anim.Cancel();
            Assert.IsFalse(anim.Busy);
            Assert.IsFalse(controller.InputLocked);
            Assert.IsFalse(anim.transform.GetComponentsInChildren<ToolRig>(false).Any(), "中断后收起工具");
            yield return new WaitForSeconds(0.3f);
            Assert.IsFalse(s.IsStepDone("remove_fastener_a"), "接触前中断，不执行步骤");
            Assert.AreEqual(RepairPartState.Installed, screw.State);
            Assert.AreEqual(home, screw.transform.localPosition, "螺丝不动");
            Assert.IsTrue(controller.TryGetPart("lease_seal", out var seal));
            Assert.AreEqual(RepairPartState.Installed, seal.State, "封条没有撕开");
            Assert.AreEqual(0, controller.Inspector.CurrentItem.GetComponentsInChildren<ToolAnchor>(true).Count(a => a.name == "ToolAnchor_Active"), "临时锚点应清理");
        }

        [UnityTest]
        public IEnumerator RestartDuringAnimationResetsEverything()
        {
            controller.SelectTool(RepairActionType.RemoveFastener);
            Assert.IsTrue(controller.ClickPoint("fastener_a"));
            float t = 0f;
            while (anim.LastContactOutcome == null && t < 3f) { t += Time.deltaTime; yield return null; }
            Assert.IsTrue(s.IsStepDone("remove_fastener_a"), "前提：已经接触并执行");
            s.Restart();                                                 // 接触之后重新开始
            yield return null;
            Assert.IsFalse(anim.Busy, "重新开始应中断工具动画");
            Assert.IsFalse(anim.transform.GetComponentsInChildren<ToolRig>(false).Any());
            Assert.AreEqual(RepairStage.Intake, s.Stage);
            Assert.IsFalse(s.IsStepDone("remove_fastener_a"), "新的一轮没有做过任何步骤");
            Assert.IsTrue(controller.TryGetPart("fastener_a", out var screw));
            Assert.AreEqual(RepairPartState.Installed, screw.State, "新放上的物品零件都在原位");
            Assert.IsFalse(screw.IsPlayingExit);
            Assert.IsTrue(controller.TryGetPart("lease_seal", out var seal));
            Assert.AreEqual(RepairPartState.Installed, seal.State);
            s.AcceptItem();
            controller.SelectTool(RepairActionType.RemoveFastener);
            Assert.IsTrue(controller.ClickPoint("fastener_a"), "重新开始后可以正常操作");
            yield return ToolAnimUtil.WaitIdle(anim);
            Assert.IsTrue(s.IsStepDone("remove_fastener_a"));
        }

        [UnityTest]
        public IEnumerator FramingHoldsWhileToolIsWorking()
        {
            var framing = Object.FindFirstObjectByType<NarrativeStageFraming>();
            controller.SelectTool(RepairActionType.RemoveFastener);
            yield return new WaitForSeconds(0.5f);
            string shot = framing.CurrentShotId;
            Assert.AreEqual("fastener_a", shot);
            Assert.IsTrue(controller.ClickPoint("fastener_a"));
            var seen = new HashSet<string>();
            while (anim.Busy) { seen.Add(framing.CurrentShotId); yield return null; }
            CollectionAssert.AreEquivalent(new[] { "fastener_a" }, seen, "工具动画进行中镜头不应换到别的零件");
            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual("fastener_b", framing.CurrentShotId, "动画结束后按原规则转向下一个零件");
        }

        [UnityTest]
        public IEnumerator FullInvestigationWithAnimationsReachesDecision()
        {
            s.SetScanMode(true);
            controller.ScanPoint("shell");
            yield return ToolAnimUtil.OpenAndTestLimiter(controller);
            Assert.IsTrue(s.HasClue("limiter_disabled"));
            var tester = Object.FindFirstObjectByType<TesterReadout>();
            Assert.AreEqual(TesterReadout.Reading.Bypass, tester.Current, "探针接触限力器后，检测仪显示旁路读数");
            Assert.IsTrue(controller.TryGetPart("force_limiter", out var limiter));
            Assert.IsTrue(limiter.TestedIndicator.activeSelf, "红灯亮");
            Assert.IsTrue(controller.ClickPoint("data_port"));
            yield return ToolAnimUtil.WaitIdle(anim);
            Assert.AreEqual(TesterReadout.Reading.Log, tester.Current);
            Assert.IsTrue(s.HasClue("remote_params"));
            Assert.IsTrue(s.TryBeginDiagnosis());
            Assert.AreEqual(RepairStage.Decide, s.Stage);
            yield return new WaitForSeconds(1.6f);
            foreach (var id in new[] { "shell", "fastener_a", "fastener_b" })
            {
                Assert.IsTrue(controller.TryGetPart(id, out var part));
                Assert.Less(Vector3.Distance(part.transform.position, part.SnapTarget.position), 0.001f, $"{id} 应落在零件盘上");
            }
        }

        [UnityTest]
        public IEnumerator CoverRingStaysNearTheBayAndCoverStillPops()
        {
            var fx = Object.FindFirstObjectByType<RepairFeedbackFx>();
            controller.UseTool(RepairActionType.RemoveFastener, "fastener_a");
            controller.UseTool(RepairActionType.RemoveFastener, "fastener_b");
            yield return new WaitForSeconds(1.3f);
            controller.SelectTool(RepairActionType.OpenHousing);
            Assert.IsTrue(controller.ClickPoint("shell"));
            float t = 0f;
            while (anim.LastContactOutcome == null && t < 3f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(ActionOutcome.Performed, anim.LastContactOutcome);
            Assert.AreEqual(Cue.OpenCover, fx.LastCue, "开盖仍有声音反馈");
            Assert.IsTrue(controller.TryGetPart("shell", out var shell));
            Assert.IsTrue(shell.IsPlayingExit, "盖板仍然弹起");
            yield return null;
            float scale = shell.transform.lossyScale.x;
            Assert.LessOrEqual(fx.LastRingMaxRadius, 0.0285f * scale, "开盖扩散环应限制在开舱区域附近");
            var coverSize = PolishUtil.VisibleBounds(WorkerHandTestUtil.Point(controller, "shell")).extents.magnitude;
            Assert.Less(fx.LastRingMaxRadius, coverSize * 0.5f, "环不应接近盖板大小");
            yield return ToolAnimUtil.WaitIdle(anim);
        }

        [UnityTest]
        public IEnumerator LimiterRedLightStaysOnSteadilyAfterProbe()
        {
            s.SetScanMode(true);
            controller.ScanPoint("shell");
            controller.UseTool(RepairActionType.RemoveFastener, "fastener_a");
            controller.UseTool(RepairActionType.RemoveFastener, "fastener_b");
            controller.UseTool(RepairActionType.OpenHousing, "shell");
            yield return new WaitForSeconds(1.5f);
            Assert.IsTrue(controller.TryGetPart("force_limiter", out var limiter));
            controller.SelectTool(RepairActionType.ServiceModule);
            Assert.IsTrue(controller.ClickPoint("force_limiter"));
            float t = 0f;
            while (anim.LastContactOutcome == null && t < 3f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(ActionOutcome.Performed, anim.LastContactOutcome);
            // 接触后 1.5 秒内每一帧红灯都亮着（包括发光增强和工具退出期间）
            for (float k = 0f; k < 1.5f; k += Time.deltaTime)
            {
                Assert.IsTrue(limiter.TestedIndicator.activeInHierarchy, $"红灯在接触后 {k:0.00} 秒熄灭了");
                yield return null;
            }
            Assert.IsTrue(limiter.TestedIndicator.activeInHierarchy);
        }

        [UnityTest]
        public IEnumerator ToolsAndGlovesDoNotPenetrateTheItem()
        {
            var report = new List<string>();
            var item = controller.Inspector.CurrentItem;
            var itemColliders = item.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && c.gameObject.activeInHierarchy).ToList();
            void Check(RepairStepDefinition step)
            {
                var rig = anim.ActiveRig;
                var target = controller.TryGetPoint(step.targetPointId, out var p) ? p : null;
                var contact = anim.ActiveAnchor.ContactPoint;
                float sc = anim.ActiveAnchor.WorldScale;
                int inside = 0, total = 0;
                var where = new Dictionary<string, int>();
                foreach (var mf in rig.GetComponentsInChildren<MeshFilter>(true))
                {
                    var m = mf.transform.localToWorldMatrix;
                    foreach (var v in mf.sharedMesh.vertices)
                    {
                        var w = m.MultiplyPoint3x4(v);
                        total++;
                        if (Vector3.Distance(w, contact) < 0.008f * sc) continue;           // 作用端本来就要接触零件
                        foreach (var c in itemColliders)
                        {
                            if (target != null && c.transform.IsChildOf(target.transform)) continue;
                            if ((c.ClosestPoint(w) - w).sqrMagnitude < 1e-10f)
                            {
                                inside++;
                                string key = $"{mf.name}→{c.name}";
                                where[key] = where.TryGetValue(key, out var n) ? n + 1 : 1;
                                break;
                            }
                        }
                    }
                }
                report.Add($"{step.stepId}: {inside}/{total}" + (where.Count > 0 ? " (" + string.Join(", ", where.Select(kv => $"{kv.Key}×{kv.Value}")) + ")" : ""));
            }
            s.StepPerformed += Check;
            yield return ToolAnimUtil.OpenAndTestLimiter(controller);
            Assert.IsTrue(controller.ClickPoint("data_port"));
            yield return ToolAnimUtil.WaitIdle(anim);
            s.StepPerformed -= Check;
            Debug.Log("[WorkerHandV2] 接触时刻工具 / 手套顶点落在物品碰撞体内的数量：" + string.Join("；", report));
            Assert.AreEqual(5, report.Count);
            foreach (var line in report) StringAssert.StartsWith(line.Split(':')[0] + ": 0/", line, "工具或手套穿进了物品：" + string.Join("；", report));
        }
    }
}
