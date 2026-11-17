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
    // The arm replacement on the strapped-in patient, in the linked close-up view: clicking his arm in the room links
    // into the table (its overhead camera, the info-only workspace, the arm bar); the operation runs part by part
    // (scan, cover screws, cover, battery plug, neural plug, his tape, four clamp bolts, the forearm onto the tray, the
    // socket, the replacement, bolts and plug back, three link passes, confirmation), every step done by hand with
    // its gantry arm as a player does: the arm brought down from the bar, its tip steered by the pointer onto the
    // part (nudged by the miss until it is on), the left button held to work, dragged over the part to cut, clean and
    // scan, loads carried and let go at their place. Off target nothing happens and the miss is said; the wrong arm
    // says which one; a wrong drop is refused. He cannot be let go mid-operation; leaving restores the room; he walks
    // out with the new arm. Screenshots (surgery-op-NN-*.png) with BORDER_REPAIR_CYBER_SHOTS.
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
            Assert.That(site.Hint, Does.Contain("森田的右臂").And.Contain("近景手术"));

            // Clicking it links the mind into the table: its overhead camera on the forearm, the arms under the link's
            // control (all folded up), the pointer free, the room HUD away.
            if (gantry.LampOn) gantry.Activate(SurgeryControlKind.Lamp);   // starts on: switched off to see that check too
            table.Activate(site);
            Assert.That(closeup.Active, Is.True, "The click opens the close-up.");
            Assert.That(closeup.View.enabled && !room.enabled, Is.True, "The close-up camera replaces the room camera.");
            Assert.That(op.Linked && gantry.Linked, Is.True, "The table is linked.");
            Assert.That(closeup.Tool, Is.Null, "No arm is out yet: the pointer inspects.");
            Assert.That(SurgeryArmPart.Pickable, Is.True);
            yield return null;
            closeup.Snap();
            yield return Wait(.3f);
            Assert.That(panel.Visible, Is.True, "The workspace is up.");
            Assert.That(closeup.Overlay.Showing, Is.True, "The visor layer is up.");
            Assert.That(walker.Aiming, Is.False, "The pointer is free while operating.");
            Assert.That(clinic.transform.Find("UnifiedClinicTradeUI").gameObject.activeSelf, Is.False, "The room HUD gives way.");
            Assert.That(Vector3.Dot(closeup.View.transform.forward, Vector3.down), Is.GreaterThan(.95f), "The table's camera looks down on the arm.");
            Assert.That(Vector3.Dot(closeup.View.transform.up, table.HeadDir), Is.GreaterThan(.95f), "His head end is up the screen.");
            Assert.That(Vector3.Distance(closeup.View.transform.position, op.TargetPoint(SurgeryProcedureRuntime.Whole)), Is.LessThan(.8f), "The camera is close to the arm.");
            // The column only informs: no command buttons, only the page tabs; the arms and switches are on the bar.
            var buttons = panel.GetComponentsInChildren<UnityEngine.UI.Button>().Select(b => b.name).ToList();
            CollectionAssert.IsEmpty(buttons.Where(n => !n.StartsWith("臂_") && !n.StartsWith("台_") && !n.StartsWith("页签_") && n != "离开手术台"),
                "Only the arm bar, the table switches, the page tabs and leave are buttons.");
            Assert.That(panel.StepRows, Is.Not.Empty, "The steps page lists the operation.");
            Assert.That(panel.StepRows[0], Does.StartWith("· ").And.Contain("扫描"));
            yield return Shoot("open");

            // Picking: the part under a screen point, a screw over the cover, the cover over the forearm.
            Assert.That(PickAt(closeup, op, "ClampBolt_02"), Is.EqualTo("ClampBolt_02"));
            Assert.That(PickAt(closeup, op, "CoverScrew_01"), Is.EqualTo("CoverScrew_01"));
            Assert.That(PickAt(closeup, op, "ServiceCover"), Is.EqualTo("ServiceCover"));
            Assert.That(PickAt(closeup, op, "ClampTape"), Is.EqualTo("ClampTape"), "The tape over bolt 3 is what the pointer meets.");
            // With no arm out the pointer inspects what it clicks: his tape.
            yield return Press(closeup, op.TargetPoint("ClampTape"));
            Assert.That(closeup.Selected, Is.EqualTo("ClampTape"));
            Assert.That(op.LastMessage, Does.Contain("3号吊机·森田"));

            // The lamp is still a switch (L, or the bar); the arms need none: the link brings each down.
            var scan = op.Find("scan");
            Assert.That(op.Why(scan), Is.EqualTo("先打开手术灯"));
            Assert.That(op.Next().id, Is.EqualTo("lamp.on"));
            yield return null;
            Assert.That(closeup.Overlay.HintText, Does.Contain("按 L"));
            panel.LampButton.onClick.Invoke();
            Assert.That(gantry.LampOn, Is.True);
            Assert.That(op.Why(scan), Is.Null, "Linked, no deploy switch is needed.");
            Assert.That(op.Next().id, Is.EqualTo("scan"));
            yield return null;
            Assert.That(closeup.Overlay.HintText, Does.Contain("按 1").And.Contain("扫描臂"));

            var done = new List<string>();
            var suggested = new List<string>();
            op.StepDone += c => done.Add(c.id);
            for (int guard = 0; !op.Complete && guard < 80; guard++)
            {
                var next = op.Next();
                Assert.That(next, Is.Not.Null, "The guide always has a next step until the end (" + op.NextStepText + ").");
                suggested.Add(next.id);
                if (next.table)
                {
                    if (next.id == "tray.out") panel.TrayButton.onClick.Invoke();
                    else Assert.That(op.Run(next.id), Is.True, next.id);
                    yield return Wait(.7f);
                    continue;
                }
                if (closeup.Tool != next.tool)
                {
                    panel.ArmButton(next.tool).onClick.Invoke();
                    Assert.That(closeup.Tool, Is.EqualTo(next.tool), next.id + ": the bar brings its arm.");
                    yield return Until(() => closeup.ArmReady, next.id + ": " + next.tool + " comes down.", 12);
                }
                switch (next.tool)
                {
                    case "Scanner":
                    {
                        Assert.That(gantry.Drop("Scanner"), Is.GreaterThan(.05f), "The shoulder dropped on its column to reach.");
                        yield return Shoot("scanner-down");
                        // Held still over the middle, the scan reads one section; it needs the whole forearm swept.
                        var middle = op.TargetPoint(SurgeryProcedureRuntime.Whole);
                        yield return Settle(closeup, middle, .03f);
                        Point(closeup, middle, true);
                        yield return Until(() => op.Running && op.Manual && closeup.Working, "The scan starts under the held button.", 5);
                        yield return Wait(.5f);
                        Assert.That(op.Progress, Is.LessThan(.5f), "Held in one place it reads only there.");
                        Assert.That(closeup.ScanSpan(out var from, out var to), Is.True);
                        bool shot = false;
                        for (int pass = 0; pass < 6 && !op.IsDone(next); pass++)
                            for (int i = 0; i <= 40 && !op.IsDone(next); i++)
                            {
                                float k = pass % 2 == 0 ? i / 40f : 1 - i / 40f;
                                Point(closeup, Vector3.LerpUnclamped(from, to, -.12f + 1.24f * k), true);
                                yield return null; yield return null;
                                if (!shot && op.Progress > .4f) { shot = true; yield return Shoot("scan-working"); }
                            }
                        Point(closeup, to, false);
                        break;
                    }
                    case "Drill":
                    {
                        var hole = closeup.HolePoint(next.target);
                        if (next.id == "remove.CoverScrew_01")
                        {
                            // A centimetre and a half off (away from the other screw) the drill does nothing and says how far.
                            var away = hole - closeup.HolePoint("CoverScrew_02"); away.y = 0;
                            var off = hole + away.normalized * .015f;
                            yield return Settle(closeup, off, .006f);
                            yield return HoldFor(closeup, off, .6f);
                            Assert.That(op.Running, Is.False, "Not on the screw: nothing turns.");
                            Assert.That(op.LastMessage, Does.Contain("还偏"));
                        }
                        yield return Settle(closeup, hole, SurgeryCloseupRuntime.ScrewTolerance * .6f);
                        Assert.That(closeup.Aligned, Is.True, next.id + ": on the screw.");
                        if (next.id == "remove.ClampBolt_01") yield return Shoot("bolt-aligned");
                        yield return HoldUntil(closeup, hole, () => op.IsDone(next), next.id);
                        break;
                    }
                    case "Probe":
                    {
                        var plug = op.TargetPoint(next.target == SurgeryProcedureRuntime.Whole ? "NeuralPlug" : next.target);
                        yield return Settle(closeup, plug, SurgeryCloseupRuntime.ProbeTolerance * .6f);
                        int links = op.LinkResults.Count;
                        yield return HoldUntil(closeup, plug, () => next.id == "test.link" ? op.LinkResults.Count > links : op.IsDone(next), next.id);
                        if (next.id == "test.link" && op.LinkResults.Count == 1) yield return Shoot("test.link-pass");
                        break;
                    }
                    case "Cutter":
                    case "Suction":
                    {
                        // Drawn back and forth over the part while held.
                        var at = op.TargetPoint(next.target);
                        yield return Settle(closeup, at, .006f);
                        Point(closeup, at, true);
                        yield return Until(() => closeup.Working, next.id + " works under the held button.", 5);
                        for (int i = 0; i < 600 && !op.IsDone(next); i++)
                        {
                            float wobble = Mathf.Sin(i * .35f) * .008f;
                            Point(closeup, at + table.HeadDir * wobble + table.Right * (wobble * .5f), true);
                            yield return null;
                        }
                        Point(closeup, at, false);
                        Assert.That(op.IsDone(next), Is.True, next.id + " finished by drawing it over the part (" + op.Progress.ToString("0.00") + "; " + closeup.DebugState
                            + "; tip " + closeup.Tip.ToString("F4") + " part " + at.ToString("F4") + ").");
                        break;
                    }
                    case "Gripper":
                    {
                        if (next.id == "cover.remove")
                        {
                            // Lifted off and set down clear of the bay.
                            var cover = op.TargetPoint("ServiceCover");
                            yield return Carry(closeup, op, cover, () => cover + table.Right * -.12f + table.HeadDir * .05f, "cover", false);
                            break;
                        }
                        var load = next.id == "forearm.remove" ? op.OldArm : op.NewArm;
                        var grab = load.TransformPoint(new Vector3(0, -.2f, 0));
                        Vector3 Place() => next.id == "forearm.remove" ? op.TraySpot(-1) : patient.BoneTransform(SurgeryPatientRuntime.Bone.RightLowerArm).position;
                        yield return Carry(closeup, op, grab, Place, next.id == "forearm.remove" ? "forearm-carry" : "fit-carry", true);
                        break;
                    }
                }
                yield return Until(() => !op.Running && !op.Queued, next.id + " finishes.", 20);
                if (next.id.StartsWith("remove.ClampBolt") && SurgeryCyberArm.Bolts.All(b => op.Get("out." + b) == 1)) yield return Shoot("bolts-out");

                switch (next.id)
                {
                    case "scan":
                        Assert.That(op.NewArm != null && op.NewArm.parent == table.Tray, Is.True, "The replacement waits on the tray.");
                        Assert.That(op.LastResult, Does.Contain("锁机"));
                        // The gripper on a screw says it needs the drill.
                        panel.ArmButton("Gripper").onClick.Invoke();
                        yield return Until(() => closeup.ArmReady, "Gripper down.", 12);
                        var screw = closeup.HolePoint("CoverScrew_01");
                        yield return Settle(closeup, screw, .01f);
                        yield return HoldFor(closeup, screw, .4f);
                        Assert.That(op.Running, Is.False);
                        Assert.That(op.LastMessage, Does.Contain("用不上夹爪臂").And.Contain("钻头臂"));
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
                        Assert.That(suggested.Contains("tray.out"), Is.True, "The guide swung the tray out first.");
                        yield return Wait(1.2f);
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
                        Assert.That(op.LinkResults.Last(), Does.StartWith("联调 " + op.LinkResults.Count + "/3"));
                        if (op.LinkResults.Count == 3) yield return Shoot("linked");
                        break;
                }
            }
            closeup.Unscript();
            yield return Until(() => op.Complete, "The third link pass confirms the operation.", 5);
            CollectionAssert.AreEqual(Path, done, "The operation ran part by part in order.");
            Assert.That(op.Holding, Is.False);
            Assert.That(Near(LampColor(op.NewArm), SurgeryCyberArm.LampGreen), Is.True, "Linked: green.");
            yield return Until(() => patient.RightHandCurl == 0, "Hand open again after the calibration fists.", 3);
            Assert.That(SurgeryCyberArm.Bolts.All(b => op.Part(b).gameObject.activeSelf), Is.True, "All four bolts are back.");
            Assert.That(op.Part("NeuralPlug").transform.localPosition, Is.EqualTo(SurgeryCyberArm.PlugSeated), "The plug is seated in the new arm.");
            yield return null;
            Assert.That(panel.StatusText, Does.Contain("手术完成"));
            Assert.That(panel.StepRows.All(r => r.StartsWith("✓ ")), Is.True, "Every step ticked: " + string.Join(" | ", panel.StepRows));
            panel.ShowTab(SurgeryWorkspaceTab.Records);
            yield return null;
            yield return Shoot("records");
            panel.ShowTab(SurgeryWorkspaceTab.Operate);

            // Leaving restores the room; the carriages go home.
            panel.LeaveButton.onClick.Invoke();
            Assert.That(closeup.Active, Is.False);
            Assert.That(op.Linked || gantry.Linked, Is.False);
            Assert.That(room.enabled && !closeup.View.enabled, Is.True, "Back to the room camera.");
            Assert.That(SurgeryArmPart.Pickable, Is.False);
            Assert.That(clinic.transform.Find("UnifiedClinicTradeUI").gameObject.activeSelf, Is.True, "The room HUD is back.");
            yield return Until(() => Mathf.Abs(gantry.Turn) < .5f, "The carriages turn back home.", 6);
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

        // ------------------------------------------------------------------------------------------------ hands on the arm
        // The pointer over a world point (recomputed each frame: the view may move), the left button up or down.
        static void Point(SurgeryCloseupRuntime closeup, Vector3 world, bool held) => closeup.Script(closeup.View.WorldToScreenPoint(world), held);

        static IEnumerator Press(SurgeryCloseupRuntime closeup, Vector3 world)
        {
            Point(closeup, world, false); yield return null;
            Point(closeup, world, true); yield return null;
            Point(closeup, world, false); yield return null;
        }

        // As a player does: point at the part, watch where the tip ends up, nudge the pointer by the miss, until the tip
        // is within a tolerance of it (seen from above).
        Vector3 settled;

        IEnumerator Settle(SurgeryCloseupRuntime closeup, Vector3 world, float tolerance)
        {
            var aim = world;
            for (int round = 0; round < 6; round++)
            {
                for (int i = 0; i < 45; i++) { Point(closeup, aim, false); yield return null; }
                for (int i = 0; i < 240 && !closeup.ArmReady; i++) { Point(closeup, aim, false); yield return null; }   // the ring may have turned
                var miss = world - closeup.Tip; miss.y = 0;
                settled = aim;
                if (miss.magnitude <= tolerance) yield break;
                aim += miss;
            }
            var last = world - closeup.Tip; last.y = 0;
            Assert.That(last.magnitude, Is.LessThanOrEqualTo(tolerance), "The tip settles on the point (" + closeup.Tool + ").");
        }

        IEnumerator HoldUntil(SurgeryCloseupRuntime closeup, Vector3 world, Func<bool> done, string what)
        {
            float deadline = Time.realtimeSinceStartup + 15;
            var aim = settled;
            int frame = 0;
            while (!done() && Time.realtimeSinceStartup < deadline)
            {
                Point(closeup, aim, true);
                yield return null;
                // Held, the player still steers by the reticle: a miss is nudged out.
                if (++frame % 30 == 0 && !closeup.Aligned) { var miss = world - closeup.Tip; miss.y = 0; aim += miss; }
            }
            Point(closeup, aim, false);
            yield return null;
            var gantry = SurgeryGantryV2Runtime.Current;
            Assert.That(done(), Is.True, what + " finishes under the held button (" + SurgeryProcedureRuntime.Current.LastMessage + "; tip " + closeup.Tip.ToString("F4")
                + " part " + world.ToString("F4") + " aim " + gantry.Aim.ToString("F4") + " ready " + closeup.ArmReady + " working " + closeup.Working + "; " + closeup.DebugState + "; hits "
                + string.Join(", ", Physics.RaycastAll(closeup.View.ScreenPointToRay(closeup.View.WorldToScreenPoint(world)), 3f, ~0, QueryTriggerInteraction.Collide)
                    .OrderBy(h => h.distance).Select(h => h.collider.name + (h.collider.isTrigger ? "(t)" : "") + "@" + h.point.y.ToString("F3"))) + ").");
        }

        IEnumerator HoldFor(SurgeryCloseupRuntime closeup, Vector3 world, float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) { Point(closeup, world, true); yield return null; }
            Point(closeup, world, false);
            yield return null;
        }

        // The gripper: pressed on the load until it is held, carried by moving the pointer, nudged until its drop place is
        // met, let go there. The load follows the tip at the offset it was taken at.
        IEnumerator Carry(SurgeryCloseupRuntime closeup, SurgeryProcedureRuntime op, Vector3 grab, Func<Vector3> place, string shot, bool checkWrongDrop)
        {
            yield return Settle(closeup, grab, .015f);
            float deadline = Time.realtimeSinceStartup + 8;
            while (closeup.Carrying == null && Time.realtimeSinceStartup < deadline) { Point(closeup, grab, true); yield return null; }
            Assert.That(closeup.Carrying, Is.Not.Null, "The gripper takes the load (" + op.LastMessage + ").");
            var offset = closeup.Handle - closeup.Tip; offset.y = 0;
            var aim = place() - offset;
            if (checkWrongDrop)
            {
                // Let go half way: refused, and it stays in the gripper.
                var halfway = Vector3.Lerp(grab, aim, .5f);
                for (int i = 0; i < 40; i++) { Point(closeup, halfway, true); yield return null; }
                Point(closeup, halfway, false); yield return null; yield return null;
                Assert.That(closeup.Carrying, Is.Not.Null, "A drop in the wrong place is refused.");
                Point(closeup, halfway, true); yield return null;
            }
            for (int round = 0; round < 8; round++)
            {
                for (int i = 0; i < 50; i++) { Point(closeup, aim, true); yield return null; }
                if (round == 0) yield return Shoot(shot);
                if (closeup.Aligned) break;
                var miss = place() - closeup.Handle; miss.y = 0;
                aim += miss;
            }
            Assert.That(closeup.Aligned, Is.True, "The load reaches its place (" + op.LastMessage + "; handle " + closeup.Handle.ToString("F3") + " place " + place().ToString("F3")
                + " tip " + closeup.Tip.ToString("F3") + " aim " + SurgeryGantryV2Runtime.Current.Aim.ToString("F3") + ").");
            Point(closeup, aim, false);
            yield return null; yield return null;
            Assert.That(closeup.Carrying, Is.Null, "Let go there (" + op.LastMessage + "; " + closeup.DebugState + "; handle " + closeup.Handle.ToString("F3") + " place " + place().ToString("F3") + ").");
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
