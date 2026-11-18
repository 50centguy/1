using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BorderRepair.RepairProps.EditorTools
{
    // The Berth 04 district (Temp_Berth04District, handed over by the exterior layout study): an inspection dump, the
    // exterior pass (ExteriorDistrictPass.Build.cs) and a shot renderer for the review viewpoints.
    // Batch: -executeMethod BorderRepair.RepairProps.EditorTools.ExteriorDistrictPass.{Dump|Apply|Shots}; EXTERIOR_OUT
    // names the output folder.
    public static partial class ExteriorDistrictPass
    {
        public const string ScenePath = "Assets/RepairProps/Scenes/Temp_Berth04District.unity";

        static string OutDir => Environment.GetEnvironmentVariable("EXTERIOR_OUT") ?? "Temp/ExteriorDistrict";

        // Review viewpoints the scene does not carry as cameras yet (the return walk). Eye height 1.62 m over the street.
        static readonly (string name, Vector3 eye, Vector3 look)[] ExtraViews =
        {
            ("Return 1 - berth looking back to B1", new Vector3(-21.5f, 2.97f, -3.4f), new Vector3(-9f, 2.4f, -4.4f)),
            ("Return 2 - approaching the stairwell", new Vector3(-8.6f, 2.97f, -4.4f), new Vector3(-1.4f, 1.9f, -2.2f)),
        };

        [MenuItem("Border Repair/Exterior/Dump Berth 04 district")]
        public static void Dump()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var sb = new StringBuilder();
            foreach (var root in scene.GetRootGameObjects()) Write(sb, root.transform, 0);
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir, "hierarchy.txt"), sb.ToString());
            Debug.Log("[Exterior] dumped " + scene.GetRootGameObjects().Length + " roots");
        }

        static void Write(StringBuilder sb, Transform t, int depth)
        {
            var renderers = t.GetComponentsInChildren<Renderer>(true);
            string box = "";
            if (renderers.Length > 0)
            {
                var b = renderers[0].bounds;
                foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
                box = " c=" + b.center.ToString("F1") + " s=" + b.size.ToString("F1");
            }
            var extras = string.Join(",", t.GetComponents<Component>().Where(c => c != null && !(c is Transform) && !(c is MeshFilter) && !(c is MeshRenderer) && !(c is BoxCollider))
                .Select(c => c.GetType().Name));
            sb.Append(new string(' ', depth * 2)).Append(t.name).Append(" p=").Append(t.position.ToString("F1"))
              .Append(" n=").Append(renderers.Length).Append(box);
            if (extras.Length > 0) sb.Append(" [").Append(extras).Append("]");
            sb.Append('\n');
            if (depth < 3 || t.childCount <= 6) foreach (Transform c in t) Write(sb, c, depth + 1);
        }

        // Renders every camera in the scene plus the extra review viewpoints to 1600x900 PNGs, with the snow running.
        [MenuItem("Border Repair/Exterior/Render review shots")]
        public static void Shots()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var dir = Path.Combine(OutDir, "shots");
            Directory.CreateDirectory(dir);
            foreach (var ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                if (ps.transform.parent == null || ps.transform.parent.GetComponent<ParticleSystem>() == null) ps.Simulate(8f, true, true);

            var temporary = new List<GameObject>();
            foreach (var view in ExtraViews)
            {
                if (GameObject.Find(view.name) != null) continue;
                var go = new GameObject(view.name);
                go.transform.SetPositionAndRotation(view.eye, Quaternion.LookRotation(view.look - view.eye));
                var cam = go.AddComponent<Camera>();
                cam.fieldOfView = 55; cam.nearClipPlane = .05f; cam.farClipPlane = 300;
                temporary.Add(go);
            }
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            int count = 0;
            foreach (var cam in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None).OrderBy(c => c.name))
            {
                var was = cam.targetTexture;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                cam.targetTexture = was;
                File.WriteAllBytes(Path.Combine(dir, Safe(cam.name) + ".png"), tex.EncodeToPNG());
                count++;
            }
            foreach (var go in temporary) UnityEngine.Object.DestroyImmediate(go);
            rt.Release();
            Debug.Log("[Exterior] rendered " + count + " shots into " + dir);
        }

        static string Safe(string name) => new string(name.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray());
    }
}
