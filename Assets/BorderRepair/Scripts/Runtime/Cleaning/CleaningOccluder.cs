using UnityEngine;

namespace BorderRepair.Cleaning
{
    /// <summary>Exact opaque geometry that blocks cleaning of the shell behind screens, labels and other parts.</summary>
    [DisallowMultipleComponent]
    public sealed class CleaningOccluder : MonoBehaviour
    {
        [SerializeField] Renderer target;
        [SerializeField] Mesh readableMesh;
        MeshCollider probe;
        GameObject probeObject;
        public Renderer Target => target;
        public Mesh ProbeMesh => readableMesh;
        public void Configure(Renderer renderer, Mesh mesh) { target = renderer; readableMesh = mesh; }
        // One probe per occluder, all parented under their part: a fixed name collided with its siblings and made
        // name-keyed hierarchy walks ambiguous (RepairPropFlow refuses duplicate object names).
        static int runtimeProbeSerial;

        void OnEnable()
        {
            if (!Application.isPlaying || probe != null || target == null || readableMesh == null || !readableMesh.isReadable) return;
            probeObject = new GameObject("CleaningOccluder_Runtime_" + ++runtimeProbeSerial) { layer = 2, hideFlags = HideFlags.DontSave };
            probeObject.transform.SetParent(target.transform, false);
            probe = probeObject.AddComponent<MeshCollider>(); probe.sharedMesh = readableMesh; probe.convex = false; probe.enabled = false;
        }
        public void SetProbeEnabled(bool value) { if (probe != null) probe.enabled = value && isActiveAndEnabled && target != null && target.enabled; }
        public bool Blocks(Ray ray, float distance) => probe != null && probe.enabled && target != null && target.enabled &&
            target.gameObject.activeInHierarchy && probe.Raycast(ray, out _, distance);
        void OnDisable() => SetProbeEnabled(false);
        void OnDestroy() { if (probeObject != null) Destroy(probeObject); }
    }
}
