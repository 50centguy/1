using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.UI.Tests
{
    // The omamori's red cord is a soft rope on the V2 shell: it hangs under gravity from the crown
    // lugs, keeps its length, stays out of the charm's body and swings when the charm is turned.
    public sealed class OmamoriCordTests
    {
        const string ScenePath = "Assets/RepairProps/Scenes/RepairProps_Clinic.unity";
        static string Folder => Environment.GetEnvironmentVariable("BORDER_REPAIR_CYBER_SHOTS");
        Scene loaded;
        GameObject charm, cameraObject;
#if UNITY_EDITOR
        UiTestGameView gameView;
#endif

        [UnitySetUp]
        public IEnumerator SetUp()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("Requires the authored editor scene.");
            yield break;
#endif
            loaded = SceneManager.GetSceneByPath(ScenePath);
            for (int i = 0; i < 10; i++) yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (charm != null) Object.Destroy(charm);
            if (cameraObject != null) Object.Destroy(cameraObject);
            if (loaded.IsValid() && loaded.isLoaded)
            {
                SceneManager.SetActiveScene(SceneManager.CreateScene("CordCleanup_" + Guid.NewGuid().ToString("N")));
                yield return SceneManager.UnloadSceneAsync(loaded);
            }
#if UNITY_EDITOR
            gameView?.Dispose(); gameView = null;
#endif
        }

        [UnityTest]
        public IEnumerator Cord_HangsKeepsLengthAndSwings()
        {
#if UNITY_EDITOR
            gameView = new UiTestGameView(); gameView.Set(1280, 720);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RepairProps/Prefabs/Omamori.prefab");
#else
            GameObject prefab = null;
#endif
            // Upright above the bench, scaled up like the inspection view so the cord reads.
            var spot = new Vector3(.55f, 1.0f, 2.75f);
            const float scale = 3;
            var centre = spot + Vector3.up * .05f * scale;
            cameraObject = new GameObject("CordShotCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.depth = 100; camera.nearClipPlane = .01f; camera.fieldOfView = 30;
            camera.transform.position = centre + new Vector3(.32f, .30f, -.72f);
            camera.transform.LookAt(centre + Vector3.up * .03f);

            // The charm without its soft cord, for comparison.
            var reference = Object.Instantiate(prefab, spot, Quaternion.Euler(0, 200, 0));
            reference.transform.localScale = Vector3.one * scale;
            yield return null;
            Object.Destroy(reference.GetComponent<OmamoriCord>());
            yield return null;
            yield return Shoot("cord-before");
            Object.Destroy(reference);
            yield return null;

            charm = Object.Instantiate(prefab, spot, Quaternion.Euler(0, 200, 0));
            charm.transform.localScale = Vector3.one * scale;
            yield return null; yield return null;

            var cord = charm.GetComponent<OmamoriCord>();
            Assert.That(cord, Is.Not.Null, "The bridge adds the soft cord to the omamori.");
            Assert.That(cord.Ready, Is.True);
            var parts = charm.GetComponentsInChildren<Renderer>(true).Select(r => r.name).ToArray();
            Assert.That(parts, Does.Contain("OM_ext_Shell").And.Contain("OM_ext_Face").And.Contain("OM_ext_Screen"), "The V2 exterior is installed.");
            Assert.That(parts.Any(n => n.StartsWith("OM_ext_Seg_")), Is.False, "No scanned Tripo pieces remain.");
            yield return Shoot("cord-00-spawn");

            yield return Wait(2.5f);
            var lug = charm.transform.TransformPoint(new Vector3(-.0165f, .0945f, 0));
            Assert.That(cord.TailEnd.y, Is.LessThan(lug.y - .05f), "The tail hangs down from the lug.");
            yield return Shoot("cord-01-settled");

            // Turn the charm on its side quickly: the cord lags, then falls the new way.
            var before = cord.TailEnd;
            float t0 = Time.time;
            while (Time.time - t0 < .25f)
            {
                // Turned about its middle so it stays in frame.
                var rotation = Quaternion.Euler(0, 200, Mathf.Lerp(0, 75, (Time.time - t0) / .25f));
                charm.transform.SetPositionAndRotation(centre - rotation * Vector3.up * .05f * scale, rotation);
                yield return null;
            }
            yield return Shoot("cord-02-swing");
            Assert.That(Vector3.Distance(before, cord.TailEnd), Is.GreaterThan(.02f), "The tail moves with the charm.");
            yield return Wait(2.5f);
            yield return Shoot("cord-03-laid");
            var low = charm.transform.TransformPoint(new Vector3(-.0165f, .0945f, 0));
            Assert.That(cord.TailEnd.y, Is.LessThan(low.y), "Still hanging under gravity after the turn.");
        }

        // Close, raking key light on the shell so lumps in the surface show (before/after the smoothing pass).
        [UnityTest]
        public IEnumerator Surface_RakingLightShots()
        {
#if UNITY_EDITOR
            gameView = new UiTestGameView(); gameView.Set(1280, 720);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RepairProps/Prefabs/Omamori.prefab");
#else
            GameObject prefab = null;
#endif
            var spot = new Vector3(.55f, 1.0f, 2.75f);
            const float scale = 3;
            var centre = spot + Vector3.up * .05f * scale;
            charm = Object.Instantiate(prefab, spot, Quaternion.Euler(0, 180, 0));
            charm.transform.localScale = Vector3.one * scale;
            cameraObject = new GameObject("SurfaceShotCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.depth = 100; camera.nearClipPlane = .01f; camera.fieldOfView = 26;
            var key = new GameObject("SurfaceKey").AddComponent<Light>();
            key.transform.SetParent(cameraObject.transform, false);
            key.type = LightType.Spot; key.range = 3; key.spotAngle = 50; key.intensity = 6; key.color = new Color(1f, .95f, .88f);
            key.transform.localPosition = new Vector3(.35f, .25f, 0);
            var views = new[] { new Vector3(0, .02f, -.62f), new Vector3(.42f, .18f, -.46f), new Vector3(-.10f, .05f, .62f) };
            for (int i = 0; i < views.Length; i++)
            {
                camera.transform.position = centre + views[i];
                camera.transform.LookAt(centre);
                key.transform.LookAt(centre);
                for (int f = 0; f < 3; f++) yield return null;
                yield return Shoot("surface-" + i);
            }
            Assert.That(charm.GetComponentsInChildren<MeshFilter>().All(m => m.sharedMesh != null), Is.True);
        }

        static IEnumerator Wait(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }

        static IEnumerator Shoot(string name)
        {
            yield return null;
            if (string.IsNullOrEmpty(Folder)) yield break;
            Directory.CreateDirectory(Folder);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(Folder, name + ".png"));
            yield return null;
        }
    }
}
