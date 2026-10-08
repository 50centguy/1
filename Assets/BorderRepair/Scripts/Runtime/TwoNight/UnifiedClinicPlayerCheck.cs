using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BorderRepair.Core;
using BorderRepair.Data;
using BorderRepair.Dock;
using BorderRepair.FirstOrder;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace BorderRepair.TwoNight
{
    // Opt-in acceptance only. Two separate player processes share an isolated disk checkpoint.
    public class UnifiedClinicPlayerCheck : MonoBehaviour
    {
        [Serializable] class Report
        {
            public string startedUtc, finishedUtc, error, scene, checkpoint;
            public int processId, phase, cash, receipts, mouseClicks, budgetSeconds;
            public bool passed, retestPassed;
            public float walkedMeters, mouseYawDegrees;
            public bool firstPersonPassed;
        }
        Report report;
        string output;
        Mouse mouse;
        Action cleanup;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Initialize()
        {
            if (Application.isEditor) return;
            var args = Environment.GetCommandLineArgs();
            int flag = Array.IndexOf(args, "-clinicCheckpointCheck");
            if (flag < 0) return;
            if (flag + 2 >= args.Length || !int.TryParse(args[flag + 2], out int phase) || (phase != 1 && phase != 2))
            { Debug.LogError("Expected -clinicCheckpointCheck <outputDirectory> <1|2>"); Application.Quit(1); return; }
            Application.runInBackground = true;
            var check = new GameObject("UnifiedClinicPlayerCheck").AddComponent<UnifiedClinicPlayerCheck>();
            DontDestroyOnLoad(check);
            check.output = Path.GetFullPath(args[flag + 1]);
            Directory.CreateDirectory(check.output);
            TwoNightSave.OverrideDirectory = Path.Combine(check.output, "Save");
            check.report = new Report { phase = phase, processId = System.Diagnostics.Process.GetCurrentProcess().Id,
                startedUtc = DateTime.UtcNow.ToString("O"), checkpoint = TwoNightSave.FilePath, budgetSeconds = phase == 2 ? 300 : 180 };
            check.StartCoroutine(check.Execute());
        }

        void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        IEnumerator Execute()
        {
            var routines = new Stack<IEnumerator>();
            routines.Push(Run());
            float deadline = Time.realtimeSinceStartup + report.budgetSeconds;
            while (routines.Count > 0)
            {
                object next = null;
                bool moved = false;
                try
                {
                    Require(Time.realtimeSinceStartup < deadline, "Checkpoint probe exceeded " + report.budgetSeconds + " seconds.");
                    moved = routines.Peek().MoveNext();
                    if (moved) next = routines.Peek().Current;
                }
                catch (Exception exception) { report.error = exception.ToString(); break; }
                if (!moved) { routines.Pop(); continue; }
                if (next is IEnumerator nested) routines.Push(nested);
                else yield return next;
            }
            cleanup?.Invoke();
            report.passed = report.error == null;
            report.finishedUtc = DateTime.UtcNow.ToString("O");
            report.scene = SceneManager.GetActiveScene().path;
            report.cash = TwoNightRun.Current != null ? TwoNightRun.Current.Cash : -1;
            report.receipts = TwoNightRun.Current != null ? TwoNightRun.Current.transactions.Count : -1;
            File.WriteAllText(Path.Combine(output, "checkpoint_phase" + report.phase + ".json"), JsonUtility.ToJson(report, true));
            Debug.Log("[ClinicCheckpoint] " + JsonUtility.ToJson(report));
            Application.Quit(report.passed ? 0 : 1);
        }

        IEnumerator WaitUntil(Func<bool> predicate, string label)
        {
            float deadline = Time.realtimeSinceStartup + 30;
            while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
            Require(predicate(), "Timeout: " + label);
            for (int i = 0; i < 5; i++) yield return null;
        }

        IEnumerator Screenshot(string name)
        {
            yield return null;
            using var frame = new ClinicOffscreenFrame(Camera.main, Screen.width, Screen.height);
            frame.Render();
            frame.Render();
            frame.SavePng(Path.Combine(output, "p" + report.phase + "_" + name + ".png"));
        }

        void Singles()
        {
            Require(FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length == 1, "Duplicate EventSystem.");
            Require(FindObjectsByType<Camera>(FindObjectsSortMode.None).Count(c => c.enabled) == 1, "Duplicate camera.");
            Require(FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(a => a.enabled) == 1, "Duplicate listener.");
        }

        IEnumerator CheckWalking(UnifiedClinicDirector clinic)
        {
            var flow = clinic.Robot.Flow;
            var walker = flow.Rig.Walker;
            Require(walker != null && flow.Rig.Walking, "Shared room did not start in first-person mode.");
            if (mouse == null) mouse = FirstOrderAcceptanceDriver.CreateVirtualMouse(out cleanup);
            var keyboard = InputSystem.AddDevice<Keyboard>("ClinicWalkingAcceptanceKeyboard");
            var previousCleanup = cleanup;
            cleanup = () => { if (keyboard.added) InputSystem.RemoveDevice(keyboard); previousCleanup?.Invoke(); };
            keyboard.MakeCurrent();
            for (int frame = 0; frame < 30; frame++) yield return null;
            Vector3 start = walker.transform.position;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
            float end = Time.realtimeSinceStartup + .35f;
            while (Time.realtimeSinceStartup < end) yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            report.walkedMeters = Vector2.Distance(new Vector2(start.x, start.z),
                new Vector2(walker.transform.position.x, walker.transform.position.z));
            Require(report.walkedMeters > .2f && report.walkedMeters < .8f, "Keyboard movement failed or jumped.");
            float yaw = walker.Yaw;
            InputSystem.QueueDeltaStateEvent(mouse.delta, new Vector2(120, 0));
            yield return null;
            report.mouseYawDegrees = Mathf.Abs(Mathf.DeltaAngle(yaw, walker.Yaw));
            Require(report.mouseYawDegrees > 5f && report.mouseYawDegrees < 20f, "Mouse-look input failed.");
            report.firstPersonPassed = true;
            yield return Screenshot("first_person_walk");
        }

        IEnumerator ClickZone(ClinicTradeAction action)
        {
            // Preserve the fixed close-up acceptance path; first-person input is checked separately.
            FindFirstObjectByType<UnifiedClinicDirector>().Robot.Flow.Rig.Go(FirstOrderCameraRig.Overview, true);
            yield return null;
            var zone = FindObjectsByType<ClinicTradeZone>(FindObjectsSortMode.None).Single(z => z.Action == action);
            Physics.SyncTransforms();
            Vector2? position = null;
            foreach (var collider in zone.GetComponentsInChildren<Collider>().Where(c => c.enabled))
            {
                var bounds = collider.bounds;
                for (int i = 0; i < 125 && position == null; i++)
                {
                    var fraction = new Vector3(i % 5, i / 5 % 5, i / 25) / 4f;
                    var screen = Camera.main.WorldToScreenPoint(bounds.min + Vector3.Scale(bounds.size, Vector3.one * .05f + fraction * .9f));
                    if (screen.z <= 0 || screen.x <= 1 || screen.x >= Screen.width - 1 || screen.y <= 1 || screen.y >= Screen.height - 1) continue;
                    if (!FirstOrderInput.IsOverUI(screen) && Physics.Raycast(Camera.main.ScreenPointToRay(screen), out var hit) &&
                        hit.collider.GetComponentInParent<ClinicTradeZone>() == zone) position = (Vector2)screen;
                }
            }
            Require(position != null, "No physical click point: " + action);
            mouse.MakeCurrent();
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position.Value }); yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position.Value }.WithButton(MouseButton.Left)); yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position.Value }); yield return null;
        }

        IEnumerator Run()
        {
            yield return WaitUntil(() => FindFirstObjectByType<TwoNightMenu>() != null, "shared menu");
            var menu = FindFirstObjectByType<TwoNightMenu>();
            Require(SceneManager.GetActiveScene().name == "UnifiedClinic_Menu", "Player must start in shared menu.");
            menu.Refresh();
            Require(TwoNightRun.Current == null, "A fresh process must not inherit run memory.");
            if (report.phase == 2)
            {
                var first = JsonUtility.FromJson<Report>(File.ReadAllText(Path.Combine(output, "checkpoint_phase1.json")));
                Require(first != null && first.passed && first.phase == 1 && first.processId != report.processId,
                    "Continue requires a successful checkpoint from a different player process.");
                Require(menu.ContinueButton.interactable, "Disk checkpoint cannot continue.");
                string before = File.ReadAllText(TwoNightSave.FilePath);
                menu.ContinueButton.onClick.Invoke();
                yield return WaitUntil(() => FindFirstObjectByType<UnifiedClinicDirector>()?.Initialized == true, "shared Continue");
                var clinic = FindFirstObjectByType<UnifiedClinicDirector>();
                Singles();
                Require(clinic.State.phase == TwoNightPhase.Night2Open && clinic.State.Cash == 800 && clinic.State.transactions.Count == 2,
                    "Disk money/phase history was not restored.");
                Require(clinic.Station.Session == null && !clinic.Counter.gameObject.activeInHierarchy, "Customer session active in night2.");
                Require(!clinic.Robot.RestoreFailed && !clinic.Robot.Flow.InspectionLocked &&
                    clinic.Robot.Flow.Step == FoStep.InspectLeftEngine && clinic.Robot.Flow.Dock.State == DockState.RotorsStopped,
                    "Night2 did not restore safe inspection state.");
                Require(before == File.ReadAllText(TwoNightSave.FilePath), "Continue changed the disk checkpoint.");
                yield return Screenshot("disk_continue");
                var input = FindFirstObjectByType<FirstOrderInput>();
                var view = FindFirstObjectByType<BorderRepair.FirstOrder.Slice.SliceView>();
                if (view.ManualOpen) view.ToggleManual();
                yield return null;
                yield return CheckWalking(clinic);
                yield return Screenshot("night2_room");
                var night2Driver = new FirstOrderAcceptanceDriver(clinic.Robot.Flow, input) { VirtualMouse = mouse };
                yield return night2Driver.RunFullOrder(resumeAtInspection: true);
                report.mouseClicks = night2Driver.Records.Count(r => !string.IsNullOrEmpty(r.screenPoint));
                report.retestPassed = clinic.Robot.Flow.RetestPassed;
                Require(night2Driver.AllPassed, string.Join("\n", night2Driver.Records.Where(r => !r.pass).Select(r => r.label + ": " + r.message)));
                Require(clinic.Robot.Flow.Step == FoStep.Done && report.retestPassed, "Night2 full order did not pass retest.");
                yield return null;
                Require(clinic.Robot.HudText.text.Contains("复测通过") && !clinic.Robot.HudText.text.Contains("未修") &&
                    clinic.Robot.CaptionText.text.Contains("复测通过"), "Completed robot repair has stale pending UI.");
                Require(clinic.State.Cash == 800 && clinic.State.transactions.Count == 2, "Internal robot repair changed customer income.");
                Require(before == File.ReadAllText(TwoNightSave.FilePath), "Night2 repair changed the disk checkpoint.");
                Singles();
                yield return Screenshot("night2_retest_passed");
                yield break;
            }
            Require(!TwoNightSave.Exists && !menu.ContinueButton.interactable, "Phase1 requires an empty isolated save directory.");
            mouse = FirstOrderAcceptanceDriver.CreateVirtualMouse(out cleanup);
            menu.NewButton.onClick.Invoke();
            yield return WaitUntil(() => FindFirstObjectByType<UnifiedClinicDirector>()?.Initialized == true, "shared NewGame");
            var room = FindFirstObjectByType<UnifiedClinicDirector>();
            Singles();
            yield return Screenshot("night1_room");
            yield return CheckWalking(room);
            yield return ClickZone(ClinicTradeAction.Receive);
            Require(room.TradeState == ClinicTradeState.InRepair, "Physical receive failed.");
            var session = room.Station.Session;
            Require(session.SetScanMode(true), "Scan mode failed.");
            foreach (var point in session.CurrentCase.inspectionPoints.Where(p => p.requiredForDiagnosis))
                Require(room.Station.ScanPoint(point.pointId) == ScanOutcome.NewFinding, "Inspection failed.");
            Require(session.TryBeginDiagnosis() && session.SubmitDiagnosis(session.CurrentCase.correctDiagnosisId) &&
                session.SubmitDecision(RepairDecision.Repair), "Authored repair decision failed.");
            Require(room.State.Cash == 500 && room.State.transactions.Count == 0, "Income before delivery.");
            yield return ClickZone(ClinicTradeAction.Deliver);
            Require(room.State.Cash == 800 && room.State.transactions.Count == 2 && room.Counter.LedgerVisible, "Physical delivery did not settle once.");
            room.Counter.ConfirmButton.onClick.Invoke();
            yield return WaitUntil(() => room.State.phase == TwoNightPhase.Night1Docking, "same-room incident");
            var flow = room.Robot.Flow;
            var driver = new FirstOrderAcceptanceDriver(flow, FindFirstObjectByType<FirstOrderInput>()) { VirtualMouse = mouse };
            flow.Rig.Go(FirstOrderCameraRig.Dock, true); yield return null;
            var grip = new[] { "Dock_Clamp_L_Grip", "Dock_Clamp_R_Grip" }.Select(n => GameObject.Find(n).GetComponent<DockInteractable>())
                .FirstOrDefault(g => driver.FindClickPoint(g, out _));
            Require(grip != null, "No visible dock grip.");
            yield return driver.Click("Seat", FirstOrderCameraRig.Dock, grip, true);
            yield return WaitUntil(() => flow.Dock.State == DockState.SeatedOpen, "seat");
            yield return driver.Click("Clamp", FirstOrderCameraRig.Dock, grip, true);
            yield return WaitUntil(() => flow.Dock.State == DockState.Clamped, "clamp");
            yield return driver.Click("Power off", FirstOrderCameraRig.Dock, GameObject.Find("Dock_PowerSwitch_LeverGrip").GetComponent<DockInteractable>(), true);
            yield return WaitUntil(() => flow.Dock.State == DockState.RotorsStopped, "rotors stopped");
            Require(driver.AllPassed, "Real mouse dock acceptance failed.");
            report.mouseClicks = 2 + driver.Records.Count(r => !string.IsNullOrEmpty(r.screenPoint));
            Require(room.Robot.CollectSafeState().IsSafe, "Unsafe registration.");
            room.Robot.RegisterButton.onClick.Invoke(); yield return null;
            Require(room.State.phase == TwoNightPhase.Night1Ended && TwoNightSave.Read().status == SaveStatus.Ok, "Safe disk checkpoint not written.");
            Require(SceneManager.GetActiveScene().name == "UnifiedClinic", "Night1 switched rooms.");
            yield return Screenshot("night1_checkpoint");
        }
    }
}
