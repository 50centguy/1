using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BorderRepair.TwoNight;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.UI.Tests
{
    // The arm replacement on the strapped-in patient, in the close-up surgery view: clicking his arm in the room opens
    // the close camera and the bench-style workspace; every precondition refuses with its reason; the operation runs
    // part by part (cover screws, cover, battery plug, neural plug, his tape, four clamp bolts, the forearm onto the
    // tray, the socket, the replacement, bolts and plug back, three link tests, confirmation), each with its gantry
    // tool, by the guide's suggested step through the panel, a tool click on the part, and the bolt batches; parts
    // are picked under the pointer; he cannot be let go mid-operation; leaving restores the room; he walks out with
    // the new arm. Screenshots with the workspace (surgery-op-NN-*.png) with BORDER_REPAIR_CYBER_SHOTS.
    public sealed class SurgeryProcedureTests
    {
        const string ScenePath = "Assets/RepairProps/Scenes/RepairProps_Clinic.unity";
        static readonly string[] Path =
        {
            "scan", "remove.CoverScrew_01", "remove.CoverScrew_02", "cover.remove", "battery.isolate", "neural.release", "tape.cut",
            "remove.ClampBolt_01", "remove.ClampBolt_02", "remove.ClampBolt_03", "remove.ClampBolt_04", "forearm.remove", "socket.clean",
            "forearm.fit", "install.ClampBolt_01", "install.ClampBolt_02", "install.ClampBolt_03", "install.ClampBolt_04",
            "neural.connect", "test.link", "test.link", "test.link", "complete",
        };
        static string Folder => Environment.GetEnvironmentVariable("BORDER_REPAIR_CYBER_SHOTS");
        TwoNightState previousRun;
        string previousSave, isolatedSave;
        Scene loaded;
        int shot;
#if UNITY_EDITOR
        UiTestGameView gameView;
#endif

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousRun = TwoNightRun.Current; previousSave = TwoNightSave.OverrideDirectory;
            isolatedSave = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SurgeryProcedure_" + Guid.NewGuid().ToString("N"));
            TwoNightSave.OverrideDirectory = isolatedSave;
            TwoNightRun.Clear();
            Time.timeScale = 1;
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("Requires the authored editor scene.");
            yield break;
#endif
            loaded = SceneManager.GetSceneByPath(ScenePath);
            UnifiedClinicDirector clinic = null;
            yield return Until(() => (clinic = Object.FindFirstObjectByType<UnifiedClinicDirector>()) != null && clinic.Initialized, "Clinic must initialize.");
            yield return Until(() => SurgeryTableV2Runtime.Current != null && SurgeryTableV2Runtime.Current.Table != null && SurgeryPatientRuntime.Current != null
                && SurgeryProcedureRuntime.Current != null && SurgeryCloseupRuntime.Current != null && SurgeryCloseupRuntime.Current.Panel != null
                && SurgeryGantryV2Runtime.Current != null && SurgeryGantryV2Runtime.Current.Gantry != null, "Surgery runtimes exist.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (loaded.IsValid() && loaded.isLoaded)
            {
                SceneManager.SetActiveScene(SceneManager.CreateScene("ProcedureCleanup_" + Guid.NewGuid().ToString("N")));
                yield return SceneManager.UnloadSceneAsync(loaded);
            }
#if UNITY_EDITOR
            gameView?.Dispose(); gameView = null;
#endif
            TwoNightRun.Set(previousRun); TwoNightSave.OverrideDirectory = previousSave;
            if (Directory.Exists(isolatedSave)) Directory.Delete(isolatedSave, true);
        }

        [UnityTest]
        public IEnumerator CloseupSurgery_TakesTheLockedArmApart_PartByPart_AndHeLeavesWithTheNewOne()
        {
#if UNITY_EDITOR
            gameView = new UiTestGameView(); gameView.Set(1280, 720);
#endif
            var clinic = Object.FindFirstObjectByType<UnifiedClinicDirector>();
            var room = clinic.Robot.Flow.Rig.Cam;
            var walker = clinic.Robot.Flow.Rig.Walker;
            var table = SurgeryTableV2Runtime.Current;
            var gantry = SurgeryGantryV2Runtime.Current;
            var patient = SurgeryPatientRuntime.Current;
            var op = SurgeryProcedureRuntime.Current;
            var closeup = SurgeryCloseupRuntime.Current;
            var panel = closeup.Panel;
            var controls = table.Controls.ToList();
            var straps = controls.Where(c => c.Kind == SurgeryControlKind.Strap).ToList();
            if (ClinicCallRuntime.Current != null) ClinicCallRuntime.Current.enabled = false;   // keeps the landlord's call off the shots
            CollectionAssert.IsSubsetOf(op.Commands.Where(c => c.tool != null).Select(c => c.tool).Distinct(), gantry.Tools.ToList(), "Every command's tool is on the gantry.");
            CollectionAssert.AreEquivalent(SurgeryProcedureRuntime.ToolOrder, op.Commands.Where(c => c.tool != null).Select(c => c.tool).Distinct(), "The operation uses all six gantry arms.");

            // Patient in and strapped down (chair pose).
            table.Activate(controls.First(c => c.Kind == SurgeryControlKind.Pendant));
            Assert.That(patient.Begin(), Is.True);
            yield return Until(() => patient.PhaseNow == SurgeryPatientRuntime.Phase.WaitClear, "Waits by the buckled table.", 20);
            foreach (var s in straps) table.Activate(s);
            yield return Until(() => patient.PhaseNow == SurgeryPatientRuntime.Phase.AwaitStraps, "Lies down.", 20);
            foreach (var s in straps) table.Activate(s);
            yield return Until(() => patient.PhaseNow == SurgeryPatientRuntime.Phase.Secured, "Secured.");
            var oldArm = patient.CyberArm;
            Assert.That(oldArm.name, Is.EqualTo("CyberArm_Leased"));
            Assert.That(SurgeryCyberArm.GetCurl(oldArm), Is.EqualTo(SurgeryCyberArm.LockedCurl), "The locked hand is frozen half-closed.");
            Assert.That(op.Part("Socket").gameObject.activeSelf, Is.False, "The socket's contacts are inside the forearm.");

            // The crosshair finds the surgery site on his right forearm; the prompt offers the close-up.
            Assert.That(patient.Site.activeSelf, Is.True, "The surgery site appears once he is secured.");
            Physics.SyncTransforms();
            var sitePos = patient.Site.transform.position;
            var eye = sitePos - table.Right * .7f + Vector3.up * .75f;
            var site = SurgeryTableV2Runtime.ControlAlong(new Ray(eye, sitePos - eye), 2f);
            Assert.That(site != null && site.Kind == SurgeryControlKind.Patient, Is.True, "Aiming at his forearm finds the surgery site (found " + (site != null ? site.name : "nothing") + ").");
            Assert.That(site.Hint, Does.Contain("罗亚的右臂").And.Contain("近景手术"));
            Assert.That(patient.Hint, Does.Contain("近景手术"));

            // Clicking it opens the close-up: its own camera, the workspace, the pointer free, the room HUD away.
            if (gantry.LampOn) gantry.Activate(SurgeryControlKind.Lamp);   // starts on: switched off to see that check too
            table.Activate(site);
            Assert.That(closeup.Active, Is.True, "The click opens the close-up.");
            Assert.That(closeup.View.enabled && !room.enabled, Is.True, "The close-up camera replaces the room camera.");
            Assert.That(SurgeryArmPart.Pickable, Is.True);
            yield return null;
            closeup.Snap();
            yield return null;
            Assert.That(panel.Visible, Is.True, "The workspace is up.");
            Assert.That(closeup.Overlay.Showing, Is.True, "The visor layer is up.");
            Assert.That(walker.Aiming, Is.False, "The pointer is free while operating.");
            Assert.That(clinic.transform.Find("UnifiedClinicTradeUI").gameObject.activeSelf, Is.False, "The room HUD gives way.");
            Assert.That(patient.Hint, Is.Null, "The room hint gives way to the guide.");
            Assert.That(Vector3.Distance(closeup.View.transform.position, op.TargetPoint(SurgeryProcedureRuntime.Whole)), Is.LessThan(.75f), "The camera is close to the arm.");
            yield return Shoot("open");

            // Preconditions refuse with their reason; the guide suggests the switch that is missing.
            var scan = op.Find("scan");
            Assert.That(op.Why(scan), Is.EqualTo("先打开手术灯"));
            Assert.That(op.Next().id, Is.EqualTo("lamp.on"));
            yield return null;
            Assert.That(panel.ReasonShown("先打开手术灯"), Is.True, "The panel says why the scan cannot run.");
            Assert.That(op.Run("scan"), Is.False);
            panel.CommandButton("lamp.on").onClick.Invoke();
            Assert.That(gantry.LampOn, Is.True);
            Assert.That(op.Why(scan), Is.EqualTo("先展开机械臂"));
            yield return null;
            panel.CommandButton(op.Next().id).onClick.Invoke();
            Assert.That(gantry.ArmsDeployed, Is.True);
            yield return Until(() => gantry.DeployAmount >= .98f, "Arms deployed.");
            Assert.That(op.Why(scan), Is.Null);
            yield return null;
            Assert.That(closeup.Overlay.HintText, Does.Contain("下一步").And.Contain("扫描右臂义体"));
            Assert.That(panel.StatusText, Is.EqualTo(op.NextStepText));

            // Picking: the part under a screen point, a screw over the cover, the cover over the forearm.
            Assert.That(PickAt(closeup, op, "ClampBolt_02"), Is.EqualTo("ClampBolt_02"));
            Assert.That(PickAt(closeup, op, "CoverScrew_01"), Is.EqualTo("CoverScrew_01"));
            Assert.That(PickAt(closeup, op, "ServiceCover"), Is.EqualTo("ServiceCover"));
            Assert.That(PickAt(closeup, op, "ClampTape"), Is.EqualTo("ClampTape"), "The tape over bolt 3 is what the pointer meets.");
            Assert.That(closeup.Pick(closeup.View.WorldToScreenPoint(oldArm.TransformPoint(new Vector3(.038f, -.215f, 0)))), Is.EqualTo(SurgeryCyberArm.Old));
            // The visor inspects what it clicks: his tape.
            closeup.SelectTool(null);
            closeup.Click(closeup.View.WorldToScreenPoint(op.TargetPoint("ClampTape")));
            Assert.That(closeup.Selected, Is.EqualTo("ClampTape"));
            Assert.That(op.LastMessage, Does.Contain("吊机 3 组"));
            yield return Shoot("ready");

            var done = new List<string>();
            var suggested = new List<string>();
            op.StepDone += c => done.Add(c.id);
            bool usedBatch = false;
            for (int guard = 0; !op.Complete && guard < 80; guard++)
            {
                var next = op.Next();
                Assert.That(next, Is.Not.Null, "The guide always has a next step until the end (" + op.NextStepText + ").");
                suggested.Add(next.id);
                if (next.id == "scan")
                {
                    // A tool aimed anywhere on the arm does the whole arm's work.
                    closeup.SelectTool("Scanner");
                    closeup.Click(closeup.View.WorldToScreenPoint(op.TargetPoint(SurgeryProcedureRuntime.Whole)));
                }
                else if (next.id == "remove.CoverScrew_01")
                {
                    // The wrong tool says so; the drill on the screw unscrews it.
                    closeup.SelectTool("Gripper");
                    closeup.Click(closeup.View.WorldToScreenPoint(op.TargetPoint("CoverScrew_01")));
                    Assert.That(op.Running, Is.False);
                    Assert.That(op.LastMessage, Does.Contain("用不上夹爪臂"));
                    closeup.SelectTool("Drill");
                    closeup.Click(closeup.View.WorldToScreenPoint(op.TargetPoint("CoverScrew_01")));
                }
                else
                {
                    if (!next.table && next.target != SurgeryProcedureRuntime.Whole) closeup.Select(next.target);   // the part first, as a player does
                    yield return null;
                    var batch = panel.BatchButtonFor(next.id);
                    if (batch != null && next.id.Contains("ClampBolt")) { usedBatch = true; batch.onClick.Invoke(); }
                    else
                    {
                        var button = panel.CommandButton(next.id);
                        Assert.That(button != null && button.interactable, Is.True, next.id + ": the suggested step is a live button on the panel.");
                        button.onClick.Invoke();
                    }
                }
                if (next.table) { yield return Wait(.7f); continue; }
                if (next.seconds <= 0) { Assert.That(op.IsDone(next), Is.True, next.id + " is instant."); continue; }
                Assert.That(op.Running, Is.True, next.id + " starts.");
                Assert.That(op.Run(next.id), Is.False, "A second command mid-step is refused.");
                yield return Wait(op.RunningCommand.seconds * .55f);
                if (next.id == "scan" || next.id == "cover.remove" || next.id == "forearm.remove" || next.id == "forearm.fit" || next.id == "test.link" && op.LinkResults.Count == 0)
                    yield return Shoot(next.id + "-working");
                yield return Until(() => !op.Running && !op.Queued, next.id + " finishes.", 20);
                if (next.id.StartsWith("remove.ClampBolt") && SurgeryCyberArm.Bolts.All(b => op.Get("out." + b) == 1)) yield return Shoot("bolts-out");

                switch (next.id)
                {
                    case "scan":
                        Assert.That(op.NewArm != null && op.NewArm.parent == table.Tray, Is.True, "The replacement waits on the tray.");
                        Assert.That(op.LastResult, Does.Contain("租赁锁"));
                        break;
                    case "cover.remove":
                        Assert.That(op.Part("ServiceCover").gameObject.activeSelf, Is.False, "The cover is off (in the parts bin).");
                        Assert.That(op.Part("LeaseLock").Exposed, Is.True, "The lock module is open to see.");
                        Assert.That(op.Why(op.Find("neural.release")), Is.EqualTo("先拔下电池服务插头：带电拔神经接头会烧触点"));
                        yield return Shoot("bay-open");
                        break;
                    case "battery.isolate":
                        Assert.That(Near(LampColor(oldArm), SurgeryCyberArm.LampDark), Is.True, "The arm went dead: lamp off.");
                        break;
                    case "neural.release":
                        Assert.That(op.Why(op.Find("remove.ClampBolt_03")), Is.EqualTo("螺栓3缠着胶布：先用切割臂切开"));
                        Assert.That(op.Why(op.Find("remove.ClampBolt_01")), Is.Null);
                        break;
                    case "forearm.remove":
                        Assert.That(patient.CyberArm, Is.Null, "Old forearm off.");
                        Assert.That(oldArm.parent, Is.SameAs(table.Tray), "Old forearm on the tray.");
                        Assert.That(op.Part("Socket").gameObject.activeSelf, Is.True, "The socket's contacts are bare.");
                        Assert.That(suggested[suggested.Count - 2], Is.EqualTo("tray.out"), "The guide swung the tray out first.");
                        yield return Wait(1.2f);   // the view eases back from the tray to the socket
                        yield return Shoot("socket-bare");
                        // He cannot be let go with his arm off: opening every strap keeps him on the table.
                        foreach (var s in straps) table.Activate(s);
                        yield return Wait(.5f);
                        Assert.That(patient.PhaseNow, Is.EqualTo(SurgeryPatientRuntime.Phase.Secured), "Stays on the table mid-operation.");
                        Assert.That(patient.Release(), Is.False);
                        Assert.That(closeup.Active, Is.True);
                        Assert.That(op.Why(op.Find("socket.clean")), Is.EqualTo("先扣上全部束带"));
                        Assert.That(op.Next().id, Is.EqualTo("straps.close"));
                        break;
                    case "forearm.fit":
                        Assert.That(patient.CyberArm, Is.SameAs(op.NewArm), "Replacement fitted.");
                        Assert.That(op.NewArm.parent, Is.SameAs(patient.BoneTransform(SurgeryPatientRuntime.Bone.RightLowerArm)));
                        Assert.That(Vector3.Distance(op.NewArm.position, patient.BoneTransform(SurgeryPatientRuntime.Bone.RightLowerArm).position), Is.LessThan(.001f));
                        Assert.That(op.Part("Socket").gameObject.activeSelf, Is.False);
                        break;
                    case "test.link":
                        Assert.That(op.LastResult, Does.StartWith("联调 " + op.LinkResults.Count + "/3"));
                        if (op.LinkResults.Count == 3) yield return Shoot("linked");
                        break;
                }
            }
            CollectionAssert.AreEqual(Path, done, "The operation ran part by part in order.");
            Assert.That(usedBatch, Is.True, "The bolt sets went as batches.");
            Assert.That(op.Complete, Is.True);
            Assert.That(op.Holding, Is.False);
            Assert.That(Near(LampColor(op.NewArm), SurgeryCyberArm.LampGreen), Is.True, "Linked: green.");
            Assert.That(patient.RightHandCurl, Is.Zero, "Hand open again after the calibration fists.");
            Assert.That(SurgeryCyberArm.Bolts.All(b => op.Part(b).gameObject.activeSelf), Is.True, "All four bolts are back.");
            Assert.That(op.Part("NeuralPlug").transform.localPosition, Is.EqualTo(SurgeryCyberArm.PlugSeated), "The plug is seated in the new arm.");
            yield return null;
            Assert.That(panel.StatusText, Does.Contain("手术完成"));
            panel.ShowTab(SurgeryWorkspaceTab.Records);
            yield return null;
            yield return Shoot("records");
            panel.ShowTab(SurgeryWorkspaceTab.Operate);

            // Leaving restores the room.
            panel.LeaveButton.onClick.Invoke();
            Assert.That(closeup.Active, Is.False);
            Assert.That(room.enabled && !closeup.View.enabled, Is.True, "Back to the room camera.");
            Assert.That(SurgeryArmPart.Pickable, Is.False);
            Assert.That(clinic.transform.Find("UnifiedClinicTradeUI").gameObject.activeSelf, Is.True, "The room HUD is back.");
            yield return null;
            Assert.That(panel.Visible, Is.False);
            Assert.That(patient.Hint, Does.Contain("手术完成"));

            // Let him go: he gets up and leaves with the new arm; the old one stays on the tray.
            var newArm = op.NewArm;
            foreach (var s in straps) table.Activate(s);
            yield return Until(() => patient.PhaseNow == SurgeryPatientRuntime.Phase.SitUp, "Released after the operation.");
            yield return Until(() => patient.PhaseNow == SurgeryPatientRuntime.Phase.Idle, "Leaves.", 25);
            Assert.That(newArm.IsChildOf(patient.Patient.transform), Is.True, "He took the new arm with him.");
            Assert.That(oldArm.parent, Is.SameAs(table.Tray), "The locked arm stays on the tray.");
        }

        static string PickAt(SurgeryCloseupRuntime closeup, SurgeryProcedureRuntime op, string id) =>
            closeup.Pick(closeup.View.WorldToScreenPoint(op.TargetPoint(id)));

        static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) < .002f && Mathf.Abs(a.g - b.g) < .002f && Mathf.Abs(a.b - b.b) < .002f;

        static Color LampColor(Transform arm)
        {
            var lamp = arm.GetComponentsInChildren<Renderer>(true).First(r => r.name == "Lamp");
            var block = new MaterialPropertyBlock();
            lamp.GetPropertyBlock(block);
            return block.GetColor("_BaseColor");
        }

        IEnumerator Shoot(string name)
        {
            yield return null;
            if (string.IsNullOrEmpty(Folder)) yield break;
            Directory.CreateDirectory(Folder);
            yield return Wait(.4f);   // let the workspace's text type in
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Folder, string.Format("surgery-op-{0:00}-{1}.png", shot++, name)));
            yield return null;
        }

        static IEnumerator Wait(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }

        static IEnumerator Until(Func<bool> ready, string reason, float timeout = 20)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!ready() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(ready(), Is.True, reason);
        }
    }
}
