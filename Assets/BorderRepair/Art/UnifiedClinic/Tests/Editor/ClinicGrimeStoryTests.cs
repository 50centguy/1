using System.Linq;
using BorderRepair.Art.UnifiedClinic.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BorderRepair.Art.UnifiedClinic.Tests
{
    // The room's storytelling grime: the worn walking lanes on the floor and the flux residue on the bench.
    // Both are baked by editor installers, so what these tests guard is that the bake is present, lands inside the
    // room, and stays out of the way of interaction.
    public class ClinicGrimeStoryTests
    {
        const string ScenePath = "Assets/RepairProps/Scenes/RepairProps_Clinic.unity";
        const string FloorMaterialPath = "Assets/BorderRepair/Art/UnifiedClinic/Shell/M_UC_Shell_Floor.mat";
        const string BenchGrimeRoot = "ClinicBenchGrime";

        // Same shape as the shader's ShellOctDist: distance to the centre of the octagonal room, so a lane that
        // wanders past the apothem would be drawn on the corridor side of the wall.
        static float OctDist(Vector2 p)
        {
            var a = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y));
            return Mathf.Max(Mathf.Max(a.x, a.y), (a.x + a.y) * 0.70710678f);
        }

        [Test]
        public void FloorWearLanes_AreBakedAndStayInsideTheRoom()
        {
            var floor = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterialPath);
            Assert.IsNotNull(floor, FloorMaterialPath);
            Assert.IsTrue(floor.HasProperty("_Traffic0"), "the floor material predates the traffic lanes");

            var centre = floor.GetVector("_RoomCenter");
            var origin = new Vector2(centre.x, centre.z);
            float apothem = floor.GetFloat("_RoomApothem");
            var radius = floor.GetVector("_TrafficRadius");
            var strength = floor.GetVector("_TrafficStrength");
            var lanes = new[] { floor.GetVector("_Traffic0"), floor.GetVector("_Traffic1"), floor.GetVector("_Traffic2"), floor.GetVector("_Traffic3") };

            int live = 0;
            for (int i = 0; i < lanes.Length; i++)
            {
                var lane = lanes[i];
                float power = strength[i];
                if (power <= 0f) continue;
                live++;
                var a = new Vector2(lane.x, lane.y);
                var b = new Vector2(lane.z, lane.w);
                Assert.Greater((b - a).magnitude, 0.5f, "lane " + i + " is too short to be a walk");
                Assert.Greater(radius[i], 0.1f, "lane " + i + " has no width");
                // The radius is the lane's half width, so the far edge -- not the centre line -- is what can poke out.
                Assert.Less(OctDist(a + origin), apothem, "lane " + i + " starts outside the room");
                Assert.Less(OctDist(b + origin), apothem, "lane " + i + " ends outside the room");
            }
            Assert.GreaterOrEqual(live, 3, "the floor should carry the shop's main routes, not just one");
        }

        [Test]
        public void BenchFluxGrime_IsPresentAndNeverBlocksInteraction()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Assert.IsTrue(scene.IsValid(), ScenePath);

            Transform root = null;
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name == BenchGrimeRoot) { root = go.transform; break; }
            }
            Assert.IsNotNull(root, BenchGrimeRoot + " is missing; run ClinicBenchGrimeInstaller.RunBatch");

            var decals = root.GetComponentsInChildren<Renderer>(true);
            Assert.GreaterOrEqual(decals.Length, 4, "the bench should carry several residues");

            foreach (var decal in decals)
            {
                Assert.IsEmpty(decal.GetComponentsInChildren<Collider>(true),
                    decal.name + " has a collider: cleaning and picking raycast this bench, a stray collider breaks both");
                Assert.IsNotNull(decal.sharedMaterial, decal.name + " has no material");
                Assert.AreEqual("BorderRepair/BenchGrime", decal.sharedMaterial.shader.name, decal.name);
            }

            // The pooled runs must carry their shape on the material asset: a MaterialPropertyBlock would not be
            // serialised with the scene, and every run would come back as a pool after a reload.
            var runs = decals.Where(d => d.name.StartsWith("Grime_Run_")).ToArray();
            Assert.IsNotEmpty(runs, "the bench edge should carry at least one run");
            foreach (var run in runs)
                Assert.AreEqual(1f, run.sharedMaterial.GetFloat("_Mode"), run.name + " lost its run shape");
            foreach (var pool in decals.Where(d => d.name.StartsWith("Grime_Pool_")))
                Assert.AreEqual(0f, pool.sharedMaterial.GetFloat("_Mode"), pool.name + " should be a pool");
        }
    }
}
