using System.Collections.Generic;
using System.Linq;
using BorderRepair.Core;
using BorderRepair.TwoNight;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BorderRepair.RepairProps
{
    // Flow transforms stay synchronous and authoritative. Only these mesh proxies animate/park in world space.
    [DefaultExecutionOrder(1000)]
    public sealed class RepairPropBenchPresentation : MonoBehaviour
    {
        sealed class Part
        {
            public PropNode node;
            public Transform source, proxy;
            public Vector3 homePosition, parkedPosition, startPosition, exitDirection;
            public Quaternion homeRotation, parkedRotation, startRotation;
            public Renderer[] originals, visuals;
            public Collider[] colliders;
            public Collider[] proxyColliders;
            public bool[] rendererEnabled, colliderEnabled;
            public bool removed, moving;
            public float started;
        }

        const float Duration = .9f;
        const float TableGap = .0015f;
        readonly List<Part> parts = new List<Part>();
        RepairPropFlow flow;
        UnifiedClinicDirector clinic;
        GameObject parkingRoot;
        bool laidOut;
        bool expandedView;
        public bool AllSlotsFit { get; private set; } = true;
        public bool IsAnimating => parts.Any(p => p.moving);
        public GameObject ParkingRoot => parkingRoot;
        public Transform ParkedVisual(string target) => parts.Find(p => p.node.objectName == target)?.proxy;

        public void Initialize(RepairPropFlow owner, UnifiedClinicDirector director)
        {
            flow = owner; clinic = director;
            parkingRoot = new GameObject("RepairParts_OnBench");
            SceneManager.MoveGameObjectToScene(parkingRoot, gameObject.scene);
            foreach (var node in flow.Definition.nodes.Where(n => n.movable))
            {
                var source = flow.GetObject(node.objectName);
                var renderers = source.GetComponentsInChildren<MeshRenderer>(true)
                    .Where(r => MovableOwner(r.transform) == source && r.GetComponent<MeshFilter>()?.sharedMesh != null).Cast<Renderer>().ToArray();
                if (renderers.Length == 0) continue;
                var colliders = source.GetComponentsInChildren<Collider>(true)
                    .Where(c => MovableOwner(c.transform) == source).ToArray();
                var proxy = new GameObject(node.objectName + "_BenchVisual").transform;
                proxy.SetParent(parkingRoot.transform, false);
                proxy.SetPositionAndRotation(source.position, source.rotation);
                proxy.localScale = source.lossyScale;
                proxy.gameObject.AddComponent<RepairPropParkedPart>().Configure(flow, node.objectName);
                foreach (var renderer in renderers)
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) continue;
                    var mesh = new GameObject(renderer.name).transform;
                    mesh.SetParent(proxy, false);
                    mesh.localPosition = source.InverseTransformPoint(renderer.transform.position);
                    mesh.localRotation = Quaternion.Inverse(source.rotation) * renderer.transform.rotation;
                    mesh.localScale = Divide(renderer.transform.lossyScale, source.lossyScale);
                    mesh.gameObject.layer = renderer.gameObject.layer;
                    mesh.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    mesh.gameObject.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
                    mesh.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                }
                var part = new Part { node = node, source = source, proxy = proxy,
                    homePosition = flow.InstalledLocalPosition(node.objectName), homeRotation = flow.InstalledLocalRotation(node.objectName),
                    originals = renderers, colliders = colliders,
                    rendererEnabled = renderers.Select(r => r.enabled).ToArray(),
                    colliderEnabled = colliders.Select(c => c.enabled).ToArray(),
                    visuals = proxy.GetComponentsInChildren<Renderer>(), proxyColliders = proxy.GetComponentsInChildren<Collider>(),
                    removed = IsRemoved(node) };
                parts.Add(part);
                proxy.gameObject.SetActive(false);
            }
            flow.Changed += Synchronize;
        }

        Transform MovableOwner(Transform t)
        {
            while (t != null && t != flow.transform.parent)
            {
                if (flow.Definition.FindNode(t.name)?.movable == true) return t;
                t = t.parent;
            }
            return null;
        }

        static Vector3 Divide(Vector3 a, Vector3 b) => new Vector3(a.x / b.x, a.y / b.y, a.z / b.z);
        bool IsRemoved(PropNode node) => flow.GetState(node.outKey) == node.outValue;
        bool OnBench => flow != null && !flow.Completed && clinic != null && !clinic.Carrying
            && flow.Session != null && flow.Session.Stage == RepairStage.Inspect
            && clinic.Station.Inspector.CurrentItem == gameObject
            && transform.parent == clinic.Station.Inspector.ItemAnchor;

        static Bounds VisualBounds(IEnumerable<Renderer> renderers)
        {
            bool found = false;
            var result = new Bounds();
            foreach (var r in renderers)
            {
                if (!found) { result = r.bounds; found = true; }
                else result.Encapsulate(r.bounds);
            }
            return result;
        }

        void HomePose(Part part, out Vector3 position, out Quaternion rotation)
        {
            position = part.source.parent.TransformPoint(part.homePosition);
            rotation = part.source.parent.rotation * part.homeRotation;
        }

        void Layout()
        {
            var table = clinic.WorkAnchor.GetComponentInParent<Renderer>();
            if (table == null) return;
            var tableBounds = table.bounds;
            var body = VisualBounds(gameObject.GetComponentsInChildren<Renderer>()
                .Where(r => r.enabled && r.gameObject.activeInHierarchy));
            body.Expand(.035f);
            var occupied = new List<Bounds> { body };
            var front = Vector3.ProjectOnPlane(clinic.BenchStandPoint - clinic.WorkAnchor.position, Vector3.up).normalized;
            var candidates = new List<Vector3>();
            for (float x = tableBounds.min.x + .02f; x < tableBounds.max.x - .02f; x += .025f)
                for (float z = tableBounds.min.z + .02f; z < tableBounds.max.z - .02f; z += .025f)
                    candidates.Add(new Vector3(x, clinic.BenchTopWorldY, z));
            var center = new Vector3(body.center.x, clinic.BenchTopWorldY, body.center.z);
            candidates.Sort((a, b) => ((a - center).sqrMagnitude - .05f * Vector3.Dot(a - center, front))
                .CompareTo((b - center).sqrMagnitude - .05f * Vector3.Dot(b - center, front)));

            foreach (var part in parts)
            {
                part.proxy.gameObject.SetActive(true);
                part.colliderEnabled = part.colliders.Select(c => c.enabled).ToArray();
            }

            // Pack large covers/modules first, then fasteners into the remaining tabletop area.
            foreach (var part in parts.OrderByDescending(p => VisualBounds(p.visuals).size.sqrMagnitude))
            {
                HomePose(part, out var home, out var rotation);
                part.proxy.gameObject.SetActive(true);
                float lowest = float.PositiveInfinity;
                foreach (var angle in new[] { Vector3.zero, new Vector3(90, 0, 0), new Vector3(-90, 0, 0),
                    new Vector3(0, 0, 90), new Vector3(0, 0, -90), new Vector3(180, 0, 0) })
                {
                    part.proxy.rotation = rotation * Quaternion.Euler(angle);
                    float height = VisualBounds(part.visuals).size.y;
                    if (height < lowest) { lowest = height; part.parkedRotation = part.proxy.rotation; }
                }
                part.proxy.rotation = part.parkedRotation;
                var bounds = VisualBounds(part.visuals);
                var extents = bounds.extents + new Vector3(.008f, 0, .008f);
                bool fit = false;
                var flatRotation = part.parkedRotation;
                foreach (float yaw in new[] { 0f, 90f })
                {
                part.proxy.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * flatRotation;
                bounds = VisualBounds(part.visuals);
                extents = bounds.extents + new Vector3(.008f, 0, .008f);
                foreach (var candidate in candidates)
                {
                    var footprint = new Bounds(candidate, new Vector3(extents.x * 2, .01f, extents.z * 2));
                    if (footprint.min.x < tableBounds.min.x + .01f || footprint.max.x > tableBounds.max.x - .01f
                        || footprint.min.z < tableBounds.min.z + .01f || footprint.max.z > tableBounds.max.z - .01f) continue;
                    if (occupied.Any(b => footprint.min.x < b.max.x && footprint.max.x > b.min.x
                        && footprint.min.z < b.max.z && footprint.max.z > b.min.z)) continue;
                    var restingCenter = candidate + Vector3.up * (bounds.extents.y + TableGap);
                    if (Physics.OverlapBox(restingCenter, new Vector3(extents.x, Mathf.Max(bounds.extents.y, .0005f), extents.z),
                        Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Any(c => !OwnGeometry(c.transform))) continue;
                    var camera = clinic.Station.Inspector.ViewCamera;
                    var viewRay = new Ray(camera.transform.position, candidate - camera.transform.position);
                    if (body.IntersectRay(viewRay, out float occlusionDistance)
                        && occlusionDistance < Vector3.Distance(candidate, camera.transform.position) - .01f) continue;
                    var direction = restingCenter - camera.transform.position;
                    if (Physics.RaycastAll(new Ray(camera.transform.position, direction), direction.magnitude - .003f,
                        ~0, QueryTriggerInteraction.Ignore).Any(h => !OwnGeometry(h.transform))) continue;
                    bool inFrame = true;
                    for (int corner = 0; corner < 4; corner++)
                    {
                        var world = candidate + new Vector3((corner & 1) == 0 ? -extents.x : extents.x,
                            TableGap, (corner & 2) == 0 ? -extents.z : extents.z);
                        var screen = camera.WorldToViewportPoint(world);
                        if (screen.z <= 0 || screen.x < .05f || screen.x > .95f || screen.y < .06f || screen.y > .94f)
                        { inFrame = false; break; }
                    }
                    if (!inFrame) continue;
                    part.parkedPosition = part.proxy.position + candidate - bounds.center
                        + Vector3.up * (bounds.extents.y + TableGap);
                    occupied.Add(footprint);
                    fit = true; break;
                }
                if (fit) { part.parkedRotation = part.proxy.rotation; break; }
                }
                if (!fit)
                {
                    if (!expandedView)
                    {
                        expandedView = true;
                        clinic.Station.Inspector.FrameBenchOverview();
                        Layout();
                        return;
                    }
                    AllSlotsFit = false;
                    part.parkedPosition = part.proxy.position + new Vector3(tableBounds.center.x,
                        clinic.BenchTopWorldY + bounds.extents.y + TableGap, tableBounds.min.z + extents.z + .01f) - bounds.center;
                    Debug.LogWarning("[RepairProps] No clear tabletop slot for " + part.node.objectName, this);
                }
                part.proxy.SetPositionAndRotation(part.parkedPosition, part.parkedRotation);
                part.proxy.gameObject.SetActive(part.removed);
            }
            laidOut = true;
            Synchronize();
        }

        void Synchronize()
        {
            if (!laidOut || !OnBench) return;
            foreach (var part in parts)
            {
                bool removed = IsRemoved(part.node);
                if (removed == part.removed) continue;
                if (!part.moving)
                {
                    if (!part.removed) { HomePose(part, out var p, out var r); part.proxy.SetPositionAndRotation(p, r); }
                    else part.proxy.SetPositionAndRotation(part.parkedPosition, part.parkedRotation);
                }
                part.startPosition = part.proxy.position;
                part.startRotation = part.proxy.rotation;
                for (int i = 0; i < part.visuals.Length; i++) part.visuals[i].sharedMaterials = part.originals[i].sharedMaterials;
                part.started = Time.unscaledTime;
                part.removed = removed;
                part.moving = true;
                HomePose(part, out var home, out _);
                var exit = transform.TransformDirection(part.node.parkingOffset);
                part.exitDirection = exit.sqrMagnitude > 1e-8f ? exit.normalized : (home - clinic.Station.Inspector.LookPoint).normalized;
                if (part.exitDirection.sqrMagnitude < .01f) part.exitDirection = Vector3.up;
            }
        }

        bool OwnGeometry(Transform t) => t.IsChildOf(flow.transform) || t.IsChildOf(parkingRoot.transform);

        void LateUpdate()
        {
            if (!OnBench)
            {
                if (parkingRoot != null) parkingRoot.SetActive(false);
                if (laidOut) foreach (var part in parts) HideOriginal(part, false);
                return;
            }
            parkingRoot.SetActive(true);
            if (!laidOut) Layout();
            if (!laidOut) return;
            Synchronize();
            foreach (var part in parts)
            {
                if (part.moving)
                {
                    float t = Mathf.Clamp01((Time.unscaledTime - part.started) / Duration);
                    HomePose(part, out var home, out var rotation);
                    var end = part.removed ? part.parkedPosition : home;
                    var endRotation = part.removed ? part.parkedRotation : rotation;
                    if (part.removed && t < .3f)
                    {
                        float u = t / .3f;
                        part.proxy.position = part.startPosition + part.exitDirection * (.025f * u);
                        part.proxy.rotation = Quaternion.AngleAxis(part.node.objectName.Contains("Screw") ? 720 * u : 0,
                            part.exitDirection) * part.startRotation;
                    }
                    else
                    {
                        float u = part.removed ? (t - .3f) / .7f : t;
                        float ease = Mathf.SmoothStep(0, 1, u);
                        var start = part.removed ? part.startPosition + part.exitDirection * .025f : part.startPosition;
                        part.proxy.position = Vector3.Lerp(start, end, ease) + Vector3.up * (Mathf.Sin(Mathf.PI * u) * .06f);
                        part.proxy.rotation = Quaternion.Slerp(part.startRotation, endRotation, ease);
                        if (!part.removed && part.node.objectName.Contains("Screw"))
                            part.proxy.rotation = Quaternion.AngleAxis(-720 * (1 - u), part.exitDirection) * part.proxy.rotation;
                    }
                    if (t >= 1) part.moving = false;
                }
                else if (part.removed) part.proxy.SetPositionAndRotation(part.parkedPosition, part.parkedRotation);
                part.proxy.gameObject.SetActive((part.removed || part.moving) && flow.InventoryVisible(part.node));
                // Moving proxies must not be selectable until their flight finishes.
                foreach (var collider in part.proxyColliders) collider.enabled = !part.moving;
                HideOriginal(part, part.removed || part.moving);
            }
        }

        static void HideOriginal(Part part, bool hidden)
        {
            for (int i = 0; i < part.originals.Length; i++)
                if (part.originals[i] != null) part.originals[i].enabled = !hidden && part.rendererEnabled[i];
            for (int i = 0; i < part.colliders.Length; i++)
                if (part.colliders[i] != null) part.colliders[i].enabled = !hidden && part.colliderEnabled[i];
        }

        void OnDestroy()
        {
            if (flow != null) flow.Changed -= Synchronize;
            foreach (var part in parts) HideOriginal(part, false);
            if (parkingRoot != null) Destroy(parkingRoot);
        }

        void OnDisable() { if (parkingRoot != null) parkingRoot.SetActive(false); }
    }
}
