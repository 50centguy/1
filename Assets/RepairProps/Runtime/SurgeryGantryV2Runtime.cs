using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BorderRepair.RepairProps
{
    // Puts the V2 surgery gantry (Resources/SurgeryGantryV2, built by SurgeryGantryV2Builder) over the V2 surgery
    // table in the four-prop desktop scene at play time, retiring the primitive Gantry_* / Tool_* meshes the same way
    // the table and bench retire theirs (forceRenderingOff, restored on destroy). It lands in the same bed frame as the
    // table. The six arms are posed by their joints: parked (forearms folded up under the ring) or deployed (tools
    // lowered over the table, drill and cutter spinning), switched from the control box on the table's foot rail,
    // which also switches the surgical lamp's real spot light.
    // Linked (the close-up surgery view: the player's mind wired into the table) one arm at a time is driven: the six
    // carriages turn together around the ring (a tool changer) to bring it to the work side, its shoulder drops on a
    // telescopic column, turns on its stem toward the target and its two joints bend to put the tool tip on the
    // point the player aims at (two-bone solve in the arm's vertical plane). The other arms fold up out of the way.
    [AddComponentMenu("")]
    public sealed class SurgeryGantryV2Runtime : MonoBehaviour
    {
        public const string PrefabResource = "SurgeryGantryV2";
        const float ParkShoulder = -10, ParkElbow = 75, DeployShoulder = 45, DeployElbow = -80, WorkDip = 14;
        const float MaxDrop = .95f, TurnSpeed = 110f, DriveSpeed = 1.3f, YawLimit = 75f, RetargetAngle = 45f;

        sealed class Arm
        {
            public string tool;
            public Transform shoulder, fore, spin, carriage, column;
            public Quaternion shoulderRest, foreRest, spinRest, carriageRest;
            public Vector3 pitchAxis, foldAxis, spinAxis, inward;   // world, at rest
            public Vector3 shoulderRestPos, carriageRestPos, tipLocal;
            public float upper, lower, home;   // joint to joint, elbow to tool tip; angle around the ring at rest
            public float spinAngle, work, workTarget, stance, drive;
        }

        public static SurgeryGantryV2Runtime Current { get; private set; }

        readonly List<Renderer> hidden = new List<Renderer>();
        readonly List<Arm> arms = new List<Arm>();
        GameObject gantry;
        Light lamp;
        float deploy, turn, turnTarget;
        Vector3 center, aim;
        string driven;

        public GameObject Gantry => gantry;
        public int StaticRenderers { get; private set; }
        public bool ArmsDeployed { get; private set; }
        public float DeployAmount => deploy;
        public bool LampOn => lamp != null && lamp.enabled;
        public Light Lamp => lamp;
        public int HiddenCount => hidden.Count;
        public Transform ToolTip(string tool) => arms.FirstOrDefault(a => a.tool == tool)?.fore;
        public IEnumerable<string> Tools => arms.Select(a => a.tool);
        /// <summary>The close-up view drives the arms (one at a time) instead of the room's park / deploy switch.</summary>
        public bool Linked { get; private set; }
        /// <summary>The arm being driven in the linked view (null: all folded up).</summary>
        public string Driven => driven;
        /// <summary>Degrees the carriages have turned around the ring from their home places.</summary>
        public float Turn => turn;
        /// <summary>Where the driven arm is told to put its tool tip.</summary>
        public Vector3 Aim => aim;
        /// <summary>How far the shoulder of an arm has dropped on its column (m).</summary>
        public float Drop(string tool) => arms.FirstOrDefault(a => a.tool == tool)?.column is Transform c && c.gameObject.activeSelf ? c.localScale.y * 2 : 0;

        public void Link(bool on)
        {
            Linked = on;
            if (!on) driven = null;
        }

        /// <summary>Brings one arm down to work (null folds it back up). The ring turns it to the side of its aim.</summary>
        public void Drive(string tool, Vector3 tip)
        {
            if (!Linked) return;
            var arm = tool != null ? arms.FirstOrDefault(a => a.tool == tool) : null;
            if (arm == null) { driven = null; return; }
            bool fresh = driven != tool;
            driven = tool; aim = tip;
            float wanted = Mathf.DeltaAngle(arm.home, AngleAround(tip));
            if (fresh || Mathf.Abs(Mathf.DeltaAngle(turnTarget, wanted)) > RetargetAngle) turnTarget = wanted;
        }

        /// <summary>Moves the driven arm's tool tip target (the arm follows with its joints, not at once).</summary>
        public void AimAt(Vector3 tip)
        {
            if (!Linked || driven == null) return;
            aim = tip;
            var arm = arms.FirstOrDefault(a => a.tool == driven);
            if (arm == null) return;
            // The carriages turn again only when the aim has gone well round the ring; small moves are the stem's yaw.
            float wanted = Mathf.DeltaAngle(arm.home, AngleAround(tip));
            if (Vector3.ProjectOnPlane(tip - center, Vector3.up).magnitude > .08f && Mathf.Abs(Mathf.DeltaAngle(turnTarget, wanted)) > RetargetAngle) turnTarget = wanted;
        }

        /// <summary>The driven arm is down and the ring has stopped: its tip goes where it is aimed.</summary>
        public bool Ready(string tool)
        {
            var arm = arms.FirstOrDefault(a => a.tool == tool);
            return Linked && driven == tool && arm != null && arm.drive >= .98f && Mathf.Abs(Mathf.DeltaAngle(turn, turnTarget)) < 2f;
        }

        float AngleAround(Vector3 point)
        {
            var flat = Vector3.ProjectOnPlane(point - center, Vector3.up);
            return flat.sqrMagnitude < 1e-6f ? 0 : Vector3.SignedAngle(Vector3.forward, flat, Vector3.up);
        }

        /// <summary>Lowers one deployed arm a little further, as if working on the patient (the operation's steps).</summary>
        public void Work(string tool, bool on)
        {
            var arm = arms.FirstOrDefault(a => a.tool == tool);
            if (arm != null) arm.workTarget = on ? 1 : 0;
        }

        /// <summary>World point at the working end of a tool arm: the bottom of its tool, carried by its forearm.</summary>
        public Vector3 TipPoint(string tool)
        {
            var arm = arms.FirstOrDefault(a => a.tool == tool);
            if (arm == null) return Vector3.zero;
            return arm.fore.TransformPoint(arm.tipLocal);
        }

        // The bottom of a forearm's geometry (measured once, in the authored pose, where the tool hangs straight down).
        static Vector3 Bottom(Arm arm)
        {
            var renderers = arm.fore.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return arm.fore.position;
            var b = renderers[0].bounds;
            foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
            // Straight under the elbow (the forearm hangs plumb as authored): the suction hose would pull a box centre aside.
            return new Vector3(arm.fore.position.x, b.min.y, arm.fore.position.z);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHooks() { SceneManager.sceneLoaded -= OnSceneLoaded; Current = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() => SceneManager.sceneLoaded += OnSceneLoaded;

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!string.Equals(scene.path, SurgeryTableV2Runtime.ScenePath, StringComparison.OrdinalIgnoreCase)) return;
            var host = new GameObject("SurgeryGantryV2 (runtime only)");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<SurgeryGantryV2Runtime>();
        }

        void Awake()
        {
            Current = this;
            var prefab = Resources.Load<GameObject>(PrefabResource);
            if (prefab == null || !SurgeryTableV2Runtime.TryBedFrame(gameObject.scene, out var floor, out var headDir))
            {
                Debug.LogWarning("[SurgeryGantryV2] " + (prefab == null ? "Resources/" + PrefabResource + " missing (run Border Repair > Repair Props > Build Surgery Gantry V2)" : "no Bed_Base in scene") + "; keeping the old gantry.");
                return;
            }
            gantry = Instantiate(prefab);
            gantry.name = "SurgeryGantryV2";
            SceneManager.MoveGameObjectToScene(gantry, gameObject.scene);
            SurgeryTableV2Runtime.AlignToBed(gantry, floor, headDir);

            foreach (var renderer in gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true)))
            {
                if (renderer.transform.IsChildOf(gantry.transform) || renderer.forceRenderingOff) continue;
                if (!renderer.name.StartsWith("Gantry_", StringComparison.Ordinal) && !renderer.name.StartsWith("Tool_", StringComparison.Ordinal)) continue;
                renderer.forceRenderingOff = true;
                hidden.Add(renderer);
            }

            var all = gantry.GetComponentsInChildren<Transform>(true);
            Transform Find(string name) => all.FirstOrDefault(t => t.name == name);
            foreach (var shoulder in all.Where(t => t.name.StartsWith("BGT_Arm_", StringComparison.Ordinal) && t.name.EndsWith("_Shoulder", StringComparison.Ordinal)))
            {
                string tool = shoulder.name.Substring(8, shoulder.name.Length - 8 - 9);
                var fore = Find("BGT_Arm_" + tool + "_Fore");
                var spin = Find("BGT_Arm_" + tool + "_Spin");
                // The arm reaches toward the ring centre; both joints pitch about the horizontal axis across it.
                var inward = Vector3.ProjectOnPlane(floor - shoulder.position, Vector3.up).normalized;
                var axis = Vector3.Cross(Vector3.up, inward).normalized;
                var pitch = (Quaternion.AngleAxis(10, axis) * inward).y < 0 ? axis : -axis;                 // + = reach end down
                var fold = Vector3.Dot(Quaternion.AngleAxis(10, axis) * Vector3.down, -inward) > 0 ? axis : -axis; // + = forearm swings out
                var carriage = Find("BGT_Carriage_" + tool);
                var arm = new Arm
                {
                    tool = tool, shoulder = shoulder, fore = fore, spin = spin, carriage = carriage,
                    shoulderRest = shoulder.rotation, foreRest = fore.rotation, spinRest = spin != null ? spin.rotation : Quaternion.identity,
                    carriageRest = carriage != null ? carriage.rotation : Quaternion.identity,
                    shoulderRestPos = shoulder.position, carriageRestPos = carriage != null ? carriage.position : Vector3.zero,
                    pitchAxis = pitch, foldAxis = fold, spinAxis = tool == "Cutter" ? axis : Vector3.up, inward = inward,
                    upper = Vector3.Distance(fore.position, shoulder.position),
                };
                var tip = Bottom(arm);
                arm.tipLocal = fore.InverseTransformPoint(tip);
                arm.lower = Vector3.Distance(tip, fore.position);
                arm.home = Vector3.SignedAngle(Vector3.forward, Vector3.ProjectOnPlane(shoulder.position - floor, Vector3.up), Vector3.up);
                arm.column = Column(shoulder, tool);
                arms.Add(arm);
            }
            center = floor;

            var head = Find("BGT_LampHead");
            if (head != null)
            {
                lamp = new GameObject("BGT_LampLight").AddComponent<Light>();
                lamp.transform.SetParent(head, false);
                lamp.transform.position = head.GetComponent<Renderer>().bounds.center + Vector3.down * .05f;
                lamp.transform.rotation = Quaternion.LookRotation(Vector3.down, headDir);
                lamp.type = LightType.Spot; lamp.color = new Color(1f, .96f, .88f); lamp.intensity = 3f; lamp.range = 3.2f;
                lamp.spotAngle = 60; lamp.innerSpotAngle = 30; lamp.shadows = LightShadows.Hard; lamp.shadowStrength = .6f;
            }
            foreach (var control in gantry.GetComponentsInChildren<SurgeryTableControl>(true))
                foreach (var filter in control.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.GetComponent<Collider>() == null && filter.sharedMesh != null)
                        filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
            // Shoulders and carriages move (the carriages turn round the ring when an arm is driven).
            StaticRenderers = V2StaticBatch.Combine(gantry, t => t.name.EndsWith("_Shoulder", StringComparison.Ordinal)
                || t.name.StartsWith("BGT_Carriage_", StringComparison.Ordinal) || t.name.StartsWith("BGT_Column_", StringComparison.Ordinal));
            Pose(0);
        }

        public void Activate(SurgeryControlKind kind)
        {
            if (kind == SurgeryControlKind.Arms) ArmsDeployed = !ArmsDeployed;
            else if (kind == SurgeryControlKind.Lamp && lamp != null) lamp.enabled = !lamp.enabled;
        }

        void Update()
        {
            if (gantry == null) return;
            deploy = Mathf.MoveTowards(deploy, ArmsDeployed ? 1 : 0, Time.unscaledDeltaTime * .55f);
            Pose(Mathf.SmoothStep(0, 1, deploy));
        }

        void Pose(float t)
        {
            float time = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            // Unlinked the carriages go home; linked they stay where the last arm was brought.
            turn = Mathf.MoveTowardsAngle(turn, Linked ? (driven != null ? turnTarget : turn) : 0, dt * TurnSpeed);
            if (!Linked) turnTarget = turn;
            var ring = Quaternion.AngleAxis(turn, Vector3.up);
            for (int i = 0; i < arms.Count; i++)
            {
                var a = arms[i];
                // Room pose: parked or deployed by the switch; linked, every arm folds up unless it is the driven one.
                a.stance = Mathf.MoveTowards(a.stance, Linked ? 0 : t, dt * .7f);
                a.drive = Mathf.MoveTowards(a.drive, Linked && a.tool == driven ? 1 : 0, dt * DriveSpeed);
                float s = a.stance;
                // Deployed arms breathe a little, each on its own phase, so the rig never looks frozen.
                float sway = s * 1.5f * Mathf.Sin(time * .8f + i * 1.7f);
                a.work = Mathf.MoveTowards(a.work, a.workTarget, dt * 1.6f);
                float dip = s * Mathf.SmoothStep(0, 1, a.work) * WorkDip;
                float pitch = Mathf.Lerp(ParkShoulder, DeployShoulder, s) + sway + dip, fold = Mathf.Lerp(ParkElbow, DeployElbow, s) - sway, yaw = 0, drop = 0;
                if (a.drive > 0)
                {
                    Solve(a, ring, aim, out float p, out float f, out float y, out float d);
                    float k = Mathf.SmoothStep(0, 1, a.drive);
                    pitch = Mathf.Lerp(pitch, p, k); fold = Mathf.Lerp(fold, f, k); yaw = Mathf.Lerp(0, y, k); drop = Mathf.Lerp(0, d, k);
                }
                if (a.carriage != null) a.carriage.SetPositionAndRotation(center + ring * (a.carriageRestPos - center), ring * a.carriageRest);
                var shoulderAt = center + ring * (a.shoulderRestPos - center);
                a.shoulder.position = shoulderAt + Vector3.down * drop;
                var shoulderTurn = ring * Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(pitch, a.pitchAxis);
                a.shoulder.rotation = shoulderTurn * a.shoulderRest;
                // The forearm hangs from the upper arm (a child of the shoulder): its rest axis moves with the shoulder.
                var foreTurn = Quaternion.AngleAxis(fold, shoulderTurn * a.foldAxis);
                a.fore.rotation = foreTurn * shoulderTurn * a.foreRest;
                if (a.column != null)
                {
                    // The telescopic column from the carriage down to the dropped shoulder hub.
                    bool show = drop > .004f;
                    if (a.column.gameObject.activeSelf != show) a.column.gameObject.SetActive(show);
                    if (show)
                    {
                        a.column.position = shoulderAt + Vector3.up * (.06f - drop * .5f);
                        a.column.rotation = Quaternion.identity;
                        a.column.localScale = new Vector3(.034f, drop * .5f, .034f);
                    }
                }
                float spinning = Mathf.Max(s, a.drive);
                if (a.spin == null) continue;
                a.spinAngle = (a.spinAngle + dt * 720f * spinning) % 360f;
                var carried = foreTurn * shoulderTurn;
                a.spin.rotation = Quaternion.AngleAxis(a.spinAngle, carried * a.spinAxis) * carried * a.spinRest;
            }
        }

        // Puts an arm's tool tip on a point: the shoulder drops until the point is a comfortable reach away, the stem
        // turns the arm's plane through the point, and the two joints bend elbow-up (law of cosines). Out of reach, the
        // tool points at it from as close as it gets.
        void Solve(Arm a, Quaternion ring, Vector3 target, out float pitch, out float fold, out float yaw, out float drop)
        {
            var top = center + ring * (a.shoulderRestPos - center);
            var inward = ring * a.inward;
            var flat = Vector3.ProjectOnPlane(target - top, Vector3.up);
            float h = flat.magnitude;
            yaw = h > 1e-4f ? Mathf.Clamp(Vector3.SignedAngle(inward, flat, Vector3.up), -YawLimit, YawLimit) : 0;
            float l1 = a.upper, l2 = a.lower, comfy = (l1 + l2) * .86f;
            float rise = Mathf.Sqrt(Mathf.Max(0, comfy * comfy - h * h));
            drop = Mathf.Clamp(top.y - (target.y + Mathf.Max(rise, .14f)), 0, MaxDrop);
            var shoulder = top + Vector3.down * drop;
            var u = (Quaternion.AngleAxis(yaw, Vector3.up) * inward).normalized;
            var d = target - shoulder;
            float x = Vector3.Dot(d, u), y = d.y;
            float reach = Mathf.Clamp(Mathf.Sqrt(x * x + y * y), Mathf.Abs(l1 - l2) + .01f, l1 + l2 - .001f);
            float beta = Mathf.Atan2(-y, x) * Mathf.Rad2Deg;
            float alpha = Mathf.Acos(Mathf.Clamp((l1 * l1 + reach * reach - l2 * l2) / (2 * l1 * reach), -1, 1)) * Mathf.Rad2Deg;
            pitch = beta - alpha;
            float r = pitch * Mathf.Deg2Rad;
            var elbow = shoulder + (u * Mathf.Cos(r) - Vector3.up * Mathf.Sin(r)) * l1;
            var f = target - elbow;
            float below = Mathf.Atan2(-f.y, Vector3.Dot(f, u)) * Mathf.Rad2Deg;
            // Authored, the forearm hangs straight down (90 below the reach direction); the shoulder's pitch carries it on.
            fold = below - (90 + pitch);
        }

        // A plain steel rod (no collider) standing in for the column the shoulder drops on, sized every frame.
        Transform Column(Transform shoulder, string tool)
        {
            var rod = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rod.name = "BGT_Column_" + tool;
            var collider = rod.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            rod.transform.SetParent(gantry.transform, true);
            var source = shoulder.GetComponent<Renderer>();
            var renderer = rod.GetComponent<Renderer>();
            if (source != null && source.sharedMaterials.Length > 0) renderer.sharedMaterial = source.sharedMaterials[source.sharedMaterials.Length > 1 ? 1 : 0];
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            rod.SetActive(false);
            return rod.transform;
        }

        void OnDestroy()
        {
            foreach (var renderer in hidden) if (renderer != null) renderer.forceRenderingOff = false;
            if (gantry != null) Destroy(gantry);
            if (Current == this) Current = null;
        }
    }
}
