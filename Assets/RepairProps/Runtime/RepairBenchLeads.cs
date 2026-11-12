using System.Collections.Generic;
using UnityEngine;

namespace BorderRepair.RepairProps
{
    // The tester's two leads. Each is a slack cable (a small verlet rope drawn with a LineRenderer) from a jack on the
    // tester to its probe. The probes rest in the tray's back slot; while the tester is in the hand the red probe goes
    // to the spot the pointer is on, needle down, and the cables hang, swing and lie on the bench between them.
    // Set up by RepairBenchV2Runtime from the bench model's BV2_Tool_Meter_Probe* / BV2_Tool_Meter_Jack* objects.
    [AddComponentMenu("")]
    public sealed class RepairBenchLeads : MonoBehaviour
    {
        const int Points = 18;
        const float Radius = .0022f;

        sealed class Lead
        {
            public Transform jack, probe;
            public Vector3 restPosition, tipLocal;
            public Quaternion restRotation;
            public float length;
            public bool follows;                 // the red probe goes to the pointed spot
            public readonly Vector3[] now = new Vector3[Points], before = new Vector3[Points];
            public LineRenderer line;
        }

        readonly List<Lead> leads = new List<Lead>();
        RepairBenchProp meter;
        Bounds tray;
        float benchY, trayY;
        Material redMaterial, blackMaterial;

        public int Count => leads.Count;
        /// <summary>World positions of one cable, jack first (for tests).</summary>
        public Vector3[] Cable(int index) => leads[index].now;
        public Transform Probe(int index) => leads[index].probe;

        /// <param name="benchTopY">World height of the bench top; <paramref name="trayBounds"/> is the tool tray, whose
        /// foam is the floor for the part of a cable that lies inside it.</param>
        public void Initialize(RepairBenchProp tester, Transform bench, float benchTopY, Bounds trayBounds, Material like)
        {
            meter = tester; benchY = benchTopY; tray = trayBounds; trayY = benchTopY + .024f;
            redMaterial = Tint(like, new Color(.50f, .07f, .05f));
            blackMaterial = Tint(like, new Color(.045f, .045f, .05f));
            Add(bench, "Red", .46f, true, redMaterial);
            Add(bench, "Black", .62f, false, blackMaterial);
        }

        static Material Tint(Material like, Color color)
        {
            var material = new Material(like) { name = "BenchLead_Runtime" };
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", null);
            if (material.HasProperty("_MetallicGlossMap")) material.SetTexture("_MetallicGlossMap", null);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .25f);
            return material;
        }

        void Add(Transform bench, string colour, float length, bool follows, Material material)
        {
            Transform jack = null, probe = null;
            foreach (var t in bench.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "BV2_Tool_Meter_Jack" + colour) jack = t;
                else if (t.name == "BV2_Tool_Meter_Probe" + colour) probe = t;
            }
            if (jack == null || probe == null) return;
            var marker = probe.Find(probe.name + "_Tip");
            var lead = new Lead { jack = jack, probe = probe, length = length, follows = follows, tipLocal = marker != null ? marker.localPosition : Vector3.zero };
            // The probe stays where it is put, whatever the tester does: off the tester, onto the bench.
            probe.SetParent(bench, true);
            lead.restPosition = probe.position; lead.restRotation = probe.rotation;
            foreach (var c in probe.GetComponentsInChildren<Collider>(true)) c.enabled = false;   // never between the pointer and the work
            for (int i = 0; i < Points; i++)
                lead.now[i] = lead.before[i] = Vector3.Lerp(jack.position, probe.position, i / (Points - 1f));
            var host = new GameObject("BenchLead_" + colour);
            host.transform.SetParent(transform, false);
            lead.line = host.AddComponent<LineRenderer>();
            lead.line.useWorldSpace = true; lead.line.positionCount = Points;
            lead.line.widthMultiplier = Radius * 2; lead.line.numCapVertices = 2; lead.line.numCornerVertices = 2;
            lead.line.sharedMaterial = material; lead.line.textureMode = LineTextureMode.Stretch;
            lead.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            leads.Add(lead);
            for (int i = 0; i < 90; i++) Step(lead, 1f / 60f);   // settle before the first frame is seen
            lead.line.SetPositions(lead.now);
        }

        void LateUpdate()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            if (dt <= 0) return;
            foreach (var lead in leads)
            {
                if (lead.jack == null || lead.probe == null) continue;
                PlaceProbe(lead, dt);
                Step(lead, dt * .5f); Step(lead, dt * .5f);
                lead.line.SetPositions(lead.now);
            }
        }

        void PlaceProbe(Lead lead, float dt)
        {
            var position = lead.restPosition; var rotation = lead.restRotation;
            if (lead.follows && meter != null && meter.InHand)
            {
                // Needle on the spot, handle leaning up and back toward the tester.
                var spot = meter.Spot;
                var back = Vector3.ProjectOnPlane(meter.transform.position - spot, Vector3.up);
                var shaft = (Vector3.up * .85f + (back.sqrMagnitude > 1e-6f ? back.normalized : Vector3.right) * .5f).normalized;
                var scale = lead.probe.lossyScale;
                var needle = lead.restRotation * Vector3.Scale(scale, lead.tipLocal).normalized;   // pivot -> tip at rest
                rotation = Quaternion.FromToRotation(needle, -shaft) * lead.restRotation;
                position = spot + Vector3.up * .002f - rotation * Vector3.Scale(scale, lead.tipLocal);
            }
            float k = 1f - Mathf.Exp(-20f * dt);
            lead.probe.SetPositionAndRotation(Vector3.Lerp(lead.probe.position, position, k), Quaternion.Slerp(lead.probe.rotation, rotation, k));
        }

        void Step(Lead lead, float dt)
        {
            var now = lead.now; var before = lead.before;
            Vector3 a = lead.jack.position, b = lead.probe.position;
            // Never shorter than the gap it has to span.
            float segment = Mathf.Max(lead.length, Vector3.Distance(a, b) * 1.03f) / (Points - 1);
            var gravity = Vector3.down * (9.81f * dt * dt);
            for (int i = 1; i < Points - 1; i++)
            {
                var velocity = (now[i] - before[i]) * .90f;
                before[i] = now[i];
                now[i] += velocity + gravity;
            }
            for (int pass = 0; pass < 14; pass++)
            {
                now[0] = a; now[Points - 1] = b;
                for (int i = 0; i < Points - 1; i++)
                {
                    var delta = now[i + 1] - now[i];
                    float d = delta.magnitude;
                    if (d < 1e-7f) continue;
                    var fix = delta * ((d - segment) / d * .5f);
                    if (i > 0) now[i] += fix;
                    if (i + 1 < Points - 1) now[i + 1] -= fix;
                }
                for (int i = 1; i < Points - 1; i++)
                {
                    float floor = (Inside(now[i]) ? trayY : benchY) + Radius;
                    if (now[i].y < floor) { now[i].y = floor; before[i] = Vector3.Lerp(before[i], now[i], .5f); }   // rubs on the bench
                }
            }
            before[0] = now[0] = a; before[Points - 1] = now[Points - 1] = b;
        }

        bool Inside(Vector3 p) => p.x > tray.min.x && p.x < tray.max.x && p.z > tray.min.z && p.z < tray.max.z;

        void OnDestroy()
        {
            if (redMaterial != null) Destroy(redMaterial);
            if (blackMaterial != null) Destroy(blackMaterial);
        }
    }
}
