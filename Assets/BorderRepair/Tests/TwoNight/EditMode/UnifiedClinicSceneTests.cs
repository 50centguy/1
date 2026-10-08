using System.Linq;
using BorderRepair.FirstOrder;
using BorderRepair.TwoNight.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BorderRepair.TwoNight.Tests
{
    public class UnifiedClinicSceneTests
    {
        [System.Serializable] class AnchorContract { public ContractAnchor[] anchors; }
        [System.Serializable] class ContractAnchor { public string name; public float[] unity_pos; }

        [Test]
        public void SharedScene_RetainsCurrentN2RobotDockAndGameplayReferencesAfterRelayout()
        {
            var source = EditorSceneManager.OpenScene(TwoNightBuilder.RobotPath, OpenSceneMode.Single);
            var sourceFlow = source.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<FirstOrderFlow>(true)).Single();
            var controller = sourceFlow.Dock.RobotAnimator.runtimeAnimatorController;
            var robotPrefab = PrefabUtility.GetCorrespondingObjectFromOriginalSource(sourceFlow.Dock.RobotRoot.gameObject);
            var parts = sourceFlow.TrackedParts.Select(p => (p.partId, p.realPath)).ToArray();
            var shared = EditorSceneManager.OpenScene(UnifiedClinicBuilder.ClinicPath, OpenSceneMode.Single);
            var frame = shared.GetRootGameObjects().Single(g => g.name == "ClinicLegacyRoomFrame").transform;
            var flow = frame.GetComponentsInChildren<FirstOrderFlow>(true).Single();
            Assert.AreEqual(controller, flow.Dock.RobotAnimator.runtimeAnimatorController);
            Assert.AreEqual(robotPrefab, PrefabUtility.GetCorrespondingObjectFromOriginalSource(flow.Dock.RobotRoot.gameObject));
            CollectionAssert.AreEquivalent(parts, flow.TrackedParts.Select(p => (p.partId, p.realPath)).ToArray());
            var incident = frame.GetComponentInChildren<TrayIncident>(true);
            Assert.AreSame(flow.Dock.RobotRoot, incident.RobotRoot);
            Assert.IsNotNull(incident.Tray);
            Assert.IsNotNull(incident.Overlay);
            Assert.IsNotNull(flow.MatZone.landing);
            Assert.IsNotNull(flow.OldTrayZone.landing);
            foreach (var id in FirstOrderCameraRig.Order) Assert.IsNotNull(flow.Rig.Get(id).pose, id);
            Assert.AreEqual(1, shared.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<RepairStationController>(true)).Count());
        }

        [Test]
        public void IntegratedScene_CounterBenchAndDockMatchExportedAnchorContract()
        {
            var scene = EditorSceneManager.OpenScene(UnifiedClinicBuilder.ClinicPath, OpenSceneMode.Single);
            var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var contract = JsonUtility.FromJson<AnchorContract>(System.IO.File.ReadAllText("ArtSource/UnifiedClinic/anchors.json"));
            Transform Anchor(string name) => all.Single(t => t.name == "ANCHOR_" + name);
            void At(Transform actual, string name)
            {
                Assert.IsNotNull(actual, name);
                var target = contract.anchors.Single(a => a.name == name).unity_pos;
                var expected = new Vector3(target[0], target[1], target[2]);
                Assert.Less(Vector3.Distance(Anchor(name).position, expected), .001f, "Exported " + name);
                Assert.Less(Vector3.Distance(actual.position, expected), .001f, name);
            }
            var clinic = all.Select(t => t.GetComponent<UnifiedClinicDirector>()).Single(c => c != null);
            foreach (var pad in new[] { (clinic.ReceiveAnchor, "TradeReceive"), (clinic.DeliveryAnchor, "TradeDeliver") })
            {
                var target = contract.anchors.Single(a => a.name == pad.Item2).unity_pos;
                var expected = new Vector3(target[0], target[1], target[2]);
                Assert.Less(Vector3.Distance(Anchor(pad.Item2).position, expected), .001f);
                Assert.Less(Vector2.Distance(new Vector2(pad.Item1.position.x, pad.Item1.position.z),
                    new Vector2(expected.x, expected.z)), .001f, pad.Item2 + " horizontal placement");
                Assert.Less(Mathf.Abs(clinic.TradeSupportWorldY(pad.Item1) - expected.y), .001f, pad.Item2 + " support surface");
            }
            At(all.Single(t => t.name == "WorkbenchArea"), "WorkbenchArea_Root");
            var dockRoot = all.Single(t => t.name == "Unit07ServiceDock");
            At(dockRoot, "Dock_Root");
            Assert.IsTrue(clinic.Robot.Flow.Dock.RobotAnchor.IsChildOf(dockRoot), "Controller uses the migrated physical dock.");
            At(clinic.Robot.Flow.Dock.RobotAnchor, "DockRobot");
            At(clinic.Robot.Flow.Dock.RobotRoot, "DockRobot");
            Assert.Greater(clinic.Robot.Flow.MaxCarrySpeed, 0, "Integrated layout must opt in to distance-based carry timing.");
            Assert.Less(Vector3.Angle(clinic.Robot.Flow.Dock.RobotRoot.up, Vector3.up), .1f);
            var objects = new SerializedObject(clinic);
            Assert.AreEqual(-.11f, objects.FindProperty("tradeSupportHeight").floatValue, .001f);
            foreach (var action in new[] { ClinicTradeAction.Receive, ClinicTradeAction.Deliver, ClinicTradeAction.Console })
            {
                var zone = all.Select(t => t.GetComponent<ClinicTradeZone>()).Single(z => z != null && z.Action == action);
                Assert.IsTrue(zone.GetComponentsInChildren<Collider>(true).Any(c => c.enabled), action.ToString());
            }
        }

        [Test]
        public void RoomRelocation_TransformsDockTrayPathAndPartLandingsTogether()
        {
            var scene = EditorSceneManager.OpenScene(UnifiedClinicBuilder.ClinicPath, OpenSceneMode.Single);
            try
            {
                var frame = scene.GetRootGameObjects().Single(g => g.name == "ClinicLegacyRoomFrame").transform;
                var flow = frame.GetComponentInChildren<FirstOrderFlow>(true);
                var incident = frame.GetComponentInChildren<TrayIncident>(true);
                var dock = flow.Dock.RobotAnchor.position;
                var path = incident.HoverPosition;
                var landing = flow.MatZone.landing.position;
                var approach = flow.MatZone.approachPoint;
                var offset = flow.MatZone.landingOffset;
                var point = new Vector3(2, 0, -1);
                var rotation = Quaternion.Euler(0, 35, 0);
                var delta = Matrix4x4.TRS(point, rotation, frame.lossyScale) * frame.worldToLocalMatrix;
                UnifiedClinicBuilder.RelocateLegacyRoom(point, rotation);
                Assert.Less(Vector3.Distance(delta.MultiplyPoint3x4(dock), flow.Dock.RobotAnchor.position), 1e-5f);
                Assert.Less(Vector3.Distance(delta.MultiplyPoint3x4(path), incident.HoverPosition), 1e-5f);
                Assert.Less(Vector3.Distance(delta.MultiplyPoint3x4(landing), flow.MatZone.landing.position), 1e-5f);
                Assert.Less(Vector3.Distance(delta.MultiplyPoint3x4(approach), flow.MatZone.approachPoint), 1e-5f);
                Assert.Less(Vector3.Distance(delta.MultiplyVector(offset), flow.MatZone.landingOffset), 1e-5f);
                Assert.IsNotNull(flow.Dock.RobotRoot);
                Assert.IsNotNull(incident.Tray);
            }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
        }
    }
}
