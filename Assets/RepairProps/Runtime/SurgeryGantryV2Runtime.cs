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
    [AddComponentMenu("")]
    public sealed class SurgeryGantryV2Runtime : MonoBehaviour
    {
        public const string PrefabResource = "SurgeryGantryV2";
        const float ParkShoulder = -10, ParkElbow = 75, DeployShoulder = 45, DeployElbow = -80, WorkDip = 14;

        sealed class Arm
        {
            public string tool;
            public Transform shoulder, fore, spin;
            public Quaternion shoulderRest, foreRest, spinRest;
            public Vector3 pitchAxis, foldAxis, spinAxis;   // world, at rest
            public float spinAngle, work, workTarget;
        }

        public static SurgeryGantryV2Runtime Current { get; private set; }

        readonly List<Renderer> hidden = new List<Renderer>();
        readonly List<Arm> arms = new List<Arm>();
        GameObject gantry;
        Light lamp;
        float deploy;

        public GameObject Gantry => gantry;
        public int StaticRenderers { get; private set; }
        public bool ArmsDeployed { get; private set; }
        public float DeployAmount => deploy;
        public bool LampOn => lamp != null && lamp.enabled;
        public Light Lamp => lamp;
        public int HiddenCount => hidden.Count;
        public Transform ToolTip(string tool) => arms.FirstOrDefault(a => a.tool == tool)?.fore;
        public IEnumerable<string> Tools => arms.Select(a => a.tool);

        /// <summary>Lowers one deployed arm a little further, as if working on the patient (the operation's steps).</summary>
        public void Work(string tool, bool on)
        {
            var arm = arms.FirstOrDefault(a => a.tool == tool);
            if (arm != null) arm.workTarget = on ? 1 : 0;
        }

        /// <summary>World point at the working end of a tool arm: the bottom of its forearm's geometry.</summary>
        public Vector3 TipPoint(string tool)
        {
            var arm = arms.FirstOrDefault(a => a.tool == tool);
            if (arm == null) return Vector3.zero;
            var renderers = arm.fore.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return arm.fore.position;
            var b = renderers[0].bounds;
            foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
            return new Vector3(b.center.x, b.min.y, b.center.z);
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
                arms.Add(new Arm
                {
                    tool = tool, shoulder = shoulder, fore = fore, spin = spin,
                    shoulderRest = shoulder.rotation, foreRest = fore.rotation, spinRest = spin != null ? spin.rotation : Quaternion.identity,
                    pitchAxis = pitch, foldAxis = fold, spinAxis = tool == "Cutter" ? axis : Vector3.up,
                });
            }

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
            StaticRenderers = V2StaticBatch.Combine(gantry, t => t.name.EndsWith("_Shoulder", StringComparison.Ordinal));
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
            float time = Time.unscaledTime;
            for (int i = 0; i < arms.Count; i++)
            {
                var a = arms[i];
                // Deployed arms breathe a little, each on its own phase, so the rig never looks frozen.
                float sway = t * 1.5f * Mathf.Sin(time * .8f + i * 1.7f);
                a.work = Mathf.MoveTowards(a.work, a.workTarget, Time.unscaledDeltaTime * 1.6f);
                float dip = t * Mathf.SmoothStep(0, 1, a.work) * WorkDip;
                var shoulderTurn = Quaternion.AngleAxis(Mathf.Lerp(ParkShoulder, DeployShoulder, t) + sway + dip, a.pitchAxis);
                a.shoulder.rotation = shoulderTurn * a.shoulderRest;
                // The forearm hangs from the upper arm (a child of the shoulder): its rest axis moves with the shoulder.
                var foreTurn = Quaternion.AngleAxis(Mathf.Lerp(ParkElbow, DeployElbow, t) - sway, shoulderTurn * a.foldAxis);
                a.fore.rotation = foreTurn * shoulderTurn * a.foreRest;
                if (a.spin == null) continue;
                a.spinAngle = (a.spinAngle + Time.unscaledDeltaTime * 720f * t) % 360f;
                var carried = foreTurn * shoulderTurn;
                a.spin.rotation = Quaternion.AngleAxis(a.spinAngle, carried * a.spinAxis) * carried * a.spinRest;
            }
        }

        void OnDestroy()
        {
            foreach (var renderer in hidden) if (renderer != null) renderer.forceRenderingOff = false;
            if (gantry != null) Destroy(gantry);
            if (Current == this) Current = null;
        }
    }
}
