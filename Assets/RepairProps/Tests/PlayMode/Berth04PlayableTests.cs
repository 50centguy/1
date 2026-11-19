using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using BorderRepair.FirstOrder;
using BorderRepair.TwoNight;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.Tests
{
    // Shared by the joined-scene tests and the baselines: viewpoints, timing, frames.
    static class Berth04Measure
    {
        public static string OutDir => Path.Combine(Environment.GetEnvironmentVariable("EXTERIOR_OUT") ?? Path.GetTempPath(), "berth04_playable");

        // Player standpoints in the clinic frame: feet, yaw, pitch. The first five are in or at the door of the room.
        public static readonly (string name, Vector3 feet, float yaw, float pitch)[] Standpoints =
        {
            ("Player - room, at the counter", new Vector3(-.6f, .05f, -.4f), 90, 8),
            ("Player - room, at the bench", new Vector3(0, .05f, .9f), 0, 12),
            ("Player - room, toward UNIT 07", new Vector3(0, .05f, 0), 270, 6),
            ("Player - room, toward the door", new Vector3(0, .05f, 1.8f), 180, 3),
            ("Player - in the doorway, up the stairs", new Vector3(0, .05f, -3.0f), 180, -18),
            ("Player - landing, up to the street", new Vector3(.05f, .05f, -5.0f), 180, -22),
            ("Player - stair top, down to the door", new Vector3(.05f, 1.41f, -8.6f), 0, 22),
            ("Player - street, the shop front", new Vector3(.05f, 1.25f, -12.5f), 0, 5),
        };
        public const int RoomStandpoints = 5;

        public static IEnumerator Until(Func<bool> condition, float seconds, string what)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > until) Assert.Fail("Timed out: " + what);
                yield return null;
            }
        }

        /// <summary>Median of 15 renders at 1920x1080, each waited out on the GPU.</summary>
        public static float Time1080(Camera c, Action before = null)
        {
            var rt = RenderTexture.GetTemporary(1920, 1080, 24, RenderTextureFormat.ARGB32);
            var was = c.targetTexture;
            c.targetTexture = rt; c.aspect = 16f / 9;
            before?.Invoke();
            for (int i = 0; i < 3; i++) Sync(c, rt);
            var ms = new List<double>();
            for (int i = 0; i < 15; i++)
            {
                var w = Stopwatch.StartNew();
                Sync(c, rt);
                ms.Add(w.Elapsed.TotalMilliseconds);
            }
            c.targetTexture = was; c.ResetAspect();
            RenderTexture.ReleaseTemporary(rt);
            ms.Sort();
            return (float)ms[ms.Count / 2];
        }

        static void Sync(Camera c, RenderTexture rt)
        {
            c.Render();
            AsyncGPUReadback.Request(rt, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
        }

        public static void Frame(Camera c, string file, Action before = null)
        {
            var rt = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
            var was = c.targetTexture;
            c.targetTexture = rt; c.aspect = 16f / 9;
            before?.Invoke();
            c.Render();
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            RenderTexture.active = active;
            c.targetTexture = was; c.ResetAspect();
            RenderTexture.ReleaseTemporary(rt);
            Directory.CreateDirectory(OutDir);
            File.WriteAllBytes(Path.Combine(OutDir, file), tex.EncodeToJPG(90));
            Object.Destroy(tex);
        }

        public static string Safe(string s) => new string(s.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());

        /// <summary>Shaders compile synchronously while measuring (an editor's first frames otherwise draw stand-ins and
        /// then pay for compiling); returns the previous setting.</summary>
        public static bool SyncShaders()
        {
#if UNITY_EDITOR
            bool was = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            return was;
#else
            return false;
#endif
        }

        public static void RestoreShaders(bool was)
        {
#if UNITY_EDITOR
            ShaderUtil.allowAsyncCompilation = was;
#endif
        }

        public static void WarmUp(Camera c, Action before = null)
        {
            var rt = RenderTexture.GetTemporary(1920, 1080, 24, RenderTextureFormat.ARGB32);
            var was = c.targetTexture;
            c.targetTexture = rt; c.aspect = 16f / 9;
            before?.Invoke();
            for (int i = 0; i < 4; i++) Sync(c, rt);
            c.targetTexture = was; c.ResetAspect();
            RenderTexture.ReleaseTemporary(rt);
        }

        public static Camera MeasuringCamera()
        {
            var c = new GameObject("Measuring camera (test)").AddComponent<Camera>();
            c.enabled = false; c.nearClipPlane = .03f; c.farClipPlane = 300;
            return c;
        }

        public static void Append(string file, string text)
        {
            Directory.CreateDirectory(OutDir);
            File.AppendAllText(Path.Combine(OutDir, file), text);
        }
    }

    // The playable Berth 04 scene: the clinic inside the district (Berth04Playable / Berth04Join).
    public sealed class Berth04PlayableTests
    {
        TwoNightState previousRun;
        string previousSave;
        float previousTimeScale;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousRun = TwoNightRun.Current;
            previousSave = TwoNightSave.OverrideDirectory;
            previousTimeScale = Time.timeScale;
            TwoNightSave.OverrideDirectory = Path.Combine(Path.GetTempPath(), "Berth04Playable_" + Guid.NewGuid().ToString("N"));
            TwoNightRun.Clear();
            Time.timeScale = 1;
            Berth04Playable.StartTutorial = false;
#if UNITY_EDITOR
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/RepairProps/Scenes/Berth04_Playable.unity"), Is.Not.Null,
                "Build the playable scene first (Border Repair > Berth 04 > Build playable scene).");
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/RepairProps/Scenes/Berth04_Playable.unity", new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("Editor scenes only; the playable scenes are not in the build list.");
            yield break;
#endif
            yield return Berth04Measure.Until(() => Berth04Playable.Current != null && Berth04Playable.Current.Ready, 120, "the clinic and the district load and join");
            yield return Berth04Measure.Until(() =>
            {
                var d = Object.FindFirstObjectByType<UnifiedClinicDirector>();
                return d != null && d.Initialized;
            }, 60, "the clinic director starts");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var cleanup = SceneManager.CreateScene("Berth04Cleanup_" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(cleanup);
            foreach (var path in new[] { Berth04Playable.DistrictScene, Berth04Playable.ClinicScene })
            {
                var s = SceneManager.GetSceneByPath(path);
                if (s.IsValid() && s.isLoaded) yield return SceneManager.UnloadSceneAsync(s);
            }
            if (Berth04Playable.Current != null) Object.Destroy(Berth04Playable.Current.gameObject);
            yield return null;
            Berth04Playable.StartTutorial = true;
            TwoNightRun.Set(previousRun);
            TwoNightSave.OverrideDirectory = previousSave;
            Time.timeScale = previousTimeScale;
        }

        [UnityTest]
        public IEnumerator Joined_DoorOpenAndLightingSwitchesByWhereTheEyeIs()
        {
            var join = Berth04Playable.Current.Join;
            Assert.That(join.Outdoor, Is.Not.Null, "the district copy carries its atmosphere");
            Assert.That(join.Walker, Is.Not.Null);
            Assert.That(GameObject.Find("ClinicClosedDoorCollision"), Is.Null, "the closed-door blocker is off");
            Assert.That(join.DistrictRendererCount, Is.GreaterThan(1000));
            join.Walker.PlaceAt(new Vector3(0, .05f, 0), 90);
            yield return null; yield return null;
            join.Evaluate(join.Eye);
            Assert.That(join.EyeInRoom, Is.True);
            Assert.That(join.OutdoorBlend, Is.EqualTo(0).Within(.01f));
            Assert.That(join.DoorInView, Is.False, "facing the counter, the door is behind");
            Assert.That(join.DistrictHiddenCount, Is.EqualTo(join.DistrictRendererCount), "nothing of the district is drawn from the room with the door out of view");
            join.Walker.PlaceAt(new Vector3(.05f, 1.41f, -8.6f), 180);
            yield return null; yield return null;
            join.Evaluate(join.Eye);
            Assert.That(join.EyeInRoom, Is.False);
            Assert.That(join.OutdoorBlend, Is.EqualTo(1).Within(.01f));
            Assert.That(RenderSettings.sun, Is.SameAs(join.Outdoor.sun), "the overcast sun is the main light outside");
            Assert.That(join.ClinicHiddenCount, Is.GreaterThan(0), "facing the street, the clinic is not drawn");
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator Player_WalksOutOfTheClinicUpToTheStreetAndBack()
        {
            var join = Berth04Playable.Current.Join;
            var walker = join.Walker;
            var cc = walker.GetComponent<CharacterController>();
            // From where the player starts, just inside the door.
            walker.PlaceAt(new Vector3(0, .05f, -2.25f), 180);
            yield return null;
            var log = new StringBuilder("walk (clinic frame):\n");
            var outward = new[]
            {
                (0f, -2.7f, "room, before the door"), (0f, -4.4f, "through the door"), (.05f, -5.2f, "landing"),
                (.05f, -8.4f, "top of the stairs"), (.05f, -9.9f, "sidewalk"), (.05f, -11.6f, "street"), (-12f, -11.6f, "down the street"),
            };
            foreach (var (x, z, label) in outward) yield return WalkTo(cc, walker, new Vector3(x, 0, z), label, log);
            Assert.That(walker.transform.position.y, Is.GreaterThan(1f), "at street level");
            join.Evaluate(join.Eye);
            Assert.That(join.OutdoorBlend, Is.EqualTo(1).Within(.01f));
            var back = new[]
            {
                (.05f, -11.6f, "back to the stairhead"), (.05f, -9.9f, "sidewalk"), (.05f, -8.4f, "top of the stairs"),
                (.05f, -5.2f, "landing"), (0f, -4.4f, "door"), (0f, -2.25f, "room"),
            };
            foreach (var (x, z, label) in back) yield return WalkTo(cc, walker, new Vector3(x, 0, z), label, log);
            Assert.That(walker.transform.position.y, Is.LessThan(.3f), "back on the clinic floor");
            join.Evaluate(join.Eye);
            Assert.That(join.EyeInRoom, Is.True);
            Berth04Measure.Append("walk.txt", log.ToString());
        }

        static IEnumerator WalkTo(CharacterController cc, FirstPersonWalker walker, Vector3 target, string label, StringBuilder log)
        {
            float until = Time.realtimeSinceStartup + 30;
            float best = float.MaxValue, bestAt = Time.realtimeSinceStartup;
            while (true)
            {
                var p = walker.transform.position;
                var d = new Vector3(target.x - p.x, 0, target.z - p.z);
                if (d.magnitude < .15f) break;
                if (d.magnitude < best - .02f) { best = d.magnitude; bestAt = Time.realtimeSinceStartup; }
                if (Time.realtimeSinceStartup - bestAt > 4 || Time.realtimeSinceStartup > until)
                    Assert.Fail("Stuck on the way to " + label + " at " + p.ToString("F2") + "\n" + log);
                float dt = Mathf.Clamp(Time.deltaTime, .005f, .05f);
                cc.Move(d.normalized * Mathf.Min(2f * dt, d.magnitude) + Vector3.down * .3f * dt);
                yield return null;
            }
            log.Append("  ").Append(label).Append(": ").Append(walker.transform.position.ToString("F2")).Append('\n');
        }

        [UnityTest]
        public IEnumerator Counter_ReceivesAnOrderInTheJoinedScene()
        {
            var clinic = Object.FindFirstObjectByType<UnifiedClinicDirector>();
            Assert.That(clinic.Station.Session.CaseCount, Is.EqualTo(4));
            string sku = RepairPropSupplyPolicy.SkuFor(RepairPropKind.Receiver);
            Assert.That(ClinicSupplies.Purchase(clinic.State, new Dictionary<string, int> { { sku, 1 } }, Guid.NewGuid().ToString("N"), out string reason), Is.True, reason);
            Assert.That(clinic.HasPendingCase, Is.True);
            Assert.That(clinic.Interact(ClinicTradeAction.Receive), Is.True, clinic.LastMessage);
            Assert.That(clinic.Station.Inspector.CurrentItem, Is.Not.Null, "the received unit is in the clinic");
            yield return null;
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator Cost_JoinedScene_DistrictViewsAndPlayerStandpoints()
        {
            var join = Berth04Playable.Current.Join;
            // Let particles and the runtimes settle.
            for (int i = 0; i < 30; i++) yield return null;
            var sb = new StringBuilder("== joined scene (ms = median of 15 renders at 1920x1080, GPU waited; culling on / off)\n");
            bool async = Berth04Measure.SyncShaders();
            var m = Berth04Measure.MeasuringCamera();
            foreach (var v in join.Outdoor.reviewViews)
                foreach (bool culling in new[] { false, true })
                {
                    m.transform.SetPositionAndRotation(v.position, v.rotation);
                    m.fieldOfView = v.fov;
                    join.Culling = culling;
                    Berth04Measure.WarmUp(m, () => join.Evaluate(m));
                }
            foreach (var s in Berth04Measure.Standpoints)
            {
                join.Walker.PlaceAt(s.feet, s.yaw, s.pitch);
                yield return null;
                join.Culling = false; Berth04Measure.WarmUp(join.Eye);
                join.Culling = true; Berth04Measure.WarmUp(join.Eye);
            }
            var on = new List<float>();
            foreach (var v in join.Outdoor.reviewViews)
            {
                m.transform.SetPositionAndRotation(v.position, v.rotation);
                m.fieldOfView = v.fov;
                join.Culling = false;
                float off = Berth04Measure.Time1080(m, () => join.Evaluate(m));
                join.Culling = true;
                float t = Berth04Measure.Time1080(m, () => join.Evaluate(m));
                on.Add(t);
                sb.AppendFormat("  district view {0,-46} {1,6:F1} / {2,6:F1} ms   hidden: district {3}, clinic {4}\n", v.name, t, off, join.DistrictHiddenCount, join.ClinicHiddenCount);
                Berth04Measure.Frame(m, "joined - " + Berth04Measure.Safe(v.name) + ".jpg", () => join.Evaluate(m));
            }
            Object.Destroy(m.gameObject);
            sb.AppendFormat("  district views: median {0:F1} ms, worst {1:F1} ms\n", on.OrderBy(x => x).ElementAt(on.Count / 2), on.Max());
            var eye = join.Eye;
            var player = new List<float>();
            foreach (var s in Berth04Measure.Standpoints)
            {
                join.Walker.PlaceAt(s.feet, s.yaw, s.pitch);
                yield return null; yield return null;
                join.Culling = false;
                float off = Berth04Measure.Time1080(eye);
                join.Culling = true;
                float t = Berth04Measure.Time1080(eye);
                player.Add(t);
                // The join's own work per frame (lighting blend, portal test of every renderer), on the main thread.
                var w = Stopwatch.StartNew();
                for (int i = 0; i < 50; i++) join.Evaluate(eye);
                double evalMs = w.Elapsed.TotalMilliseconds / 50;
                sb.AppendFormat("  {0,-52} {1,6:F1} / {2,6:F1} ms   join {6:F2} ms   hidden: district {3}, clinic {4}, outdoor {5:F2}\n", s.name, t, off, join.DistrictHiddenCount, join.ClinicHiddenCount, join.OutdoorBlend, evalMs);
                Berth04Measure.Frame(eye, "joined - " + Berth04Measure.Safe(s.name) + ".jpg");
            }
            sb.AppendFormat("  player standpoints: median {0:F1} ms, worst {1:F1} ms\n", player.OrderBy(x => x).ElementAt(player.Count / 2), player.Max());
            Berth04Measure.RestoreShaders(async);
            Berth04Measure.Append("cost.txt", sb.ToString());
            TestContext.WriteLine(sb.ToString());
        }
    }

    // The same measurements on the scenes alone: the district (its review views, the measuring camera) and the clinic
    // (the player's camera at the room standpoints).
    public sealed class Berth04BaselineCostTests
    {
        Scene loaded;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (loaded.IsValid() && loaded.isLoaded)
            {
                var cleanup = SceneManager.CreateScene("Berth04BaselineCleanup_" + Guid.NewGuid().ToString("N"));
                SceneManager.SetActiveScene(cleanup);
                yield return SceneManager.UnloadSceneAsync(loaded);
            }
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator Cost_DistrictAlone()
        {
#if UNITY_EDITOR
            const string path = "Assets/RepairProps/Scenes/Temp_Berth04District.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
            loaded = SceneManager.GetSceneByPath(path);
            for (int i = 0; i < 30; i++) yield return null;
            var cams = loaded.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true)).OrderBy(c => c.name).ToList();
            var m = Berth04Measure.MeasuringCamera();
            bool async = Berth04Measure.SyncShaders();
            foreach (var c in cams)
            {
                m.transform.SetPositionAndRotation(c.transform.position, c.transform.rotation);
                m.fieldOfView = c.fieldOfView;
                Berth04Measure.WarmUp(m);
            }
            var sb = new StringBuilder("== district alone (same measuring camera at its review views)\n");
            var times = new List<float>();
            foreach (var c in cams)
            {
                m.transform.SetPositionAndRotation(c.transform.position, c.transform.rotation);
                m.fieldOfView = c.fieldOfView;
                float t = Berth04Measure.Time1080(m);
                times.Add(t);
                sb.AppendFormat("  district view {0,-46} {1,6:F1} ms\n", c.name, t);
            }
            Object.Destroy(m.gameObject);
            sb.AppendFormat("  district views: median {0:F1} ms, worst {1:F1} ms\n", times.OrderBy(x => x).ElementAt(times.Count / 2), times.Max());
            Berth04Measure.RestoreShaders(async);
            Berth04Measure.Append("cost.txt", sb.ToString());
            TestContext.WriteLine(sb.ToString());
#else
            Assert.Ignore("Editor scenes only.");
            yield break;
#endif
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator Cost_ClinicAlone()
        {
#if UNITY_EDITOR
            const string path = "Assets/RepairProps/Scenes/RepairProps_Clinic.unity";
            var previousRun = TwoNightRun.Current;
            var previousSave = TwoNightSave.OverrideDirectory;
            TwoNightSave.OverrideDirectory = Path.Combine(Path.GetTempPath(), "Berth04Baseline_" + Guid.NewGuid().ToString("N"));
            TwoNightRun.Clear();
            try
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
                loaded = SceneManager.GetSceneByPath(path);
                yield return Berth04Measure.Until(() =>
                {
                    var d = Object.FindFirstObjectByType<UnifiedClinicDirector>();
                    return d != null && d.Initialized;
                }, 60, "the clinic director starts");
                for (int i = 0; i < 30; i++) yield return null;
                var rig = Object.FindFirstObjectByType<FirstOrderCameraRig>();
                bool async = Berth04Measure.SyncShaders();
                for (int i = 0; i < Berth04Measure.RoomStandpoints; i++)
                {
                    var s = Berth04Measure.Standpoints[i];
                    rig.Walker.PlaceAt(s.feet, s.yaw, s.pitch);
                    yield return null;
                    Berth04Measure.WarmUp(rig.Cam);
                }
                var sb = new StringBuilder("== clinic alone (the player's camera)\n");
                var times = new List<float>();
                for (int i = 0; i < Berth04Measure.RoomStandpoints; i++)
                {
                    var s = Berth04Measure.Standpoints[i];
                    rig.Walker.PlaceAt(s.feet, s.yaw, s.pitch);
                    yield return null; yield return null;
                    float t = Berth04Measure.Time1080(rig.Cam);
                    times.Add(t);
                    sb.AppendFormat("  {0,-52} {1,6:F1} ms\n", s.name, t);
                    Berth04Measure.Frame(rig.Cam, "clinic alone - " + Berth04Measure.Safe(s.name) + ".jpg");
                }
                sb.AppendFormat("  room standpoints: median {0:F1} ms, worst {1:F1} ms\n", times.OrderBy(x => x).ElementAt(times.Count / 2), times.Max());
                Berth04Measure.RestoreShaders(async);
                Berth04Measure.Append("cost.txt", sb.ToString());
                TestContext.WriteLine(sb.ToString());
            }
            finally
            {
                TwoNightRun.Set(previousRun);
                TwoNightSave.OverrideDirectory = previousSave;
            }
#else
            Assert.Ignore("Editor scenes only.");
            yield break;
#endif
        }
    }
}
