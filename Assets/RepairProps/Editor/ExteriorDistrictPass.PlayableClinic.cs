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

        // The B1's floor-standing dressing (cabinets, cart, cooler, plank stack ...) mostly has no collider. One box per
        // piece of furniture (the pass's items: the children of its groups), from what is drawn, on Ignore Raycast so the
        // clinic's pointer still reaches what stands behind. Items already solid somewhere in them are left alone.
        static readonly string[] NotSolid = { "mat", "puddle", "water in", "streak", "stain", "frost", "glass", "glow", "light", "lamp", "bulb", "batten", "skyline", "rag" };

        static void B1Colliders(Transform holder)
        {
            var pass = holder.GetComponentsInChildren<Transform>(true).First(t => t.name.StartsWith("09 Interior pass", StringComparison.Ordinal));
            var root = new GameObject("B1 collision (generated)").transform;
            root.SetParent(holder, false);
            root.rotation = Quaternion.identity;
            var room = B1Room; room.Expand(-.05f);
            int added = 0;
            var names = new List<string>();
            void Item(Transform t, int depth)
            {
                if (NotSolid.Any(n => t.name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)) return;
                var rs = t.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
                if (rs.Length == 0) return;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                float wide = Mathf.Max(b.size.x, b.size.z);
                if (depth >= 2 && !room.Contains(b.center)) return;
                // A group, or something too big to be one box: look at its parts.
                // (The drawn-back curtain hangs from a rail across the whole bay opening: its folds only.)
                if ((depth < 2 || wide > 2.6f || t.name.StartsWith("Curtain", StringComparison.Ordinal)) && t.childCount > 0) { foreach (Transform c in t) Item(c, depth + 1); return; }
                if (t.GetComponentsInChildren<Collider>(false).Any(c => c.enabled)) return;
                if (b.min.y > .3f || b.size.y < .3f || wide < .08f || wide > 2.6f) return;   // floor-standing, knee-high or more
                var go = new GameObject("Solid - " + t.name) { layer = 2 };
                go.transform.SetParent(root, false);
                go.transform.position = b.center;
                var box = go.AddComponent<BoxCollider>();
                box.size = new Vector3(b.size.x / root.lossyScale.x, b.size.y / root.lossyScale.y, b.size.z / root.lossyScale.z);
                added++; names.Add(t.name);
            }
            Item(pass, 0);
            log.Append("B1 furniture given a box collider: ").Append(added).Append(" (").Append(string.Join(", ", names)).Append(")\n");
        }

        // The surgery bay grows 0.95 m south over the waiting side's empty floor: its curtain line, partition and tile
        // come forward; the notice board that hung there moves south along the waiting wall, the heater clears the
        // curtain and the instrument cart stands in front of the bed's foot, leaving both its sides free.
        const float BayGrow = .95f;

        static void WidenSurgeryBay(Transform holder)
        {
            var all = holder.GetComponentsInChildren<Transform>(true);
            Transform Named(string name) => all.FirstOrDefault(t => t.name == name);
            IEnumerable<Transform> AllNamed(string name) => all.Where(t => t.name == name);
            var bay = all.First(t => t.name.StartsWith("Surgery bay", StringComparison.Ordinal));
            float front = BoundsOf(bay).min.z;   // the partition's south end and the curtain line
            int n = 0;

            // Stretch along world z (keeping the north end) the pieces that run the bay's depth.
            void Deepen(Transform t)
            {
                var b = BoundsOf(t);
                float f = (b.size.z + BayGrow) / b.size.z;
                var local = t.InverseTransformDirection(Vector3.forward);
                int axis = Mathf.Abs(local.x) > Mathf.Abs(local.y) ? (Mathf.Abs(local.x) > Mathf.Abs(local.z) ? 0 : 2) : (Mathf.Abs(local.y) > Mathf.Abs(local.z) ? 1 : 2);
                var s = t.localScale; s[axis] *= f; t.localScale = s;
                var nb = BoundsOf(t);
                t.position += new Vector3(0, 0, b.max.z - nb.max.z);
                n++;
            }
            foreach (var name in new[] { "Surgery privacy partition", "Tile - partition", "Tile edge - partition", "Tile - right wall", "Tile edge - right" })
                foreach (var t in AllNamed(name)) Deepen(t);
            var curtain = bay.Cast<Transform>().First(t => t.name.StartsWith("Curtain", StringComparison.Ordinal));
            curtain.position += new Vector3(0, 0, -BayGrow); n++;

            var heater = Named("Electric radiator");
            if (heater != null) { heater.position += new Vector3(0, 0, .15f); n++; }
            var cart = Named("Instrument cart");
            if (cart != null)
            {
                var b = BoundsOf(cart);
                cart.position += new Vector3(3.62f - b.min.x, 0, front - BayGrow + .12f - b.min.z); n++;
            }

            // The notice board and its frame: south along the east wall to the first free span above the waiting chairs.
            var board = new[] { Named("Notice board"), Named("Board frame") }.Where(t => t != null).ToArray();
            // The care poster goes up into the gap under the leak's damp streak first, which opens a span big enough for the board.
            var poster = Named("Care poster");
            var streak = Named("Damp streak under the joint");
            if (poster != null && streak != null)
            {
                float dz = BoundsOf(streak).min.z - .06f - BoundsOf(poster).max.z;
                poster.position += new Vector3(0, 0, dz); n++;
                log.Append("  care poster moved ").Append(dz.ToString("F2")).Append(" m\n");
            }
            if (board.Length > 0)
            {
                var bb = BoundsOf(board[0]); foreach (var t in board) bb.Encapsulate(BoundsOf(t));
                var others = holder.GetComponentsInChildren<Renderer>(false)
                    .Where(r => r.enabled && !board.Any(t => r.transform.IsChildOf(t)) && r.bounds.max.x > 4.85f && r.bounds.size.x < .5f
                                && r.bounds.min.y < bb.max.y && r.bounds.max.y > bb.min.y && r.bounds.size.z < 3
                                && r.bounds.min.z < bb.max.z && r.bounds.max.z > -3.4f && !(r.bounds.size.x < .04f && r.bounds.size.z > 1.2f)).ToList();
                foreach (var o in others) log.Append("  (east wall) ").Append(HierPath(o.transform)).Append(' ').Append(o.bounds.min.ToString("F2")).Append("..").Append(o.bounds.max.ToString("F2")).Append('\n');
                float limit = front - BayGrow - .05f;
                for (float dz = limit - bb.max.z; bb.min.z + dz > -3.35f; dz -= .05f)
                {
                    var moved = new Bounds(bb.center + new Vector3(0, 0, dz), bb.size + new Vector3(0, .06f, .06f));
                    if (others.Any(o => o.bounds.Intersects(moved))) continue;
                    foreach (var t in board) t.position += new Vector3(0, 0, dz);
                    log.Append("  notice board moved ").Append(dz.ToString("F2")).Append(" m south\n");
                    n++;
                    break;
                }
            }
            log.Append("surgery bay deepened ").Append(BayGrow.ToString("F2")).Append(" m (front ").Append(front.ToString("F2")).Append(" -> ")
               .Append((front - BayGrow).ToString("F2")).Append("), ").Append(n).Append(" pieces moved or stretched\n");
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
            layout.room = PlayableRoom;   // the B1 and its back room
            layout.ceiling = 2.55f;
            layout.zones = b1Zones;

            // Routes. The customer comes in at the gate and stands at the counter's front (the pads ride with the counter);
            // the patient walks round the counter's east end into the surgery bay; UNIT 07 flies out over the workshop.
            layout.npcEntrance = new Vector3(.30f, 0, -3.05f);
            layout.npcApproach = new[] { new Vector3(.22f, 0, -2.55f) };
            layout.patientIn = new[] { new Vector3(.30f, 0, -3.05f), new Vector3(1.05f, 0, -2.75f), new Vector3(2.25f, 0, -2.15f), new Vector3(3.2f, 0, -1.55f), new Vector3(3.25f, 0, -.25f) };
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
            // The counter, stool and dock proxies were shaped for the clinic's own room: the zone parts carry their own now.
            foreach (var t in Named("COL_Counter_Long", "COL_Counter_Angled", "COL_Stool", "COL_Unit07Frame")) Off(t);
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
