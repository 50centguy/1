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

        Camera View => clinic.Station.Inspector.ViewCamera;

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
