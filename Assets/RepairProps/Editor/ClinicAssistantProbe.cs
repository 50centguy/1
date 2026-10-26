using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BorderRepair.RepairProps.EditorTools
{
    // Read-only survey for the UNIT 07 assistant: the robot's hierarchy, animator and the clinic anchors it travels
    // between. Opens the scene without saving anything.
    static class ClinicAssistantProbe
    {
        const string ScenePath = "Assets/RepairProps/Scenes/RepairProps_Clinic.unity";

        public static void Run()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var sb = new StringBuilder();
            var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var model = all.FirstOrDefault(t => t.name == "UNIT07_RobotV4_DockReady") ?? all.FirstOrDefault(t => t.GetComponentsInChildren<Renderer>(true).Any(r => r.name == "ScreenContent_Animated") && t.GetComponent<Animator>() != null);
            var screen = all.FirstOrDefault(t => t.name == "ScreenContent_Animated");
            Transform root = screen;
            while (root != null && root.parent != null && root.parent.name != "ClinicLegacyRoomFrame" && root.parent.parent != null) root = root.parent;
            sb.AppendLine("screen path: " + Path(screen));
            for (var t = screen; t != null; t = t.parent)
                sb.AppendLine(string.Format("  up {0} active={1} pos={2} rot={3} scale={4} comps={5}", t.name, t.gameObject.activeSelf, t.position.ToString("F3"), t.eulerAngles.ToString("F0"), t.lossyScale.ToString("F2"),
                    string.Join(",", t.GetComponents<Component>().Select(c => c.GetType().Name))));
            foreach (var animator in Object.FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (screen == null || !screen.IsChildOf(animator.transform)) continue;
                sb.AppendLine("animator on " + Path(animator.transform) + " controller=" + (animator.runtimeAnimatorController ? AssetDatabase.GetAssetPath(animator.runtimeAnimatorController) : "none") + " avatar=" + animator.avatar + " enabled=" + animator.enabled);
                if (animator.runtimeAnimatorController) foreach (var clip in animator.runtimeAnimatorController.animationClips) sb.AppendLine("  clip " + clip.name + " len=" + clip.length);
                var bounds = animator.GetComponentsInChildren<Renderer>(true).Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
                sb.AppendLine("  bounds center=" + bounds.center.ToString("F3") + " size=" + bounds.size.ToString("F3"));
                Dump(animator.transform, 0, 3, sb);
            }
            foreach (var name in new[] { "Unit07ServiceDock", "Dock_RobotAnchor", "WorkbenchArea", "RP_Bench", "RP_EngineBay", "ClinicReceiveAnchor", "ReceiveAnchor", "DeliveryAnchor", "Counter", "ClinicCounter" })
                foreach (var t in all.Where(t => t.name == name)) sb.AppendLine(string.Format("anchor {0} pos={1} path={2}", name, t.position.ToString("F3"), Path(t)));
            foreach (var t in all.Where(t => t.name.IndexOf("Anchor", System.StringComparison.OrdinalIgnoreCase) >= 0 && (t.name.Contains("Receive") || t.name.Contains("Deliver") || t.name.Contains("Pad"))))
                sb.AppendLine(string.Format("pad {0} pos={1}", Path(t), t.position.ToString("F3")));
            sb.AppendLine("-- room blocks (renderer bounds, depth 2 under each root)");
            foreach (var rootObject in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                foreach (Transform first in rootObject.transform)
                {
                    var targets = first.childCount > 0 && first.childCount < 40 ? first.Cast<Transform>().Prepend(first) : new[] { first };
                    foreach (var t in targets)
                    {
                        var renderers = t.GetComponentsInChildren<Renderer>(true);
                        if (renderers.Length == 0) continue;
                        var b = renderers[0].bounds;
                        foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
                        if (b.size.magnitude < .25f) continue;
                        sb.AppendLine(string.Format("  {0} min={1} max={2}", Path(t), b.min.ToString("F2"), b.max.ToString("F2")));
                    }
                }
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/assistant_probe.txt", sb.ToString());
            Debug.Log("[AssistantProbe]\n" + sb);
        }

        static void Dump(Transform t, int depth, int max, StringBuilder sb)
        {
            sb.AppendLine(new string(' ', depth * 2 + 2) + t.name + " " + t.localPosition.ToString("F3") + " [" + string.Join(",", t.GetComponents<Component>().Skip(1).Select(c => c.GetType().Name)) + "]");
            if (depth < max) foreach (Transform child in t) Dump(child, depth + 1, max, sb);
        }

        static string Path(Transform t)
        {
            if (t == null) return "(none)";
            string p = t.name;
            while ((t = t.parent) != null) p = t.name + "/" + p;
            return p;
        }
    }
}
