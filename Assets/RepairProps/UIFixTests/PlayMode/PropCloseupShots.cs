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
    // Close-up review shots of a repair prop as the game renders it (backface culling, clinic lights plus a key light),
    // from eight directions round it, for spotting holes and broken faces in the mesh.
    public sealed class PropCloseupShots
    {
        const string ScenePath = "Assets/RepairProps/Scenes/RepairProps_Clinic.unity";
        static string Folder => Environment.GetEnvironmentVariable("BORDER_REPAIR_CYBER_SHOTS");
        Scene loaded;
        GameObject prop, cameraObject;
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
            if (prop != null) Object.Destroy(prop);
            if (cameraObject != null) Object.Destroy(cameraObject);
            if (loaded.IsValid() && loaded.isLoaded)
            {
                SceneManager.SetActiveScene(SceneManager.CreateScene("CloseupCleanup_" + Guid.NewGuid().ToString("N")));
                yield return SceneManager.UnloadSceneAsync(loaded);
            }
#if UNITY_EDITOR
            gameView?.Dispose(); gameView = null;
#endif
        }

        [UnityTest]
        public IEnumerator Cartridge_EightViews() { yield return Views("Cartridge"); }

        IEnumerator Views(string kind)
        {
#if UNITY_EDITOR
            gameView = new UiTestGameView(); gameView.Set(1280, 720);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RepairProps/Prefabs/" + kind + ".prefab");
#else
            GameObject prefab = null;
#endif
            const float scale = 4;
            var spot = new Vector3(.55f, 1.45f, 2.75f);   // held clear of the bench so the views from below see the underside
            prop = Object.Instantiate(prefab, spot, Quaternion.identity);
            prop.transform.localScale = Vector3.one * scale;
            yield return null;
            var bounds = prop.GetComponentsInChildren<Renderer>().Where(r => r.enabled).Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
            var centre = bounds.center;
            float distance = bounds.extents.magnitude * 3.2f;
            cameraObject = new GameObject("CloseupCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.depth = 100; camera.nearClipPlane = .01f; camera.fieldOfView = 30;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(1f, 0f, 1f);
            var key = new GameObject("CloseupKey").AddComponent<Light>();
            key.transform.SetParent(cameraObject.transform, false);
            key.type = LightType.Spot; key.range = 4; key.spotAngle = 60; key.intensity = 4;
            key.transform.localPosition = new Vector3(.25f, .2f, 0);
            for (int i = 0; i < 8; i++)
            {
                // Four round the sides from above, four from below.
                float yaw = i % 4 * 90f + 45f, pitch = i < 4 ? 35f : -35f;
                camera.transform.position = centre + Quaternion.Euler(pitch, yaw, 0) * Vector3.back * distance;
                camera.transform.LookAt(centre);
                key.transform.LookAt(centre);
                for (int f = 0; f < 3; f++) yield return null;
                if (string.IsNullOrEmpty(Folder)) continue;
                Directory.CreateDirectory(Folder);
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(Folder, "closeup-" + kind + "-" + i + ".png"));
                yield return null;
            }
            Assert.That(prop.GetComponentsInChildren<MeshFilter>().All(m => m.sharedMesh != null), Is.True);
        }
    }
}
