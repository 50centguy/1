using System.Linq;
using BorderRepair.RepairProps;
using NUnit.Framework;

namespace BorderRepair.RepairProps.Tests
{
    public sealed class ClinicHelpRowsTests
    {
        static string[] Keys(bool devKeys) => ClinicHelpRuntime.RowsFor(devKeys).Where(r => r[0] != null).Select(r => r[0]).ToArray();

        [Test]
        public void ReleaseSheetListsTheNormalControlsAndNoPlaytestKeys()
        {
            var keys = Keys(false);
            CollectionAssert.IsSubsetOf(new[] { "W A S D / 方向键", "按住 Shift", "C", "Tab", "-", "鼠标左键", "F1" }, keys);
            CollectionAssert.DoesNotContain(keys, "F4");
            CollectionAssert.DoesNotContain(keys, "F5");
            CollectionAssert.DoesNotContain(keys, "F3");
            CollectionAssert.DoesNotContain(keys, "=");
        }

        [Test]
        public void DevelopmentSheetAddsThePlaytestKeysToTheSameControls()
        {
            CollectionAssert.IsSubsetOf(Keys(false), Keys(true));
            CollectionAssert.IsSubsetOf(new[] { "F4", "F5" }, Keys(true));
        }
    }
}
