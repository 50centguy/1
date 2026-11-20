using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.EditorTools
{
    // The clinic's side of the street as a wall (Docs/Art/INTERIOR_B1_PASS.md section 25.13).
    //   - The court west of the clinic (x -18.6..-7, 4.4 m deep behind the street line) left a hole of open snow in the
    //     street wall, and past the east neighbour (x 21.6) the north side simply stopped. Both are built up: the court
    //     takes a block flush with the street line, and the residents' building face that stood at the back of the
    //     court (its windows, cornice, the residents' door with its intercom and camera, the service door and the lamp
    //     under the lintel) comes forward onto it, so nothing designed there is lost; the washing line, the hanging
    //     utility runs and the locker hologram, which only make sense in a court, go. At the east end a new block of
    //     the same brick runs to x 33, with the residents' window set on its face.
    //   - Above the clinic: its street-floor front ended at 8.8 m, under a 10.8 m neighbour, so the street had a low
    //     sky over the B1 door. Upper floors go up: the clinic's block to 24 m, the east neighbour to 19.5 m, the
    //     new blocks to 16-17 m, each set back a little behind a dark cornice with snow on it, in the district's
    //     world-mapped city-block material (its window grid, which the far-depth pass then turns cold and dim).
    // Colliders on the two street-level blocks, so the walk map's edge walls follow the new street line.
    public static partial class ExteriorDistrictPass
    {
        static readonly Bounds CourtVolume = new Bounds(new Vector3(-12.8f, 6f, -1.5f), new Vector3(11.6f, 12f, 4.4f));
        const float StreetLine = -3.72f;

        static void StreetWall(Transform holder)
        {
            var done = new List<string>();
            var renderers = holder.GetComponentsInChildren<Renderer>(true);
            var brickR = renderers.Where(r => r.name == "Adjacent apartment mass").OrderBy(r => r.bounds.center.x).FirstOrDefault();
            var clinicR = renderers.FirstOrDefault(r => r.name == "Clinic street-floor facade");
            var cityR = renderers.FirstOrDefault(r => r.sharedMaterial != null && r.sharedMaterial.shader.name == "BorderRepair/Exterior/CityBlock" && r.sharedMaterial.name.Contains("scene fog"));
            if (brickR == null || clinicR == null) { log.Append("street wall: masses not found\n"); return; }
            var brick = brickR.sharedMaterial;
            var upper = cityR != null ? cityR.sharedMaterial : brick;
            var cornice = Worn("Street wall - cornice, dark concrete", Hex("#2C3337"), .3f, .1f);
            var snow = SnowMaterial();
            var root = new GameObject("Street wall (generated) - infill, east end, upper floors").transform;
            root.SetParent(holder, true);
            root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            // The residents' window set (frame, glazing, sashes, transoms, sill and its snow) on the back face of the court,
            // lower floor, west end: copied onto the east block's face.
            var kit = renderers.Where(r => r.gameObject.activeInHierarchy && Within(r.bounds.center, new Vector3(-17.6f, 3.3f, .2f), new Vector3(-15.9f, 5.0f, .85f))).ToArray();
            float kitFace = .7f;

            // 1. The east end (before the court's face moves: its window set is copied from there).
            const float ex0 = 21.62f, ex1 = 33f, eBack = 10f, eTop = 10.8f;
            var east = Box("East end block", root, new Vector3((ex0 + ex1) * .5f, eTop * .5f, (StreetLine + eBack) * .5f), new Vector3(ex1 - ex0, eTop, eBack - StreetLine), brick);
            east.AddComponent<BoxCollider>();
            int kits = 0;
            if (kit.Length > 0)
            {
                var kb = kit.Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
                var g = Group(root, "East end block - residents' windows");
                foreach (var y in new[] { 0f, 2.5f, 5.0f })
                    for (float x = ex0 + 1.4f; x < ex1 - .9f; x += 2.6f)
                    {
                        var off = new Vector3(x - kb.center.x, y, StreetLine - kitFace);
                        foreach (var r in kit)
                        {
                            var copy = Object.Instantiate(r.gameObject, g);
                            copy.name = r.name;
                            copy.SetActive(true);
                            copy.transform.SetPositionAndRotation(r.transform.position + off, r.transform.rotation);
                            copy.transform.localScale = r.transform.lossyScale;
                            foreach (var c in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                        }
                        kits++;
                    }
            }
            Box("East end - side cornice", root, new Vector3((ex0 + ex1) * .5f, eTop + .12f, StreetLine - .12f), new Vector3(ex1 - ex0 + .3f, .24f, .4f), cornice);
            done.Add("east end: block x " + ex0 + ".." + ex1 + ", " + eTop + " m, " + kits + " window sets");

            // 2. The court: its back face comes forward to the street line; what stood in the court goes.
            int moved = 0, hidden = 0;
            var face = new[] { "Vertical sash", "Clouded glazing", "Recessed sash frame", "Transom", "Snow on projecting sill", "Icicles - sill", "Curtain edge",
                               "Neighbour cornice", "Housing service door", "Entry lamp under lintel", "Lamp volume - court door" };
            float dz = StreetLine - kitFace;
            var west = brickR.bounds;
            var court = new List<Transform>();
            foreach (var r in renderers)
            {
                if (r == null || !r.gameObject.activeInHierarchy || r.transform.IsChildOf(root)) continue;
                var b = r.bounds;
                if (!CourtVolume.Contains(b.center) || b.size.x > 12f || b.size.z > 5f) continue;
                if (r.name == "Adjacent apartment mass") continue;
                bool onFace = b.center.z > .05f && (face.Contains(r.name) || HasParent(r.transform, "Residents' door"));
                if (onFace) court.Add(r.transform);
                else { r.gameObject.SetActive(false); hidden++; }
            }
            foreach (var l in holder.GetComponentsInChildren<Light>(true).Where(l => l.gameObject.activeInHierarchy && CourtVolume.Contains(l.transform.position)))
                court.Add(l.transform);
            // Move the topmost object of each, once (a door group moves with its parts).
            foreach (var t in court.Select(t => TopWithin(t, holder)).Distinct())
            {
                t.position += new Vector3(0, 0, dz);
                moved++;
            }
            var infill = Box("Court infill - the residents' block brought to the street", root,
                new Vector3((west.min.x + west.max.x) * .5f, (1.2f + west.max.y) * .5f, (StreetLine + west.min.z) * .5f + .01f),
                new Vector3(west.size.x, west.max.y - 1.2f, west.min.z - StreetLine), brick);
            infill.AddComponent<BoxCollider>();
            done.Add("court: block x " + west.min.x.ToString("F1") + ".." + west.max.x.ToString("F1") + " to the street line, " + moved + " face parts forward " + dz.ToString("F1") + " m, " + hidden + " court things off");

            // 3. Upper floors, set back behind a cornice with snow on it.
            var c0 = clinicR.bounds;
            var tiers = new[]
            {
                (name: "Court infill", x0: west.min.x, x1: west.max.x, from: west.max.y, to: 16.5f, front: StreetLine + .35f),
                (name: "Clinic block", x0: c0.min.x - .05f, x1: c0.max.x + .05f, from: c0.max.y, to: 24f, front: StreetLine + .3f),
                (name: "East neighbour", x0: 10.05f, x1: 21.6f, from: 10.8f, to: 19.5f, front: StreetLine + .35f),
                (name: "East end block", x0: ex0, x1: ex1, from: eTop, to: 17f, front: StreetLine + .4f),
            };
            const float back = 9f;
            foreach (var t in tiers)
            {
                var g = Group(root, "Upper floors - " + t.name);
                float w = t.x1 - t.x0, cx = (t.x0 + t.x1) * .5f;
                Box("Upper floors", g, new Vector3(cx, (t.from + t.to) * .5f, (t.front + back) * .5f), new Vector3(w, t.to - t.from, back - t.front), upper);
                Box("Cornice", g, new Vector3(cx, t.from + .1f, StreetLine + .05f), new Vector3(w + .1f, .2f, .55f), cornice);
                Box("Snow on the cornice", g, new Vector3(cx, t.from + .22f, StreetLine + .08f), new Vector3(w, .05f, .5f), snow);
                Box("Parapet", g, new Vector3(cx, t.to + .35f, t.front + .12f), new Vector3(w, .7f, .24f), cornice);
                Box("Snow on the parapet", g, new Vector3(cx, t.to + .72f, t.front + .12f), new Vector3(w, .05f, .26f), snow);
                done.Add(t.name + " " + t.from.ToString("F1") + " -> " + t.to + " m");
            }
            // A water tank and a mast on the clinic block's roof, for its outline against the sky.
            var tank = Lit("Street wall - roof tank", Hex("#262D31"), .3f, .2f);
            Cyl("Roof water tank", root, new Vector3(c0.min.x + 4f, 24f + 1.5f, 4f), 1.4f, 3f, tank);
            Rod("Roof mast", root, new Vector3(c0.max.x - 3f, 24f, 5f), new Vector3(c0.max.x - 3f, 31f, 5f), .09f, tank);

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (r.bounds.max.y > 9f || r.bounds.size.x > 6f) r.shadowCastingMode = ShadowCastingMode.Off;
            log.Append("street wall: ").Append(string.Join("; ", done)).Append('\n');
        }

        static bool Within(Vector3 p, Vector3 min, Vector3 max) => p.x >= min.x && p.y >= min.y && p.z >= min.z && p.x <= max.x && p.y <= max.y && p.z <= max.z;

        static bool HasParent(Transform t, string prefix)
        {
            for (var p = t; p != null; p = p.parent) if (p.name.StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }

        // The highest ancestor of t (below the holder's own groups) that still lies wholly in the court.
        static Transform TopWithin(Transform t, Transform holder)
        {
            var top = t;
            for (var p = t.parent; p != null && p != holder && p.parent != holder; p = p.parent)
            {
                var rs = p.GetComponentsInChildren<Renderer>(true);
                if (rs.Length == 0 || rs.Any(r => !CourtVolume.Contains(r.bounds.center))) break;
                if (p.GetComponentsInChildren<Light>(true).Any(l => !CourtVolume.Contains(l.transform.position))) break;
                top = p;
            }
            return top;
        }

        // The red aviation and obstruction lamps out over the city read too large: all of them at .6 of their size.
        static void SmallerRedLamps(Transform holder)
        {
            const float k = .6f;
            var names = new HashSet<string> { "Aviation lamp", "Aviation lamp - mast", "Aviation lamp - roof", "Aviation lamp - mid", "Ring aviation lamp",
                                              "Spire aviation lamp", "Crane obstruction lamp", "Housing lamp", "Crane jib tip lamp" };
            int n = 0, merged = 0;
            foreach (var r in holder.GetComponentsInChildren<Renderer>(true))
            {
                if (names.Contains(r.name)) { r.transform.localScale *= k; n++; continue; }
                if (r.name != "Ring aviation lamps - steady") continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mesh = Object.Instantiate(mf.sharedMesh);
                mesh.name = mf.sharedMesh.name + " (lamps at " + k + ")";
                var v = mesh.vertices;
                // Each lamp is a small separate ball: group the vertices by nearness, shrink each group about its middle.
                var group = new int[v.Length];
                var centres = new List<Bounds>();
                for (int i = 0; i < v.Length; i++)
                {
                    int g = centres.FindIndex(c => (c.center - v[i]).sqrMagnitude < 16f);
                    if (g < 0) { centres.Add(new Bounds(v[i], Vector3.zero)); g = centres.Count - 1; }
                    else { var c = centres[g]; c.Encapsulate(v[i]); centres[g] = c; }
                    group[i] = g;
                }
                for (int i = 0; i < v.Length; i++) { var c = centres[group[i]].center; v[i] = c + (v[i] - c) * k; }
                mesh.vertices = v;
                mesh.RecalculateBounds();
                var path = PlayableDir + "/Skyline_steady_lamps_small.asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(mesh, path);
                mf.sharedMesh = mesh;
                merged += centres.Count;
            }
            log.Append("red lamps: ").Append(n).Append(" lamps and ").Append(merged).Append(" lamps in the skyline's steady set at ").Append(k).Append(" of their size\n");
        }
    }
}
