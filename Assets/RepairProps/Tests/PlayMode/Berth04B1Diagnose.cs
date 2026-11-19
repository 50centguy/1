using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.Tests
{
    // Read only: a dump of the joined scene's B1 (cameras and volumes, colliders against their meshes, overlapping and
    // coplanar renderers) and close frames of the stations.
    public sealed partial class Berth04PlayableTests
    {
        static string P(Transform t) { var s = t.name; for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s; return s; }
        static string V(Vector3 v) => v.ToString("F2");

        [UnityTest, Explicit("diagnostic dump; run by name"), Timeout(900000)]
        public IEnumerator Diagnose_B1()
        {
            for (int i = 0; i < 30; i++) yield return null;
            var join = Berth04Playable.Current.Join;
            var sb = new StringBuilder();
            var room = ClinicLayout.Active.room; room.Expand(.2f);

            object G(object o, string n) { if (o == null) return null; var t = o.GetType(); var pr = t.GetProperty(n); if (pr != null) return pr.GetValue(o); return t.GetField(n)?.GetValue(o); }
            string Mask(object m) => m == null ? "?" : (G(m, "value") ?? m).ToString();
            sb.Append("== cameras\n");
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var d = c.GetComponents<Component>().FirstOrDefault(x => x != null && x.GetType().Name == "UniversalAdditionalCameraData");
                var retro = c.GetComponents<Behaviour>().FirstOrDefault(x => x != null && x.GetType().Name == "RetroClinicCamera");
                sb.Append("  ").Append(P(c.transform)).Append(" [").Append(c.gameObject.scene.name).Append("] enabled ").Append(c.isActiveAndEnabled)
                  .Append(" eye ").Append(c == join.Eye).Append(" postFX ").Append(G(d, "renderPostProcessing")).Append(" volMask ").Append(Mask(G(d, "volumeLayerMask")))
                  .Append(" retro ").Append(retro == null ? "none" : retro.isActiveAndEnabled + " bindings " + ((G(retro, "rendererBindings") as Array)?.Length ?? 0)).Append('\n');
            }
            sb.Append("  pipeline ").Append((QualitySettings.renderPipeline ?? UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline)?.name).Append('\n');
            sb.Append("== volumes\n");
            foreach (var v in Object.FindObjectsByType<Behaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(x => x != null && x.GetType().Name == "Volume"))
            {
                var prof = G(v, "sharedProfile") as ScriptableObject;
                var comps = G(prof, "components") as IEnumerable;
                sb.Append("  ").Append(P(v.transform)).Append(" [").Append(v.gameObject.scene.name).Append("] active ").Append(v.isActiveAndEnabled)
                  .Append(" layer ").Append(v.gameObject.layer).Append(" global ").Append(G(v, "isGlobal")).Append(" prio ").Append(G(v, "priority")).Append(" w ").Append(G(v, "weight"))
                  .Append(" profile ").Append(prof == null ? "null" : prof.name + " {" + (comps == null ? "" : string.Join(",", comps.Cast<object>().Select(x => x.GetType().Name + (Equals(G(x, "active"), false) ? "-off" : "")))) + "}").Append('\n');
            }
            foreach (var st in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(m => m != null && m.GetType().Name.Contains("Retro")))
                sb.Append("  retro component ").Append(st.GetType().Name).Append(" on ").Append(P(st.transform)).Append(" [").Append(st.gameObject.scene.name).Append("] active ").Append(st.isActiveAndEnabled).Append('\n');

            // Colliders in the B1 whose box is off its own mesh.
            sb.Append("== colliders in the B1 off their renderers (centre gap > .12 or size ratio off)\n");
            foreach (var col in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (!col.enabled || col.isTrigger || !room.Contains(col.bounds.center)) continue;
                var rs = col.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
                if (rs.Length == 0) { sb.Append("  (no renderer) ").Append(P(col.transform)).Append(' ').Append(col.GetType().Name).Append(' ').Append(V(col.bounds.min)).Append("..").Append(V(col.bounds.max)).Append('\n'); continue; }
                var rb = rs[0].bounds; foreach (var r in rs) rb.Encapsulate(r.bounds);
                var cb = col.bounds;
                float gap = Vector3.Distance(cb.center, rb.center);
                bool sizeOff = Mathf.Abs(cb.size.x - rb.size.x) > .2f || Mathf.Abs(cb.size.z - rb.size.z) > .2f || Mathf.Abs(cb.size.y - rb.size.y) > .3f;
                if (gap > .12f || sizeOff)
                    sb.Append("  ").Append(P(col.transform)).Append(' ').Append(col.GetType().Name).Append(" col ").Append(V(cb.min)).Append("..").Append(V(cb.max))
                      .Append(" mesh ").Append(V(rb.min)).Append("..").Append(V(rb.max)).Append('\n');
            }

            // Renderers in the B1 by scene, and coplanar duplicates (z-fighting).
            var inRoom = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && !r.forceRenderingOff && r.gameObject.activeInHierarchy && room.Contains(r.bounds.center) && !(r is ParticleSystemRenderer)).ToList();
            sb.Append("== renderers in the B1: ").Append(inRoom.Count).Append(" (").Append(string.Join(", ", inRoom.GroupBy(r => r.gameObject.scene.name).Select(g => g.Key + " " + g.Count()))).Append(")\n");
            sb.Append("== coplanar pairs (a face of one within 1 cm of a face of another, overlapping > 60%)\n");
            int pairs = 0;
            for (int i = 0; i < inRoom.Count && pairs < 200; i++)
                for (int j = i + 1; j < inRoom.Count && pairs < 200; j++)
                {
                    var a = inRoom[i].bounds; var b = inRoom[j].bounds;
                    for (int ax = 0; ax < 3; ax++)
                    {
                        bool thinA = a.size[ax] < .03f, thinB = b.size[ax] < .03f;
                        if (!thinA && !thinB) continue;
                        float fa = thinA ? a.center[ax] : float.NaN, fb = thinB ? b.center[ax] : float.NaN;
                        bool same = (thinA && (Mathf.Abs(fa - b.min[ax]) < .01f || Mathf.Abs(fa - b.max[ax]) < .01f || (thinB && Mathf.Abs(fa - fb) < .01f)))
                                    || (thinB && (Mathf.Abs(fb - a.min[ax]) < .01f || Mathf.Abs(fb - a.max[ax]) < .01f));
                        if (!same) continue;
                        float ov = 1;
                        for (int k = 0; k < 3; k++) if (k != ax)
                            {
                                float o = Mathf.Min(a.max[k], b.max[k]) - Mathf.Max(a.min[k], b.min[k]);
                                float s = Mathf.Min(a.size[k], b.size[k]);
                                ov *= s > .001f ? Mathf.Clamp01(o / s) : 0;
                            }
                        if (ov < .6f) continue;
                        pairs++;
                        sb.Append("  axis ").Append("xyz"[ax]).Append(' ').Append(P(inRoom[i].transform)).Append(" [").Append(inRoom[i].gameObject.scene.name).Append("] <> ")
                          .Append(P(inRoom[j].transform)).Append(" [").Append(inRoom[j].gameObject.scene.name).Append("] ").Append(V(a.center)).Append('\n');
                        break;
                    }
                }
            // Full overlap of the same size (duplicates), any thickness.
            sb.Append("== near-identical bounds (all within 2 cm)\n");
            for (int i = 0; i < inRoom.Count; i++)
                for (int j = i + 1; j < inRoom.Count; j++)
                {
                    var a = inRoom[i].bounds; var b = inRoom[j].bounds;
                    if (Vector3.Distance(a.min, b.min) < .02f && Vector3.Distance(a.max, b.max) < .02f && a.size.sqrMagnitude > .0004f)
                        sb.Append("  ").Append(P(inRoom[i].transform)).Append(" [").Append(inRoom[i].gameObject.scene.name).Append("] == ").Append(P(inRoom[j].transform)).Append(" [").Append(inRoom[j].gameObject.scene.name).Append("] ").Append(V(a.center)).Append('\n');
                }

            // What stands around the bench and the bed (by scene), to see what crowds them.
            Transform Find(string n) => Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).FirstOrDefault(t => t.name == n);
            foreach (var (label, at, r) in new[] { ("bench", Find("RepairBenchV2").position, 1.3f), ("bed", Find("SurgeryTableV2").position, 1.8f) })
            {
                sb.Append("== around the ").Append(label).Append(' ').Append(V(at)).Append('\n');
                foreach (var rr in inRoom.Where(x => Vector3.Distance(new Vector3(x.bounds.center.x, at.y, x.bounds.center.z), at) < r).OrderBy(x => x.gameObject.scene.name).ThenBy(x => P(x.transform)))
                    sb.Append("  [").Append(rr.gameObject.scene.name).Append("] ").Append(P(rr.transform)).Append(' ').Append(V(rr.bounds.min)).Append("..").Append(V(rr.bounds.max))
                      .Append(rr.GetComponent<Collider>() != null || rr.GetComponentInParent<Collider>() != null ? " col" : "").Append('\n');
                foreach (var cc in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Where(x => x.enabled && !x.isTrigger && Vector3.Distance(new Vector3(x.bounds.center.x, at.y, x.bounds.center.z), at) < r))
                    sb.Append("  collider [").Append(cc.gameObject.scene.name).Append("] ").Append(P(cc.transform)).Append(' ').Append(V(cc.bounds.min)).Append("..").Append(V(cc.bounds.max)).Append('\n');
            }
            // The B1's district groups (two levels under the interior pass and the shell), with their extents.
            sb.Append("== B1 groups\n");
            foreach (var g in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                         .Where(t => t.parent != null && (t.parent.name.StartsWith("09 Interior pass") || t.parent.name.StartsWith("01 Basement shell") || (t.parent.parent != null && t.parent.parent.name.StartsWith("09 Interior pass")))))
            {
                var rs = g.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
                if (rs.Length == 0) continue;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                if (!room.Intersects(b)) continue;
                sb.Append("  ").Append(P(g)).Append("  ").Append(V(b.min)).Append("..").Append(V(b.max)).Append(g.GetComponentsInChildren<Collider>().Any(c => c.enabled) ? " col" : "").Append('\n');
            }
            sb.Append("== street particles hidden from inside: ").Append(join.LeakingParticleCount).Append('\n');
            sb.Append("== transparent or unlit renderers drawn in the B1\n");
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && !r.forceRenderingOff && r.gameObject.activeInHierarchy && room.Intersects(r.bounds) && r.bounds.size.magnitude < 20
                         && r.sharedMaterials.Any(m => m != null && (m.renderQueue >= 2450 || m.shader.name.IndexOf("Unlit", StringComparison.OrdinalIgnoreCase) >= 0))))
                sb.Append("  [").Append(r.gameObject.scene.name).Append("] ").Append(P(r.transform)).Append("  ").Append(V(r.bounds.min)).Append("..").Append(V(r.bounds.max))
                  .Append("  ").Append(string.Join(",", r.sharedMaterials.Where(m => m != null).Select(m => m.name + "/" + m.shader.name + "/" + m.renderQueue))).Append('\n');
            sb.Append("== anything above the window cabinets (x 1.5..4.5, y 1.25..1.85, z -3.35..-2.6)\n");
            var probe = new Bounds(new Vector3(3f, 1.55f, -2.97f), new Vector3(3f, .6f, .75f));
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && !r.forceRenderingOff && r.gameObject.activeInHierarchy && probe.Intersects(r.bounds)))
                sb.Append("  [").Append(r.gameObject.scene.name).Append("] ").Append(P(r.transform)).Append(' ').Append(r.GetType().Name).Append("  ").Append(V(r.bounds.min)).Append("..").Append(V(r.bounds.max))
                  .Append("  ").Append(string.Join(",", r.sharedMaterials.Where(m => m != null).Select(m => m.name + "/" + m.shader.name))).Append('\n');
            sb.Append("== items in the bay extension (x 2.2..5.1, z -1.3..0.1)\n");
            var ext = new Bounds(new Vector3(3.65f, 1.3f, -.6f), new Vector3(2.9f, 2.6f, 1.4f));
            foreach (var t in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.gameObject.scene.name.Contains("District") && ext.Intersects(r.bounds)))
                sb.Append("  ").Append(P(t.transform)).Append("  ").Append(V(t.bounds.min)).Append("..").Append(V(t.bounds.max)).Append(t.GetComponent<Collider>() != null ? " col" : "").Append('\n');
            Berth04Measure.Append("diagnose.txt", sb.ToString());

            // Top-down plan of the B1 below 2 m (ceiling, ring and lamps cut away).
            var plan = Berth04Measure.MeasuringCamera();
            plan.orthographic = true; plan.orthographicSize = 3.6f; plan.nearClipPlane = .01f; plan.farClipPlane = 3;
            plan.transform.SetPositionAndRotation(new Vector3(ClinicLayout.Active.room.center.x, 2.0f, ClinicLayout.Active.room.center.z), Quaternion.Euler(90, 0, 0));
            join.Evaluate(join.Eye);
            Berth04Measure.Frame(plan, "diag - plan.jpg");
            // The same with every collider's footprint outlined (red: clinic scene, cyan: district), drawn just under the camera.
            var marks = new GameObject("collider marks (test)");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            Material Mat(Color c) { var m = new Material(unlit); m.SetColor("_BaseColor", c); return m; }
            var red = Mat(new Color(1, .1f, .1f)); var cyan = Mat(new Color(.1f, 1, 1));
            void Bar(Vector3 a, Vector3 b, Material m)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(g.GetComponent<Collider>());
                g.transform.SetParent(marks.transform, false);
                g.transform.position = (a + b) / 2 + Vector3.up * 1.9f;
                g.transform.localScale = new Vector3(Mathf.Max(.025f, Mathf.Abs(b.x - a.x)), .01f, Mathf.Max(.025f, Mathf.Abs(b.z - a.z)));
                g.GetComponent<Renderer>().sharedMaterial = m;
            }
            foreach (var col in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (!col.enabled || col.isTrigger || col is CharacterController) continue;
                var cb = col.bounds;
                if (!room.Intersects(cb) || cb.min.y > 1.8f || cb.size.x > 6 || cb.size.z > 6) continue;
                var m = col.gameObject.scene.name.Contains("District") ? cyan : red;
                Vector3 a = new Vector3(cb.min.x, 0, cb.min.z), b = new Vector3(cb.max.x, 0, cb.min.z), c2 = new Vector3(cb.max.x, 0, cb.max.z), d = new Vector3(cb.min.x, 0, cb.max.z);
                Bar(a, b, m); Bar(b, c2, m); Bar(c2, d, m); Bar(d, a, m);
            }
            Berth04Measure.Frame(plan, "diag - plan colliders.jpg");
            Object.Destroy(marks);
            Object.Destroy(plan.gameObject);

            var eye = join.Eye;
            foreach (var (name, feet, yaw, pitch) in new[]
                     {
                         ("bench close", new Vector3(.25f, .05f, .5f), 270f, 28f), ("bench side", new Vector3(-.3f, .05f, -.6f), 330f, 18f),
                         ("surgery bay", new Vector3(3.0f, .05f, -.5f), 40f, 14f), ("bed close", new Vector3(3.3f, .05f, .4f), 70f, 30f),
                         ("counter from behind", new Vector3(.2f, .05f, -1.0f), 180f, 20f),
                     })
            {
                join.Walker.PlaceAt(feet, yaw, pitch);
                yield return null; yield return null;
                Berth04Measure.Frame(eye, "diag - " + Berth04Measure.Safe(name) + ".jpg");
            }
        }
    }
}
