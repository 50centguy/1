using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BorderRepair.TwoNight;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.UI.Tests
{
    // The surgery patient's whole way onto the V2 table and off again: walks in clear of the room's solid geometry,
    // sits on the edge, swings in, lies back following the table's back section (and follows it live when the pendant
    // flattens it), is secured once all three straps are buckled, and gets up and walks out when they are all opened.
    // With BORDER_REPAIR_CYBER_SHOTS set it writes a screenshot per step (surgery-NN-*.png) from a fixed observer.
    public sealed class SurgeryPatientFlowTests
    {
        const string ScenePath = "Assets/RepairProps/Scenes/RepairProps_Clinic.unity";
        static string Folder => Environment.GetEnvironmentVariable("BORDER_REPAIR_CYBER_SHOTS");
        TwoNightState previousRun;
        string previousSave, isolatedSave;
        Scene loaded;
        GameObject observer;
        int shot;
#if UNITY_EDITOR
        UiTestGameView gameView;
#endif

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousRun = TwoNightRun.Current; previousSave = TwoNightSave.OverrideDirectory;
            isolatedSave = Path.Combine(Path.GetTempPath(), "SurgeryPatient_" + Guid.NewGuid().ToString("N"));
            TwoNightSave.OverrideDirectory = isolatedSave;
            TwoNightRun.Clear();
            Time.timeScale = 1;
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("Requires the authored editor scene.");
            yield break;
#endif
            loaded = SceneManager.GetSceneByPath(ScenePath);
            UnifiedClinicDirector clinic = null;
            yield return Until(() => (clinic = Object.FindFirstObjectByType<UnifiedClinicDirector>()) != null && clinic.Initialized, "Clinic must initialize.");
            yield return Until(() => SurgeryTableV2Runtime.Current != null && SurgeryTableV2Runtime.Current.Table != null && SurgeryPatientRuntime.Current != null, "Table and patient runtime exist.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (observer != null) Object.Destroy(observer);
            if (loaded.IsValid() && loaded.isLoaded)
            {
                SceneManager.SetActiveScene(SceneManager.CreateScene("SurgeryCleanup_" + Guid.NewGuid().ToString("N")));
                yield return SceneManager.UnloadSceneAsync(loaded);
            }
#if UNITY_EDITOR
            gameView?.Dispose(); gameView = null;
#endif
            TwoNightRun.Set(previousRun); TwoNightSave.OverrideDirectory = previousSave;
            if (Directory.Exists(isolatedSave)) Directory.Delete(isolatedSave, true);
        }

        [UnityTest]
        public IEnumerator Patient_GetsOnTheTable_IsStrappedIn_AndLeavesAgain()
        {
#if UNITY_EDITOR
            gameView = new UiTestGameView(); gameView.Set(1280, 720);
#endif
            var table = SurgeryTableV2Runtime.Current;
            var flow = SurgeryPatientRuntime.Current;
            var controls = table.Controls.ToList();
            var pendant = controls.First(c => c.Kind == SurgeryControlKind.Pendant);
            var straps = controls.Where(c => c.Kind == SurgeryControlKind.Strap).ToList();
            Assert.That(straps.Count, Is.EqualTo(3));
            // The table waits in chair pose, as the player would set it before calling the patient in.
            table.Activate(pendant);
            yield return Until(() => table.PoseAmount > .98f, "Chair pose reached.");

            var phases = new List<SurgeryPatientRuntime.Phase>();
            flow.PhaseChanged += p => phases.Add(p);
            Observer(table);
            Assert.That(flow.Begin(), Is.True);
            Assert.That(flow.Begin(), Is.False, "Only one patient at a time.");
            var hips = flow.BoneTransform(SurgeryPatientRuntime.Bone.Hips);
            var head = flow.BoneTransform(SurgeryPatientRuntime.Bone.Head);
            Assert.That(flow.Patient.GetComponentsInChildren<Collider>(false), Is.Empty, "Visual only while walking: nothing for the crosshair or the player to hit.");
            Assert.That(flow.Site != null && !flow.Site.activeSelf, Is.True, "The surgery site only appears once he is secured.");

            // Walk in: sample the body against the room's solid colliders (the player's own capsule excepted).
            int blocked = 0; string blocker = null;
            float half = 0;
            while (flow.PhaseNow == SurgeryPatientRuntime.Phase.WalkIn)
            {
                var feet = new Vector3(hips.position.x, 0, hips.position.z);
                foreach (var c in Physics.OverlapCapsule(feet + Vector3.up * .25f, feet + Vector3.up * 1.6f, .17f, ~0, QueryTriggerInteraction.Ignore))
                    if (!(c is CharacterController) && c.GetComponentInParent<SurgeryPatientRuntime>() == null) { blocked++; blocker = c.name + " at " + feet.ToString("F2"); }
                half += Time.deltaTime;
                if (half > 1.6f) { half = 0; yield return Shoot("walk"); }
                yield return null;
            }
            Assert.That(blocked, Is.Zero, "The walk-in path hits solid geometry: " + blocker);

            // The table's restraints start buckled: he waits at the bedside, facing it, until they are all open.
            Assert.That(table.StrapsClosed, Is.EqualTo(3));
            Assert.That(flow.PhaseNow, Is.EqualTo(SurgeryPatientRuntime.Phase.WaitClear));
            Assert.That(flow.Hint, Does.Contain("解开"));
            yield return null;
            var hintLabel = Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsSortMode.None).FirstOrDefault(t => t.transform.root.name == "SurgeryPatientHint");
            Assert.That(hintLabel != null && hintLabel.isActiveAndEnabled && hintLabel.text == flow.Hint, Is.True, "The hint is on screen.");
            yield return Wait(1.2f);
            Assert.That(flow.PhaseNow, Is.EqualTo(SurgeryPatientRuntime.Phase.WaitClear), "Does not climb onto buckled straps.");
            yield return Shoot("wait-straps");
            foreach (var strap in straps) { table.Activate(strap); yield return Wait(.2f); }
            Assert.That(table.StrapsClosed, Is.Zero);

            foreach (var step in new[] { SurgeryPatientRuntime.Phase.TurnAround, SurgeryPatientRuntime.Phase.SitDown, SurgeryPatientRuntime.Phase.SwingIn, SurgeryPatientRuntime.Phase.LieBack })
            {
                yield return Until(() => flow.PhaseNow != step, step + " finishes.", 8);
                yield return Shoot(step.ToString());
            }
            yield return Until(() => flow.PhaseNow == SurgeryPatientRuntime.Phase.AwaitStraps, "Lying, waiting for the straps.");
            yield return Wait(.3f);
            yield return Shoot("lying-chair");

            // Lying where the pads are: hips on the seat, head up the raised back, heels short of the foot end.
            var local = new Func<Vector3, Vector3>(p => new Vector3(Vector3.Dot(p - table.Floor, table.Right), p.y, Vector3.Dot(p - table.Floor, table.HeadDir)));
            var h = local(hips.position);
            Assert.That(Mathf.Abs(h.x), Is.LessThan(.08f), "Centred across the table.");
            Assert.That(h.z, Is.InRange(-.15f, .15f), "Hips on the seat section.");
            Assert.That(h.y, Is.InRange(SurgeryTableV2Runtime.PadTop, SurgeryTableV2Runtime.PadTop + .2f), "Hips on the pads.");
            var foot = local(flow.BoneTransform(SurgeryPatientRuntime.Bone.LeftFoot).position);
            Assert.That(foot.z, Is.InRange(-.98f, -.6f), "Legs along the leg section, heels near its end.");
            // Head rests on the back section: just above its surface (a plane through the hinge at +0.11, tilted up).
            float AbovePad()
            {
                float a = table.BackAngle * Mathf.Deg2Rad;
                var hinge = table.Floor + table.HeadDir * .11f + Vector3.up * SurgeryTableV2Runtime.PadTop;
                var normal = Vector3.up * Mathf.Cos(a) - table.HeadDir * Mathf.Sin(a);
                return Vector3.Dot(head.position - hinge, normal);
            }
            Assert.That(head.position.y - hips.position.y, Is.GreaterThan(.18f), "Upper body raised with the chair-pose back.");
            Assert.That(AbovePad(), Is.InRange(0f, .2f), "Head on the raised back pad, not sunk into it or floating.");

            // The pendant flattens the table: the patient follows it.
            table.Activate(pendant);
            yield return Until(() => table.PoseAmount < .02f, "Flat pose reached.");
            yield return Wait(.2f);
            Assert.That(head.position.y - hips.position.y, Is.LessThan(.12f), "Lies flat with the table.");
            Assert.That(AbovePad(), Is.InRange(0f, .2f), "Head on the flat pad.");
            yield return Shoot("lying-flat");
            table.Activate(pendant);
            yield return Until(() => table.PoseAmount > .98f, "Back to chair pose.");

            // Buckle the restraints one by one.
            foreach (var strap in straps)
            {
                Assert.That(flow.PhaseNow, Is.EqualTo(SurgeryPatientRuntime.Phase.AwaitStraps));
                Assert.That(flow.Hint, Does.Contain("扣上"));
                table.Activate(strap);
                yield return Wait(.25f);
            }
            yield return Until(() => flow.PhaseNow == SurgeryPatientRuntime.Phase.Secured, "Secured once all three straps are buckled.");
            yield return Shoot("secured");

            // Open them again: he sits up, swings out, stands and walks out.
            foreach (var strap in straps) { table.Activate(strap); yield return Wait(.15f); }
            foreach (var step in new[] { SurgeryPatientRuntime.Phase.SitUp, SurgeryPatientRuntime.Phase.SwingOut, SurgeryPatientRuntime.Phase.StandUp })
            {
                yield return Until(() => flow.PhaseNow == step, step + " begins.", 6);
                yield return Until(() => flow.PhaseNow != step, step + " finishes.", 6);
                yield return Shoot(step.ToString());
            }
            yield return Until(() => flow.PhaseNow == SurgeryPatientRuntime.Phase.Idle, "Walks out and leaves.", 15);
            Assert.That(flow.Patient.activeSelf, Is.False);
            CollectionAssert.AreEqual(new[]
            {
                SurgeryPatientRuntime.Phase.WalkIn, SurgeryPatientRuntime.Phase.WaitClear, SurgeryPatientRuntime.Phase.TurnAround, SurgeryPatientRuntime.Phase.SitDown, SurgeryPatientRuntime.Phase.SwingIn,
                SurgeryPatientRuntime.Phase.LieBack, SurgeryPatientRuntime.Phase.AwaitStraps, SurgeryPatientRuntime.Phase.Secured, SurgeryPatientRuntime.Phase.SitUp,
                SurgeryPatientRuntime.Phase.SwingOut, SurgeryPatientRuntime.Phase.StandUp, SurgeryPatientRuntime.Phase.WalkOut, SurgeryPatientRuntime.Phase.Idle,
            }, phases);
            Debug.Log("[SurgeryPatient] phases: " + string.Join(" > ", phases));
        }

        void Observer(SurgeryTableV2Runtime table)
        {
            observer = new GameObject("SurgeryObserver");
            var cam = observer.AddComponent<Camera>();
            cam.depth = 100; cam.fieldOfView = 50; cam.nearClipPlane = .05f;
            // From the west-south corner, high: sees the bedside, the table and the entrance path.
            observer.transform.position = table.Floor - table.Right * 1.9f - table.HeadDir * 2.1f + Vector3.up * 2.2f;
            observer.transform.LookAt(table.Floor - table.Right * .25f - table.HeadDir * .6f + Vector3.up * .8f);
        }

        IEnumerator Shoot(string name)
        {
            yield return null;
            if (string.IsNullOrEmpty(Folder)) yield break;
            Directory.CreateDirectory(Folder);
            // HUD canvases off for a clean frame, back on afterwards.
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.enabled && c.isRootCanvas).ToList();
            foreach (var c in canvases) c.enabled = false;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(Folder, string.Format("surgery-{0:00}-{1}.png", shot++, name)));
            yield return null;
            foreach (var c in canvases) if (c != null) c.enabled = true;
        }

        static IEnumerator Wait(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }

        static IEnumerator Until(Func<bool> ready, string reason, float timeout = 20)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!ready() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(ready(), Is.True, reason);
        }
    }
}
