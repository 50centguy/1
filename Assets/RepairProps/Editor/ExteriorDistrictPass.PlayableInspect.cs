using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BorderRepair.RepairProps.EditorTools
{
    public static partial class ExteriorDistrictPass
    {
        // What of the playable district copy is solid: colliders and collider-less renderers near the door, by group.
        public static void InspectPlayable()
        {
            EditorSceneManager.OpenScene(PlayableDistrict, OpenSceneMode.Single);
            var sb = new StringBuilder();
            var door = new Vector3(0, 0, -3.5f);
            bool Near(Bounds b) => Vector2.Distance(new Vector2(b.center.x, b.center.z), new Vector2(door.x, door.z)) < 90;
            string Group(Transform t, int depth)
            {
                var chain = new List<string>();
                for (var p = t; p != null; p = p.parent) chain.Insert(0, p.name);
                return string.Join("/", chain.Skip(1).Take(depth));
            }
            string B(Bounds b) => b.min.ToString("F1") + ".." + b.max.ToString("F1");
            var cols = Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(c => c.enabled).ToList();
            sb.Append("colliders enabled: ").Append(cols.Count).Append(" (triggers ").Append(cols.Count(c => c.isTrigger)).Append(")\n");
            foreach (var g in cols.Where(c => !c.isTrigger).GroupBy(c => Group(c.transform, 2)).OrderBy(g => g.Key))
            {
                var u = g.First().bounds; foreach (var c in g) u.Encapsulate(c.bounds);
                sb.Append("  ").Append(g.Count()).Append("  ").Append(g.Key).Append("  ").Append(string.Join(",", g.Select(c => c.GetType().Name).Distinct())).Append("  ").Append(B(u)).Append('\n');
            }
            sb.Append("big colliders (>6 m):\n");
            foreach (var c in cols.Where(c => !c.isTrigger && Mathf.Max(c.bounds.size.x, c.bounds.size.z) > 6))
                sb.Append("  ").Append(HierPath(c.transform)).Append("  ").Append(c.GetType().Name).Append("  ").Append(B(c.bounds)).Append('\n');
            var rs = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(r => r.enabled && r.GetComponent<Collider>() == null && Near(r.bounds)).ToList();
            sb.Append("collider-less mesh renderers within 90 m: ").Append(rs.Count).Append('\n');
            foreach (var g in rs.GroupBy(r => Group(r.transform, 2)).OrderBy(g => g.Key))
            {
                var u = g.First().bounds; foreach (var r in g) u.Encapsulate(r.bounds);
                int tall = g.Count(r => r.bounds.size.y > 1.5f && r.bounds.min.y < 2.5f);
                var shaders = string.Join(",", g.SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.shader.name.Replace("BorderRepair/Exterior/", "").Replace("Universal Render Pipeline/", "URP/")).Distinct().Take(5));
                sb.Append("  ").Append(g.Count()).Append(" (tall at street ").Append(tall).Append(")  ").Append(g.Key).Append("  ").Append(B(u)).Append("  [").Append(shaders).Append("]\n");
            }
            sb.Append("tall collider-less renderers reaching street level (sample 80):\n");
            foreach (var r in rs.Where(r => r.bounds.size.y > 1.5f && r.bounds.min.y < 2.5f).OrderByDescending(r => r.bounds.size.x * r.bounds.size.z).Take(80))
            {
                var mf = r.GetComponent<MeshFilter>();
                sb.Append("  ").Append(HierPath(r.transform)).Append("  ").Append(B(r.bounds)).Append("  tris ").Append(mf && mf.sharedMesh ? mf.sharedMesh.triangles.Length / 3 : -1)
                  .Append("  ").Append(r.sharedMaterial ? r.sharedMaterial.shader.name : "-").Append('\n');
            }
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir, "playable_inspect.txt"), sb.ToString());
            Debug.Log("[Exterior] playable inspected");
        }

        // Read only: what of the clinic stands in its door (x +-1.1, y 0..2.7, z -4.2..-3.0).
        public static void InspectClinicDoor()
        {
            EditorSceneManager.OpenScene(Berth04Playable.ClinicScene, OpenSceneMode.Single);
            var box = new Bounds(new Vector3(0, 1.35f, -3.6f), new Vector3(2.2f, 2.7f, 1.2f));
            var sb = new StringBuilder("clinic renderers / colliders in the door box\n");
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(r => r.bounds.Intersects(box)).OrderBy(r => HierPath(r.transform)))
                sb.Append(r.gameObject.activeInHierarchy && r.enabled ? "  on  " : "  off ").Append(HierPath(r.transform)).Append("  ").Append(r.bounds.min.ToString("F2")).Append("..").Append(r.bounds.max.ToString("F2"))
                  .Append("  ").Append(r.sharedMaterial ? r.sharedMaterial.name : "-").Append('\n');
            foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(c => c.bounds.Intersects(box)).OrderBy(c => HierPath(c.transform)))
                sb.Append(c.gameObject.activeInHierarchy && c.enabled ? "  col on  " : "  col off ").Append(HierPath(c.transform)).Append("  ").Append(c.bounds.min.ToString("F2")).Append("..").Append(c.bounds.max.ToString("F2")).Append(" layer ").Append(c.gameObject.layer).Append('\n');
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir, "clinic_door.txt"), sb.ToString());
            Debug.Log("[Exterior] clinic door inspected");
        }
    }
}
