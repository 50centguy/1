using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BorderRepair.TwoNight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BorderRepair.Art.UnifiedClinic.EditorTools
{
    // The floor's F5 traffic lanes are authored from the room's real anchors, not by eye: the shop's floor is worn
    // where people actually walk (customer to the counter, item to the bench, bench to the dock). This reads the
    // authored scene, turns those anchors into world-XZ segments, and writes them into the floor shell material.
    // Batch entry: -executeMethod BorderRepair.Art.UnifiedClinic.EditorTools.ClinicFloorTrafficBaker.RunBatch
    public static class ClinicFloorTrafficBaker
    {
        const string ScenePath = "Assets/RepairProps/Scenes/RepairProps_Clinic.unity";
        const string FloorMaterialPath = "Assets/BorderRepair/Art/UnifiedClinic/Shell/M_UC_Shell_Floor.mat";
        const string ReportPath = "Docs/Integration/UnifiedClinic/Shell/floor_traffic.txt";

        struct Lane
        {
            public string what;
            public Vector2 from, to;
            public float radius, strength;
        }

        public static void RunBatch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid()) { Debug.LogError("[FloorTraffic] cannot open " + ScenePath); return; }

            var report = new StringBuilder();
            report.AppendLine("floor traffic lanes, baked from " + ScenePath);
            report.AppendLine(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

            var director = UnityEngine.Object.FindFirstObjectByType<UnifiedClinicDirector>();
            if (director == null) { Debug.LogError("[FloorTraffic] no UnifiedClinicDirector in the scene."); return; }

            var door = FindByName(scene, "Fixture_Door");
            // The dock zone is built at runtime, so the authored scene only has the viewpoint used to look at it.
            // Flow roots sit at the room centre and would draw a lane to nowhere, so they are the last resort.
            var dock = FindByName(scene, "Unit07ServiceDock") ?? FindByName(scene, "CamPose_Dock") ?? FindByName(scene, "Unit07DockFlow");
            var receive = director.ReceiveAnchor;
            var bench = director.WorkAnchor;
            var deliver = director.DeliveryAnchor;

            var lanes = new List<Lane>();
            Add(lanes, report, "customer: door -> counter", door, receive, .45f, 1f);
            Add(lanes, report, "item: counter -> bench", receive, bench, .50f, 1f);
            Add(lanes, report, "assistant: bench -> dock", bench, dock, .42f, .85f);
            Add(lanes, report, "customer: counter -> pickup", receive, deliver, .38f, .70f);

            var material = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterialPath);
            if (material == null) { Debug.LogError("[FloorTraffic] missing " + FloorMaterialPath); return; }

            var centre = material.GetVector("_RoomCenter");
            var origin = new Vector2(centre.x, centre.z);
            // Lane slots are fixed properties, so a room with fewer routes must not keep stale segments behind.
            var seg = new Vector4[4];
            var radius = Vector4.zero;
            var strength = Vector4.zero;
            for (int i = 0; i < lanes.Count && i < 4; i++)
            {
                var lane = lanes[i];
                Vector2 a = lane.from - origin, b = lane.to - origin;
                seg[i] = new Vector4(a.x, a.y, b.x, b.y);
                radius[i] = lane.radius;
                strength[i] = lane.strength;
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  lane {0}  {1,-28} ({2:F2},{3:F2}) -> ({4:F2},{5:F2})  length {6:F2} m  radius {7:F2}  strength {8:F2}",
                    i, lane.what, a.x, a.y, b.x, b.y, (b - a).magnitude, lane.radius, lane.strength));
            }

            material.SetVector("_Traffic0", seg[0]);
            material.SetVector("_Traffic1", seg[1]);
            material.SetVector("_Traffic2", seg[2]);
            material.SetVector("_Traffic3", seg[3]);
            material.SetVector("_TrafficRadius", radius);
            material.SetVector("_TrafficStrength", strength);
            SeedLook(material);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();

            report.AppendLine("room centre (material _RoomCenter) = " + centre.ToString("F3"));
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, report.ToString());
            Debug.Log("[FloorTraffic] wrote " + lanes.Count + " lanes into " + FloorMaterialPath + Environment.NewLine + report);
        }

        // The look knobs are the artist's, not the baker's: they are only seeded the first time this material sees
        // them, so re-running the baker after the anchors move never throws away tuning done in the Inspector.
        static void SeedLook(Material material)
        {
            var floats = material.GetPropertyNames(MaterialPropertyType.Float);
            void Once(string name, float value)
            {
                if (Array.IndexOf(floats, name) >= 0) return;
                material.SetFloat(name, value);
            }
            Once("_TrafficDark", .22f);        // tracked-in grime over the whole walked band
            Once("_TrafficEdgeDark", .34f);    // dirt pushed to the edges of the band
            Once("_TrafficLight", .09f);       // the polished streak: this is what actually reads at night
            Once("_TrafficPolish", .12f);
        }

        static void Add(List<Lane> lanes, StringBuilder report, string what, Transform from, Transform to, float radius, float strength)
        {
            if (from == null || to == null)
            {
                report.AppendLine("  SKIPPED " + what + ": " + (from == null ? "start anchor" : "end anchor") + " not found");
                return;
            }
            Vector2 a = new Vector2(from.position.x, from.position.z);
            Vector2 b = new Vector2(to.position.x, to.position.z);
            if ((b - a).sqrMagnitude < .0625f)   // under 25 cm: the two anchors are the same spot, a lane would be a dot
            {
                report.AppendLine("  SKIPPED " + what + ": anchors are " + (b - a).magnitude.ToString("F3") + " m apart");
                return;
            }
            lanes.Add(new Lane { what = what, from = a, to = b, radius = radius, strength = strength });
        }

        static Transform FindByName(UnityEngine.SceneManagement.Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return t;
            return null;
        }
    }
}
