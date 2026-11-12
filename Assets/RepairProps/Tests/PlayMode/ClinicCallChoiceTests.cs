using System.Collections;
using System.Linq;
using BorderRepair.RepairProps;
using BorderRepair.TwoNight;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using static BorderRepair.RepairProps.RepairPropStoryText;

namespace BorderRepair.RepairProps.Tests
{
    // A call with replies on the real call card: the replies appear under the card, the call waits for a pick instead
    // of running on, the pick is said as the player's line, its flag is set, and the call follows the chosen branch.
    public sealed class ClinicCallChoiceTests
    {
        const string ScenePath = "Assets/RepairProps/Scenes/RepairProps_Clinic.unity";
        TwoNightState previousRun;
        string previousSaveDirectory;
        Scene loadedScene;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousRun = TwoNightRun.Current;
            previousSaveDirectory = TwoNightSave.OverrideDirectory;
            TwoNightSave.OverrideDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ClinicCallChoice_" + System.Guid.NewGuid().ToString("N"));
            TwoNightRun.Clear();
            // Only the test call exists, so none of the night's calls can ring in the middle of it.
            UseCallsForTests(new CallFile { calls = new[] { new Call
            {
                id = "test-choice", caller = "Kasumi", name = "霞", title = "测试",
                lines = new[]
                {
                    new CallLine { text = "问你一件事。", choices = new[]
                    {
                        new Choice { text = "好。", next = "yes", set = "said-yes" },
                        new Choice { text = "不行。", next = "no" },
                        new Choice { text = "不该出现", when = "flag:never-set" },
                    } },
                    new CallLine { id = "yes", text = "那就好。", next = "end" },
                    new CallLine { id = "no", text = "随你。" },
                },
            } } });
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("Requires the existing editor scene.");
            yield break;
#endif
            loadedScene = SceneManager.GetSceneByPath(ScenePath);
            float deadline = Time.realtimeSinceStartup + 20f;
            UnifiedClinicDirector clinic = null;
            while (clinic == null || !clinic.Initialized || ClinicCallRuntime.Current == null)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The clinic never finished starting.");
                clinic = Object.FindFirstObjectByType<UnifiedClinicDirector>();
                yield return null;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Reload();
            yield return null;
            if (loadedScene.IsValid() && loadedScene.isLoaded)
            {
                var cleanup = SceneManager.CreateScene("ClinicCallChoiceCleanup_" + System.Guid.NewGuid().ToString("N"));
                SceneManager.SetActiveScene(cleanup);
                yield return SceneManager.UnloadSceneAsync(loadedScene);
            }
            TwoNightRun.Set(previousRun);
            TwoNightSave.OverrideDirectory = previousSaveDirectory;
        }

        [UnityTest]
        public IEnumerator RepliesWaitForAPick_AndTheCallFollowsIt()
        {
            var calls = ClinicCallRuntime.Current;
            Assert.That(calls.Begin("test-choice", true), Is.True);
            calls.Pickup();
            yield return null;

            Assert.That(calls.Choosing, Is.True, "The first line offers replies.");
            CollectionAssert.AreEqual(new[] { "好。", "不行。" }, calls.ChoiceTexts.ToArray(), "A reply whose condition fails stays hidden.");
            var panel = calls.Card.Find("Choices");
            Assert.That(panel != null && panel.gameObject.activeSelf, Is.True, "The replies show under the card.");
            Assert.That(panel.Cast<Transform>().Count(t => t.gameObject.activeSelf), Is.EqualTo(2));

            yield return new WaitForSecondsRealtime(3f);   // longer than the line's hold time
            Assert.That(calls.Choosing, Is.True, "The call waits for the player instead of running on.");
            calls.Advance();
            Assert.That(calls.Choosing, Is.True, "Skipping does not pass over a choice.");

            calls.Choose(0);
            yield return null;
            Assert.That(calls.Choosing, Is.False);
            Assert.That(panel.gameObject.activeSelf, Is.False, "The replies go away once one is picked.");
            StringAssert.StartsWith("你　好。", calls.Subtitle);
            Assert.That(ClinicCallRuntime.HasFlag("said-yes"), Is.True);

            calls.Advance();
            StringAssert.StartsWith("霞　那就好。", calls.Subtitle, "The pick leads to its branch.");
            calls.Advance();
            for (float t = 0; t < 3f && calls.Active; t += Time.unscaledDeltaTime) yield return null;
            Assert.That(calls.Active, Is.False, "\"end\" hangs up.");
        }
    }
}
