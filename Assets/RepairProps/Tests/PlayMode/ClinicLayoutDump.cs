using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace BorderRepair.RepairProps.Tests
{
    // Read only: where everything of the clinic stands once its runtimes have installed (for mapping it onto the B1).
    public class ClinicLayoutDump
    {
        [UnityTest, Explicit("writes a layout dump; run by name")]
        public IEnumerator Dump_ClinicRuntimeLayout()
        {
#if UNITY_EDITOR
            var op = EditorSceneManager.LoadSceneAsyncInPlayMode(Berth04Playable.ClinicScene, new LoadSceneParameters(LoadSceneMode.Single));
            while (!op.isDone) yield return null;
            for (int i = 0; i < 10; i++) yield return null;
            var sb = new StringBuilder();
            string V(Vector3 v) => v.ToString("F2");
            void Dump(Transform t, int depth, int max)
            {
                var rs = t.GetComponentsInChildren<Renderer>(true);
                var u = rs.Length > 0 ? rs[0].bounds : new Bounds(t.position, Vector3.zero);
                foreach (var r in rs) u.Encapsulate(r.bounds);
                var comps = string.Join(",", t.GetComponents<Component>().Where(c => c != null && !(c is Transform) && !(c is MeshFilter) && !(c is MeshRenderer)).Select(c => c.GetType().Name));
                sb.Append(new string(' ', depth * 2)).Append(t.gameObject.activeInHierarchy ? "" : "[off] ").Append(t.name).Append("  pos ").Append(V(t.position))
                  .Append(" rotY ").Append(t.eulerAngles.y.ToString("F0")).Append("  r ").Append(rs.Length).Append("  ")
                  .Append(rs.Length > 0 ? V(u.min) + ".." + V(u.max) : "-").Append("  {").Append(comps).Append("}\n");
                if (depth < max) foreach (Transform c in t) Dump(c, depth + 1, max);
            }
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) Dump(root.transform, 0, 2);
            var dir = System.Environment.GetEnvironmentVariable("EXTERIOR_OUT") ?? "Temp";
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "clinic_runtime_layout.txt"), sb.ToString());
#endif
            yield return null;
        }
    }
}
