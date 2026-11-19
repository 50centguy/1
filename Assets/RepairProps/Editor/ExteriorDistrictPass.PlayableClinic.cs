using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.EditorTools
{
    // The playable's clinic is the B1: the district keeps its B1 (shell, surfaces, lamps, dressing) and a copy of the
    // clinic scene brings the gameplay stations into the places the B1 pass gave the project's assets (counter facing the
    // gate, bench on the west wall, UNIT 07 on the back wall, surgery in the east bay). RepairProps_Clinic is untouched.
    public static partial class ExteriorDistrictPass
    {
        const string AssetsGroup = "02 Clinic assets from the project";
        // The B1's inside, clinic frame (walls x -2.1..5.15, front lining z -3.4, back wall z 2.62).
        static readonly Bounds B1Room = new Bounds(new Vector3(1.53f, 1.3f, -.49f), new Vector3(7.3f, 2.8f, 6.5f));

        // Captured from the district copy's B1 before it is saved; used for the clinic copy.
        static ClinicLayout.Pose[] b1Zones;
        static Pose b1Counter, b1DockCorner, b1Bench;
        static Vector3 b1BedFloor, b1BedHead;

        struct Pose { public Vector3 position; public Quaternion rotation; public float Yaw => rotation.eulerAngles.y; }

        static Pose PoseOf(Transform t) => new Pose { position = t.position, rotation = t.rotation };

        static Transform Under(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true).First(t => t.name == name || t.name.StartsWith(name + ".", StringComparison.Ordinal));

        // In the district copy: read the B1's placement of the project's assets, then switch those assets off (the clinic
        // copy brings the live ones), and open the staff gate so the player can get behind the counter.
        static void OpenB1(Transform holder)
        {
            var assets = holder.Cast<Transform>().First(t => t.name == AssetsGroup);
            var zones = assets.Cast<Transform>().First(t => t.name == "ClinicZonesV2");
            var list = new List<ClinicLayout.Pose>();
            foreach (var t in zones.GetComponentsInChildren<Transform>(true))
            {
                string path = t == zones ? "" : AnimationUtility.CalculateTransformPath(t, zones);
                list.Add(new ClinicLayout.Pose { path = path, position = t.position, rotation = t.rotation, active = t.gameObject.activeSelf });
            }
            b1Zones = list.ToArray();
            b1Counter = PoseOf(Under(zones, "TradeCounterV2"));
            b1DockCorner = PoseOf(Under(zones, "DockCornerV2"));
            b1Bench = PoseOf(assets.Cast<Transform>().First(t => t.name == "RepairBenchV2"));
            var table = assets.Cast<Transform>().First(t => t.name == "SurgeryTableV2");
            b1BedFloor = Under(table, "Ref_Origin").position;
            b1BedHead = Vector3.ProjectOnPlane(Under(table, "Ref_Head").position - b1BedFloor, Vector3.up).normalized;
            assets.gameObject.SetActive(false);
            log.Append("B1 kept whole; project assets placed in it off (").Append(b1Zones.Length).Append(" zone parts captured)\n");
            log.Append("  B1 counter ").Append(b1Counter.position.ToString("F2")).Append(" yaw ").Append(b1Counter.Yaw.ToString("F0"))
               .Append(", dock corner ").Append(b1DockCorner.position.ToString("F2")).Append(" yaw ").Append(b1DockCorner.Yaw.ToString("F0"))
               .Append(", bench ").Append(b1Bench.position.ToString("F2")).Append(" yaw ").Append(b1Bench.Yaw.ToString("F0"))
               .Append(", bed ").Append(b1BedFloor.ToString("F2")).Append(" head ").Append(b1BedHead.ToString("F2")).Append('\n');

            // The staff gate (waist-high rail from the counter wing to the partition) would shut the player out of the
            // workshop: it stands open, i.e. it goes.
            int gate = 0;
            foreach (var t in holder.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.IndexOf("staff", StringComparison.OrdinalIgnoreCase) < 0 || t.name.IndexOf("gate", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (t.name.StartsWith("Layout", StringComparison.Ordinal)) continue;   // the group holding shelving and dock corner too
                foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                foreach (var c in t.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                gate++;
                log.Append("  staff gate off: ").Append(HierPath(t)).Append('\n');
            }
            if (gate == 0)
            {
                var layout = holder.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith("Layout - shelving", StringComparison.Ordinal));
                if (layout != null) foreach (Transform c in layout) log.Append("  (layout part) ").Append(c.name).Append(' ').Append(BoundsOf(c).ToString("F2")).Append('\n');
            }
        }

        static Bounds BoundsOf(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(t.position, Vector3.zero);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        // The B1's dressing (chairs, heater, cart, cooler ...) mostly has no collider: boxes, as on the street.
        static void B1Colliders(Transform holder)
        {
            int added = 0;
            var room = B1Room; room.Expand(-.05f);
            foreach (var r in holder.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!r.enabled || r.GetComponent<Collider>() != null) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                if (r.sharedMaterials.Any(m => m == null || UnlitShaders.Contains(m.shader.name) || m.renderQueue >= 2450)) continue;
                var b = r.bounds;
                if (!room.Contains(b.center)) continue;
                var path = HierPath(r.transform);
                if (path.IndexOf("gate", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("mat", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("puddle", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                float wide = Mathf.Max(b.size.x, b.size.z);
                if (wide < .06f || wide > 4 || b.size.y < .25f || b.min.y > 1.2f) continue;
                var box = r.gameObject.AddComponent<BoxCollider>();
                box.center = mf.sharedMesh.bounds.center; box.size = mf.sharedMesh.bounds.size;
                added++;
            }
            log.Append("B1 dressing given a box collider: ").Append(added).Append('\n');
        }

        static ClinicLayout.Move MoveFrom(Vector3 fromPos, float fromYaw, Vector3 toPos, float toYaw)
        {
            float yaw = Mathf.DeltaAngle(0, toYaw - fromYaw);
            return new ClinicLayout.Move { yaw = yaw, offset = toPos - Quaternion.Euler(0, yaw, 0) * fromPos };
        }

        static void MoveBy(Transform t, ClinicLayout.Move m)
        {
            var q = Quaternion.Euler(0, m.yaw, 0);
            t.SetPositionAndRotation(q * t.position + m.offset, q * t.rotation);
        }

        static Vector3 Map(ClinicLayout.Move m, Vector3 p) => Quaternion.Euler(0, m.yaw, 0) * p + m.offset;

        // The clinic scene, saved as a copy with its stations where the B1 has them and its own room switched off.
        static void BuildPlayableClinic()
        {
            var scene = EditorSceneManager.OpenScene(Berth04Playable.ClinicScene, OpenSceneMode.Single);
            var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
            Transform One(string name) => all.FirstOrDefault(t => t.name == name) ?? all.FirstOrDefault(t => t.name.StartsWith(name, StringComparison.Ordinal));
            IEnumerable<Transform> Named(params string[] names) => names.Select(One).Where(t => t != null);

            // Where the clinic has them (the zones prefab lands at the room frame, turned 180; the bench V2 on Bench_Top,
            // which is WorkbenchArea's frame; the surgery table on Bed_Base).
            var workbench = One("WorkbenchArea");
            if (!SurgeryTableV2Runtime.TryBedFrame(scene, out var bedFloor, out var bedHead)) throw new InvalidOperationException("no Bed_Base in the clinic");
            var layout = new GameObject("Clinic layout - the stations in the Berth 04 B1").AddComponent<ClinicLayout>();
            layout.counter = MoveFrom(Vector3.zero, 180, b1Counter.position, b1Counter.Yaw);
            layout.dock = MoveFrom(Vector3.zero, 180, b1DockCorner.position, b1DockCorner.Yaw);
            layout.bench = MoveFrom(workbench.position, workbench.eulerAngles.y, b1Bench.position, b1Bench.Yaw);
            float bedYaw = Vector3.SignedAngle(bedHead, b1BedHead, Vector3.up);
            layout.surgery = new ClinicLayout.Move { yaw = bedYaw, offset = b1BedFloor - Quaternion.Euler(0, bedYaw, 0) * bedFloor };
            layout.room = B1Room;
            layout.ceiling = 2.55f;
            layout.zones = b1Zones;

            // Routes. The customer comes in at the gate and stands at the counter's front (the pads ride with the counter);
            // the patient walks round the counter's east end into the surgery bay; UNIT 07 flies out over the workshop.
            layout.npcEntrance = new Vector3(.30f, 0, -3.05f);
            layout.npcApproach = new[] { new Vector3(.22f, 0, -2.55f) };
            layout.patientIn = new[] { new Vector3(.30f, 0, -3.05f), new Vector3(1.05f, 0, -2.75f), new Vector3(2.25f, 0, -2.15f), new Vector3(3.15f, 0, -1.05f), new Vector3(3.25f, 0, -.25f) };
            layout.dockOut = Map(layout.dock, new Vector3(-2.10f, .95f, 0f));
            layout.westLane = new Vector3(layout.dockOut.x, 1.15f, .9f);
            layout.eastLane = new Vector3(-1.0f, 1.15f, -.25f);
            layout.counterLane = new Vector3(-1.72f, 1.15f, -1.25f);
            layout.counterHover = new Vector3(-1.72f, 1.0f, Map(layout.counter, new Vector3(1.85f, .95f, .92f)).z);
            layout.benchLane = Map(layout.bench, new Vector3(.55f, 1.15f, 1.40f));
            layout.benchHover = Map(layout.bench, new Vector3(.55f, 1.05f, 1.85f));
            layout.counterYaw = 90; layout.benchYaw = 270;
            SceneManager.MoveGameObjectToScene(layout.gameObject, scene);
            layout.transform.SetAsFirstSibling();

            int moved = 0;
            void Move(ClinicLayout.Move m, params string[] names) { foreach (var t in Named(names)) { MoveBy(t, m); moved++; } }
            Move(layout.bench, "WorkbenchArea", "FO_NewBearing", "FO_Drop_Mat", "FO_Drop_PartsTray", "Bench_WorkLamp_Spot", "ClinicBenchGrime", "UC_MonitorArm");
            Move(layout.dock, "Unit07ServiceDock", "UNIT07_RobotV4_DockReady", "Dock_ServiceLight", "COL_Unit07Frame");
            Move(layout.counter, "ClinicTradeAnchors", "ClinicTradeTaskLight", "COL_Counter_Long", "COL_Counter_Angled", "COL_Stool");
            Move(layout.surgery, "UC_Surgery", "ClinicSurgicalTaskLight", "COL_Bed");

            // The director's stand point in front of the bench.
            var director = all.Select(t => t.GetComponent("UnifiedClinicDirector")).FirstOrDefault(c => c != null);
            if (director != null)
            {
                var so = new SerializedObject(director);
                var stand = so.FindProperty("benchStandPoint");
                if (stand != null) { stand.vector3Value = Map(layout.bench, stand.vector3Value); so.ApplyModifiedPropertiesWithoutUndo(); }
            }

            // The clinic's own room: shell, door, old furniture, clutter and storage, fixture lamps and wall collision.
            int off = 0;
            void Off(Transform t)
            {
                foreach (var r in t.GetComponentsInChildren<Renderer>(true)) if (r.enabled) { r.enabled = false; off++; }
                foreach (var c in t.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            }
            foreach (var t in Named("UC_Shell", "UC_Clutter", "UC_Storage", "UC_Door", "UC_Trade", "UC_Unit07Zone", "UnifiedClinicClutter")) Off(t);
            var surgery = One("UC_Surgery");
            if (surgery != null)   // the old bed and gantry booms are the table's and gantry's frames (their runtimes hide them)
                foreach (Transform c in surgery) if (!c.name.StartsWith("Bed_", StringComparison.Ordinal) && !c.name.StartsWith("Gantry_", StringComparison.Ordinal)) Off(c);
            foreach (var t in all.Where(t => t.name.StartsWith("COL_Wall", StringComparison.Ordinal) || t.name.StartsWith("COL_DoorLeaf", StringComparison.Ordinal)))
                Off(t);
            foreach (var t in Named("COL_Floor", "COL_EastShelves", "COL_LimbShelf", "COL_Lockers", "COL_NECabinet", "COL_NWCabinet", "COL_RedToolChest", "COL_InstrumentCart")) Off(t);
            int lightsOff = 0;
            foreach (var t in Named("Ceiling_Fluo", "ClinicFixtureLights"))
                foreach (var l in t.GetComponentsInChildren<Light>(true)) if (l.enabled) { l.enabled = false; lightsOff++; }
            var blocker = One("ClinicClosedDoorCollision");
            if (blocker != null) blocker.gameObject.SetActive(false);

            log.Append("clinic copy: stations moved (").Append(moved).Append(" objects), counter yaw ").Append(layout.counter.yaw.ToString("F0"))
               .Append(" +").Append(layout.counter.offset.ToString("F2")).Append(", dock yaw ").Append(layout.dock.yaw.ToString("F0")).Append(" +").Append(layout.dock.offset.ToString("F2"))
               .Append(", bench yaw ").Append(layout.bench.yaw.ToString("F0")).Append(" +").Append(layout.bench.offset.ToString("F2"))
               .Append(", surgery yaw ").Append(layout.surgery.yaw.ToString("F0")).Append(" +").Append(layout.surgery.offset.ToString("F2"))
               .Append("; own room off: ").Append(off).Append(" renderers, ").Append(lightsOff).Append(" lights\n");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, Berth04Playable.PlayableClinicScene);
        }
    }
}
