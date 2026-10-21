using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps
{
    // Cyberpunk layer of the four-prop desktop scene (ART_DIRECTION.md, SETTING_PROPS.md): places the two ClinicCyberKit
    // prefabs on the frames they were authored in (room = world, surgery = the old bed's frame), adds the two lights
    // those neons motivate (hot orange-red over the counter, magenta spill under the door sign) and gives the floor a wet
    // sheen with a one-shot reflection probe so the neon reads on it. Play-time only; everything added is destroyed and
    // the floor material restored with this object.
    [AddComponentMenu("")]
    public sealed class ClinicCyberRuntime : MonoBehaviour
    {
        public const string ScenePath = RepairBenchV2Runtime.ScenePath;
        public static ClinicCyberRuntime Current { get; private set; }

        readonly List<Object> created = new List<Object>();
        readonly List<(Renderer renderer, Material[] shared)> floors = new List<(Renderer, Material[])>();
        ReflectionProbe probe;
        int probeFrames = 3;

        public GameObject RoomKit { get; private set; }
        public GameObject SurgeryKit { get; private set; }
        public Light CounterLight { get; private set; }
        public Light SignLight { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHooks() { SceneManager.sceneLoaded -= OnSceneLoaded; Current = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() => SceneManager.sceneLoaded += OnSceneLoaded;

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!string.Equals(scene.path, ScenePath, StringComparison.OrdinalIgnoreCase) || UnityEngine.XR.XRSettings.isDeviceActive) return;
            var host = new GameObject("ClinicCyber (runtime only)");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<ClinicCyberRuntime>();
        }

        GameObject Spawn(string resource)
        {
            var prefab = Resources.Load<GameObject>(resource);
            if (prefab == null) { Debug.LogWarning("[ClinicCyber] Resources/" + resource + " missing (Border Repair > Repair Props > Build Clinic Cyber Kit)."); return null; }
            var instance = Instantiate(prefab);
            instance.name = resource;
            SceneManager.MoveGameObjectToScene(instance, gameObject.scene);
            created.Add(instance);
            return instance;
        }

        static Transform Find(GameObject root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name || t.name.StartsWith(name + ".", StringComparison.Ordinal));

        // Start: the bench / table runtimes place their assets in Awake, so their frames exist by now.
        void Start()
        {
            Current = this;
            RoomKit = Spawn("CyberRoomKit");
            if (RoomKit != null) SurgeryTableV2Runtime.AlignToBed(RoomKit, Vector3.zero, Vector3.forward);   // room frame = world
            SurgeryKit = Spawn("CyberSurgeryKit");
            if (SurgeryKit != null)
            {
                if (SurgeryTableV2Runtime.TryBedFrame(gameObject.scene, out var floor, out var head)) SurgeryTableV2Runtime.AlignToBed(SurgeryKit, floor, head);
                else SurgeryKit.SetActive(false);
            }

            if (RoomKit != null)
            {
                var tubes = Find(RoomKit, "CY_CounterTubes");
                if (tubes != null) CounterLight = AddLight("CY_CounterNeonLight", tubes.GetComponent<Renderer>().bounds.center + Vector3.down * .15f, new Color(1f, .36f, .16f), 2.4f, 2.8f);
                var sign = Find(RoomKit, "CY_SignPlate_Main");
                if (sign != null) SignLight = AddLight("CY_DoorSignLight", sign.GetComponent<Renderer>().bounds.center + Vector3.forward * .35f, new Color(1f, .22f, .62f), 1.4f, 2.4f);
            }
            WetFloor();
        }

        Light AddLight(string name, Vector3 at, Color color, float intensity, float range)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.transform.SetParent(transform, false);
            light.transform.position = at;
            light.type = LightType.Point; light.color = color; light.intensity = intensity; light.range = range; light.shadows = LightShadows.None;
            created.Add(light.gameObject);
            return light;
        }

        // Wet-looking concrete: a private copy of the floor material with higher smoothness, and a reflection probe
        // rendered once (neon and lamps then streak across the floor). The floor's original shared materials return
        // on destroy.
        void WetFloor()
        {
            foreach (var renderer in gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true))
                         .Where(r => r.name == "Floor_Main"))
            {
                floors.Add((renderer, renderer.sharedMaterials));
                var copies = renderer.sharedMaterials.Select(m =>
                {
                    if (m == null) return null;
                    var copy = new Material(m) { name = m.name + " (wet)" };
                    if (copy.HasProperty("_Smoothness")) copy.SetFloat("_Smoothness", .62f);
                    if (copy.HasProperty("_Metallic")) copy.SetFloat("_Metallic", Mathf.Max(copy.GetFloat("_Metallic"), .05f));
                    created.Add(copy);
                    return copy;
                }).ToArray();
                renderer.sharedMaterials = copies;
            }
            var go = new GameObject("CY_FloorReflection");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(0, 1.3f, 0);
            probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.size = new Vector3(7.2f, 3.1f, 7.2f);
            probe.boxProjection = true;
            probe.resolution = 128;
            probe.importance = 2;
            created.Add(go);
        }

        void Update()
        {
            // The probe renders once, a few frames in, after every V2 asset and the neon are in place.
            if (probe != null && probeFrames > 0 && --probeFrames == 0) probe.RenderProbe();
        }

        void OnDestroy()
        {
            foreach (var (renderer, shared) in floors) if (renderer != null) renderer.sharedMaterials = shared;
            foreach (var o in created) if (o != null) Destroy(o);
            if (Current == this) Current = null;
        }
    }
}
