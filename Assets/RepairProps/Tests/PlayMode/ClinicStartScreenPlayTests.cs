using System.Collections;
using BorderRepair.RepairProps;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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
}
