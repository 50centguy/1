using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static BorderRepair.TwoNight.MeshClearance;

namespace BorderRepair.TwoNight.EditorTools
{
    /// <summary>只读诊断：托盘原位附近的托盘架零件（警示牌等）与托盘的距离。</summary>
    public static class TwoNightShelfProbe
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene(TwoNightBuilder.RobotPath, OpenSceneMode.Single);
            var tray = GameObject.Find("Dock_PartsTray").GetComponent<Renderer>();
            var tm = World(tray);
            Debug.Log($"[ShelfProbe] tray bounds {tray.bounds.min:F4} – {tray.bounds.max:F4}");
            foreach (var r in GameObject.Find("Unit07ServiceDock").GetComponentsInChildren<Renderer>(true).Where(r => r.bounds.Intersects(new Bounds(tray.bounds.center, tray.bounds.size + Vector3.one * 0.06f))))
            {
                var m = World(r); if (m == null) continue;
                string row = string.Join(" ", new[] { 0f, 0.002f, 0.004f, 0.006f, 0.008f, 0.012f, 0.02f }.Select(o => $"+{o * 1000:F0}:{Distance(Moved(tm, Matrix4x4.Translate(Vector3.up * o)), m, 0.05f) * 1000:F1}"));
                Debug.Log($"[ShelfProbe] {r.name} {r.transform.parent?.name} bounds {r.bounds.min:F4} – {r.bounds.max:F4} | tray up offset mm→gap mm: {row}");
            }
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
