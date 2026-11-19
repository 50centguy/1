using System.Collections;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.Tests
{
    // Read only: what of the clinic copy stands in the B1 (by root and child), and what occupies the spots the cyber
    // dressing would use.
    public sealed partial class Berth04PlayableTests
    {
        [UnityTest, Explicit("diagnostic dump; run by name"), Timeout(600000)]
        public IEnumerator Diagnose_B1Space()
        {
            for (int i = 0; i < 30; i++) yield return null;
            var room = ClinicLayout.Active.room;
            var sb = new StringBuilder("== clinic copy in the B1 (roots and their children)\n");
            var clinic = SceneManager.GetSceneByPath(Berth04Playable.PlayableClinicScene);
            foreach (var root in clinic.GetRootGameObjects())
                foreach (var t in new[] { root.transform }.Concat(root.transform.Cast<Transform>()))
                {
                    var rs = t.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !r.forceRenderingOff && !(r is ParticleSystemRenderer)).ToArray();
                    if (rs.Length == 0) continue;
                    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    if (!room.Intersects(b) || b.size.magnitude > 30) continue;
                    sb.Append(t == root.transform ? "  " : "      ").Append(t.name).Append("  ").Append(V(b.min)).Append("..").Append(V(b.max)).Append('\n');
                }
            var spots = new (string name, Bounds b)[]
            {
                ("north wall x .8-2.55", new Bounds(new Vector3(1.67f, 1.6f, 2.42f), new Vector3(1.75f, 1.2f, .4f))),
                ("east wall in the bay", new Bounds(new Vector3(4.77f, 1.55f, 2.0f), new Vector3(.54f, .9f, 1.2f))),
                ("bed head, west corner", new Bounds(new Vector3(3.15f, 1.0f, 2.26f), new Vector3(.82f, 2.0f, .72f))),
                ("gate, right jamb wall", new Bounds(new Vector3(1.1f, 1.25f, -3.3f), new Vector3(.63f, .7f, .2f))),
                ("counter holo", new Bounds(new Vector3(.65f, 2.2f, -2.45f), new Vector3(.7f, .65f, .7f))),
                ("ceiling run north", new Bounds(new Vector3(1.43f, 2.45f, 2.4f), new Vector3(2.26f, .2f, .44f))),
                ("west wall south of coat", new Bounds(new Vector3(-1.85f, 1.6f, -2.2f), new Vector3(.3f, 1.0f, .8f))),
                ("sill cabinet top", new Bounds(new Vector3(2.68f, 1.2f, -3.17f), new Vector3(2.5f, .4f, .5f))),
            };
            foreach (var (name, box) in spots)
            {
                sb.Append("== ").Append(name).Append(' ').Append(V(box.min)).Append("..").Append(V(box.max)).Append('\n');
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && !r.forceRenderingOff && r.gameObject.activeInHierarchy && box.Intersects(r.bounds) && r.bounds.size.magnitude < 8))
                    sb.Append("  [").Append(r.gameObject.scene.name.Replace("Berth04_Playable_", "")).Append("] ").Append(P(r.transform).Replace("District - moved to meet the clinic door/", ""))
                      .Append("  ").Append(V(r.bounds.min)).Append("..").Append(V(r.bounds.max)).Append('\n');
            }
            Berth04Measure.Append("space.txt", sb.ToString());
        }
    }
}
