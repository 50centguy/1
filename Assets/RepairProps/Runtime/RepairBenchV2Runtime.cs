using System;
using System.Collections.Generic;
using System.Linq;
using BorderRepair.TwoNight;
using BorderRepair.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BorderRepair.RepairProps
{
    // Puts the V2 repair bench (Resources/RepairBenchV2, built by RepairBenchV2Builder) into the four-prop desktop
    // scene at play time and retires the primitive WorkbenchArea bench it replaces. Nothing in the scene asset changes:
    // old renderers are switched off with forceRenderingOff (their bounds stay valid, so the clinic still measures the
    // bench top from Bench_Top), their colliders are disabled, and everything is restored if this object is destroyed.
    // It also drives the bench's tool rack, the task lamp and the CRT readout.
    [AddComponentMenu("")]
    public sealed class RepairBenchV2Runtime : MonoBehaviour
    {
        public const string ScenePath = "Assets/RepairProps/Scenes/RepairProps_Clinic.unity";
        public const string PrefabResource = "RepairBenchV2";
        // Everything WorkbenchArea still draws: the bench itself, its desk clutter and (wall pass) the pegboard, upper
        // shelf, parts bins and the papers pinned above the bench, all rebuilt in the V2 asset.
        static readonly string[] HiddenPrefixes = { "Bench_", "Diag_", "Lamp_", "Toolbox_", "Tray_", "Placeholder_", "Box_",
            "Clutter_", "Records_", "Storage_", "Wall_" };
        // The room's wall-mounted monitor arm over the bench doubles the V2 bench's CRT.
        const string MonitorArm = "UC_MonitorArm";

        public static RepairBenchV2Runtime Current { get; private set; }

        readonly List<Renderer> hidden = new List<Renderer>();
        readonly List<Collider> disabled = new List<Collider>();
        readonly List<Light> darkened = new List<Light>();
        readonly List<RepairBenchProp> props = new List<RepairBenchProp>();
        GameObject bench;
        Transform benchTop;
        Light lamp;
        Text crt;
        float nextCrt;
        UnifiedClinicDirector clinic;
        Transform fixtureNeedleV, fixtureNeedleA;
        Quaternion needleRestV, needleRestA;
        Vector3 needleAxisV, needleAxisA;
        Renderer fixtureLedPower, fixtureLedLink;
        float needleV, needleA;

        Transform koi, puck, catArm;
        Quaternion koiRest, catRest;
        Vector3 koiOffset, catAxis;

        public Transform FixtureNeedleV => fixtureNeedleV;
        public Transform HoloKoi => koi;
        public Transform LuckyCatArm => catArm;
        public bool FixturePowerLamp => fixtureLedPower != null && fixtureLedPower.enabled;
        public bool FixtureLinkLamp => fixtureLedLink != null && fixtureLedLink.enabled;

        public GameObject Bench => bench;
        public int StaticRenderers { get; private set; }
        public IReadOnlyList<RepairBenchProp> Props => props;
        public int HiddenCount => hidden.Count;
        public Light Lamp => lamp;
        public string CrtText => crt != null ? crt.text : "";
        /// <summary>The bench CRT's screen surface (its readout canvas).</summary>
        public RectTransform CrtScreen => crt != null ? (RectTransform)crt.transform.parent : null;
        public RepairBenchProp FindProp(string objectName) => props.FirstOrDefault(p => p.name == objectName);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHooks() { SceneManager.sceneLoaded -= OnSceneLoaded; Current = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() => SceneManager.sceneLoaded += OnSceneLoaded;

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!string.Equals(scene.path, ScenePath, StringComparison.OrdinalIgnoreCase)) return;
            var host = new GameObject("RepairBenchV2 (runtime only)");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<RepairBenchV2Runtime>();
        }

        void Awake()
        {
            Current = this;
            var prefab = Resources.Load<GameObject>(PrefabResource);
            benchTop = gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == "Bench_Top");
            if (prefab == null || benchTop == null)
            {
                Debug.LogWarning("[RepairBenchV2] " + (prefab == null ? "Resources/" + PrefabResource + " missing (run Border Repair > Repair Props > Build Repair Bench V2)" : "no Bench_Top in scene") + "; keeping the old bench.");
                return;
            }
            bench = Instantiate(prefab);
            bench.name = "RepairBenchV2";
            SceneManager.MoveGameObjectToScene(bench, gameObject.scene);
            var reference = Find(bench.transform, "Ref_BenchTopOrigin");
            // The new FBX shares the old bench's frame and export axes, so its reference empty lands on Bench_Top's pivot.
            bench.transform.rotation = benchTop.rotation * Quaternion.Inverse(reference.rotation) * bench.transform.rotation;
            bench.transform.position += benchTop.position - reference.position;
            HideOldBench();
            var lip = Find(bench.transform, "BV2_TopLip");
            var front = Vector3.ProjectOnPlane(lip.position - reference.position, Vector3.up).normalized;
            SetupProps(front);
            SetupCrt(front);
            SetupFixture(front);
            koi = Find(bench.transform, "BV2_Holo_Koi");
            puck = Find(bench.transform, "BV2_HoloPuck");
            catArm = Find(bench.transform, "BV2_LuckyCat_Arm");
            if (koi != null && puck != null) { koiRest = koi.rotation; koiOffset = koi.position - puck.position; }
            if (catArm != null) { catRest = catArm.localRotation; catAxis = Quaternion.Inverse(catArm.rotation) * Vector3.Cross(Vector3.up, front).normalized; }
            StaticRenderers = V2StaticBatch.Combine(bench, t => t.GetComponent<RepairBenchProp>() != null
                || t.name.StartsWith("BV2_Fixture_Needle", StringComparison.Ordinal) || t == koi || t == catArm);
        }

        void HideOldBench()
        {
            var root = benchTop;
            while (root.parent != null && !root.name.StartsWith("WorkbenchArea", StringComparison.Ordinal)) root = root.parent;
            // The old task lamp's light (soft shadows) would double up with the V2 lamp: retire it with its mesh.
            // (It lives in the scene's lighting rig, not under the bench.)
            foreach (var light in gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Light>(true)))
                if (light.enabled && light.name.StartsWith("Bench_WorkLamp", StringComparison.Ordinal)) { light.enabled = false; darkened.Add(light); }
            var arm = gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == MonitorArm);
            var candidates = root.GetComponentsInChildren<Renderer>(true).Where(r => HiddenPrefixes.Any(p => r.name.StartsWith(p, StringComparison.Ordinal)));
            if (arm != null) candidates = candidates.Concat(arm.GetComponentsInChildren<Renderer>(true));
            foreach (var renderer in candidates)
            {
                if (renderer.forceRenderingOff) continue;
                renderer.forceRenderingOff = true;
                hidden.Add(renderer);
                if (renderer.transform == benchTop) continue; // the clinic measures and parks on Bench_Top
                foreach (var collider in renderer.GetComponents<Collider>())
                    if (collider.enabled && !collider.isTrigger) { collider.enabled = false; disabled.Add(collider); }
            }
        }

        void SetupProps(Vector3 front)
        {
            foreach (var mesh in bench.GetComponentsInChildren<MeshFilter>(true))
                // No collider on the 4 mm service mat: parked parts rest on the bench top through it (as with the old mat).
                if (mesh.GetComponent<Collider>() == null && mesh.sharedMesh != null && mesh.name != "BV2_Mat")
                    mesh.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh.sharedMesh;
            var bulb = Find(bench.transform, "BV2_Lamp_Bulb");
            if (bulb != null)
            {
                lamp = new GameObject("BV2_LampLight").AddComponent<Light>();
                lamp.transform.SetParent(bulb, false);
                lamp.type = LightType.Spot; lamp.color = new Color(1f, .8f, .55f); lamp.intensity = 2.0f; lamp.range = 2.2f;
                lamp.spotAngle = 80; lamp.innerSpotAngle = 35; lamp.shadows = LightShadows.Hard; lamp.shadowStrength = .55f;
                var aim = Find(bench.transform, "BV2_Mat");
                lamp.transform.rotation = Quaternion.LookRotation((aim != null ? aim.position : benchTop.position) - bulb.position, Vector3.up);
            }
            foreach (var prop in bench.GetComponentsInChildren<RepairBenchProp>(true))
            {
                if (prop.Kind == RepairBenchPropKind.Door)
                {
                    // Swing the free edge toward the player, whichever way the bench is turned.
                    var far = prop.GetComponentInChildren<Renderer>().bounds.center;
                    var probe = Quaternion.AngleAxis(-90, Vector3.up) * (far - prop.transform.position);
                    if (Vector3.Dot(probe, front) < 0) prop.Configure(RepairBenchPropKind.Door, prop.Label, null, 0, 100);
                }
                prop.Initialize(front, prop.Kind == RepairBenchPropKind.Lamp ? lamp : null);
                props.Add(prop);
            }
        }

        void SetupFixture(Vector3 front)
        {
            fixtureNeedleV = Find(bench.transform, "BV2_Fixture_NeedleV");
            fixtureNeedleA = Find(bench.transform, "BV2_Fixture_NeedleA");
            if (fixtureNeedleV == null || fixtureNeedleA == null) { fixtureNeedleV = null; return; }
            needleRestV = fixtureNeedleV.localRotation; needleRestA = fixtureNeedleA.localRotation;
            // The dial faces the player: swing about the world "toward the player" axis, expressed in each needle's parent.
            needleAxisV = Quaternion.Inverse(fixtureNeedleV.rotation) * -front;
            needleAxisA = Quaternion.Inverse(fixtureNeedleA.rotation) * -front;
            fixtureLedPower = Find(bench.transform, "BV2_Fixture_LedPower")?.GetComponent<Renderer>();
            fixtureLedLink = Find(bench.transform, "BV2_Fixture_LedLink")?.GetComponent<Renderer>();
        }

        void SetupCrt(Vector3 front)
        {
            var screen = Find(bench.transform, "BV2_Crt_Screen");
            if (screen == null) return;
            // The screen is a flat quad (it may be turned toward the work area): its thinnest local axis is the normal.
            var mesh = screen.GetComponent<MeshFilter>().sharedMesh;
            var size = Vector3.Scale(mesh.bounds.size, screen.lossyScale);
            int thin = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
            var axes = new[] { screen.right, screen.up, screen.forward };
            var normal = axes[thin];
            if (Vector3.Dot(normal, front) < 0) normal = -normal;
            int a = (thin + 1) % 3, b = (thin + 2) % 3;
            int upAxis = Mathf.Abs(Vector3.Dot(axes[a], Vector3.up)) > Mathf.Abs(Vector3.Dot(axes[b], Vector3.up)) ? a : b;
            float height = size[upAxis], width = size[upAxis == a ? b : a];
            var center = screen.TransformPoint(mesh.bounds.center);
            var canvasObject = new GameObject("BV2_CrtReadout", typeof(RectTransform));
            canvasObject.transform.SetParent(bench.transform, false);
            var canvas = canvasObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)canvasObject.transform;
            const float unit = .0005f;
            rect.sizeDelta = new Vector2(width / unit, height / unit);
            rect.localScale = Vector3.one * unit;
            rect.SetPositionAndRotation(center + normal * .003f, Quaternion.LookRotation(-normal, Vector3.up));
            var textObject = new GameObject("Text", typeof(RectTransform));
            textObject.transform.SetParent(rect, false);
            var textRect = (RectTransform)textObject.transform;
            textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one; textRect.offsetMin = new Vector2(14, 10); textRect.offsetMax = new Vector2(-14, -10);
            crt = textObject.AddComponent<Text>();
            crt.font = RepairPropCyberStyle.UiFontOr(UIFontProvider.GetCjkFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));
            crt.fontSize = 22; crt.lineSpacing = .95f; crt.color = new Color(.45f, 1f, .62f); crt.alignment = TextAnchor.UpperLeft;
            crt.horizontalOverflow = HorizontalWrapMode.Wrap; crt.verticalOverflow = VerticalWrapMode.Truncate; crt.raycastTarget = false;
            crt.text = "BRS-07 DIAG\n> 待机";
        }

        void Update()
        {
            if (bench == null) return;
            if (clinic == null) clinic = FindFirstObjectByType<UnifiedClinicDirector>();
            var station = clinic != null ? clinic.Station : null;
            var item = station != null && station.Inspector != null ? station.Inspector.CurrentItem : null;
            var bridge = item != null ? item.GetComponent<RepairPropStationBridge>() : null;
            bool atBench = bridge != null && bridge.WorkspaceVisible;
            foreach (var prop in props)
                if (prop.Kind == RepairBenchPropKind.Tool) prop.SetSelected(atBench && station.ActiveTool == prop.Tool);
            if (crt != null && Time.unscaledTime >= nextCrt)
            {
                nextCrt = Time.unscaledTime + .25f;
                crt.text = CrtLines(bridge);
            }
            DriveFixture(bridge);
            AnimateTrinkets();
        }

        // The desk's cheap toys: a holographic koi circling its projector puck, and the lucky cat's waving paw.
        void AnimateTrinkets()
        {
            float time = Time.unscaledTime;
            if (koi != null)
            {
                float a = time * 50f;
                var orbit = Quaternion.AngleAxis(a, Vector3.up);
                koi.position = puck.position + orbit * koiOffset + Vector3.up * (.006f * Mathf.Sin(time * 1.7f));
                koi.rotation = orbit * Quaternion.AngleAxis(8f * Mathf.Sin(time * 6f), Vector3.up) * koiRest;
            }
            if (catArm != null) catArm.localRotation = catRest * Quaternion.AngleAxis(28f * Mathf.Sin(time * 3.2f), catAxis);
        }

        // The leased test fixture on the riser shows the flow's own state: PWR lamp and the volts needle follow test
        // power, the LINK lamp follows the external test connection, the milliamps needle swings while a load is applied.
        // Needles rest pegged left; the swing is about the panel normal (toward the player).
        void DriveFixture(RepairPropStationBridge bridge)
        {
            if (fixtureNeedleV == null) return;
            var flow = bridge != null ? bridge.Flow : null;
            bool power = flow != null && flow.GetState("power") != 0;
            bool link = flow != null && flow.GetState("fixture") != 0;
            float t = 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime);
            needleV = Mathf.Lerp(needleV, power ? 1 : 0, t);
            float load = power && link ? .55f + .08f * Mathf.Sin(Time.unscaledTime * 2.3f) + .03f * Mathf.Sin(Time.unscaledTime * 11f) : 0f;
            needleA = Mathf.Lerp(needleA, load, t);
            fixtureNeedleV.localRotation = needleRestV * Quaternion.AngleAxis(Mathf.Lerp(-45, 35, needleV), needleAxisV);
            fixtureNeedleA.localRotation = needleRestA * Quaternion.AngleAxis(Mathf.Lerp(-45, 45, needleA), needleAxisA);
            if (fixtureLedPower != null) fixtureLedPower.enabled = power;
            if (fixtureLedLink != null) fixtureLedLink.enabled = link;
        }

        static string CrtLines(RepairPropStationBridge bridge)
        {
            if (bridge == null || bridge.Flow == null || bridge.Flow.Definition == null) return "BRS-07 DIAG\n> 待机 · 无工件\n> 收件后上台连接";
            var flow = bridge.Flow;
            var lines = new List<string> { "BRS-07 DIAG  " + RepairPropText.Item(flow.Definition.kind), "> 阶段 " + RepairPropWorkflowStatus.Label(RepairPropWorkflowStatus.Phase(flow)) };
            foreach (var test in flow.Definition.tests)
            {
                var status = RepairPropWorkflowStatus.TestStatus(flow, test, bridge.TestJournal);
                string tag = status == RepairPropTestStatus.Passed || status == RepairPropTestStatus.Normal ? "[ OK ]"
                    : status == RepairPropTestStatus.ExpectedFault || status == RepairPropTestStatus.RetestFailed ? "[FAIL]" : "[ -- ]";
                lines.Add(tag + " " + test.label);
            }
            lines.Add("PWR " + (flow.GetState("power") != 0 ? "ON " : "OFF") + "  FIX " + (flow.GetState("fixture") != 0 ? "ON" : "OFF"));
            return string.Join("\n", lines);
        }

        static Transform Find(Transform root, string name) =>
            root.name == name ? root : root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        void OnDestroy()
        {
            foreach (var renderer in hidden) if (renderer != null) renderer.forceRenderingOff = false;
            foreach (var collider in disabled) if (collider != null) collider.enabled = true;
            foreach (var light in darkened) if (light != null) light.enabled = true;
            hidden.Clear(); disabled.Clear(); darkened.Clear();
            if (bench != null) Destroy(bench);
            if (Current == this) Current = null;
        }
    }
}
