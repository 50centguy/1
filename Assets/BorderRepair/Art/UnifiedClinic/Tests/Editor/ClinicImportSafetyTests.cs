using System;
using System.IO;
using System.Linq;
using BorderRepair.Art.UnifiedClinic.EditorTools;
using BorderRepair.TwoNight.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BorderRepair.Art.UnifiedClinic.Tests
{
    public class ClinicImportSafetyTests
    {
        [Test]
        public void CollisionProxies_PreserveDiagonalWallAndSouthDoorOpening()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ClinicArtImporter.PrefabPath);
            Assert.IsNotNull(prefab);
            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var colliders = instance.GetComponentsInChildren<MeshCollider>(true);
                Assert.AreEqual(23, colliders.Length);
                Assert.IsEmpty(instance.GetComponentsInChildren<BoxCollider>(true), "AABBs do not preserve baked wall rotations.");
                Physics.SyncTransforms();
                var nw = Array.Find(colliders, c => c.name == "COL_Wall_NW");
                var south = Array.Find(colliders, c => c.name == "COL_Wall_S");
                Assert.IsNotNull(nw);
                Assert.IsNotNull(south);
                Assert.IsFalse(nw.Raycast(new Ray(new Vector3(-2.8f, 1, .6f), Vector3.forward), out _, 1),
                    "A room-interior route must not hit a diagonal wall's enclosing rectangle.");
                Assert.IsTrue(nw.Raycast(new Ray(new Vector3(-1.8f, 1, 1.8f), new Vector3(-1, 0, 1).normalized), out var wallHit, 2));
                Assert.That(wallHit.distance, Is.InRange(.84f, .88f));
                Assert.IsFalse(south.Raycast(new Ray(new Vector3(0, 1, -3), Vector3.back), out _, 1),
                    "The wall proxy must keep the authored door opening, separate from the door leaf.");
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        [Test]
        public void FailedClutterPreflight_PreservesLastPublishedSceneAndReport()
        {
            const string stats = "ArtSource/UnifiedClinicClutter/stats.json";
            const string report = "Docs/Integration/UnifiedClinic/gameplay_layout.json";
            Assert.IsTrue(File.Exists(UnifiedClinicBuilder.ClinicPath));
            Assert.IsTrue(File.Exists(stats));
            var sceneBytes = File.ReadAllBytes(UnifiedClinicBuilder.ClinicPath);
            var reportBytes = File.Exists(report) ? File.ReadAllBytes(report) : null;
            var sourceBytes = File.ReadAllBytes(stats);
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var buildSettings = EditorBuildSettings.scenes;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                File.WriteAllText(stats, "{\"passed_all\":false,\"failures\":[\"intentional import safety test\"]}");
                var exception = Assert.Throws<InvalidOperationException>(() => ClinicGameplayIntegrator.Build());
                StringAssert.Contains("Clutter source checks failed", exception.Message);
                CollectionAssert.AreEqual(sceneBytes, File.ReadAllBytes(UnifiedClinicBuilder.ClinicPath));
                if (reportBytes == null) Assert.IsFalse(File.Exists(report));
                else CollectionAssert.AreEqual(reportBytes, File.ReadAllBytes(report));
            }
            finally
            {
                File.WriteAllBytes(stats, sourceBytes);
                EditorBuildSettings.scenes = buildSettings;
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                if (Array.Exists(setup, s => s.isLoaded && s.isActive && !string.IsNullOrEmpty(s.path)))
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            CollectionAssert.AreEqual(sourceBytes, File.ReadAllBytes(stats));
        }

        [Test]
        public void DirtyAdditiveScene_RejectsBuildWithoutUnloadingEdits()
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var clean = EditorSceneManager.OpenScene(UnifiedClinicBuilder.ClinicPath, OpenSceneMode.Single);
            var dirty = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var marker = new GameObject("UnsavedAdditiveEdit");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(marker, dirty);
            EditorSceneManager.MarkSceneDirty(dirty);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(clean);
            try
            {
                var exception = Assert.Throws<InvalidOperationException>(() => ClinicGameplayIntegrator.Build());
                StringAssert.Contains("Save open scene edits", exception.Message);
                Assert.IsTrue(dirty.isLoaded && dirty.isDirty);
                Assert.IsNotNull(marker);
                Assert.AreEqual(clean, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            }
            finally
            {
                if (setup.Any(s => s.isLoaded && s.isActive && !string.IsNullOrEmpty(s.path)))
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailureAfterImports_RestoresPublishedResourcesAndSceneBytes(bool withoutClutterFolder)
        {
            const string anchors = "ArtSource/UnifiedClinic/anchors.json";
            const string probeTexture = "ArtSource/UnifiedClinic/Textures/T_UC_RollbackProbe.png";
            const string clutterRoot = ClinicArtImporter.Root + "/Clutter";
            var source = File.ReadAllBytes(anchors);
            var setup = EditorSceneManager.GetSceneManagerSetup();
            bool createdProbe = false, movedClutter = false;
            string backup = "Library/ClinicRollback_" + Guid.NewGuid().ToString("N");
            string projectRoot = Path.GetFullPath(".") + Path.DirectorySeparatorChar;
            Assert.IsTrue(Path.GetFullPath(clutterRoot).StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(Path.GetFullPath(backup).StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase));
            var clutterMeta = File.ReadAllBytes(clutterRoot + ".meta");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string materialPath = Directory.GetFiles(ClinicArtImporter.Root + "/Materials", "*.mat").First();
            var originalMaterial = File.ReadAllBytes(materialPath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            material.SetColor("_BaseColor", Color.magenta);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            if (withoutClutterFolder)
            {
                Directory.Move(clutterRoot, backup);
                movedClutter = true;
                File.Delete(clutterRoot + ".meta");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            var directories = new[] { "Models", "Textures", "Materials", "Prefabs", "Clutter" }
                .Select(p => ClinicArtImporter.Root + "/" + p).Append(UnifiedClinicBuilder.SceneDirectory);
            var files = directories.Where(Directory.Exists).SelectMany(p => Directory.GetFiles(p, "*", SearchOption.AllDirectories))
                .Append("Docs/Integration/UnifiedClinic/ArtImport/clutter_import.json")
                .Append("Docs/Integration/UnifiedClinic/gameplay_layout.json").ToDictionary(p => p, File.ReadAllBytes);
            try
            {
                var data = JsonUtility.FromJson<ClinicArtImporter.AnchorFile>(File.ReadAllText(anchors));
                data.anchors.Single(a => a.name == "Dock_Root").name = "IntentionalMissingDockForRollbackTest";
                File.WriteAllText(anchors, JsonUtility.ToJson(data));
                Assert.IsFalse(File.Exists(probeTexture));
                File.Copy(Directory.GetFiles("ArtSource/UnifiedClinic/Textures", "*.png").First(), probeTexture);
                createdProbe = true;
                Assert.Throws<InvalidOperationException>(() => ClinicGameplayIntegrator.Build());
                foreach (var file in files)
                    CollectionAssert.AreEqual(file.Value, File.ReadAllBytes(file.Key), file.Key);
                CollectionAssert.AreEquivalent(files.Keys.Where(p => p.StartsWith("Assets/")),
                    directories.Where(Directory.Exists).SelectMany(p => Directory.GetFiles(p, "*", SearchOption.AllDirectories)));
                if (withoutClutterFolder)
                {
                    Assert.IsFalse(Directory.Exists(clutterRoot), "Refresh must not recreate metas for newly added empty folders.");
                    Assert.IsFalse(File.Exists(clutterRoot + ".meta"));
                }
            }
            finally
            {
                File.WriteAllBytes(anchors, source);
                if (createdProbe && File.Exists(probeTexture)) File.Delete(probeTexture);
                File.WriteAllBytes(materialPath, originalMaterial);
                if (movedClutter)
                {
                    Directory.Move(backup, clutterRoot);
                    File.WriteAllBytes(clutterRoot + ".meta", clutterMeta);
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                if (setup.Any(s => s.isLoaded && s.isActive && !string.IsNullOrEmpty(s.path)))
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }
    }
}
