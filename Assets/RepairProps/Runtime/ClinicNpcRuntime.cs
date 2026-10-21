using System.Collections.Generic;
using System.Linq;
using BorderRepair.TwoNight;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace BorderRepair.RepairProps
{
    // The clinic's people (ArtSource/ClinicNpcs, prefabs from ClinicNpcBuilder) in the four-prop desktop scene:
    //  - a portrait studio far below the room: one low-res camera per NPC, rendering only while some UI asks for it
    //    (the call window, the dealer slot in the parts terminal);
    //  - the customer of the waiting order walks in from the east entrance to the customer strip in front of the pad
    //    whose lamp is lit, faces the counter, looks at the player, talks while the intake dialogue is up, and leaves
    //    when the order moves on. Purely visual: no colliders, and it fades out if the player steps into it.
    // Play-time only; nothing in the scene asset changes.
    [AddComponentMenu("")]
    public sealed class ClinicNpcRuntime : MonoBehaviour
    {
        public const string ScenePath = RepairBenchV2Runtime.ScenePath;
        public static readonly string[] Names = { "Kasumi", "Tobi", "Gen", "Rin" };
        static readonly Vector3 StudioOrigin = new Vector3(0, -400, 0);
        static readonly Vector3 Entrance = new Vector3(.30f, 0, -3.05f);
        static readonly Vector3[] Approach = { new Vector3(.45f, 0, -2.55f), new Vector3(.77f, 0, -1.75f), new Vector3(1.30f, 0, -1.20f) };
        static readonly Vector3 ReceiveSpot = new Vector3(1.39f, 0, -.62f), DeliverSpot = new Vector3(1.39f, 0, .56f);

        public static ClinicNpcRuntime Current { get; private set; }

        sealed class Booth { public ClinicNpc npc; public Camera camera; public RenderTexture texture; public float wanted; }
        readonly Dictionary<string, Booth> booths = new Dictionary<string, Booth>();
        readonly List<Light> studioLights = new List<Light>();
        UnifiedClinicDirector clinic;
        ClinicNpc customer, leaving;
        string customerKey;
        float nextCheck;

        public ClinicNpc Customer => customer;
        public string CustomerName => customer != null ? customer.NpcName : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHooks() { SceneManager.sceneLoaded -= OnSceneLoaded; Current = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() => SceneManager.sceneLoaded += OnSceneLoaded;

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!string.Equals(scene.path, ScenePath, System.StringComparison.OrdinalIgnoreCase)) return;
            if (UnityEngine.XR.XRSettings.isDeviceActive) return;
            if (Resources.Load<GameObject>("Npcs/NPC_Kasumi") == null) return;
            var host = new GameObject("ClinicNpcs (runtime only)");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<ClinicNpcRuntime>();
        }

        public static ClinicNpc Spawn(string name, Transform parent)
        {
            var prefab = Resources.Load<GameObject>("Npcs/NPC_" + name);
            if (prefab == null) return null;
            var go = Instantiate(prefab, parent);
            go.name = "NPC_" + name;
            return go.GetComponent<ClinicNpc>();
        }

        /// <summary>The NPC who owns an order, by the order's case id (repair-prop-receiver etc.).</summary>
        public static string NpcForCase(string caseId)
        {
            if (string.IsNullOrEmpty(caseId)) return null;
            if (caseId.EndsWith("receiver")) return "Rin";
            if (caseId.EndsWith("cartridge")) return "Gen";
            if (caseId.EndsWith("power") || caseId.EndsWith("omamori")) return "Tobi";
            return null;
        }

        void Awake()
        {
            Current = this;
            // Key and rim for the booths; small ranges so nothing in the room is touched.
            foreach (var (offset, color, intensity) in new[] { (new Vector3(-.6f, 1.75f, -.9f), new Color(1f, .86f, .72f), 2.6f), (new Vector3(.7f, 1.7f, .6f), new Color(1f, .36f, .62f), 2.2f) })
            {
                var light = new GameObject("StudioLight").AddComponent<Light>();
                light.transform.SetParent(transform, false);
                light.transform.position = StudioOrigin + offset;
                light.type = LightType.Point; light.range = 3.2f; light.color = color; light.intensity = intensity; light.shadows = LightShadows.None;
                light.enabled = false;
                studioLights.Add(light);
            }
        }

        void Start() => clinic = FindObjectsByType<UnifiedClinicDirector>(FindObjectsSortMode.None).FirstOrDefault(c => c.gameObject.scene == gameObject.scene);

        /// <summary>Live portrait of an NPC; call every frame it is on screen (the booth sleeps half a second after the last call).</summary>
        public Texture Portrait(string name, bool talking = false)
        {
            if (!booths.TryGetValue(name, out var booth))
            {
                booth = MakeBooth(name);
                if (booth == null) return null;
                booths[name] = booth;
            }
            booth.wanted = Time.unscaledTime;
            booth.npc.Talking = talking;
            if (!booth.camera.enabled) { booth.camera.enabled = true; booth.npc.gameObject.SetActive(true); }
            return booth.texture;
        }

        public ClinicNpc BoothNpc(string name) => booths.TryGetValue(name, out var booth) ? booth.npc : null;

        Booth MakeBooth(string name)
        {
            int index = System.Array.IndexOf(Names, name);
            var npc = Spawn(name, transform);
            if (npc == null) return null;
            // Booths in a row on the studio floor, facing -Z (toward their cameras).
            npc.transform.position = StudioOrigin + new Vector3(index * 4f, 0, 0);
            npc.Face(Vector3.back);
            var texture = new RenderTexture(176, 220, 16, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point, name = "Portrait_" + name, antiAliasing = 1 };
            var cameraObject = new GameObject("PortraitCamera_" + name);
            cameraObject.transform.SetParent(transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.targetTexture = texture; camera.fieldOfView = 21; camera.nearClipPlane = .1f; camera.farClipPlane = 4f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f, .022f, .012f, 1);
            camera.allowHDR = false; camera.allowMSAA = false; camera.depth = -50;
            var data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderShadows = false; data.renderPostProcessing = false;
            var eye = npc.Eye != null ? npc.Eye.position : npc.transform.position + Vector3.up * 1.5f;
            var forward = npc.ModelForward;
            camera.transform.position = eye + forward * .95f + Vector3.up * .01f;
            camera.transform.rotation = Quaternion.LookRotation(eye - Vector3.up * .07f - camera.transform.position);
            npc.LookTarget = camera.transform.position;
            foreach (var light in studioLights) light.enabled = true;
            foreach (var r in npc.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return new Booth { npc = npc, camera = camera, texture = texture, wanted = Time.unscaledTime };
        }

        void Update()
        {
            foreach (var booth in booths.Values)
            {
                bool on = Time.unscaledTime - booth.wanted < .5f;
                if (booth.camera.enabled != on) { booth.camera.enabled = on; booth.npc.gameObject.SetActive(on); }
                // Studio lights follow the booth in use.
                if (on) studioLights[0].transform.position = booth.npc.transform.position + new Vector3(-.6f, 1.75f, -.9f);
                if (on) studioLights[1].transform.position = booth.npc.transform.position + new Vector3(.7f, 1.7f, .6f);
            }
            if (clinic == null || clinic.State == null) return;
            if (Time.unscaledTime >= nextCheck) { nextCheck = Time.unscaledTime + .25f; UpdateCustomer(); }
            var view = Camera.main;
            foreach (var npc in new[] { customer, leaving })
            {
                if (npc == null) continue;
                bool near = false;
                if (view != null)
                {
                    var d = npc.transform.position - view.transform.position; d.y = 0;
                    near = d.magnitude < .55f;
                    npc.LookTarget = d.magnitude < 4.5f ? view.transform.position : (Vector3?)null;
                }
                foreach (var r in npc.GetComponentsInChildren<Renderer>()) if (r.enabled == near) r.enabled = !near;
            }
            if (customer != null) customer.Talking = clinic.Counter != null && clinic.Counter.DialogueVisible;
            if (leaving != null && !leaving.Walking) { Destroy(leaving.gameObject); leaving = null; }
        }

        // Who should be at the counter now, and where.
        void UpdateCustomer()
        {
            string want = null;
            Vector3 spot = ReceiveSpot;
            var state = clinic.State;
            var cases = clinic.Station != null && clinic.Station.Session != null ? clinic.Station.Session.Shift.cases : null;
            if (cases != null && clinic.ReceiveLampState == UnifiedClinicDirector.PadLamp.On)
            {
                int next = state.ActiveTrade == null ? clinic.Station.Session.CaseIndex : state.ActiveTrade.queueIndex + 1;
                if (next >= 0 && next < cases.Count) want = NpcForCase(cases[next].caseId);
                spot = ReceiveSpot;
            }
            else if (clinic.DeliverLampState != UnifiedClinicDirector.PadLamp.Off && state.ActiveTrade != null)
            {
                want = NpcForCase(state.ActiveTrade.caseId);
                spot = DeliverSpot;
            }
            string key = want != null ? want + "@" + spot : null;
            if (key == customerKey) return;
            customerKey = key;
            if (customer != null)
            {
                if (leaving != null) Destroy(leaving.gameObject);
                leaving = customer; customer = null;
                var back = new List<Vector3>(Approach.Reverse()) { Entrance };
                leaving.WalkTo(back, Vector3.back);
            }
            if (want == null) return;
            customer = Spawn(want, transform);
            if (customer == null) return;
            customer.transform.position = Entrance;
            var route = new List<Vector3>(Approach) { spot };
            customer.WalkTo(route, Vector3.right);
        }

        void OnDestroy()
        {
            foreach (var booth in booths.Values) if (booth.texture != null) { booth.camera.targetTexture = null; booth.texture.Release(); Destroy(booth.texture); }
            if (Current == this) Current = null;
        }
    }
}
