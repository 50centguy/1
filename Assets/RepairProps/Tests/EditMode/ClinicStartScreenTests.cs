using BorderRepair.RepairProps;
using NUnit.Framework;
using UnityEngine;

namespace BorderRepair.RepairProps.Tests
{
    public sealed class ClinicStartScreenTests
    {
        [Test]
        public void PublishedStringsAreTheOnesAgreedFor()
        {
            Assert.AreEqual("地下义体维修处", ClinicStartScreenRuntime.Title);
            Assert.AreEqual("BORDER REPAIR STATION", ClinicStartScreenRuntime.Subtitle);
            CollectionAssert.AreEqual(new[] { "开始营业", "退出" }, ClinicStartScreenRuntime.MenuItems);
        }

        [Test]
        public void FactoryBuildsThePageUnderTheGivenCanvasAndDrawsThoseStrings()
        {
            var canvas = new GameObject("StartScreenTestCanvas", typeof(RectTransform), typeof(Canvas));
            try
            {
                var screen = ClinicStartScreenRuntime.Create(canvas.transform);

                Assert.IsNotNull(screen);
                Assert.AreSame(canvas.transform, screen.transform.parent);
                Assert.AreEqual(0, screen.SelectedIndex, "The first item, 开始营业, starts selected.");
                var drawn = screen.RenderedTexts;
                CollectionAssert.Contains(drawn, ClinicStartScreenRuntime.Title);
                CollectionAssert.Contains(drawn, ClinicStartScreenRuntime.Subtitle);
                foreach (var item in ClinicStartScreenRuntime.MenuItems) CollectionAssert.Contains(drawn, item);
            }
            finally
            {
                Object.DestroyImmediate(canvas);
            }
        }
    }
}
