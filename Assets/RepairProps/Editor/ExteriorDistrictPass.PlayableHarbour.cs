using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.EditorTools
{
    // The harbour's cranes in the playable copy (Docs/Art/INTERIOR_B1_PASS.md section 25.12): bigger, further out and of
    // three kinds instead of four copies of one.
    //   - A new far container terminal across the basin (z 37..80), so the cranes stand 30-50 m further out than the old
    //     freight pier.
    //   - A, old manned portal crane (one, the nearest): the district's own lattice crane scaled to 2.0, with a square cab
    //     high on the tower (dim old-yellow glass) and a still chain with a grab hanging from the jib tip.
    //   - B, new unmanned knuckle-boom crane (two, mid-far): a heavy block base, a turret, a folded hydraulic arm with bare
    //     piston rods, no cab; a faint cyan sensor strip under the arm's end.
    //   - C, huge container gantry (one, the furthest): a rail-mounted portal over the terminal with a very long top
    //     girder and a container hanging from its trolley.
    // Faded industrial blue-grey #3A474F and cold rust #4E433B, lifted for the dim sky; snow on the tops; dim red
    // obstruction lamps at the tips. Low poly boxes, no shadows, no colliders (out of reach across the water).
    public static partial class ExteriorDistrictPass
    {
        const string CraneKindPrefix = "Dock crane - ";

        static void HarbourCranes(Transform holder)
        {
            var done = new List<string>();
            var all = holder.GetComponentsInChildren<Transform>(true);
            var portals = all.Where(t => t.name == "Dock portal crane").ToArray();
            var pier = all.FirstOrDefault(t => t.name == "Far freight pier");
            if (portals.Length == 0 || pier == null) { log.Append("harbour cranes: no portal cranes or pier\n"); return; }
            var harbour = portals[0].parent;

            // Materials: copies of the portal crane's own steel (the Surface shader, with snow on the tops).
            var steelSrc = portals[0].GetComponentsInChildren<Renderer>(true).Select(r => r.sharedMaterial)
                .First(m => m != null && m.shader != null && m.shader.name.EndsWith("/Surface", StringComparison.Ordinal));
            Material SteelCopy(string name, Color c)
            {
                if (mats.TryGetValue(name, out var have)) return have;
                var m = new Material(steelSrc);
                m.SetColor("_BaseColor", c); m.SetFloat("_TopSnow", 1f); m.SetFloat("_SnowSlope", .4f);
                m.SetFloat("_Specular", .25f);
                return Save(name, m);
            }
            var grey = SteelCopy("Crane - steel, faded industrial blue-grey", Hex("#3A474F") * 1.75f);
            var rust = SteelCopy("Crane - steel, cold rust", Hex("#4E433B") * 1.6f);
            var boxRust = SteelCopy("Crane - container, faded rust", Hex("#5A473C") * 1.5f);
            var boxTeal = SteelCopy("Crane - container, teal-grey", Hex("#3E5058") * 1.6f);
            var dark = Lit("Crane - dark steel", Hex("#3A474F") * 1.25f, .35f, .5f);
            var rod = Lit("Crane - piston rod, bare steel", Hex("#8C979C"), .75f, .85f);
            var glass = Lit("Crane - cab glass, old yellow", Hex("#1E1C18"), .8f, 0, null, Hex("#C9A86A") * .32f);
            var sensor = Lit("Crane - sensor strip, faint cyan", Hex("#10181A"), .6f, 0, null, HoloCyan * .55f);
            var lamp = Beacon("Crane jib tip lamp", AlarmRed, 1.6f, .45f, .4f);

            // The quays they stand on: the far pier carried on west (to x -120), and a terminal north-west past the basin's
            // far edge. (Further north the skyline's North blocks stand, and from the quay and the junction the view
            // runs west-north-west: the cranes go out that way.)
            GameObject Slab(string name, float xa, float xb, float za, float zb)
            {
                var go = Object.Instantiate(pier.gameObject, pier.parent);
                go.name = name;
                go.transform.position = new Vector3((xa + xb) * .5f, pier.position.y, (za + zb) * .5f);
                go.transform.localScale = new Vector3(xb - xa, pier.localScale.y, zb - za);
                return go;
            }
            Slab("Far pier - carried on west", -120f, -72.5f, 10f, 37f);
            Slab("Far container terminal - north-west past the basin", -120f, -58f, 37f, 72f);
            float ground = pier.GetComponent<Renderer>().bounds.max.y;
            done.Add("quays: pier on west x -120..-72.5 z 10..37, terminal x -120..-58 z 37..72, top " + ground.ToString("F2"));

            // A: keep the biggest portal crane, the rest retire.
            var keep = portals.OrderByDescending(t => t.lossyScale.x).First();
            foreach (var t in portals.Where(t => t != keep)) t.gameObject.SetActive(false);
            float lift = ground - pier.GetComponent<Renderer>().bounds.max.y;
            keep.position = new Vector3(-62f, keep.position.y + lift, 14.3f);
            keep.rotation = Quaternion.Euler(0, 102f, 0) * keep.rotation;   // jib side-on to the quay and the junction, out over the basin
            keep.localScale = Vector3.one * 2.0f;
            foreach (var t in keep.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Hoist cable" || t.name == "Hook block")) t.gameObject.SetActive(false);
            Physics.SyncTransforms();
            var kb = RenderBounds(keep);
            var tower = keep.GetComponentsInChildren<Renderer>(true).Where(r => r.name.StartsWith("A frame", StringComparison.Ordinal)).Select(r => r.bounds)
                .DefaultIfEmpty(kb).Aggregate((x, y) => { x.Encapsulate(y); return x; });
            var jib = keep.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.name == "Fly jib" && f.sharedMesh != null);
            Vector3 tip = kb.center + Vector3.up * kb.extents.y;
            if (jib != null)
            {
                float far = -1;
                foreach (var v in jib.sharedMesh.vertices)
                {
                    var w = jib.transform.TransformPoint(v);
                    float d = new Vector2(w.x - keep.position.x, w.z - keep.position.z).magnitude + .2f * w.y;
                    if (d > far) { far = d; tip = w; }
                }
            }
            var aOut = Vector3.ProjectOnPlane(tip - tower.center, Vector3.up).normalized;
            if (aOut.sqrMagnitude < .1f) aOut = Vector3.back;
            var aG = Group(keep, "Type A additions - cab and chain");
            {
                float h = kb.size.y;
                var cab = new Vector3(tower.center.x, kb.min.y + .62f * h, tower.center.z) + aOut * (tower.extents.magnitude * .25f + 1.6f);
                var rot = Quaternion.LookRotation(aOut);
                Box("Cab", aG, cab, new Vector3(3.0f, 2.6f, 3.0f), grey, rot);
                Box("Cab roof", aG, cab + Vector3.up * 1.42f, new Vector3(3.4f, .24f, 3.4f), grey, rot);
                foreach (var (dx, dz, w) in new[] { (0f, 1.52f, 2.6f), (0f, -1.52f, 2.6f) })
                    Box("Cab glazing band", aG, cab + rot * new Vector3(dx, .35f, dz), new Vector3(w, .9f, .06f), glass, rot);
                foreach (var dx in new[] { -1.52f, 1.52f })
                    Box("Cab glazing band", aG, cab + rot * new Vector3(dx, .35f, 0), new Vector3(.06f, .9f, 2.6f), glass, rot);
                Beam("Cab hanger", aG, cab + Vector3.up * 1.3f, new Vector3(tower.center.x, cab.y + 2.6f, tower.center.z), .35f, .35f, dark);
                Beam("Cab hanger", aG, cab - Vector3.up * 1.3f, new Vector3(tower.center.x, cab.y - 3.2f, tower.center.z), .35f, .35f, dark);
                // The chain, still, from the jib tip to a grab hanging a third of the way up.
                float bottom = kb.min.y + .3f * h;
                int links = Mathf.FloorToInt((tip.y - 1.2f - bottom) / .5f);
                for (int i = 0; i < links; i++)
                    Box("Chain link", aG, tip + Vector3.down * (1.2f + i * .5f), new Vector3(i % 2 == 0 ? .3f : .1f, .55f, i % 2 == 0 ? .1f : .3f), dark);
                var g0 = tip + Vector3.down * (1.2f + links * .5f);
                Box("Grab head", aG, g0 + Vector3.down * .4f, new Vector3(1.6f, .8f, 1.1f), rust, rot);
                foreach (var s in new[] { -1f, 1f })
                    Box("Grab shell", aG, g0 + Vector3.down * 1.5f + rot * new Vector3(s * .55f, 0, 0), new Vector3(.9f, 1.5f, 1.3f), rust, rot * Quaternion.Euler(0, 0, s * 18f));
                done.Add("A old portal crane x2.0 at " + keep.position.ToString("F0") + " (cab at " + cab.ToString("F0") + ", chain " + links + " links)");
            }

            // B: two unmanned knuckle-boom cranes.
            KnuckleCrane(harbour, new Vector3(-86f, ground, 22f), 10f, 1.15f, grey, rust, dark, rod, sensor, lamp);
            KnuckleCrane(harbour, new Vector3(-72f, ground, 46f), -15f, 1.0f, grey, rust, dark, rod, sensor, lamp);
            done.Add("B knuckle-boom x1.15 at (-86, 22) yaw 10, x1.0 at (-72, 46) yaw -15 (arms out over the water)");

            // C: the gantry, furthest out, with a couple of container stacks under it for scale.
            Gantry(harbour, new Vector3(-104f, ground, 45f), -90f, grey, rust, dark, boxRust, lamp);
            var stacks = Group(harbour, "Far terminal - container stacks");
            var rnd = new System.Random(7);
            foreach (var (x, z, n) in new[] { (-84f, 60f, 3), (-66f, 64f, 2), (-92f, 14f, 2), (-114f, 66f, 3), (-76f, 31f, 2) })
                for (int k = 0; k < n; k++)
                    Box("Container", stacks, new Vector3(x + (float)rnd.NextDouble() * .4f, ground + 1.3f + k * 2.6f, z), new Vector3(12.2f, 2.6f, 2.45f), rnd.Next(2) == 0 ? boxRust : boxTeal);
            done.Add("C gantry at (-104, 45), girder along z, span 44 m, girder 78 m, height 30 m; container stacks 5");

            foreach (var r in harbour.GetComponentsInChildren<Renderer>(true).Where(r => r.transform.IsChildOf(stacks) || r.transform.IsChildOf(aG) || r.transform.parent != null && r.transform.parent.name.StartsWith(CraneKindPrefix, StringComparison.Ordinal)))
                r.shadowCastingMode = ShadowCastingMode.Off;
            log.Append("harbour cranes: ").Append(string.Join("; ", done)).Append('\n');
        }

        static Bounds RenderBounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(false).Where(r => r is MeshRenderer).ToArray();
            if (rs.Length == 0) return new Bounds(t.position, Vector3.zero);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        // A box from a to b, w wide and h deep across it.
        static GameObject Beam(string name, Transform parent, Vector3 a, Vector3 b, float w, float h, Material m)
        {
            var d = b - a;
            var up = Mathf.Abs(Vector3.Dot(d.normalized, Vector3.up)) > .95f ? Vector3.forward : Vector3.up;
            return Box(name, parent, (a + b) * .5f, new Vector3(w, h, d.magnitude), m, Quaternion.LookRotation(d, up));
        }

        static void KnuckleCrane(Transform parent, Vector3 at, float yaw, float scale, Material grey, Material rust, Material dark, Material rod, Material sensor, Material lamp)
        {
            var root = Group(parent, CraneKindPrefix + "B, unmanned knuckle-boom");
            root.SetPositionAndRotation(at, Quaternion.Euler(0, yaw, 0));
            Vector3 P(float x, float y, float z) => root.TransformPoint(new Vector3(x, y, z) * scale);
            float S(float v) => v * scale;
            var rot = root.rotation;
            Box("Base block", root, P(0, 1.5f, 0), new Vector3(S(9), S(3), S(9)), rust, rot);
            foreach (var (x, z) in new[] { (-4f, -4f), (4f, -4f), (-4f, 4f), (4f, 4f) })
                Box("Base pad", root, P(x, .3f, z), new Vector3(S(2.2f), S(.6f), S(2.2f)), dark, rot);
            Box("Turret", root, P(0, 5f, -.5f), new Vector3(S(6), S(4), S(7)), grey, rot);
            Box("Power pack", root, P(0, 5.5f, 3.6f), new Vector3(S(6.4f), S(3.4f), S(3)), rust, rot);
            Box("Turret ring", root, P(0, 3.15f, 0), new Vector3(S(7), S(.3f), S(7)), dark, rot);
            // The arm: up and out, a knuckle, the jib down and out over the water.
            var p0 = P(0, 6.5f, -2.5f);
            var p1 = P(0, 6.5f + 20 * Mathf.Sin(62 * Mathf.Deg2Rad), -2.5f - 20 * Mathf.Cos(62 * Mathf.Deg2Rad));
            var d2 = root.rotation * new Vector3(0, -Mathf.Sin(28 * Mathf.Deg2Rad), -Mathf.Cos(28 * Mathf.Deg2Rad));
            var p2 = p1 + d2 * S(17);
            Beam("Main boom", root, p0, p1, S(2.4f), S(2.0f), grey);
            Box("Knuckle", root, p1, Vector3.one * S(2.8f), dark, rot);
            Beam("Jib", root, p1, p2, S(1.8f), S(1.5f), grey);
            Box("Tool head", root, p2 + Vector3.down * S(1.6f), new Vector3(S(2.2f), S(2.6f), S(2.0f)), rust, rot);
            Box("Boom pivot", root, p0, Vector3.one * S(2.2f), dark, rot);
            // Hydraulics: two lift cylinders under the boom, one fold cylinder over the knuckle; bare rods.
            var q1 = Vector3.Lerp(p0, p1, .45f);
            foreach (var s in new[] { -1.4f, 1.4f })
            {
                var side = root.rotation * Vector3.right * S(s);
                var a = P(0, 3.6f, -4.2f) + side; var b = q1 + side + Vector3.down * S(.6f);
                Beam("Lift cylinder", root, a, Vector3.Lerp(a, b, .56f), S(.8f), S(.8f), dark);
                Beam("Lift piston rod", root, Vector3.Lerp(a, b, .5f), b, S(.38f), S(.38f), rod);
            }
            var up = Vector3.up * S(1.7f);
            var c0 = Vector3.Lerp(p0, p1, .7f) + up; var c1 = Vector3.Lerp(p1, p2, .35f) + up * .8f;
            Beam("Fold cylinder", root, c0, Vector3.Lerp(c0, c1, .55f), S(.7f), S(.7f), dark);
            Beam("Fold piston rod", root, Vector3.Lerp(c0, c1, .5f), c1, S(.32f), S(.32f), rod);
            // The sensor strip under the arm's last few metres, and the lamps.
            var under = -Vector3.Cross(d2, root.rotation * Vector3.right).normalized;
            if (Vector3.Dot(under, Vector3.down) < 0) under = -under;
            Beam("Sensor strip", root, p2 - d2 * S(4.2f) + under * S(.82f), p2 - d2 * S(.4f) + under * S(.82f), S(.3f), S(.12f), sensor);
            Sphere("Obstruction lamp - knuckle", root, p1 + Vector3.up * S(1.7f), S(.2f), lamp);
            Sphere("Obstruction lamp - jib tip", root, p2 + Vector3.up * S(.95f), S(.18f), lamp);
        }

        static void Gantry(Transform parent, Vector3 at, float yaw, Material grey, Material rust, Material dark, Material box, Material lamp)
        {
            var root = Group(parent, CraneKindPrefix + "C, container gantry");
            root.SetPositionAndRotation(at, Quaternion.Euler(0, yaw, 0));
            var rot = root.rotation;
            Vector3 L(float x, float y, float z) => root.TransformPoint(new Vector3(x, y, z));
            const float half = 22f, depth = 6f, H = 30f, over = 17f;
            foreach (var sx in new[] { -half, half })
            {
                foreach (var sz in new[] { -depth, depth })
                {
                    Box("Leg", root, L(sx, H * .5f, sz), new Vector3(1.8f, H, 1.8f), grey, rot);
                    Box("Bogie", root, L(sx, .6f, sz), new Vector3(2.6f, 1.2f, 3.2f), dark, rot);
                }
                Box("Sill beam", root, L(sx, 1.8f, 0), new Vector3(1.4f, 1.4f, depth * 2 + 1.8f), rust, rot);
                Box("Portal tie", root, L(sx, H * .55f, 0), new Vector3(1.2f, 1.2f, depth * 2), grey, rot);
                Box("Leg head beam", root, L(sx, H + .9f, 0), new Vector3(2.2f, 1.8f, depth * 2 + 2.4f), grey, rot);
                Beam("Brace", root, L(sx, 2.5f, -depth), L(sx, H * .55f - .6f, depth), .8f, .8f, dark);
            }
            float gy = H + 1.8f + 1.6f;
            Box("Top girder", root, L(0, gy, 0), new Vector3(half * 2 + over * 2, 3.2f, 3.6f), grey, rot);
            Box("Girder walkway", root, L(0, gy - 1.9f, 2.2f), new Vector3(half * 2 + over * 2 - 2, .2f, 1.0f), dark, rot);
            Box("Machinery house", root, L(-12f, gy + 3.6f, 0), new Vector3(9f, 4f, 5.5f), rust, rot);
            Beam("Head stay", root, L(-half, gy + 1.6f, 0), L(-half - over + 1, gy + .6f, 0), .5f, .5f, dark);
            Beam("Head stay", root, L(half, gy + 1.6f, 0), L(half + over - 1, gy + .6f, 0), .5f, .5f, dark);
            Box("A frame", root, L(half, gy + 4f, 0), new Vector3(1f, 6f, 1f), grey, rot);
            Box("A frame", root, L(-half, gy + 4f, 0), new Vector3(1f, 6f, 1f), grey, rot);
            // Trolley, ropes, spreader and the container, hanging still.
            var tl = new Vector3(9f, gy - 2.6f, 0); var trolley = L(tl.x, tl.y, tl.z);
            Box("Trolley", root, trolley, new Vector3(5f, 2.2f, 4.6f), dark, rot);
            float sy = H - 13f;
            foreach (var (dx, dz) in new[] { (-1.6f, -1.2f), (1.6f, -1.2f), (-1.6f, 1.2f), (1.6f, 1.2f) })
                Beam("Hoist rope", root, L(tl.x + dx, tl.y - 1.1f, dz), L(tl.x + dx * 2.6f, sy + .3f, dz), .07f, .07f, dark);
            Box("Spreader", root, L(tl.x, sy, 0), new Vector3(12.6f, .6f, 2.7f), dark, rot);
            Box("Container", root, L(tl.x, sy - 1.6f, 0), new Vector3(12.2f, 2.6f, 2.45f), box, rot);
            foreach (var x in new[] { -half - over + .4f, half + over - .4f })
                Sphere("Obstruction lamp - girder end", root, L(x, gy + 1.9f, 0), .22f, lamp);
            Sphere("Obstruction lamp - machinery", root, L(-12f, gy + 5.8f, 0), .22f, lamp);
        }
    }
}
