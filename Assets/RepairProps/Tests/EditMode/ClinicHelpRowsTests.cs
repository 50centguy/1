using System.Collections.Generic;
using System.Linq;
using BorderRepair.RepairProps;
using NUnit.Framework;

namespace BorderRepair.RepairProps.Tests
{
    public sealed class ClinicHelpRowsTests
    {
        const string PlaytestGroup = "试玩快捷键";
        const string HandsGroup = "动手";

        static string[] Keys(bool devKeys) => ClinicHelpRuntime.RowsFor(devKeys).Where(r => r[0] != null).Select(r => r[0]).ToArray();

        static string What(string key) => ClinicHelpRuntime.RowsFor(false).First(r => r[0] == key)[1];

        // A row with a null key starts a group; the rows after it belong to that group.
        static string[] KeysInGroup(bool devKeys, string group)
        {
            var keys = new List<string>();
            bool inGroup = false;
            foreach (var row in ClinicHelpRuntime.RowsFor(devKeys))
            {
                if (row[0] == null) inGroup = row[1] == group;
                else if (inGroup) keys.Add(row[0]);
            }
            return keys.ToArray();
        }

        [Test]
        public void ReleaseSheetListsTheNormalControlsAndNoPlaytestKeys()
        {
            var keys = Keys(false);
            CollectionAssert.IsSubsetOf(new[] { "W A S D / 方向键", "按住 Shift", "C", "Tab", "-", "鼠标左键", "F1" }, keys);
            CollectionAssert.DoesNotContain(keys, "F4");
            CollectionAssert.DoesNotContain(keys, "F5");
            Assert.AreEqual(0, KeysInGroup(false, PlaytestGroup).Length, "A release build has no playtest group.");
        }

        [Test]
        public void PictureSettingsAndDebugInfoAreNormalControlsInEveryBuild()
        {
            CollectionAssert.IsSubsetOf(new[] { "=", "F3" }, KeysInGroup(false, HandsGroup));
            CollectionAssert.IsSubsetOf(new[] { "=", "F3" }, KeysInGroup(true, HandsGroup));
        }

        [Test]
        public void DevelopmentSheetAddsOnlyF4AndF5ToTheSameControls()
        {
            CollectionAssert.IsSubsetOf(Keys(false), Keys(true));
            CollectionAssert.AreEqual(new[] { "F4", "F5" }, KeysInGroup(true, PlaytestGroup));
        }

        [Test]
        public void F4SaysItOnlyCleansTheCurrentRepairItem()
        {
            string what = ClinicHelpRuntime.RowsFor(true).First(r => r[0] == "F4")[1];
            StringAssert.Contains("当前维修物品", what);
            StringAssert.DoesNotContain("所有", what);
        }

        [Test]
        public void EscSaysWhatItDoesAndNeverClaimsToOpenTheManual()
        {
            Assert.AreEqual("松开光标；关闭手册；维修或手术时取消动作、退出近景", What("Esc"));
        }

        [Test]
        public void CameraKeysSayTheyOnlyWorkWhileDockingUnit07()
        {
            Assert.AreEqual("停靠七号时切换固定机位（按移动键回到行走）", What("1 – 4"));
        }
    }
}
