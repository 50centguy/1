using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BorderRepair.RepairProps
{
    // Storage pass of the four-prop desktop clinic (README section 12): places Resources/ClinicStorageV2 (built by
    // ClinicStorageV2Builder) in the room frame and switches off the plain painted cabinets, shelf, lockers and their
    // merged west dressing with forceRenderingOff. Old colliders stay (the new pieces keep the old footprints); the
    // west dock cables (Clutter_West_Rubber) and the south coats / bin / extinguisher stay as they were.
    // Play-time only; everything is restored on destroy.
    [AddComponentMenu("")]
    public sealed class ClinicStorageV2Runtime : MonoBehaviour
    {
        public const string ScenePath = RepairBenchV2Runtime.ScenePath;
        public const string PrefabResource = "ClinicStorageV2";
        public static readonly string[] Retired =
        {
            "Storage_RedToolChest", "Storage_NWCabinet", "Storage_NWCabinet_Note", "Storage_LimbShelf", "Storage_LimbShelf_Chart",
            "Storage_NECabinet", "Storage_NECabinet_Hazard", "Storage_Lockers", "Storage_Lockers_Photo", "Unit07_CrateLabel_OldBrg",
            "Clutter_West_DarkSteel", "Clutter_West_Ivory", "Clutter_West_Red", "Clutter_West_PaintGreen", "Clutter_West_Steel",
        };

        public static ClinicStorageV2Runtime Current { get; private set; }
        readonly List<Renderer> hidden = new List<Renderer>();
        GameObject storage;

        public GameObject Storage => storage;
        public int HiddenCount => hidden.Count;
        public int StaticRenderers { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHooks() { SceneManager.sceneLoaded -= OnSceneLoaded; Current = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() => SceneManager.sceneLoaded += OnSceneLoaded;

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!string.Equals(scene.path, ScenePath, StringComparison.OrdinalIgnoreCase)) return;
            var host = new GameObject("ClinicStorageV2 (runtime only)");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<ClinicStorageV2Runtime>();
        }

        void Awake()
        {
            Current = this;
            var prefab = Resources.Load<GameObject>(PrefabResource);
            if (prefab == null) { Debug.LogWarning("[ClinicStorageV2] Resources/" + PrefabResource + " missing (Border Repair > Repair Props > Build Clinic Storage V2); keeping the old storage."); return; }
            storage = Instantiate(prefab);
            storage.name = PrefabResource;
            SceneManager.MoveGameObjectToScene(storage, gameObject.scene);
            SurgeryTableV2Runtime.AlignToBed(storage, Vector3.zero, Vector3.forward);   // room frame = world
            foreach (var renderer in gameObject.scene.GetRootGameObjects()
                         .Where(r => r.name.StartsWith("UnifiedClinicEnvironment", StringComparison.Ordinal))
                         .SelectMany(r => r.GetComponentsInChildren<Renderer>(true)))
                if (Retired.Contains(renderer.name) && !renderer.forceRenderingOff) { renderer.forceRenderingOff = true; hidden.Add(renderer); }
            StaticRenderers = V2StaticBatch.Combine(storage, t => false);
        }

        void OnDestroy()
        {
            foreach (var r in hidden) if (r != null) r.forceRenderingOff = false;
            if (storage != null) Destroy(storage);
            if (Current == this) Current = null;
        }
    }
}
