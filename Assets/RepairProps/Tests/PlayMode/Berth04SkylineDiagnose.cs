using System.Collections;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.Tests
{
    // Read only: eight-way panoramas from the district's review views (to find where the far view runs out), and the
    // B1's small dressing with positions (candidates for things to pick up and fiddle with).
    public sealed partial class Berth04PlayableTests
    {
        [UnityTest, Explicit("diagnostic dump; run by name"), Timeout(900000)]
        public IEnumerator Diagnose_Skyline()
        {
            for (int i = 0; i < 30; i++) yield return null;
            var join = Berth04Playable.Current.Join;
            var m = Berth04Measure.MeasuringCamera();
            m.fieldOfView = 62; m.farClipPlane = 420;
            var sb = new StringBuilder("== fog ").Append(RenderSettings.fog).Append(' ').Append(RenderSettings.fogMode).Append(' ').Append(RenderSettings.fogDensity)
                .Append(' ').Append(RenderSettings.fogColor).Append('\n');
            foreach (var v in join.Outdoor.reviewViews.Where(v => v.position.y > .5f))
            {
                for (int k = 0; k < 8; k++)
                {
                    m.transform.SetPositionAndRotation(v.position, Quaternion.Euler(-2, k * 45, 0));
                    join.Evaluate(m);
                    Berth04Measure.Frame(m, "pano - " + Berth04Measure.Safe(v.name) + " - " + (k * 45).ToString("000") + ".jpg", () => join.Evaluate(m));
                }
                sb.Append("view ").Append(v.name).Append(' ').Append(V(v.position)).Append('\n');
                yield return null;
            }
            Object.Destroy(m.gameObject);

            var pass = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).First(t => t.name.StartsWith("09 Interior pass"));
            var room = ClinicLayout.Active.room;
            sb.Append("== B1 items\n");
            foreach (Transform g in pass)
                foreach (Transform item in g)
                {
                    var rs = item.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
                    if (rs.Length == 0) continue;
                    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    if (!room.Contains(b.center)) continue;
                    sb.Append("  ").Append(g.name).Append(" / ").Append(item.name).Append("  ").Append(V(b.min)).Append("..").Append(V(b.max))
                      .Append("  parts ").Append(string.Join(",", item.GetComponentsInChildren<Transform>().Where(t => t != item).Select(t => t.name).Distinct().Take(12))).Append('\n');
                }
            Berth04Measure.Append("skyline.txt", sb.ToString());
        }
    }
}
