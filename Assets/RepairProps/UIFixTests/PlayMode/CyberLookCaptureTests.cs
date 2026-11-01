using System;
using System.Collections;
using System.IO;
using System.Linq;
using BorderRepair.Art.UnifiedClinic.PS2;
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
    // What the player actually sees: the game camera with its PS2 pass, then every overlay canvas drawn on top by a
    // separate UI-only camera (overlay UI is never processed by the PS2 pass in play). Also checks the AR overlay marks
    // the suggested target. Images are written only when BORDER_REPAIR_CYBER_SHOTS names a folder.
    public sealed class CyberLookCaptureTests
    {
        const string ScenePath = "Assets/RepairProps/Scenes/RepairProps_Clinic.unity";
        static readonly Vector2Int[] Sizes = { new Vector2Int(1920, 1080), new Vector2Int(1280, 720) };
        static string Folder => Environment.GetEnvironmentVariable("BORDER_REPAIR_CYBER_SHOTS");
        TwoNightState previousRun;
        string previousSave, isolatedSave;
        int previousWidth, previousHeight;
        FullScreenMode previousMode;
        Scene loaded;
        UnifiedClinicDirector clinic;
        RepairPropStationBridge bridge;
#if UNITY_EDITOR
        UiTestGameView gameView;
#endif
        RepairPropFlow Flow => bridge.Flow;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousRun = TwoNightRun.Current; previousSave = TwoNightSave.OverrideDirectory;
            previousWidth = Screen.width; previousHeight = Screen.height; previousMode = Screen.fullScreenMode;
            isolatedSave = Path.Combine(Path.GetTempPath(), "RepairPropCyberCapture_" + Guid.NewGuid().ToString("N"));
            TwoNightSave.OverrideDirectory = isolatedSave;
            TwoNightRun.Clear();
            Time.timeScale = 1;
            RetroPS2Settings.ResetToDefaults();
            RetroPS2CameraEffect.IncludeOffscreenCaptures = true;
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("Requires the authored editor scene.");
            yield break;
#endif
            loaded = SceneManager.GetSceneByPath(ScenePath);
            yield return Until(() => (clinic = Object.FindFirstObjectByType<UnifiedClinicDirector>()) != null && clinic.Initialized, "Clinic must initialize.");
            clinic.Robot.Flow.Rig.Walker.LeaveSystemCursor = true;
            Assert.That(ClinicSupplies.Purchase(clinic.State, new System.Collections.Generic.Dictionary<string, int> { { "rx-rf", 1 } },
                Guid.NewGuid().ToString("N"), out string reason), Is.True, reason);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            RetroPS2CameraEffect.IncludeOffscreenCaptures = false;
            if (loaded.IsValid() && loaded.isLoaded)
            {
                SceneManager.SetActiveScene(SceneManager.CreateScene("CyberCaptureCleanup_" + Guid.NewGuid().ToString("N")));
                yield return SceneManager.UnloadSceneAsync(loaded);
            }
#if UNITY_EDITOR
            gameView?.Dispose(); gameView = null;
#endif
            Screen.SetResolution(previousWidth, previousHeight, previousMode);
            TwoNightRun.Set(previousRun); TwoNightSave.OverrideDirectory = previousSave;
            if (Directory.Exists(isolatedSave)) Directory.Delete(isolatedSave, true);
        }

        [UnityTest]
        public IEnumerator Receiver_PlayerView_WithPs2AndVisorHud()
        {
            yield return Shoot("00-room-walk", clinic.Robot.Flow.Rig.Cam);
            Assert.That(clinic.Interact(ClinicTradeAction.Receive), Is.True, clinic.LastMessage);
            bridge = clinic.Station.Inspector.CurrentItem.GetComponent<RepairPropStationBridge>();
            yield return Until(() => Flow.Session != null, "Bridge must attach.");
            clinic.Counter.DismissDialogue();
            Assert.That(clinic.Interact(ClinicTradeAction.Console), Is.True, clinic.LastMessage);
            yield return Until(() => bridge.CanInteract, "Bench must be interactive.");
            for (int i = 0; i < 40; i++) yield return null;

            Assert.That(bridge.ArOverlay, Is.Not.Null, "The visor overlay attaches with the workspace.");
            yield return null;
            Assert.That(bridge.ArOverlay.Showing, Is.True);
            Assert.That(bridge.ArOverlay.NextTarget, Is.Not.Null.And.EqualTo(RepairPropWorkflowStatus.NextCommand(Flow)?.target));
            StringAssert.StartsWith("提示 // ", bridge.ArOverlay.HintText);
            yield return Shoot("01-bench-intake", View);

            // V2 bench: installed over the old one, its tools hand the station a real tool, drawers and lamp respond.
            var bench = RepairBenchV2Runtime.Current;
            Assert.That(bench != null && bench.Bench != null, Is.True, "The V2 bench is placed at play time.");
            Assert.That(bench.HiddenCount, Is.GreaterThan(20), "The primitive bench pieces are retired.");
            var screwdriver = bench.FindProp("BV2_Tool_Screwdriver");
            screwdriver.Activate(bridge);
            Assert.That(clinic.Station.ActiveTool, Is.EqualTo(BorderRepair.Data.RepairActionType.RemoveFastener));
            for (int i = 0; i < 30; i++) yield return null;
            Assert.That(screwdriver.Lifted, Is.True, "The selected tool rises out of the tray.");
            var drawer = bench.FindProp("BV2_Drawer_1");
            var closed = drawer.transform.position;
            drawer.Activate(bridge);
            bench.FindProp("BV2_CabinetDoor").Activate(bridge);
            for (int i = 0; i < 40; i++) yield return null;
            Assert.That(Vector3.Distance(closed, drawer.transform.position), Is.GreaterThan(.2f), "The drawer slides out.");
            Assert.That(bench.Lamp != null && bench.Lamp.enabled, Is.True);
            StringAssert.Contains(RepairPropText.Item(Flow.Definition.kind), bench.CrtText);
            // The leased test fixture is the flow's power / test-connection state made physical.
            Assert.That(bench.FixturePowerLamp, Is.EqualTo(Flow.GetState("power") != 0), "PWR lamp follows test power.");
            Assert.That(bench.FixtureLinkLamp, Is.EqualTo(Flow.GetState("fixture") != 0), "LINK lamp follows the test connection.");
            // Desk life: the holo koi circles its puck and the lucky cat waves (both kept out of the static batch).
            Assert.That(bench.HoloKoi != null && bench.LuckyCatArm != null, Is.True, "Desk trinkets exist.");
            var koiAt = bench.HoloKoi.GetComponent<Renderer>().bounds.center; var paw = bench.LuckyCatArm.rotation;
            yield return new WaitForSecondsRealtime(.8f);
            Assert.That(Vector3.Distance(koiAt, bench.HoloKoi.GetComponent<Renderer>().bounds.center), Is.GreaterThan(.01f), "The holo koi swims around its puck.");
            Assert.That(Quaternion.Angle(paw, bench.LuckyCatArm.rotation), Is.GreaterThan(1f), "The lucky cat waves.");
            Select("RX_CoverScrew_01");
            yield return Shoot("02-screw-selected-tool-in-hand", View);
            drawer.Activate(bridge); bench.FindProp("BV2_CabinetDoor").Activate(bridge);
            bench.FindProp("BV2_Tool_Loupe").Activate(bridge);
            Assert.That(clinic.Station.ActiveTool.HasValue, Is.False, "The loupe means empty-handed inspection.");
            Do("test.signal"); Do("test.identity"); Do("power.off"); Do("fixture.disconnect");
            Select(Flow.Definition.rootObjectName);
            yield return null;
            Assert.That(RepairBenchV2Runtime.Current.FixturePowerLamp || RepairBenchV2Runtime.Current.FixtureLinkLamp, Is.False,
                "Powered off and disconnected: both fixture lamps go dark.");
            Assert.That(RepairPropWorkflowStatus.NextCommand(Flow).id, Does.StartWith("remove."), "After safe shutdown the visor points at a fastener.");
            yield return Shoot("03-diagnosed-next-screw", View);
            for (int i = 1; i <= 4; i++) Do("remove.RX_CoverScrew_" + i.ToString("00"));
            Do("cover.remove"); Do("isolate"); Do("harness.release");
            yield return Until(() => bridge.BenchPresentation == null || !bridge.BenchPresentation.IsAnimating, "Parts must settle.");
            Select("RX_RearCover");
            yield return Shoot("04-opened-cover-parked", View);
            clinic.Station.Inspector.Rotate(new Vector2(70, 20));
            for (int i = 0; i < 30; i++) yield return null;
            yield return Shoot("05-held-turned", View);
            // Windows drag out of the column by their title bars and snap back on a double click.
            var actionWindow = (RectTransform)bridge.ContextPanel.PanelRectTransform.Find("ActionWindow");
            var drag = actionWindow.Find("TitleBar").GetComponent<HudWindowDrag>();
            Assert.That(drag, Is.Not.Null, "Window title bars are drag handles.");
            var before = actionWindow.position;
            var pointer = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { delta = new Vector2(-520, 120) };
            drag.OnBeginDrag(pointer); drag.OnDrag(pointer);
            yield return null;
            Assert.That(drag.Floating, Is.True);
            Assert.That(Vector3.Distance(before, actionWindow.position), Is.GreaterThan(100f), "The window moved.");
            yield return Shoot("06-window-dragged", View);
            drag.OnPointerClick(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { clickCount = 2 });
            yield return null; Canvas.ForceUpdateCanvases(); yield return null;
            Assert.That(drag.Floating, Is.False, "Double click docks it again.");
        }

        [UnityTest]
        public IEnumerator SurgeryTable_ReplacesOldBed_AndPosesFromItsControls()
        {
            var table = SurgeryTableV2Runtime.Current;
            Assert.That(table != null && table.Table != null, Is.True, "The V2 surgery table is placed at play time.");
            Assert.That(table.HiddenCount, Is.GreaterThanOrEqualTo(5), "All primitive Bed_* meshes are retired.");
            var controls = table.Controls.ToList();
            Assert.That(controls.Count(c => c.Kind == SurgeryControlKind.Strap), Is.EqualTo(3));
            var back = table.Table.GetComponentsInChildren<Transform>().First(t => t.name == "BST_Pad_Back");
            var headPad = table.Table.GetComponentsInChildren<Transform>().First(t => t.name == "BST_Pad_Head");
            float flatHeight = headPad.GetComponent<Renderer>().bounds.center.y;
            Assert.That(flatHeight, Is.InRange(.76f, .92f), "Upright, with the head pad at the old bed's height.");
            var oldBed = GameObject.Find("Bed_Base").GetComponent<Renderer>().bounds;
            var body = table.Table.GetComponentsInChildren<Renderer>().Where(r => r.name == "BST_Base" || r.name == "BST_Pad_Seat").Select(r => r.bounds).ToList();
            foreach (var b in body)
                Assert.That(Mathf.Abs(b.center.x - oldBed.center.x) < .4f && Mathf.Abs(b.center.z - oldBed.center.z) < 1f, Is.True, "Sits where the old bed stood.");
            var root = table.Table.transform.position;
            var longAxis = Vector3.ProjectOnPlane(headPad.GetComponent<Renderer>().bounds.center - root, Vector3.up).normalized;
            var trayRenderer = table.Table.GetComponentsInChildren<Transform>().First(t => t.name == "BST_Tray").GetComponent<Renderer>();
            float Lateral(Vector3 p) { var d = Vector3.ProjectOnPlane(p - root, Vector3.up); return (d - longAxis * Vector3.Dot(d, longAxis)).magnitude; }
            float trayIn = Lateral(trayRenderer.bounds.center);
            yield return Shoot("10-room-surgery-flat", clinic.Robot.Flow.Rig.Cam);

            table.Activate(controls.First(c => c.Kind == SurgeryControlKind.Pendant));
            table.Activate(controls.First(c => c.Kind == SurgeryControlKind.Tray));
            var strap = controls.First(c => c.Kind == SurgeryControlKind.Strap);
            table.Activate(strap);
            yield return Until(() => table.PoseAmount > .98f, "The pendant drives the chair pose.");
            yield return new WaitForSecondsRealtime(.5f);
            Assert.That(table.ChairPose && table.PoseAmount > .95f, Is.True);
            Assert.That(headPad.GetComponent<Renderer>().bounds.center.y - flatHeight, Is.GreaterThan(.25f), "The pendant raises the back and head sections.");
            Assert.That(table.StrapClosed(strap), Is.False, "A restraint can be opened.");
            Assert.That(table.TrayOut, Is.True);
            Assert.That(Lateral(trayRenderer.bounds.center) - trayIn, Is.GreaterThan(.2f), "The tray swings out beside the table, not over it.");
            yield return Shoot("11-room-surgery-chair", clinic.Robot.Flow.Rig.Cam);
            table.Activate(controls.First(c => c.Kind == SurgeryControlKind.Pendant));
            yield return Until(() => table.PoseAmount < .005f, "The pendant lays the table flat again.");
            Assert.That(Mathf.Abs(headPad.GetComponent<Renderer>().bounds.center.y - flatHeight), Is.LessThan(.01f), "Flat again.");
        }

        [UnityTest]
        public IEnumerator SurgeryGantry_ReplacesOldRing_ArmsDeployOverTheTable()
        {
            var gantry = SurgeryGantryV2Runtime.Current;
            Assert.That(gantry != null && gantry.Gantry != null, Is.True, "The V2 gantry is placed at play time.");
            Assert.That(gantry.HiddenCount, Is.GreaterThanOrEqualTo(20), "The primitive Gantry_* / Tool_* meshes are retired.");
            Assert.That(gantry.LampOn, Is.True, "The surgical lamp starts lit.");
            var tools = new[] { "Probe", "Drill", "Gripper", "Scanner", "Cutter", "Suction" };
            var centre = SurgeryTableV2Runtime.Current.Table.transform.position;
            float Low(string tool) => gantry.ToolTip(tool).GetComponentsInChildren<Renderer>().Min(r => r.bounds.min.y);
            float Reach(string tool) { var b = gantry.ToolTip(tool).GetComponent<Renderer>().bounds.center; return new Vector2(b.x - centre.x, b.z - centre.z).magnitude; }
            yield return null;
            var parkedLow = tools.ToDictionary(t => t, Low);
            var parkedReach = tools.ToDictionary(t => t, Reach);
            foreach (var tool in tools) Assert.That(parkedLow[tool], Is.GreaterThan(1.85f), tool + " parks above head height.");
            yield return Shoot("12-room-gantry-parked", clinic.Robot.Flow.Rig.Cam);

            var controls = gantry.Gantry.GetComponentsInChildren<SurgeryTableControl>().ToList();
            SurgeryTableV2Runtime.Current.Activate(controls.First(c => c.Kind == SurgeryControlKind.Arms));
            yield return Until(() => gantry.DeployAmount >= 1f, "Arms deploy.");
            var spin = gantry.Gantry.GetComponentsInChildren<Transform>().First(t => t.name == "BGT_Arm_Drill_Spin").rotation;
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(Quaternion.Angle(spin, gantry.Gantry.GetComponentsInChildren<Transform>().First(t => t.name == "BGT_Arm_Drill_Spin").rotation), Is.GreaterThan(5), "The drill bit spins while deployed.");
            foreach (var tool in tools)
            {
                Assert.That(Low(tool), Is.LessThan(parkedLow[tool] - .4f), tool + " lowers over the table.");
                Assert.That(Low(tool), Is.GreaterThan(1.0f), tool + " stays clear of the table and patient.");
                Assert.That(Reach(tool), Is.LessThan(parkedReach[tool]), tool + " reaches in toward the table.");
            }
            yield return Shoot("13-room-gantry-deployed", clinic.Robot.Flow.Rig.Cam);
            SurgeryTableV2Runtime.Current.Activate(controls.First(c => c.Kind == SurgeryControlKind.Lamp));
            Assert.That(gantry.LampOn, Is.False);
            SurgeryTableV2Runtime.Current.Activate(controls.First(c => c.Kind == SurgeryControlKind.Arms));
            yield return Until(() => gantry.DeployAmount <= 0f, "Arms park again.");
        }

        [UnityTest]
        public IEnumerator Zones_CounterDockAndBenchWall_ReplacedInPlace()
        {
            yield return null; yield return null;
            var zones = ClinicZonesV2Runtime.Current;
            Assert.That(zones != null && zones.Zones != null, Is.True, "The counter / dock-corner asset is placed.");
            Assert.That(zones.HiddenCount, Is.GreaterThanOrEqualTo(25), "The primitive counter, its shelving and the dock wall papers are retired.");
            Assert.That(zones.TintedDockRenderers, Is.GreaterThan(0), "The dock's grey paint is tinted to the shared steel green.");
            var all = loaded.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).ToList();
            Renderer Old(string name) => all.First(r => r.name == name);
            foreach (var lamp in new[] { "Trade_StatusLamp_Receive", "Trade_StatusLamp_Deliver" })
                Assert.That(Old(lamp).forceRenderingOff, Is.False, lamp + " stays: the director drives it.");
            foreach (var (tray, pad) in new[] { ("ZC_Tray_In", "Trade_Pad_Receive"), ("ZC_Tray_Out", "Trade_Pad_Deliver") })
            {
                var a = all.First(r => r.name == tray).bounds.center; var b = Old(pad).bounds.center;
                Assert.That(Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z)), Is.LessThan(.03f), tray + " sits on the logical pad.");
                Assert.That(Old(pad).GetComponent<Collider>() == null || Old(pad).GetComponent<Collider>().enabled, Is.True, pad + " keeps its collider.");
            }
            var top = all.First(r => r.name == "ZC_CounterTop").bounds; var oldTop = Old("Trade_CounterTop").bounds;
            Assert.That(Mathf.Abs(top.max.y - oldTop.max.y), Is.LessThan(.01f), "Same counter height as the old top.");
            var blades = zones.FanBlades.rotation;
            yield return new WaitForSecondsRealtime(.6f);
            Assert.That(Quaternion.Angle(blades, zones.FanBlades.rotation), Is.GreaterThan(5f), "The desk fan spins.");
            StringAssert.Contains("现金 " + clinic.State.Cash, zones.TerminalText, "The intake terminal shows the run's cash.");
            // Bench wall pass: the old pegboard, shelf and monitor arm are retired, the V2 wall is there.
            Assert.That(Old("Wall_Pegboard").forceRenderingOff && Old("Wall_ShelfUpper").forceRenderingOff, Is.True);
            Assert.That(all.Where(r => r.transform.GetComponentsInParent<Transform>(true).Any(t => t.name == "UC_MonitorArm")).All(r => r.forceRenderingOff), Is.True,
                "The wall monitor arm (a second green screen over the bench) is retired.");
            var bench = RepairBenchV2Runtime.Current.Bench;
            foreach (var n in new[] { "BV2_Pegboard", "BV2_WallShelf", "BV2_ShelfGoods", "BV2_WallPrints" })
                Assert.That(bench.GetComponentsInChildren<Renderer>(true).Any(r => r.name == n), Is.True, n);
            var leftovers = loaded.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true))
                .Where(r => r.enabled && r.gameObject.activeInHierarchy && !r.forceRenderingOff && r.GetComponent<TextMesh>() == null
                    && r.transform.GetComponentsInParent<Transform>(true).Any(t => t.name == "WorkbenchArea")).Select(r => r.name).ToList();
            Assert.That(leftovers, Is.Empty, "Nothing of the primitive workbench is drawn any more.");
        }

        [UnityTest]
        public IEnumerator StyleTour_RoomViewsForArtReview()
        {
            var mood = ClinicMoodRuntime.Current;
            Assert.That(mood != null && mood.FogOn, Is.True, "Art direction: distance fog unifies the room.");
            Assert.That(mood.Roles.Values.Count(r => r == ClinicMoodRuntime.Role.Surgical), Is.EqualTo(1), "One surgical key light, the old duplicate retired.");
            var cyber = ClinicCyberRuntime.Current;
            Assert.That(cyber != null && cyber.RoomKit != null && cyber.SurgeryKit != null, Is.True, "The cyberpunk dressing kits are placed.");
            Assert.That(cyber.RoomKit.activeSelf && cyber.SurgeryKit.activeSelf, Is.True);
            Assert.That(cyber.SurgeryKit.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("CY_Holo") || t.name.StartsWith("CY_Scan")), Is.False,
                "Setting: no holograms in this clinic (SETTING_PROPS.md).");
            Assert.That(cyber.CounterLight != null && cyber.SignLight != null, Is.True, "Neon-motivated lights exist.");
            var curtain = cyber.SurgeryKit.GetComponentsInChildren<Renderer>().First(r => r.name == "CY_Curtain");
            Assert.That(curtain.bounds.min.y, Is.GreaterThan(1.95f), "The strip curtain stays above head height.");
            Assert.That(curtain.GetComponent<Collider>(), Is.Null);
            // Six standing-height views around the table centre for the art-direction review (images only).
            // The first-person rig drives the room camera every frame, so the tour renders through a clone of it
            // (same URP data, retro look and PS2 pass), posed freely.
            var room = clinic.Robot.Flow.Rig.Cam;
            var cam = Object.Instantiate(room.gameObject, room.transform.position, room.transform.rotation).GetComponent<Camera>();
            foreach (Transform child in cam.transform) Object.Destroy(child.gameObject);
            foreach (var listener in cam.GetComponents<AudioListener>()) Object.Destroy(listener);
            cam.enabled = false;
            var centre = SurgeryTableV2Runtime.Current != null ? SurgeryTableV2Runtime.Current.Table.transform.position : Vector3.zero;
            var bench = RepairBenchV2Runtime.Current != null ? RepairBenchV2Runtime.Current.Bench.transform.position : centre + Vector3.forward * 2;
            var dock = GameObject.Find("UNIT07_ServiceDock") ?? GameObject.Find("Unit07ServiceDock");
            var dockAt = dock != null ? dock.transform.position : centre + Vector3.left * 2;
            var cyberKit = ClinicCyberRuntime.Current;
            var counter = cyberKit != null && cyberKit.CounterLight != null ? cyberKit.CounterLight.transform.position - Vector3.up * 1.2f : centre + Vector3.right * 2;
            var sign = cyberKit != null && cyberKit.SignLight != null ? cyberKit.SignLight.transform.position : centre - Vector3.forward * 3f;
            var shots = new (string name, Vector3 from, Vector3 to)[]
            {
                ("20-tour-bench", Vector3.Lerp(centre, bench, .45f) + Vector3.up * 1.6f, bench + Vector3.up * 1.0f),
                ("21-tour-dock", Vector3.Lerp(centre, dockAt, .4f) + Vector3.up * 1.6f, dockAt + Vector3.up * 1.0f),
                ("22-tour-counter", Vector3.Lerp(centre, counter, .4f) + Vector3.up * 1.6f, counter + Vector3.up * 1.0f),
                ("23-tour-entrance", new Vector3(sign.x, 1.55f, centre.z - .4f), sign + Vector3.down * .5f),
                ("26-tour-counter-neon", new Vector3(centre.x + .3f, 1.6f, counter.z - 1.4f), counter + Vector3.up * .6f),
                ("24-tour-gantry-below", centre + new Vector3(.75f, 1.45f, -.75f), centre + Vector3.up * 2.4f),
                ("25-tour-overview", centre + (centre - bench).normalized * 2.6f + Vector3.up * 2.6f, centre + Vector3.up * .6f),
                // Where the player actually stands to work each zone (room frame: bench north, counter east, dock west).
                ("27-zone-counter-front", centre + new Vector3(.75f, 1.6f, -.45f), centre + new Vector3(2.4f, .95f, -.45f)),
                ("28-zone-counter-side", centre + new Vector3(1.0f, 1.6f, -2.5f), centre + new Vector3(2.4f, 1.0f, .2f)),
                ("29-zone-dock-front", centre + new Vector3(-1.05f, 1.6f, .25f), centre + new Vector3(-3.0f, 1.0f, 0f)),
                ("30-zone-dock-side", centre + new Vector3(-1.3f, 1.6f, -1.8f), centre + new Vector3(-2.9f, .9f, .4f)),
                ("31-zone-bench-wall", centre + new Vector3(.1f, 1.62f, 1.75f), centre + new Vector3(0f, 1.5f, 3.4f)),
            };
            foreach (var shot in shots)
            {
                cam.transform.position = shot.from; cam.transform.LookAt(shot.to);
                yield return Shoot(shot.name, cam);
            }
            Object.Destroy(cam.gameObject);
        }

        [UnityTest]
        public IEnumerator People_CustomerCallAndDealer()
        {
            var npcs = ClinicNpcRuntime.Current;
            Assert.That(npcs != null, Is.True, "The NPC runtime installs with the clinic scene.");
            // The first order's owner (the receiver: Rin) walks in to the receive pad while its lamp is lit.
            yield return Until(() => npcs.Customer != null, "A customer appears for the waiting order.");
            Assert.That(npcs.Customer.NpcName, Is.EqualTo("Rin"));
            yield return Until(() => !npcs.Customer.Walking, "The customer reaches the counter.");
            var at = npcs.Customer.transform.position;
            Assert.That(at.x, Is.InRange(1.22f, 1.555f), "Stands in the customer strip.");
            Assert.That(Mathf.Abs(at.z + .62f), Is.LessThan(.05f), "In front of the receive pad.");
            Assert.That(npcs.Customer.GetComponentsInChildren<Collider>(), Is.Empty, "Visual only.");
            var face = npcs.Customer.Eye.position - npcs.Customer.transform.position; face.y = 0;
            Assert.That(Vector3.Dot(npcs.Customer.ModelForward, Vector3.right), Is.GreaterThan(.95f), "Faces the counter.");
            bool blinked = false;
            for (float t = 0; t < 7 && !blinked; t += Time.unscaledDeltaTime) { blinked |= npcs.Customer.EyesClosed; yield return null; }
            Assert.That(blinked, Is.True, "Face frames: the customer blinks.");
            var room = clinic.Robot.Flow.Rig.Cam;
            var cam = Object.Instantiate(room.gameObject, room.transform.position, room.transform.rotation).GetComponent<Camera>();
            foreach (Transform child in cam.transform) Object.Destroy(child.gameObject);
            foreach (var listener in cam.GetComponents<AudioListener>()) Object.Destroy(listener);
            cam.enabled = false;
            cam.transform.position = new Vector3(.35f, 1.58f, -1.55f); cam.transform.LookAt(at + Vector3.up * 1.2f);
            yield return Shoot("32-customer-at-counter", cam);
            cam.transform.position = at + npcs.Customer.ModelForward * .9f + Vector3.up * 1.45f; cam.transform.LookAt(at + Vector3.up * 1.38f);
            yield return Shoot("33-customer-close", cam);

            // A call: rings, picks up, the caller's live portrait talks, the line types out.
            var calls = ClinicCallRuntime.Current;
            Assert.That(calls != null, Is.True);
            Assert.That(calls.Begin("rent-n1", true), Is.True);
            Assert.That(calls.Ringing, Is.True);
            for (int i = 0; i < 20; i++) yield return null;
            yield return Shoot("34-call-ringing", room);
            calls.Pickup();
            yield return null;
            StringAssert.StartsWith("霞", calls.Subtitle);
            var booth = npcs.BoothNpc("Kasumi");
            Assert.That(booth != null && booth.gameObject.activeSelf, Is.True, "The caller's portrait booth renders while on the line.");
            Assert.That(calls.Card.GetComponentInChildren<UnityEngine.UI.RawImage>().texture, Is.Not.Null);
            bool talked = false;
            for (float t = 0; t < .6f && !talked; t += Time.unscaledDeltaTime) { talked |= booth.MouthOpen; yield return null; }
            Assert.That(talked, Is.True, "The caller's mouth moves while the line plays.");
            calls.Advance();
            yield return new WaitForSecondsRealtime(.9f);
            yield return Shoot("35-call-talking", room);
            for (int i = 0; i < 12; i++) calls.Advance();
            yield return new WaitForSecondsRealtime(1.3f);
            Assert.That(calls.Active, Is.False, "The call hangs up after its last line.");

            // The parts terminal shows the dealer on the line.
            var shop = Object.FindFirstObjectByType<RepairPropEconomyController>();
            Assert.That(shop.Open(), Is.True);
            for (int i = 0; i < 30; i++) yield return null;
            var panel = shop.GetComponent<RepairPropShopPanel>();
            yield return Until(() => panel.DeviceReady, "The view settles on the intake terminal.");
            Assert.That(panel.OnScreen, Is.True, "The parts program runs on the counter terminal's screen.");
            Assert.That(panel.VendorPortrait.texture, Is.Not.Null, "Dealer portrait feed in the terminal.");
            yield return new WaitForSecondsRealtime(1.2f);   // let the program finish booting
            yield return Shoot("36-shop-dealer", panel.DeviceCamera);
            shop.Close();

            // Receiving the order: the customer hands it over and leaves.
            Assert.That(clinic.Interact(ClinicTradeAction.Receive), Is.True, clinic.LastMessage);
            yield return Until(() => npcs.Customer == null, "The customer leaves once the order is taken.");
            Object.Destroy(cam.gameObject);
        }

        [UnityTest]
        public IEnumerator Storage_AndProjectedSigns_ReplaceTheOldOnes()
        {
            var storage = ClinicStorageV2Runtime.Current;
            Assert.That(storage != null && storage.Storage != null, Is.True, "The storage kit is placed at play time.");
            Assert.That(storage.HiddenCount, Is.GreaterThanOrEqualTo(10), "The plain cabinets, shelf and lockers are retired.");
            var environment = loaded.GetRootGameObjects().Where(r => r.name.StartsWith("UnifiedClinicEnvironment"))
                .SelectMany(r => r.GetComponentsInChildren<Renderer>(true)).ToList();
            foreach (var name in new[] { "Storage_NWCabinet", "Storage_NECabinet", "Storage_LimbShelf", "Storage_Lockers" })
                Assert.That(environment.Where(r => r.name == name).All(r => r.forceRenderingOff), Is.True, name + " retired");
            Assert.That(environment.Any(r => r.name == "Clutter_West_Rubber" && !r.forceRenderingOff), Is.True, "The dock cables stay.");
            var signage = ClinicSignageRuntime.Current;
            Assert.That(signage != null, Is.True);
            yield return Until(() => signage.SignNames.Count >= 7, "Projections are built once the kits are placed.");
            foreach (var name in new[] { "PJ_Door", "PJ_Hours", "PJ_Receive", "PJ_PriceList", "PJ_Workbench", "PJ_Pad_Receive", "PJ_Pad_Deliver" })
                Assert.That(signage.SignNames, Does.Contain(name));
            var kit = ClinicCyberRuntime.Current.RoomKit.GetComponentsInChildren<Renderer>(true);
            Assert.That(kit.Where(r => r.name.StartsWith("CY_Sign_Main_") || r.name == "CY_Sign_24H").All(r => r.forceRenderingOff), Is.True, "The Chinese neon letters are off.");
            foreach (var caption in Object.FindObjectsByType<ClinicTradeZone>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                         .Select(z => z.GetComponentInChildren<TextMesh>(true)).Where(t => t != null))
                Assert.That(caption.GetComponent<Renderer>().forceRenderingOff, Is.True, caption.text + " caption is projected instead");
            var room = clinic.Robot.Flow.Rig.Cam;
            var cam = Object.Instantiate(room.gameObject, room.transform.position, room.transform.rotation).GetComponent<Camera>();
            foreach (Transform child in cam.transform) Object.Destroy(child.gameObject);
            foreach (var listener in cam.GetComponents<AudioListener>()) Object.Destroy(listener);
            cam.enabled = false;
            var shots = new (string name, Vector3 from, Vector3 to)[]
            {
                ("37-storage-nw", new Vector3(-.55f, 1.6f, .55f), new Vector3(-2.25f, 1.0f, 2.3f)),
                ("38-storage-ne", new Vector3(.35f, 1.6f, .85f), new Vector3(1.98f, 1.05f, 2.83f)),
                ("39-storage-sw", new Vector3(-.5f, 1.6f, -.5f), new Vector3(-2.3f, 1.0f, -2.3f)),
                ("40-storage-lockers", new Vector3(.45f, 1.6f, -1.45f), new Vector3(1.17f, 1.0f, -3.2f)),
                ("41-signs-door", new Vector3(0f, 1.6f, -1.1f), new Vector3(0f, 2.35f, -3.4f)),
                ("42-signs-counter", new Vector3(.45f, 1.6f, .1f), new Vector3(2.27f, 1.75f, 0f)),
                ("43-signs-bench", new Vector3(.1f, 1.6f, 1.35f), new Vector3(0f, 2.15f, 3.4f)),
            };
            foreach (var shot in shots)
            {
                cam.transform.position = shot.from; cam.transform.LookAt(shot.to);
                yield return Shoot(shot.name, cam);
            }
            Object.Destroy(cam.gameObject);
        }

        static readonly (string sign, Vector3[] from)[] HoloVantages =
        {
            ("PJ_Door", new[] { new Vector3(0f, 1.6f, -1.1f), new Vector3(-1.2f, 1.6f, -1.6f), new Vector3(1.0f, 1.6f, -1.6f), new Vector3(0f, 1.6f, 1.0f) }),
            ("PJ_Hours", new[] { new Vector3(-1.06f, 1.6f, -1.1f), new Vector3(.2f, 1.6f, -1.2f), new Vector3(-2f, 1.6f, -1.6f) }),
            ("PJ_Receive", new[] { new Vector3(1.0f, 1.6f, -1.8f), new Vector3(.2f, 1.6f, -2.4f), new Vector3(1.1f, 1.6f, -.9f) }),
            ("PJ_PriceList", new[] { new Vector3(.45f, 1.6f, .1f), new Vector3(.5f, 1.6f, -1.4f), new Vector3(.5f, 1.6f, 1.4f), new Vector3(-1.2f, 1.6f, 0f) }),
            ("PJ_Workbench", new[] { new Vector3(.1f, 1.6f, 1.35f), new Vector3(-1.2f, 1.6f, 1.6f), new Vector3(1.1f, 1.6f, 1.6f), new Vector3(0f, 1.6f, 0f) }),
            ("PJ_Pad_Receive", new[] { new Vector3(.9f, 1.6f, -.62f), new Vector3(.6f, 1.6f, -1.5f), new Vector3(.6f, 1.6f, .2f) }),
            ("PJ_Pad_Deliver", new[] { new Vector3(.9f, 1.6f, .62f), new Vector3(.6f, 1.6f, -.2f), new Vector3(.6f, 1.6f, 1.5f) }),
        };

        // The panels' depth test against the room. Each sign is rendered three times in one frame from where a player
        // stands: light off, depth tested, and with ZTest Always as the "nothing ever hides it" baseline. Pixels the
        // baseline lights and the depth tested render does not are the ones something in the room cuts off.
        [UnityTest]
        public IEnumerator ProjectedSigns_PeopleOcclude_WallClutterDoesNot()
        {
            var signage = ClinicSignageRuntime.Current;
            Assert.That(signage != null, Is.True);
            yield return Until(() => signage.SignNames.Count >= 7, "Projections are built once the kits are placed.");
            yield return new WaitForSecondsRealtime(1.5f);   // past the staggered materialise
            var npcs = ClinicNpcRuntime.Current;
            yield return Until(() => npcs.Customer != null && !npcs.Customer.Walking, "A customer stands at the counter.");
            foreach (var renderer in npcs.Customer.GetComponentsInChildren<Renderer>())
                foreach (var material in renderer.sharedMaterials)
                    if (material != null) Assert.That(material.renderQueue, Is.LessThanOrEqualTo(2500), renderer.name + " must write depth to hide a hologram.");
            var room = clinic.Robot.Flow.Rig.Cam;
            var cam = Object.Instantiate(room.gameObject, room.transform.position, room.transform.rotation).GetComponent<Camera>();
            foreach (Transform child in cam.transform) Object.Destroy(child.gameObject);
            foreach (var listener in cam.GetComponents<AudioListener>()) Object.Destroy(listener);
            cam.enabled = false;
            bool wasPs2 = RetroPS2Settings.Enabled;
            RetroPS2Settings.Enabled = false;   // measure the raw picture: the PS2 dither would read as coverage changes
            var report = new System.Text.StringBuilder();
            var scene = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            // The customer stands at the pad, which is the wanted occlusion: this table is about the room itself.
            npcs.Customer.gameObject.SetActive(false);
            foreach (var (sign, from) in HoloVantages)
            {
                var graphics = signage.SignRoot(sign).GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
                Assert.That(graphics.Any(g => g is HoloWidget), Is.True, sign + " carries a motion graphic.");
                var centre = signage.SignRoot(sign).GetComponentInChildren<Canvas>(true).transform.position;
                foreach (var at in from)
                {
                    cam.transform.position = at; cam.transform.LookAt(centre);
                    float blocked = HoloBlockedFraction(cam, graphics, out int lit);
                    report.AppendLine($"{sign} from {at}: {blocked:P1} blocked of {lit} px{(blocked > .005f ? "  <- " + HoloBlockers(scene, at, signage.SignRoot(sign)) : "")}");
                }
            }
            npcs.Customer.gameObject.SetActive(true);

            // A person standing between the viewer and the door sign, 40 % of the way along the sight line. The sign's lower
            // edge is 2.37 m up, so from eye height (1.6 m) a person that far along would have to be over 1.9 m tall to touch
            // it at all. The feet stay on the floor and the camera height is solved instead, from the person's measured
            // height, so that the line to the panel's centre passes through their head.
            const float personAlongSightLine = .4f, headOnBody = .92f;
            var person = Object.Instantiate(npcs.Customer.gameObject);
            foreach (var script in person.GetComponentsInChildren<MonoBehaviour>()) script.enabled = false;
            var doorCanvas = (RectTransform)signage.SignRoot("PJ_Door").GetComponentInChildren<Canvas>(true).transform;
            var doorCentre = doorCanvas.position;
            var doorGraphics = signage.SignRoot("PJ_Door").GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
            var doorFloor = new Vector3(0f, 0f, -1.1f);
            var personFloor = Vector3.Lerp(doorFloor, new Vector3(doorCentre.x, 0f, doorCentre.z), personAlongSightLine);
            person.transform.SetPositionAndRotation(personFloor, Quaternion.LookRotation(doorFloor - personFloor, Vector3.up));
            yield return null;   // let the clone's renderers register and take their pose
            var personRenderers = person.GetComponentsInChildren<Renderer>();
            Bounds PersonBounds()
            {
                var all = personRenderers[0].bounds;
                foreach (var r in personRenderers) all.Encapsulate(r.bounds);
                return all;
            }
            float headHeight = PersonBounds().max.y * headOnBody;
            var doorFrom = doorFloor + Vector3.up * Mathf.Max(.35f, (headHeight - personAlongSightLine * doorCentre.y) / (1f - personAlongSightLine));
            cam.transform.position = doorFrom; cam.transform.LookAt(doorCentre);
            person.SetActive(false);
            var doorClear = HoloMap.Measure(cam, doorGraphics);
            person.SetActive(true);
            yield return null;
            float personCoverage = doorClear.CoveredBy(HoloMasks.Measure(cam, personRenderers, doorCanvas));
            var doorCorners = new Vector3[4];
            doorCanvas.GetWorldCorners(doorCorners);
            string doorPlacement = $"camera {doorFrom:F3}, person root {person.transform.position:F3}, person bounds {PersonBounds().min:F3} to {PersonBounds().max:F3}, panel centre {doorCentre:F3}, panel corners {doorCorners[0]:F3} to {doorCorners[2]:F3}, silhouette on {personCoverage:P1} of the panel";
            if (personCoverage < .05f) Debug.LogWarning("[HoloOcclusion] The person does not cover the door sign: " + doorPlacement);
            var doorPerson = HoloSplit.Measure(cam, doorGraphics, personRenderers, doorCanvas, doorClear);
            report.AppendLine($"PJ_Door, person: {doorPerson}; blocked with nobody there: {doorClear.BlockedFraction:P1}; {doorPlacement}");

            // A thin slab through the plane of the door sign, one end in front of it and the other between it and the
            // wall: the same frame must hide the sign behind the front end and draw the sign over the back end.
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = "Slab (stand-in)"; Object.Destroy(slab.GetComponent<Collider>());
            slab.transform.localScale = new Vector3(.5f, .2f, .01f);
            slab.transform.SetPositionAndRotation(doorCentre, doorCanvas.rotation * Quaternion.Euler(0f, 25f, 0f));
            person.SetActive(false);
            var doorSlab = HoloSplit.Measure(cam, doorGraphics, new[] { slab.GetComponent<Renderer>() }, doorCanvas, doorClear);
            report.AppendLine($"PJ_Door, slab through its plane: {doorSlab}");
            Object.Destroy(slab);
            person.SetActive(true);

            // A held box close to the camera at the bench sign.
            var benchCanvas = (RectTransform)signage.SignRoot("PJ_Workbench").GetComponentInChildren<Canvas>(true).transform;
            var benchCentre = benchCanvas.position;
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "HeldBox (stand-in)"; Object.Destroy(box.GetComponent<Collider>());
            box.transform.localScale = new Vector3(.3f, .2f, .3f);
            var benchFrom = new Vector3(.1f, 1.6f, 1.35f);
            cam.transform.position = benchFrom; cam.transform.LookAt(benchCentre);
            var benchGraphics = signage.SignRoot("PJ_Workbench").GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
            box.SetActive(false);
            person.SetActive(false);
            var benchClear = HoloMap.Measure(cam, benchGraphics);
            box.SetActive(true);
            box.transform.position = Vector3.Lerp(benchFrom, benchCentre, .35f) + new Vector3(.12f, 0f, 0f);
            var benchBox = HoloSplit.Measure(cam, benchGraphics, new[] { box.GetComponent<Renderer>() }, benchCanvas, benchClear);
            report.AppendLine($"PJ_Workbench, held box: {benchBox}; blocked with nothing there: {benchClear.BlockedFraction:P1}");
            person.SetActive(true);
            RetroPS2Settings.Enabled = wasPs2;

            Debug.Log("[HoloOcclusion]\n" + report);
            if (!string.IsNullOrEmpty(Folder)) { Directory.CreateDirectory(Folder); File.WriteAllText(Path.Combine(Folder, "holo-occlusion.txt"), report.ToString()); }

            // The pictures, with the PS2 pass as the player sees them.
            cam.transform.position = doorFrom; cam.transform.LookAt(doorCentre);
            box.SetActive(false);
            yield return Shoot("50-holo-door-person", cam);
            box.SetActive(true);
            person.SetActive(false);
            cam.transform.position = benchFrom; cam.transform.LookAt(benchCentre);
            yield return Shoot("51-holo-bench-box", cam);
            box.SetActive(false);
            yield return Shoot("52-holo-bench-clear", cam);
            cam.transform.position = new Vector3(.35f, 1.58f, -1.55f); cam.transform.LookAt(npcs.Customer.transform.position + Vector3.up * .9f);
            yield return Shoot("53-holo-pad-customer", cam);
            Object.Destroy(person); Object.Destroy(box); Object.Destroy(cam.gameObject);

            // Last, so a failure still leaves the report and the pictures, and the PS2 pass back on for the next test.
            doorPerson.AssertInFrontHides($"a person in front of the door sign ({doorPlacement})");
            doorSlab.AssertInFrontHides("the front end of the slab");
            doorSlab.AssertBehindDoesNotHide("the back end of the slab");
            benchBox.AssertInFrontHides("a carried box in front of the bench sign");
        }

        // Motion graphics only ever use the room the text leaves, and the two signs read from across the room stay big
        // enough to read. Glyph extents come from the text generator, so this holds for the fonts' real metrics.
        [UnityTest]
        public IEnumerator ProjectedSigns_Graphics_StayOffTheText()
        {
            var signage = ClinicSignageRuntime.Current;
            Assert.That(signage != null, Is.True);
            yield return Until(() => signage.SignNames.Count >= 7, "Projections are built once the kits are placed.");
            foreach (var sign in signage.SignNames)
            {
                var texts = signage.SignRoot(sign).GetComponentsInChildren<UnityEngine.UI.Text>(true);
                Assert.That(texts, Is.Not.Empty, sign + " has lettering.");
                foreach (var text in texts)
                {
                    var content = (RectTransform)text.transform.parent;
                    var ink = HoloInk(text, content);
                    Assert.That(ink.width, Is.GreaterThan(0f), $"{sign} '{text.text}' generated no glyphs.");
                    // The small hours panel keeps a visible 4 mm between its fill bar and its lettering.
                    if (sign == "PJ_Hours") ink = Rect.MinMaxRect(ink.xMin - 4f, ink.yMin - 4f, ink.xMax + 4f, ink.yMax + 4f);
                    foreach (var widget in content.GetComponentsInChildren<HoloWidget>(true))
                    {
                        var corners = new Vector3[4];
                        widget.rectTransform.GetWorldCorners(corners);
                        Vector2 min = content.InverseTransformPoint(corners[0]), max = content.InverseTransformPoint(corners[2]);
                        var area = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                        Assert.That(area.Overlaps(ink), Is.False, $"{sign}: {widget.WidgetKind} {area} draws over '{text.text}' {ink}.");
                    }
                    // 288 virtual lines put ~12 mm in a virtual pixel at 3 m, and glyphs under ~4 of them are noise.
                    if (sign == "PJ_Door" || sign == "PJ_PriceList")
                        Assert.That(text.fontSize, Is.GreaterThanOrEqualTo(50), $"{sign} '{text.text}' is too small to read across the room.");
                }
            }
        }

        // The rectangle in `content` that a Text's glyphs actually cover. A fresh generator, so signs that are switched
        // off (the workbench and the pads until the director shows them) get measured too.
        static Rect HoloInk(UnityEngine.UI.Text text, RectTransform content)
        {
            var settings = text.GetGenerationSettings(text.rectTransform.rect.size);
            var generator = new TextGenerator();
            generator.Populate(text.text, settings);
            var verts = generator.verts;
            float unit = 1f / Mathf.Max(settings.scaleFactor, .0001f);
            bool any = false;
            Vector2 min = default, max = default;
            for (int i = 0; i + 3 < verts.Count; i += 4)
            {
                Vector2 a = verts[i].position, c = verts[i + 2].position;
                if (Mathf.Abs((c.x - a.x) * (c.y - a.y)) < 1f) continue;   // spaces and the generator's end padding
                foreach (var corner in new[] { a, c })
                {
                    Vector2 p = content.InverseTransformPoint(text.rectTransform.TransformPoint(corner * unit));
                    min = any ? Vector2.Min(min, p) : p;
                    max = any ? Vector2.Max(max, p) : p;
                    any = true;
                }
            }
            return any ? Rect.MinMaxRect(min.x, min.y, max.x, max.y) : default;
        }

        // Motion graphics: every widget animates from one static mesh, the frames differ, and the pictures over time.
        [UnityTest]
        public IEnumerator ProjectedSigns_MotionGraphics_AnimateWithoutRebuilding()
        {
            var signage = ClinicSignageRuntime.Current;
            Assert.That(signage != null, Is.True);
            yield return Until(() => signage.SignNames.Count >= 7, "Projections are built once the kits are placed.");
            yield return new WaitForSecondsRealtime(1.5f);
            var widgets = signage.SignNames.SelectMany(n => signage.SignRoot(n).GetComponentsInChildren<HoloWidget>(true)).ToArray();
            Assert.That(widgets.Length, Is.GreaterThan(7 * 8), "Each panel has 8 corner bars plus its graphics.");
            yield return null;
            var builds = widgets.Select(w => w.MeshBuilds).ToArray();
            var room = clinic.Robot.Flow.Rig.Cam;
            var cam = Object.Instantiate(room.gameObject, room.transform.position, room.transform.rotation).GetComponent<Camera>();
            foreach (Transform child in cam.transform) Object.Destroy(child.gameObject);
            foreach (var listener in cam.GetComponents<AudioListener>()) Object.Destroy(listener);
            cam.enabled = false;
            var shots = new (string name, string sign, Vector3 from)[]
            {
                ("60-motion-door", "PJ_Door", new Vector3(0f, 1.6f, -1.3f)),
                ("61-motion-hours", "PJ_Hours", new Vector3(-1.06f, 1.7f, -2.2f)),
                ("62-motion-receive", "PJ_Receive", new Vector3(.9f, 1.6f, -1.7f)),
                ("63-motion-prices", "PJ_PriceList", new Vector3(.45f, 1.6f, 0f)),
                ("64-motion-bench", "PJ_Workbench", new Vector3(.1f, 1.6f, 1.6f)),
                ("65-motion-pad", "PJ_Pad_Receive", new Vector3(.95f, 1.3f, -.62f)),
            };
            foreach (var shot in shots)
            {
                var centre = signage.SignRoot(shot.sign).GetComponentInChildren<Canvas>(true).transform.position;
                cam.transform.position = shot.from; cam.transform.LookAt(centre);
                for (int frame = 0; frame < 3; frame++)
                {
                    yield return Shoot(shot.name + "-t" + frame, cam);
                    yield return new WaitForSecondsRealtime(1.1f);
                }
            }
            for (int i = 0; i < widgets.Length; i++)
                Assert.That(widgets[i].MeshBuilds, Is.EqualTo(builds[i]), widgets[i].name + " must not rebuild its mesh to animate.");
            Object.Destroy(cam.gameObject);
        }

        // Fraction of the panel's lit pixels that the depth test removes, for the current camera.
        static float HoloBlockedFraction(Camera cam, UnityEngine.UI.Graphic[] graphics, out int lit)
        {
            var map = HoloMap.Measure(cam, graphics);
            lit = map.LitCount;
            return map.BlockedFraction;
        }

        const int HoloWidth = 640, HoloHeight = 360;

        static Color32[] HoloGrab(Camera cam)
        {
            var target = RenderTexture.GetTemporary(HoloWidth, HoloHeight, 24, RenderTextureFormat.ARGB32);
            var previousTarget = cam.targetTexture; var previousActive = RenderTexture.active;
            cam.targetTexture = target; cam.Render(); cam.targetTexture = previousTarget;
            var read = new Texture2D(HoloWidth, HoloHeight, TextureFormat.RGBA32, false);
            RenderTexture.active = target; read.ReadPixels(new Rect(0, 0, HoloWidth, HoloHeight), 0, 0);
            var pixels = read.GetPixels32();
            RenderTexture.active = previousActive; RenderTexture.ReleaseTemporary(target); Object.Destroy(read);
            return pixels;
        }

        // Per pixel: which ones the panel lights, and which of those the depth test removes. Three renders within one
        // frame, so the panels' animation, the room's lamps and the camera are identical: light off, the panel as it is
        // really set up, and with ZTest Always as the "nothing ever hides it" baseline. The middle one keeps the material's
        // own _ZTest, so a panel switched to drawing through things shows up here as nothing being blocked.
        sealed class HoloMap
        {
            public bool[] Lit, Blocked;
            public int LitCount => Lit.Count(p => p);
            public float BlockedFraction { get { int lit = LitCount; return lit == 0 ? 0f : Blocked.Count(p => p) / (float)lit; } }

            public static HoloMap Measure(Camera cam, UnityEngine.UI.Graphic[] graphics)
            {
                var materials = graphics.Select(g => g.material).Distinct().ToArray();
                Color32[] Render(bool lightOff, bool throughEverything)
                {
                    var intensity = materials.Select(m => m.GetFloat("_Intensity")).ToArray();
                    var test = materials.Select(m => m.GetFloat("_ZTest")).ToArray();
                    foreach (var m in materials)
                    {
                        if (lightOff) m.SetFloat("_Intensity", 0f);
                        if (throughEverything) m.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
                    }
                    var pixels = HoloGrab(cam);
                    for (int i = 0; i < materials.Length; i++) { materials[i].SetFloat("_Intensity", intensity[i]); materials[i].SetFloat("_ZTest", test[i]); }
                    return pixels;
                }
                var off = Render(true, false);
                var tested = Render(false, false);
                var always = Render(false, true);
                int Delta(Color32 a, Color32 b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
                var map = new HoloMap { Lit = new bool[off.Length], Blocked = new bool[off.Length] };
                for (int i = 0; i < off.Length; i++)
                {
                    map.Lit[i] = Delta(always[i], off[i]) >= 8;
                    // Lower than the lit test: the same faint plate adds very little over a bright hat or vest.
                    map.Blocked[i] = map.Lit[i] && Delta(always[i], tested[i]) >= 4;
                }
                return map;
            }

            // How much of this panel's light the subject's silhouette lands on, in front of the panel or behind it.
            public float CoveredBy(HoloMasks subject)
            {
                int lit = LitCount;
                if (lit == 0) return 0f;
                int covered = 0;
                for (int i = 0; i < Lit.Length; i++) if (Lit[i] && subject.All[i]) covered++;
                return covered / (float)lit;
            }
        }

        // Where a stand-in is on screen, judged by the depth buffer rather than by assuming where it stands. It draws
        // white on its own over black, then again with an opaque black sheet in the plane of the panel's backmost layer:
        // whatever stays white has to be in front of the panel.
        sealed class HoloMasks
        {
            public bool[] All, InFront;

            public static HoloMasks Measure(Camera cam, Renderer[] subject, RectTransform panel)
            {
                const int maskLayer = 31;
                var white = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                white.SetColor("_BaseColor", Color.white);
                var black = new Material(white);
                black.SetColor("_BaseColor", Color.black);
                black.SetFloat("_Cull", 0f);
                var sheet = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.Destroy(sheet.GetComponent<Collider>());
                sheet.layer = maskLayer;
                // The glow plate sits 12 canvas units (mm) behind the lettering; 1.3x so the sheet covers the whole panel.
                sheet.transform.SetPositionAndRotation(panel.TransformPoint(0f, 0f, 12f), panel.rotation);
                sheet.transform.localScale = new Vector3(panel.rect.width * .0013f, panel.rect.height * .0013f, 1f);
                sheet.GetComponent<Renderer>().sharedMaterial = black;

                var layers = subject.Select(r => r.gameObject.layer).ToArray();
                var looks = subject.Select(r => r.sharedMaterials).ToArray();
                for (int i = 0; i < subject.Length; i++)
                {
                    subject[i].gameObject.layer = maskLayer;
                    subject[i].sharedMaterials = Enumerable.Repeat(white, looks[i].Length).ToArray();
                }
                int mask = cam.cullingMask; var clear = cam.clearFlags; var background = cam.backgroundColor;
                cam.cullingMask = 1 << maskLayer; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;

                var withSheet = HoloGrab(cam);
                sheet.SetActive(false);
                var alone = HoloGrab(cam);

                cam.cullingMask = mask; cam.clearFlags = clear; cam.backgroundColor = background;
                for (int i = 0; i < subject.Length; i++) { subject[i].gameObject.layer = layers[i]; subject[i].sharedMaterials = looks[i]; }
                Object.Destroy(sheet); Object.Destroy(white); Object.Destroy(black);
                return new HoloMasks { All = alone.Select(p => p.r > 64).ToArray(), InFront = withSheet.Select(p => p.r > 64).ToArray() };
            }
        }

        // The panel's light under a stand-in, split by which side of the panel the stand-in is on. Pixels the room already
        // hides with the stand-in gone are left out, so what remains is the stand-in's doing alone.
        sealed class HoloSplit
        {
            int total, frontLit, frontBlocked, behindLit, behindBlocked;

            public static HoloSplit Measure(Camera cam, UnityEngine.UI.Graphic[] graphics, Renderer[] subject, RectTransform panel, HoloMap clear)
            {
                // The stand-in draws black here (same depth, same silhouette). Over a lit surface the graded picture
                // compresses a faint plate's added light below the map's thresholds, and hidden plate pixels stop counting.
                var black = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                black.SetColor("_BaseColor", Color.black);
                var looks = subject.Select(r => r.sharedMaterials).ToArray();
                for (int i = 0; i < subject.Length; i++) subject[i].sharedMaterials = Enumerable.Repeat(black, looks[i].Length).ToArray();
                var map = HoloMap.Measure(cam, graphics);
                for (int i = 0; i < subject.Length; i++) subject[i].sharedMaterials = looks[i];
                Object.Destroy(black);
                var masks = HoloMasks.Measure(cam, subject, panel);
                var split = new HoloSplit { total = clear.LitCount };
                for (int i = 0; i < map.Lit.Length; i++)
                {
                    if (!clear.Lit[i] || clear.Blocked[i] || !masks.All[i]) continue;
                    if (masks.InFront[i]) { split.frontLit++; if (map.Blocked[i]) split.frontBlocked++; }
                    else { split.behindLit++; if (map.Blocked[i]) split.behindBlocked++; }
                }
                return split;
            }

            static float Share(int part, int whole) => whole == 0 ? 0f : part / (float)whole;

            public override string ToString() =>
                $"in front covers {frontLit} of {total} lit px, {Share(frontBlocked, frontLit):P1} of them hidden; behind covers {behindLit} px, {Share(behindBlocked, behindLit):P1} of them hidden";

            // At least 5 % of the panel, or the stand-in is not really in the way and the measurement says nothing. 85 %, not
            // 100 %: the clear map comes from an earlier frame, so the panel's sway moves a few pixels at the silhouette's edge.
            public void AssertInFrontHides(string what)
            {
                Assert.That(frontLit, Is.GreaterThanOrEqualTo(Mathf.Max(1, total / 20)), $"The check needs {what} to cover the panel: {this}");
                Assert.That(Share(frontBlocked, frontLit), Is.GreaterThanOrEqualTo(.85f), $"The hologram must not draw through {what}: {this}");
            }

            public void AssertBehindDoesNotHide(string what)
            {
                Assert.That(behindLit, Is.GreaterThanOrEqualTo(Mathf.Max(1, total / 100)), $"The check needs {what} to sit behind the panel: {this}");
                Assert.That(Share(behindBlocked, behindLit), Is.LessThanOrEqualTo(.1f), $"The hologram must draw over {what}: {this}");
            }
        }

        // Which scene renderers' boxes the sight line to the panel passes through, for the report.
        static string HoloBlockers(Renderer[] scene, Vector3 from, GameObject sign)
        {
            var canvas = (RectTransform)sign.GetComponentInChildren<Canvas>(true).transform;
            var corners = new Vector3[4];
            canvas.GetWorldCorners(corners);
            var targets = corners.Select(c => Vector3.Lerp(c, canvas.position, .1f)).Append(canvas.position).ToArray();
            var names = scene.Where(r => r != null && r.enabled && r.gameObject.activeInHierarchy && !r.forceRenderingOff && !r.transform.IsChildOf(sign.transform)
                    && r.GetComponent<CanvasRenderer>() == null && targets.Any(t => r.bounds.IntersectRay(new Ray(from, t - from), out float d) && d < (t - from).magnitude - .02f))
                .Select(r => r.name).Distinct().Take(8);
            return string.Join(", ", names);
        }

        [UnityTest]
        public IEnumerator MainMenu_FromLauncher_ReturnsToThisClinic()
        {
            var launcher = RepairPropLauncher.Current;
            Assert.That(launcher != null, Is.True);
            launcher.Toggle(true);
            launcher.MenuEntry.onClick.Invoke();
            yield return Until(() => SceneManager.GetActiveScene().name == TwoNightScenes.ClinicMenu, "The clinic menu opens.");
            Assert.That(RepairPropLauncher.ReturnScenePath, Is.EqualTo(ScenePath));
            // What the menu's continue / new game does: load the clinic scene by name.
            SceneManager.LoadScene(TwoNightScenes.Clinic);
            yield return Until(() => SceneManager.GetActiveScene().path == ScenePath, "Back in the desktop clinic, not the main game's clinic.");
            yield return Until(() => (clinic = Object.FindFirstObjectByType<UnifiedClinicDirector>()) != null && clinic.Initialized, "The clinic initializes again.");
            loaded = SceneManager.GetActiveScene();
        }

        [UnityTest]
        public IEnumerator Shop_FromLauncher_OverWorkspace_KeepsAppearanceClosed()
        {
            Assert.That(clinic.Interact(ClinicTradeAction.Receive), Is.True, clinic.LastMessage);
            bridge = clinic.Station.Inspector.CurrentItem.GetComponent<RepairPropStationBridge>();
            yield return Until(() => Flow.Session != null, "Bridge must attach.");
            clinic.Counter.DismissDialogue();
            Assert.That(clinic.Interact(ClinicTradeAction.Console), Is.True, clinic.LastMessage);
            yield return Until(() => bridge.CanInteract, "Bench must be interactive.");
            for (int i = 0; i < 20; i++) yield return null;
            var launcher = RepairPropLauncher.Current;
            launcher.Toggle(true);
            launcher.ShopEntry.onClick.Invoke();
            Assert.That(Object.FindFirstObjectByType<RepairPropEconomyController>().IsOpen, Is.True, "The shop opens over the workspace.");
            for (int i = 0; i < 20; i++) yield return null;
            yield return Shoot("20-shop-over-workspace", View);
            DumpCanvases("20-shop-over-workspace");
            Assert.That(Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None).Any(r => r.name == "外观面板" && r.gameObject.activeInHierarchy),
                Is.False, "Opening the parts shop must not pop the appearance editor out of the hidden workspace.");
        }

        [UnityTest]
        public IEnumerator Night2_PlayerView_UiTour()
        {
            var s = TwoNightRun.NewGame(null, true);
            TwoNightRun.ReceiveCommunicator(s, "case_n1_collector_communicator");
            TwoNightRun.CompleteCommunicatorRepair(s, true, s.communicatorCaseId);
            TwoNightRun.DeliverCommunicator(s, s.communicatorCaseId, null);
            TwoNightRun.ConfirmLedger(s);
            TwoNightRun.Set(s);
            clinic.BeginRobotNight();
            yield return Until(() => clinic.Robot.MenuButton != null, "The robot part of night 1 builds its end panel.");
            for (int i = 0; i < 3; i++) yield return null;
            TwoNightRun.FinishIncident(s);
            Assert.That(TwoNightRun.RegisterUnit07(s, new Unit07SafeState { seated = true, clamped = true,
                powerOff = true, rotorsStopped = true, trayStowed = true, robotUpright = true }, out _), Is.True);
            // Night 1 ended in this clinic and was saved; its end panel goes to the menu, and "continue" must bring the
            // player back here for night 2 (not to the main game's clinic, and not reset to a fresh night 1).
            Assert.That(TwoNightSave.Write(s, out _), Is.True);
            clinic.Robot.MenuButton.onClick.Invoke();
            yield return Until(() => SceneManager.GetActiveScene().name == TwoNightScenes.ClinicMenu, "The night-end button opens the clinic menu.");
            Assert.That(RepairPropLauncher.ReturnScenePath, Is.EqualTo(ScenePath), "Leaving from the night-end panel remembers this clinic.");
            yield return null;
            var menu = Object.FindFirstObjectByType<TwoNightMenu>();
            Assert.That(menu.ContinueButton.interactable, Is.True);
            menu.ContinueButton.onClick.Invoke();
            yield return Until(() => SceneManager.GetActiveScene().path == ScenePath, "Continue returns to the desktop clinic.");
            loaded = SceneManager.GetActiveScene();
            yield return Until(() => (clinic = Object.FindFirstObjectByType<UnifiedClinicDirector>()) != null && clinic.Initialized, "Night 2 clinic must initialize.");
            Assert.That(clinic.State.phase, Is.EqualTo(TwoNightPhase.Night2Open));
            var rig = clinic.Robot.Flow.Rig;
            rig.Walker.LeaveSystemCursor = true;
            for (int i = 0; i < 40; i++) yield return null;
            // Night 2 wears the bench workspace's layout: the right-hand column and the guide band replace the old
            // slice bars, which stay alive (hidden) behind it.
            var workspace = RobotNightWorkspace.Current;
            Assert.That(workspace != null && workspace.Visible, Is.True, "The night-2 workspace column is shown.");
            var oldStatus = Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None).First(r => r.name == "Status" && r.GetComponentInParent<BorderRepair.FirstOrder.Slice.SliceView>() != null);
            Assert.That(oldStatus.GetComponent<Canvas>() != null && !oldStatus.GetComponent<Canvas>().enabled, Is.True, "The old status bar is hidden.");
            StringAssert.StartsWith("提示 // 下一步：", workspace.GuideText);
            StringAssert.Contains("步骤", workspace.StepText);
            yield return Shoot("30-night2-manual", rig.Cam); DumpCanvases("30-night2-manual");
            var manualCorners = new Vector3[4]; var columnCorners = new Vector3[4];
            Object.FindFirstObjectByType<BorderRepair.FirstOrder.Slice.SliceView>().ManualPanelRect.GetWorldCorners(manualCorners);
            workspace.PanelRectTransform.GetWorldCorners(columnCorners);
            Assert.That(manualCorners[2].x, Is.LessThanOrEqualTo(columnCorners[0].x), "The manual sits left of the column, not under it.");
            workspace.ManualButton.onClick.Invoke();
            Assert.That(Object.FindFirstObjectByType<BorderRepair.FirstOrder.Slice.SliceView>().ManualOpen, Is.False, "The column's manual button closes the manual.");
            yield return Shoot("31-night2-walk", rig.Cam);
            foreach (var view in new[] { BorderRepair.FirstOrder.FirstOrderCameraRig.Dock, BorderRepair.FirstOrder.FirstOrderCameraRig.EngineL, BorderRepair.FirstOrder.FirstOrderCameraRig.Bench })
            {
                workspace.ViewButton(view).onClick.Invoke();
                yield return Until(() => rig.Current == view, "The column's view button moves the camera to " + view);
                for (int i = 0; i < 60; i++) yield return null;
                yield return Shoot("32-night2-" + view, rig.Cam);
            }
            Assert.That(workspace.BackButton.interactable, Is.True, "From a preset view the column offers the way back to walking.");
            workspace.BackButton.onClick.Invoke();
            yield return Until(() => rig.Walking, "Back returns to walking.");
        }

        static void DumpCanvases(string state)
        {
            if (string.IsNullOrEmpty(Folder)) return;
            var lines = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .Where(c => c.isRootCanvas && c.enabled && c.gameObject.activeInHierarchy && c.renderMode != RenderMode.WorldSpace)
                .OrderBy(c => c.sortingOrder)
                .Select(c => c.sortingOrder + "  " + PathOf(c.transform) + "\n    " + string.Join(" | ",
                    c.GetComponentsInChildren<UnityEngine.UI.Text>().Where(t => t.isActiveAndEnabled && t.text.Length > 0)
                        .Select(t => t.name + "=" + t.text.Replace("\n", "⏎")).Take(14)));
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, state + "-canvases.txt"), string.Join("\n", lines));
        }

        static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

        Camera View => clinic.Station.Inspector.ViewCamera;

        [UnityTest]
        public IEnumerator Assistant_Unit07HaulsPartsAndTalks()
        {
            var assistant = ClinicAssistantRuntime.Current;
            Assert.That(assistant != null, Is.True, "The UNIT 07 assistant installs with the clinic scene.");
            yield return Until(() => assistant.Engaged, "It works while the counter is open on night 1.");
            var robot = assistant.Robot;
            Vector3 home = robot.position; Quaternion homeRotation = robot.rotation;
            var tray = Object.FindFirstObjectByType<TrayIncident>(FindObjectsInactive.Include).Tray;
            var overlay = robot.GetComponent<TrayCarryOverlay>();
            yield return Until(() => assistant.Line.StartsWith("七号上线"), "It greets the player.");
            // The fixture's own parts order (SetUp) is hauled first; let that run finish.
            for (float t = 0; t < 60f && (assistant.Busy || assistant.Hauls == 0); t += Time.unscaledDeltaTime) yield return null;
            Assert.That(assistant.Hauls, Is.EqualTo(1), "The fixture's order was hauled.");
            Assert.That(Vector3.Distance(robot.position, home), Is.LessThan(.001f), "Back on its dock spot.");
            var room = clinic.Robot.Flow.Rig.Cam;
            var cam = Object.Instantiate(room.gameObject, room.transform.position, room.transform.rotation).GetComponent<Camera>();
            foreach (Transform child in cam.transform) Object.Destroy(child.gameObject);
            foreach (var listener in cam.GetComponents<AudioListener>()) Object.Destroy(listener);
            cam.enabled = false;
            var screen = robot.GetComponentsInChildren<Renderer>().First(r => r.name == "ScreenContent_Animated").transform.position;
            // Floor plan as the room really stands at runtime (replacement kits included), for checking the flight lanes:
            // orthographic, from just under the lamps, north up. 1280x720: x = 640 + X * 97.3, y = 360 - Z * 97.3.
            cam.orthographic = true; cam.orthographicSize = 3.7f; cam.nearClipPlane = .01f;
            cam.transform.SetPositionAndRotation(new Vector3(0, 2.2f, 0), Quaternion.LookRotation(Vector3.down, Vector3.forward));
            yield return Shoot("39-assistant-plan", cam);
            cam.orthographic = false;
            yield return Shoot("41-assistant-greet-hud", room);

            // A parts order sends it to the counter and on to the bench with the crate on its tray.
            var cart = ClinicSupplies.Products.Take(1).ToDictionary(p => p.Id, p => 1);
            Assert.That(ClinicSupplies.Purchase(clinic.State, cart, Guid.NewGuid().ToString("N"), out string reason), Is.True, reason);
            yield return Until(() => assistant.Busy, "A purchase sends UNIT 07 for the parts.");
            Assert.That(tray.Taken, Is.True, "It carries its parts tray.");
            // Two vantage points in turn: from the south over the surgery bed, and from the counter across the north lane.
            var views = new[] { (new Vector3(0f, 2.55f, -2.8f), new Vector3(0f, .6f, .4f)), (new Vector3(2.4f, 2.1f, -2.4f), new Vector3(-.6f, 1.0f, 1.2f)),
                (new Vector3(.6f, 1.9f, -.6f), new Vector3(1.6f, 1.0f, 1.4f)) };
            float start = Time.realtimeSinceStartup, nextShot = 1.5f;
            int shot = 0;
            while (assistant.Busy && Time.realtimeSinceStartup - start < 70f)
            {
                if (Time.realtimeSinceStartup - start > nextShot && shot < 12)
                {
                    var view = views[shot % views.Length];
                    cam.transform.position = view.Item1; cam.transform.LookAt(view.Item2);
                    yield return Shoot("42-assistant-haul-" + (shot++).ToString("00"), cam);
                    nextShot += 2.2f;
                }
                yield return null;
            }
            Assert.That(assistant.Busy, Is.False, "The run finishes.");
            Assert.That(assistant.Hauls, Is.EqualTo(2));
            Assert.That(assistant.BenchCrate != null && assistant.BenchCrate.transform.position.z > 2.2f, Is.True, "The crate is left on the bench.");
            Assert.That(Vector3.Distance(robot.position, home), Is.LessThan(.001f), "Back on its dock spot.");
            Assert.That(Quaternion.Angle(robot.rotation, homeRotation), Is.LessThan(.1f));
            Assert.That(tray.Taken, Is.False, "The tray is back on the dock shelf.");
            Assert.That(overlay.RightWeight + overlay.LeftWeight + overlay.JawOpen, Is.EqualTo(0).Within(1e-4f), "Arms handed back to the Animator.");
            var crate = assistant.BenchCrate.transform.position;
            cam.transform.position = crate + new Vector3(-.2f, .55f, -.75f); cam.transform.LookAt(crate);
            yield return Shoot("43-assistant-crate-on-bench", cam);

            cam.transform.position = screen + new Vector3(.95f, .12f, .35f); cam.transform.LookAt(screen);
            yield return Shoot("45-assistant-home-face", cam);

            // Talking to it: a hint for the next step, then small talk (its lines wait while a call is on the line).
            var calls = ClinicCallRuntime.Current;
            for (int i = 0; i < 40 && calls != null && calls.Active; i++) { calls.Pickup(); calls.Advance(); yield return null; }
            yield return new WaitForSecondsRealtime(1.3f);
            for (float t = 0; t < 30f && assistant.Line.Length > 0; t += Time.unscaledDeltaTime) yield return null;   // let queued lines play out
            assistant.Talk();
            yield return Until(() => assistant.Line.Length > 0, "It answers when clicked.");
            Assert.That(assistant.Card.gameObject.activeSelf, Is.True);
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Shoot("44-assistant-talk", room);
            Object.Destroy(cam.gameObject);
        }

        IEnumerator Shoot(string state, Camera camera)
        {
            foreach (var size in Sizes)
            {
#if UNITY_EDITOR
                if (gameView == null) gameView = new UiTestGameView();
                gameView.Set(size.x, size.y);
#endif
                Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed);
                for (int i = 0; i < 30 && (Screen.width != size.x || Screen.height != size.y); i++) yield return null;
                yield return null; yield return null;
                Canvas.ForceUpdateCanvases();
                if (string.IsNullOrEmpty(Folder)) continue;
                Directory.CreateDirectory(Folder);
                File.WriteAllBytes(Path.Combine(Folder, state + "-" + size.x + "x" + size.y + ".png"), Composite(camera));
            }
        }

        static byte[] Composite(Camera camera)
        {
            int width = Screen.width, height = Screen.height;
            var scene = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var ui = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var read = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var overlays = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .Where(c => c.isActiveAndEnabled && c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay)
                .Select(c => (canvas: c, camera: c.worldCamera, plane: c.planeDistance)).ToArray();
            var uiCameraObject = new GameObject("CyberCaptureUiCamera");
            uiCameraObject.transform.position = new Vector3(0, -5000, 0);
            var uiCamera = uiCameraObject.AddComponent<Camera>();
            uiCamera.clearFlags = CameraClearFlags.SolidColor; uiCamera.backgroundColor = new Color(0, 0, 0, 0);
            uiCamera.nearClipPlane = .01f; uiCamera.farClipPlane = 3; uiCamera.enabled = false;
            var previous = RenderTexture.active;
            var previousTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = scene; camera.Render(); camera.targetTexture = previousTarget;
                RenderTexture.active = scene; read.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                var basePixels = read.GetPixels32();
                foreach (var o in overlays) { o.canvas.renderMode = RenderMode.ScreenSpaceCamera; o.canvas.worldCamera = uiCamera; o.canvas.planeDistance = 1; }
                Canvas.ForceUpdateCanvases();
                uiCamera.targetTexture = ui; uiCamera.Render();
                RenderTexture.active = ui; read.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                var uiPixels = read.GetPixels32();
                for (int i = 0; i < basePixels.Length; i++)
                {
                    // UI was drawn over transparent black: rgb is already weighted by coverage; its alpha holds a^2.
                    float a = Mathf.Sqrt(uiPixels[i].a / 255f);
                    var b = basePixels[i]; var u = uiPixels[i];
                    basePixels[i] = new Color32((byte)Mathf.Min(255, u.r + b.r * (1 - a)), (byte)Mathf.Min(255, u.g + b.g * (1 - a)),
                        (byte)Mathf.Min(255, u.b + b.b * (1 - a)), 255);
                }
                read.SetPixels32(basePixels); read.Apply();
                return read.EncodeToPNG();
            }
            finally
            {
                foreach (var o in overlays) { o.canvas.renderMode = RenderMode.ScreenSpaceOverlay; o.canvas.worldCamera = o.camera; o.canvas.planeDistance = o.plane; }
                camera.targetTexture = previousTarget;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(scene); RenderTexture.ReleaseTemporary(ui);
                Object.Destroy(read); Object.Destroy(uiCameraObject);
                Canvas.ForceUpdateCanvases();
            }
        }

        void Select(string target) => Assert.That(bridge.SelectTarget(target), Is.True, target);

        void Do(string id)
        {
            var command = Flow.Definition.FindCommand(id);
            Assert.That(command, Is.Not.Null, id);
            Assert.That(bridge.SelectTarget(command.target), Is.True, id);
            Assert.That(bridge.ExecuteFromPanel(id), Is.True, id + ": " + Flow.LastFeedback);
        }

        static IEnumerator Until(Func<bool> ready, string reason)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            while (!ready() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(ready(), Is.True, reason);
        }
    }
}
