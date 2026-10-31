using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BorderRepair.Art.UnifiedClinic.EditorTools;
using BorderRepair.TwoNight.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BorderRepair.Art.UnifiedClinic.Tests
{
    public class ClinicShellTests
    {
        [Test]
        public void ShellShaderCompilesWithAllUrpPasses()
        {
            var shader = Shader.Find(ClinicRetroLook.ShellShaderName);
            Assert.IsNotNull(shader, ClinicRetroLook.ShellShaderName);
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), string.Join("; ", ShaderUtil.GetShaderMessages(shader).Select(m => m.message)));
            // Under -batchmode there is no graphics device, so Shader.passCount collapses to the fallback
            // subshader (1) and Material.FindPass(name) reports -1. Assert on the shader source instead,
            // which is deterministic headless, and leave the on-GPU proof to CyberLookCaptureTests.
            string path = AssetDatabase.GetAssetPath(shader);
            Assert.IsNotEmpty(path, ClinicRetroLook.ShellShaderName + " has no asset path");
            string source = File.ReadAllText(path);
            foreach (string pass in new[] { "ForwardLit", "ShadowCaster", "DepthOnly", "DepthNormals" })
                Assert.IsTrue(Regex.IsMatch(source, "Name\\s+\"" + pass + "\""), "missing pass " + pass);
        }

        [Test]
        public void ShellMaterialsExistWithMatchingKindAndDetailTexture()
        {
            var expected = new[]
            {
                (path: ClinicRetroLook.ShellFloorPath, kind: 0f),
                (path: ClinicRetroLook.ShellWallPath, kind: 1f),
                (path: ClinicRetroLook.ShellCeilingPath, kind: 2f),
            };
            foreach (var (path, kind) in expected)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                Assert.IsNotNull(material, path);
                Assert.AreEqual(ClinicRetroLook.ShellShaderName, material.shader.name, path);
                Assert.AreEqual(kind, material.GetFloat("_ShellKind"), path);
                Assert.IsNotNull(material.GetTexture("_BaseMap"), path + " needs its detail texture");
            }
            Assert.DoesNotThrow(() => ClinicRetroLook.LoadShellMaterials());
        }

        [Test]
        public void ApplyBindShellRestoreRoundTripIsExact()
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var scene = EditorSceneManager.OpenScene(UnifiedClinicBuilder.ClinicPath, OpenSceneMode.Single);
            try
            {
                var shells = ClinicRetroLook.LoadShellMaterials();
                ClinicRetroLook.Restore(scene);
                var renderers = Renderers(scene);
                var original = Snapshot(renderers);

                ClinicRetroLook.Apply(scene);
                var retro = Snapshot(renderers);
                int retroPairs = ClinicRetroLook.FindState(scene).materials.Count;

                // Bind (material assets untouched: configureMaterials = false).
                var result = ClinicRetroLook.BindShell(scene, false);
                Assert.Greater(result.floorSlots, 0, "floor slots");
                Assert.Greater(result.wallSlots, 0, "wall slots");
                Assert.Greater(result.ceilingSlots, 0, "ceiling slots");
                var bound = Snapshot(renderers);
                var used = bound.SelectMany(m => m).Where(m => m != null).Distinct().ToArray();
                CollectionAssert.Contains(used, shells.floor);
                CollectionAssert.Contains(used, shells.wall);
                CollectionAssert.Contains(used, shells.ceiling);

                var state = ClinicRetroLook.FindState(scene);
                bool IsWall(Material m) => m != null && AssetDatabase.GetAssetPath(m) == ClinicRetroLook.WallSourcePath;
                bool IsFloor(Material m) => m != null && AssetDatabase.GetAssetPath(m) == ClinicRetroLook.FloorSourcePath;
                var retroShellSources = state.materials
                    .Where(p => (IsWall(p.original) || IsFloor(p.original)) && !ClinicRetroLook.IsShellMaterial(p.styled))
                    .Select(p => p.styled).ToArray();
                Assert.AreEqual(2, retroShellSources.Length, "one Retro variant each for M_UC_Wall and M_UC_Floor");
                foreach (var variant in retroShellSources) CollectionAssert.DoesNotContain(used, variant, variant.name + " should be fully rebound");
                // The same original now owns several styled partners: Retro variant + wall shell + ceiling shell.
                Assert.AreEqual(3, state.materials.Count(p => IsWall(p.original)), "M_UC_Wall pairs");
                Assert.AreEqual(2, state.materials.Count(p => IsFloor(p.original)), "M_UC_Floor pairs");
                int boundPairs = state.materials.Count;
                int boundSlots = state.shellSlots.Count;
                Assert.AreEqual(result.floorSlots + result.wallSlots + result.ceilingSlots, boundSlots);

                // Binding twice and re-applying the Retro look keep exactly the same bindings and bookkeeping.
                ClinicRetroLook.BindShell(scene, false);
                AssertBindings(bound, renderers, "rebind");
                Assert.AreEqual(boundPairs, ClinicRetroLook.FindState(scene).materials.Count, "rebind pairs");
                Assert.AreEqual(boundSlots, ClinicRetroLook.FindState(scene).shellSlots.Count, "rebind slots");
                ClinicRetroLook.Apply(scene);
                AssertBindings(bound, renderers, "re-apply");
                Assert.AreEqual(boundPairs, ClinicRetroLook.FindState(scene).materials.Count, "re-apply pairs");
                Assert.AreEqual(boundSlots, ClinicRetroLook.FindState(scene).shellSlots.Count, "re-apply slots");

                // Unbind -> exactly the Retro bindings.
                ClinicRetroLook.UnbindShell(scene);
                AssertBindings(retro, renderers, "unbind");
                Assert.AreEqual(retroPairs, ClinicRetroLook.FindState(scene).materials.Count, "unbind pairs");
                Assert.AreEqual(0, ClinicRetroLook.FindState(scene).shellSlots.Count, "unbind slots");

                // Bind again, then a full Restore -> exactly the original bindings.
                ClinicRetroLook.BindShell(scene, false);
                ClinicRetroLook.Restore(scene);
                Assert.IsNull(ClinicRetroLook.FindState(scene));
                AssertBindings(original, renderers, "restore");
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (setup.Any(s => s.isLoaded && !string.IsNullOrEmpty(s.path))) EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
        }

        static Renderer[] Renderers(Scene scene) =>
            scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).ToArray();

        static Material[][] Snapshot(Renderer[] renderers) => renderers.Select(r => r.sharedMaterials.ToArray()).ToArray();

        static void AssertBindings(Material[][] expected, Renderer[] renderers, string stage)
        {
            Assert.AreEqual(expected.Length, renderers.Length, stage);
            for (int i = 0; i < renderers.Length; i++)
                CollectionAssert.AreEqual(expected[i], renderers[i].sharedMaterials, stage + ": " + renderers[i].name);
        }
    }
}
