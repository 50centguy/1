using System;
using UnityEngine;

namespace BorderRepair.RepairProps
{
    /// <summary>
    /// The clinic's gameplay stations rearranged into another room: the Berth 04 playable copy of the clinic carries one,
    /// with the stations where the B1 has them. The runtimes read their authored room points through it. Without one
    /// (RepairProps_Clinic itself) every point is exactly as authored.
    /// A station moves rigidly: p' = yaw * p + offset, in the clinic's own frame.
    /// </summary>
    [DefaultExecutionOrder(-32000)]
    public sealed class ClinicLayout : MonoBehaviour
    {
        public enum Station { Room, Counter, Dock, Bench, Surgery }

        [Serializable] public struct Move { public float yaw; public Vector3 offset; }
        [Serializable] public struct Pose { public string path; public Vector3 position; public Quaternion rotation; public bool active; }

        public static ClinicLayout Active { get; private set; }

        [Header("Stations (clinic frame -> this room)")]
        public Move counter, dock, bench, surgery;
        [Tooltip("This room's inside (walls to walls, floor to ceiling), clinic frame.")]
        public Bounds room = new Bounds(new Vector3(1.53f, 1.3f, -.49f), new Vector3(7.3f, 2.8f, 6.5f));
        [Tooltip("A second opening onto the street (the B1's display window), clinic frame.")]
        public Bounds window = new Bounds(new Vector3(2.67f, 2.12f, -3.45f), new Vector3(2.5f, .75f, .3f));
        [Tooltip("Underside of this room's ceiling: hanging things stop just short of it.")]
        public float ceiling = 2.55f;

        [Header("The zones prefab (counter, dock corner) child by child, world poses, parents first")]
        public Pose[] zones = new Pose[0];

        [Header("What does not come along")]
        public bool skipStorage = true, skipRoomKit = true, skipWallSigns = true;

        [Header("Routes (world points in this room)")]
        public Vector3 npcEntrance;
        public Vector3[] npcApproach = new Vector3[0];
        public Vector3[] patientIn = new Vector3[0];
        public Vector3 dockOut, westLane, eastLane, counterLane, counterHover, benchLane, benchHover;
        [Tooltip("Absolute yaw of the assistant over the counter and at the bench.")]
        public float counterYaw = 90, benchYaw = 270;

        void Awake() => Active = this;
        void OnDestroy() { if (Active == this) Active = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatic() => Active = null;

        Move Of(Station s) => s switch { Station.Counter => counter, Station.Dock => dock, Station.Bench => bench, Station.Surgery => surgery, _ => default };

        public static Vector3 P(Station s, Vector3 p)
        {
            if (Active == null || s == Station.Room) return p;
            var m = Active.Of(s);
            return Quaternion.Euler(0, m.yaw, 0) * p + m.offset;
        }

        public static Vector3 D(Station s, Vector3 d) => Q(s) * d;

        public static Quaternion Q(Station s) => Active == null || s == Station.Room ? Quaternion.identity : Quaternion.Euler(0, Active.Of(s).yaw, 0);

        /// <summary>Hanging things (scrim wires, projectors) end just under this room's ceiling.</summary>
        public static float Ceiling(float authored) => Active == null ? authored : Mathf.Min(authored, Active.ceiling - .03f);

        /// <summary>Puts the zones prefab's parts where this room has them (the B1 turned the racking against its
        /// partition and hung the dock corner on its back wall), and gives the racking a solid box.</summary>
        public static void ApplyZones(GameObject instance)
        {
            if (Active == null || instance == null) return;
            var root = instance.transform;
            foreach (var p in Active.zones)
            {
                var t = string.IsNullOrEmpty(p.path) ? root : root.Find(p.path);
                if (t == null) continue;
                t.SetPositionAndRotation(p.position, p.rotation);
                if (t != root && t.gameObject.activeSelf != p.active) t.gameObject.SetActive(p.active);
            }
            foreach (var r in instance.GetComponentsInChildren<MeshRenderer>(true))
                if ((r.name == "ZC_Racking" || r.name == "ZC_Counter") && r.GetComponent<Collider>() == null)
                {
                    var box = r.gameObject.AddComponent<BoxCollider>();
                    var f = r.GetComponent<MeshFilter>();
                    if (f != null && f.sharedMesh != null) { box.center = f.sharedMesh.bounds.center; box.size = f.sharedMesh.bounds.size; }
                }
        }
    }
}
