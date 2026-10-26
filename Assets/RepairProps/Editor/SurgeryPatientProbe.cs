using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BorderRepair.RepairProps.EditorTools
{
    // Read-only survey for the surgery patient flow: the bed frame the V2 table lands on, and every solid collider near
    // it (what a walking patient must avoid). Opens the scene without saving anything.
    static class SurgeryPatientProbe
    {
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene(SurgeryTableV2Runtime.ScenePath, OpenSceneMode.Single);
            var sb = new StringBuilder();
            SurgeryTableV2Runtime.TryBedFrame(scene, out var floor, out var headDir);
            sb.AppendLine("bed floor=" + floor.ToString("F3") + " headDir=" + headDir.ToString("F2"));
            foreach (var c in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Collider>(true))
                         .Where(c => c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger)
                         .Where(c => Vector2.Distance(new Vector2(c.bounds.center.x, c.bounds.center.z), new Vector2(floor.x, floor.z)) < 3.2f)
                         .Where(c => c.bounds.max.y > .05f && c.bounds.min.y < 1.6f && c.bounds.size.magnitude > .25f && c.bounds.center.x > -2.6f)
                         .OrderBy(c => c.bounds.center.x))
                sb.AppendLine(string.Format("  {0,-40} min={1} max={2}", c.name, c.bounds.min.ToString("F2"), c.bounds.max.ToString("F2")));
            Debug.Log("[SurgeryPatientProbe]\n" + sb);
        }
    }
}
