using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BorderRepair.Data;
using BorderRepair.RepairProps.Cosmetics;
using BorderRepair.TwoNight;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.VR.Tests
{
    public sealed class ClinicVrAdapterTests
    {
        const string Scene = "Assets/RepairPropsVR/Scenes/RepairProps_Clinic_VR.unity";
        const string Reports = "C:/Users/Administrator/Documents/Codex/2026-09-29/border-repair-station-art/work/repair-props-vr-2026-10-18/simulation";
        TwoNightState previous;
        string previousSave;
        ClinicVrSession vr;
        UnityEngine.SceneManagement.Scene loaded;
        readonly List<string> finished = new List<string>();
        readonly List<VrSimulatedController> simulatedControllers = new List<VrSimulatedController>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previous = TwoNightRun.Current;
            previousSave = TwoNightSave.OverrideDirectory;
            TwoNightSave.OverrideDirectory = Path.Combine(Path.GetTempPath(), "RepairVR_" + Guid.NewGuid().ToString("N"));
            TwoNightRun.Clear();
#if UNITY_EDITOR
            ClinicVrSession.EditorSimulationForTests = true;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(Scene, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("Editor scene simulation only; not a headset test.");
            yield break;
#endif
            loaded = SceneManager.GetSceneByPath(Scene);
            yield return Until(() => { vr = Object.FindFirstObjectByType<ClinicVrSession>(); return vr != null && vr.Ready; });
            foreach (var behaviour in vr.origin.GetComponentsInChildren<Behaviour>(true))
                if (behaviour.GetType().Name == "TrackedPoseDriver") behaviour.enabled = false;
            InputSystem.RegisterLayout<VrSimulatedController>();
            foreach (var usage in new[] { CommonUsages.LeftHand, CommonUsages.RightHand })
            {
                var device = InputSystem.AddDevice<VrSimulatedController>();
                simulatedControllers.Add(device);
                InputSystem.SetDeviceUsage(device, usage);
                InputSystem.QueueStateEvent(device, new VrControllerState { tracked=1,trackingState=3,rotation=Quaternion.identity });
            }
            yield return Until(() => vr.rightHand.isActiveAndEnabled);
            vr.origin.Camera.transform.localPosition = Vector3.up * 1.6f;
            finished.Clear();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (loaded.IsValid() && loaded.isLoaded)
            {
                var clean = SceneManager.CreateScene("VRTestCleanup_" + Guid.NewGuid().ToString("N"));
                SceneManager.SetActiveScene(clean);
                yield return SceneManager.UnloadSceneAsync(loaded);
            }
#if UNITY_EDITOR
            ClinicVrSession.EditorSimulationForTests = false;
#endif
            TwoNightRun.Set(previous);
            foreach (var device in simulatedControllers) if (device.added) InputSystem.RemoveDevice(device);
            simulatedControllers.Clear();
            InputSystem.RemoveLayout(nameof(VrSimulatedController));
            TwoNightSave.OverrideDirectory = previousSave;
            Directory.CreateDirectory(Reports);
            File.WriteAllText(Path.Combine(Reports, finished.Count > 0 ? "adapter_result.json" : "lifecycle_result.json"), JsonUtility.ToJson(new Evidence { finishedOrders=finished.ToArray(), allFour=finished.Count==4 }, true));
        }

        [UnityTest]
        public IEnumerator SimulationFourOrders_UseExistingGatesAndTrade_WithoutMovingHmdToBenchCamera()
        {
            var clinic = vr.clinic;
            int cash = clinic.State.Cash;
            var names = new[] { "RX_CoverScrew_", "MC_CoverScrew_", "PS_CoverScrew_", "OM_CoverScrew_" };
            var modules = new[] { "RX_RFScrew_", "MC_IFScrew_", "PS_ModuleScrew_", "OM_WakeScrew_" };
            for (int index = 0; index < 4; index++)
            {
                PlaceHead(clinic.ReceiveAnchor.position + Vector3.up * .6f);
                vr.Receive();
                yield return Until(() => vr.ActiveFlow != null && vr.ActiveFlow.Session == clinic.Station.Session && clinic.Carrying &&
                    clinic.CarriedItem.GetComponent<XRGrabInteractable>() != null);
                var flow = vr.ActiveFlow;
                var item = clinic.CarriedItem;
                Assert.IsFalse(vr.CanWork);
                Assert.IsFalse(vr.Execute("power.off"));
                var record = flow.Session.CurrentRecord;
                var retained = flow.Definition.nodes.Where(n => n.immutable).ToDictionary(n => n.objectName,
                    n => new Pose(flow.GetObject(n.objectName).localPosition, flow.GetObject(n.objectName).localRotation));

                // XRI owns selection; this is a public-manager simulation, not physical controller input.
                var grab = item.GetComponent<XRGrabInteractable>();
                var manager = grab.interactionManager;
                vr.rightHand.transform.position = Bounds(item).center;
                Physics.SyncTransforms();
                SetGrip(true);
                yield return Until(() => vr.rightHand.isSelectActive);
                Assert.IsTrue(manager.CanSelect(vr.rightHand, grab),
                    "XRI selection rejected: handActive=" + vr.rightHand.isActiveAndEnabled +
                    ", grabActive=" + grab.isActiveAndEnabled + ", managerRegistered=" + manager.IsRegistered((IXRInteractable)grab));
                if (!grab.isSelected) manager.SelectEnter((IXRSelectInteractor)vr.rightHand, (IXRSelectInteractable)grab);
                yield return null;
                Assert.IsTrue(grab.isSelected);
                Assert.IsFalse(vr.CanWork);
                SetGrip(false);
                yield return Until(() => !grab.isSelected);
                yield return null;
                yield return null;
                Assert.IsTrue(clinic.Carrying, "A release outside the dock must keep the same order, not discard it.");
                Assert.That(item.transform.parent, Is.EqualTo(clinic.ReceiveAnchor));

                var b = Bounds(item);
                var dock = clinic.WorkAnchor.position;
                dock.y = clinic.BenchTopWorldY + b.extents.y + .002f;
                item.transform.position += dock - b.center;
                PlaceHead(clinic.BenchStandPoint + Vector3.up * 1.6f);
                Assert.IsTrue(vr.TryDock(item));
                Assert.IsFalse(grab.enabled, "Docking must release XRI ownership and disable the carry proxy.");
                yield return Until(() => vr.CanWork);
                Assert.That(Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Count(c => c.isActiveAndEnabled), Is.EqualTo(1));
                Assert.IsTrue(vr.origin.Camera.enabled);
                Assert.IsFalse(clinic.Station.Inspector.ViewCamera.enabled);
                Vector3 head = vr.origin.Camera.transform.position;
                Reject("complete");
                foreach (var test in flow.Definition.tests) Do("test." + test.id);
                Do("power.off");
                Reject("cover.remove");
                Do("fixture.disconnect");
                for (int s = 1; s <= 4; s++) Do("remove." + names[index] + s.ToString("00"));
                Do("cover.remove");
                if (flow.Definition.kind != RepairPropKind.Cartridge) Do("isolate");
                Do("harness.release");
                if (flow.Definition.kind == RepairPropKind.Power)
                { Do("remove.PS_TrayScrew_01"); Do("remove.PS_TrayScrew_02"); Do("tray.remove"); }
                for (int s = 1; s <= 2; s++) Do("remove." + modules[index] + s.ToString("00"));
                Do("module.remove.old");
                Do("module.install.new");
                Reject("module.install.old");
                for (int s = 1; s <= 2; s++) Do("install." + modules[index] + s.ToString("00"));
                if (flow.Definition.kind == RepairPropKind.Power)
                { Do("tray.install"); Do("install.PS_TrayScrew_01"); Do("install.PS_TrayScrew_02"); }
                Do("harness.connect");
                if (flow.Definition.kind != RepairPropKind.Cartridge) Do("supply.connect");
                Do("cover.install");
                for (int s = 1; s <= 4; s++) Do("install." + names[index] + s.ToString("00"));
                Do("fixture.connect"); Do("power.on");
                foreach (var test in flow.Definition.tests)
                    for (int pass = 0; pass < test.consecutivePasses; pass++) Do("test." + test.id);
                foreach (var pose in retained)
                {
                    Assert.That(Vector3.Distance(pose.Value.position, flow.GetObject(pose.Key).localPosition), Is.LessThan(1e-6));
                    Assert.That(Quaternion.Angle(pose.Value.rotation, flow.GetObject(pose.Key).localRotation), Is.LessThan(.001));
                }
                Assert.That(flow.Session.CurrentRecord, Is.SameAs(record));
                yield return null;
                Assert.That(Vector3.Distance(head, vr.origin.Camera.transform.position), Is.LessThan(.001));
                Capture(flow.Definition.kind.ToString());
                Do("complete");
                yield return Until(() => clinic.TradeState == ClinicTradeState.ReadyForDelivery);
                Assert.IsFalse(vr.CanWork);
                PlaceHead(clinic.DeliveryAnchor.position + Vector3.up * .6f);
                vr.Deliver();
                cash += record.Case.estimatedRepairCost;
                Assert.That(clinic.State.Cash, Is.EqualTo(cash));
                vr.Deliver();
                Assert.That(clinic.State.Cash, Is.EqualTo(cash));
                finished.Add(flow.Definition.kind.ToString());
                yield return null;
            }
            Assert.That(clinic.State.phase, Is.EqualTo(TwoNightPhase.Night1Ledger));
            Assert.That(clinic.State.customerTrades.Count, Is.EqualTo(4));
        }

        [UnityTest]
        public IEnumerator SimulatedTriggerPaintsOnlyShellAndTrackingLossExitsCosmetics()
        {
            PlaceHead(vr.clinic.ReceiveAnchor.position+Vector3.up*.6f);
            vr.Receive();
            yield return Until(()=>vr.ActiveFlow!=null && vr.clinic.Carrying && vr.ActiveFlow.Session!=null,"Cosmetics intake");
            var item=vr.clinic.CarriedItem;
            var b=Bounds(item);
            var dock=vr.clinic.WorkAnchor.position;
            dock.y=vr.clinic.BenchTopWorldY+b.extents.y+.002f;
            item.transform.position+=dock-b.center;
            PlaceHead(vr.clinic.BenchStandPoint+Vector3.up*1.6f);
            Assert.IsTrue(vr.TryDock(item));
            yield return Until(()=>vr.CanWork,"Cosmetics dock");
            Do("power.off");Do("fixture.disconnect");
            yield return Until(()=>vr.Cosmetics!=null && vr.Cosmetics.Available,"Cosmetics safe bench: "+vr.Cosmetics?.Feedback);
            var cosmetics=vr.Cosmetics;
            Assert.IsTrue(cosmetics.IsVR);
            int history=vr.ActiveFlow.History.Count;
            Assert.IsTrue(cosmetics.SetMode(CosmeticMode.Spray));
            Assert.IsFalse(vr.CanWork);
            Assert.IsFalse(vr.Execute("power.on"));
            Ray ray=default;bool found=false;
            foreach(var surface in cosmetics.Item.Surfaces)
            {
                var bounds=surface.Renderer.bounds;
                foreach(var direction in new[]{Vector3.forward,Vector3.back,Vector3.right,Vector3.left,Vector3.up})
                {
                    for(int x=-1;x<=1&&!found;x++) for(int y=-1;y<=1&&!found;y++)
                    {
                        var point=bounds.center+new Vector3(x*bounds.extents.x*.6f,y*bounds.extents.y*.6f,0);
                        var candidate=new Ray(point+direction*(bounds.extents.magnitude+.03f),-direction);
                        if(cosmetics.TryHit(candidate,.7f,vr.origin.transform,out _)) {ray=candidate;found=true;}
                    }
                    if(found) break;
                }
                if(found) break;
            }
            Assert.IsTrue(found,"A real imported exterior must be reachable by a brush ray.");
            var aim=vr.rightHand.curveOrigin!=null ? vr.rightHand.curveOrigin : vr.rightHand.transform;
            var controller=vr.rightHand.transform;
            while(controller.parent!=null && controller.name!="Right Controller") controller=controller.parent;
            Assert.AreEqual("Right Controller",controller.name);
            // Drive the controller pose, not the stabilized child ray origin which XRI rewrites each frame.
            controller.SetPositionAndRotation(ray.origin,Quaternion.LookRotation(ray.direction));
            InputSystem.QueueStateEvent(simulatedControllers[1],new VrControllerState {tracked=1,trackingState=3,rotation=Quaternion.identity});
            yield return Until(()=>Vector3.Distance(aim.position,ray.origin)<.005f && Vector3.Angle(aim.forward,ray.direction)<1,"Stabilized controller aim must converge");
            yield return null;
            InputSystem.QueueStateEvent(simulatedControllers[1],new VrControllerState {tracked=1,trackingState=3,rotation=Quaternion.identity,triggerPressed=1});
            float paintDeadline=Time.realtimeSinceStartup+2;
            while(!cosmetics.Item.Surfaces.Any(s=>s.Edited) && Time.realtimeSinceStartup<paintDeadline) yield return null;
            Assert.IsTrue(cosmetics.Item.Surfaces.Any(s=>s.Edited),"Trigger should paint: mode="+cosmetics.Mode+", available="+cosmetics.Available+", feedback="+cosmetics.Feedback+
                ", input="+simulatedControllers[1]["triggerPressed"].ReadValueAsObject()+", UI="+vr.rightHand.TryGetCurrentUIRaycastResult(out _)+
                ", rayDelta="+Vector3.Distance(aim.position,ray.origin)+", angle="+Vector3.Angle(aim.forward,ray.direction)+", aimed="+
                cosmetics.TryHit(new Ray(aim.position,aim.forward),1.25f,vr.origin.transform,out _));
            InputSystem.QueueStateEvent(simulatedControllers[1],new VrControllerState {tracked=1,trackingState=3,rotation=Quaternion.identity});
            yield return null;yield return null;
            Assert.Greater(cosmetics.Item.ActionCount,0);
            Assert.That(vr.ActiveFlow.History.Count,Is.EqualTo(history));
            Capture("Receiver_cosmetic");
            InputSystem.QueueStateEvent(simulatedControllers[1],new VrControllerState {rotation=Quaternion.identity});
            yield return Until(()=>cosmetics.Mode==CosmeticMode.Off);
            Assert.IsFalse(vr.clinic.Station.CleaningActive);
            Assert.IsFalse(cosmetics.Available);
        }

        [UnityTest]
        public IEnumerator TrackingLossReturnsCarriedOrderWithoutDiscardingIt()
        {
            PlaceHead(vr.clinic.ReceiveAnchor.position + Vector3.up * .6f);
            vr.Receive();
            yield return Until(() => vr.clinic.CarriedItem != null && vr.clinic.CarriedItem.GetComponent<XRGrabInteractable>() != null);
            var item = vr.clinic.CarriedItem;
            var record = vr.clinic.Station.Session.CurrentRecord;
            var grab = item.GetComponent<XRGrabInteractable>();
            vr.rightHand.transform.position = Bounds(item).center;
            Physics.SyncTransforms();
            SetGrip(true);
            yield return Until(() => vr.rightHand.isSelectActive);
            if (!grab.isSelected) grab.interactionManager.SelectEnter((IXRSelectInteractor)vr.rightHand, (IXRSelectInteractable)grab);
            yield return Until(() => grab.isSelected);
            InputSystem.QueueStateEvent(simulatedControllers[1], new VrControllerState { rotation=Quaternion.identity });
            yield return Until(() => !simulatedControllers[1].isTracked.isPressed && !grab.isSelected);
            yield return null;
            yield return null;
            Assert.That(vr.clinic.CarriedItem, Is.SameAs(item));
            Assert.That(item.transform.parent, Is.SameAs(vr.clinic.ReceiveAnchor));
            Assert.That(vr.clinic.Station.Session.CurrentRecord, Is.SameAs(record));
            Assert.IsFalse(vr.CanWork);
        }

        [UnityTest]
        public IEnumerator DisablingAdapterRestoresDesktopCameraAndInputOwnership()
        {
            var desktop = vr.clinic.Station.Inspector.ViewCamera;
            Assert.IsFalse(desktop.enabled);
            vr.enabled = false;
            yield return null;
            Assert.IsFalse(vr.origin.gameObject.activeSelf);
            Assert.IsFalse(vr.Ready);
            Assert.IsTrue(vr.clinic.enabled);
            Assert.IsTrue(vr.clinic.Robot.Flow.Rig.Walker.enabled);
            Assert.IsTrue(vr.clinic.Robot.Flow.Rig.Cam.enabled);
            Assert.IsTrue(vr.panel.GetComponentsInChildren<Canvas>(true).All(c => !c.enabled));
        }

        void Do(string id)
        {
            var c = vr.ActiveFlow.Definition.FindCommand(id);
            Assert.IsNotNull(c, id);
            vr.SelectTarget(c.target);
            vr.SelectTool(c.tool);
            Assert.IsTrue(vr.Execute(id), id + ": " + vr.Feedback);
        }

        void Reject(string id)
        {
            var c = vr.ActiveFlow.Definition.FindCommand(id);
            vr.SelectTarget(c.target); vr.SelectTool(c.tool);
            Assert.IsFalse(vr.Execute(id), id);
        }

        void PlaceHead(Vector3 point) => vr.origin.MoveCameraToWorldLocation(point);
        void SetGrip(bool pressed) => InputSystem.QueueStateEvent(simulatedControllers[1],
            new VrControllerState { tracked=1,trackingState=3,rotation=Quaternion.identity,gripPressed=(byte)(pressed?1:0),grip=pressed?1:0 });
        static Bounds Bounds(GameObject item)
        {
            var renderers = item.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            var b = renderers[0].bounds;
            foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
            return b;
        }

        void Capture(string name)
        {
            Directory.CreateDirectory(Reports);
            var camera = vr.origin.Camera;
            camera.transform.LookAt(vr.clinic.WorkAnchor.position + Vector3.up * .25f);
            var rt = RenderTexture.GetTemporary(1280, 720, 24);
            var previous = camera.targetTexture;
            var active = RenderTexture.active;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                File.WriteAllBytes(Path.Combine(Reports, name + "_simulation.png"), image.EncodeToPNG());
            }
            finally { camera.targetTexture = previous; RenderTexture.active = active; RenderTexture.ReleaseTemporary(rt); Object.Destroy(image); }
        }

        static IEnumerator Until(Func<bool> condition,string context="VR adapter lifecycle timed out.")
        {
            float deadline = Time.realtimeSinceStartup + 15;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(condition(), context);
        }

        [Serializable] class Evidence
        {
            public string input = "Editor-only no-display opt-in; virtual tracked XR controllers and grip Input System events plus public XRI manager/clinic/repair calls. No physical headset, optical/stereo or physical controller acceptance.";
            public bool allFour;
            public string[] finishedOrders;
        }
    }
}
