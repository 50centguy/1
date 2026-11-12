using BorderRepair.RepairProps;
using NUnit.Framework;
using UnityEditor;

namespace BorderRepair.RepairProps.Tests
{
    public sealed class ClinicTutorialRulesTests
    {
        [TestCase("power.off", "fixture.disconnect", true)]
        [TestCase("remove.RX_CoverScrew_01", "remove.RX_CoverScrew_04", true)]
        [TestCase("test.signal", "test.identity", true)]
        [TestCase("module.remove.old", "module.install.new", true)]
        [TestCase("power.on", "remove.RX_CoverScrew_01", false)]
        [TestCase("cover.remove", "remove.RX_CoverScrew_01", false)]
        [TestCase("complete", "test.signal", false)]
        public void Family_GroupsStepsOfOneMove(string a, string b, bool same)
        {
            Assert.AreEqual(same, ClinicTutorialRuntime.Family(a) == ClinicTutorialRuntime.Family(b));
        }

        [Test]
        public void TutorialFault_IsTheReceiversNewPartFault_AndOnlyTheReceivers()
        {
            var receiver = AssetDatabase.LoadAssetAtPath<RepairPropFlowDefinition>("Assets/RepairProps/Data/Receiver_Flow.asset");
            Assert.That(receiver, Is.Not.Null);
            int fault = ClinicTutorialRuntime.TutorialFault(receiver);
            Assert.That(fault, Is.GreaterThan(0), "The receiver has a fault that is fixed with a new part.");
            CollectionAssert.Contains(receiver.faults[fault - 1].route, "module.install.new");
            var cartridge = AssetDatabase.LoadAssetAtPath<RepairPropFlowDefinition>("Assets/RepairProps/Data/Cartridge_Flow.asset");
            Assert.That(ClinicTutorialRuntime.TutorialFault(cartridge), Is.EqualTo(0));
            Assert.That(ClinicTutorialRuntime.TutorialFault(null), Is.EqualTo(0));
        }
    }
}
