using System.Collections;
using System.Reflection;
using BorderRepair.RepairProps;
using BorderRepair.TwoNight;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace BorderRepair.RepairProps.Tests
{
    // 开始页最难用眼睛验的部分是"按下开始之后"：页面要拆掉。构建里的输入没法从自动化里驱动（elevated
    // 会话的合成输入到不了玩家窗口，键盘和鼠标都试过），所以这段逻辑靠这里守。
    // 光标本身在 batchmode 里锁不住（Cursor.lockState 读回来永远是 None），所以这里不断言锁，只断言页面收场。
    public sealed class ClinicStartScreenPlayTests
    {
        [UnityTest]
        public IEnumerator BeginTakesThePageDown()
        {
            var canvas = TestCanvas();
            var screen = ClinicStartScreenRuntime.Create(canvas.transform);
            yield return null;

            Assert.That(screen.RenderedTexts, Does.Contain(ClinicStartScreenRuntime.Title));
            Assert.That(screen.SelectedIndex, Is.EqualTo(0), "开始营业 must be the default choice");

            screen.Begin();
            yield return null;
            yield return null;

            Assert.That(Object.FindObjectsByType<ClinicStartScreenRuntime>(FindObjectsSortMode.None), Is.Empty,
                "Begin must take the page down");
            Object.Destroy(canvas.gameObject);
        }

        [UnityTest]
        public IEnumerator SelectWrapsAndNeverLeavesTheMenu()
        {
            var canvas = TestCanvas();
            var screen = ClinicStartScreenRuntime.Create(canvas.transform);
            yield return null;

            int count = ClinicStartScreenRuntime.MenuItems.Length;
            screen.Select(-1);
            Assert.That(screen.SelectedIndex, Is.EqualTo(count - 1));
            screen.Select(count);
            Assert.That(screen.SelectedIndex, Is.EqualTo(0));

            Object.Destroy(canvas.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BlockingLastsFromThePageOpeningUntilBeginOrDestruction()
        {
            var canvas = TestCanvas();
            var screen = ClinicStartScreenRuntime.Create(canvas.transform);
            Assert.That(ClinicStartScreenRuntime.Blocking, Is.True, "an open page blocks");
            yield return null;
            Assert.That(ClinicStartScreenRuntime.Blocking, Is.True, "and keeps blocking until it is dismissed");

            screen.Begin();
            Assert.That(ClinicStartScreenRuntime.Blocking, Is.False, "Begin lifts the block at once");
            yield return null;
            Object.Destroy(canvas.gameObject);
            yield return null;

            var other = TestCanvas();
            ClinicStartScreenRuntime.Create(other.transform);
            Assert.That(ClinicStartScreenRuntime.Blocking, Is.True);
            Object.Destroy(other.gameObject);
            yield return null;
            Assert.That(ClinicStartScreenRuntime.Blocking, Is.False, "destroying the page without Begin lifts it too");
        }

        // 只给一个裸 Canvas：这个页面自己搭布局，挂 CanvasScaler / GraphicRaycaster 反而会在销毁时
        // 报 "Can't remove Canvas because ... depends on it"。
        static Canvas TestCanvas()
        {
            var go = new GameObject("StartScreenTestCanvas", typeof(Canvas));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            return canvas;
        }
    }

    // 来电的延迟要从开始页关闭算起。真等 25 秒太慢，所以用反射把基准拨到很久以前：
    // 这样"开始页挡住了来电"和"关页后基准被推后了"都不是因为时间没到才成立；最后再拨一次，证明这个场景确实打得出电话。
    public sealed class ClinicCallBehindStartScreenTests
    {
        const string ScenePath = "Assets/RepairProps/Scenes/RepairProps_Clinic.unity";
        TwoNightState previousRun;
        string previousSaveDirectory;
        Scene loadedScene;
        Canvas canvas;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousRun = TwoNightRun.Current;
            previousSaveDirectory = TwoNightSave.OverrideDirectory;
            TwoNightSave.OverrideDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ClinicCallStartScreen_" + System.Guid.NewGuid().ToString("N"));
            TwoNightRun.Clear();   // 新的一局：played 会被清空，rent-n1 还没打过
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
            Assert.That(clinic.State.phase, Is.EqualTo(TwoNightPhase.Night1Counter));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (canvas != null) Object.Destroy(canvas.gameObject);
            yield return null;
            if (loadedScene.IsValid() && loadedScene.isLoaded)
            {
                var cleanup = SceneManager.CreateScene("ClinicCallCleanup_" + System.Guid.NewGuid().ToString("N"));
                SceneManager.SetActiveScene(cleanup);
                yield return SceneManager.UnloadSceneAsync(loadedScene);
            }
            TwoNightRun.Set(previousRun);
            TwoNightSave.OverrideDirectory = previousSaveDirectory;
        }

        [UnityTest]
        public IEnumerator NoCallRingsBehindThePageAndTheDelayRestartsWhenItCloses()
        {
            var calls = ClinicCallRuntime.Current;
            Assert.That(calls.Active, Is.False, "No call may be up before the test starts.");

            var go = new GameObject("StartScreenTestCanvas", typeof(Canvas));
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var screen = ClinicStartScreenRuntime.Create(canvas.transform);
            Assert.That(ClinicStartScreenRuntime.Blocking, Is.True);

            MakeEveryDelayLongOverdue(calls);
            for (int i = 0; i < 5; i++) yield return null;
            Assert.That(calls.Active, Is.False, "A call rang behind the start page.");

            MakeEveryDelayLongOverdue(calls);
            yield return null;   // 最后一帧仍在屏蔽中，基准被追回到现在
            screen.Begin();
            for (int i = 0; i < 5; i++) yield return null;
            Assert.That(calls.Active, Is.False, "A call rang the moment the page closed instead of waiting out its delay.");

            // 对照：同一个场景、页面已关，延迟到了就照常打进来。
            MakeEveryDelayLongOverdue(calls);
            for (int i = 0; i < 5; i++) yield return null;
            Assert.That(calls.Ringing, Is.True, "With the page gone and the delay over, the call must ring.");
            Assert.That(calls.CallId, Is.EqualTo("rent-n1"));
        }

        static void MakeEveryDelayLongOverdue(ClinicCallRuntime calls)
        {
            float past = Time.unscaledTime - 1000f;
            SetFloat(calls, "sceneStart", past);
            SetFloat(calls, "lastHangup", past);   // 否则刚启动的编辑器会被 20 秒的挂断间隔挡住，对照就不成立
        }

        static void SetFloat(ClinicCallRuntime calls, string field, float value)
        {
            var info = typeof(ClinicCallRuntime).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(info, Is.Not.Null, field);
            info.SetValue(calls, value);
        }
    }
}
