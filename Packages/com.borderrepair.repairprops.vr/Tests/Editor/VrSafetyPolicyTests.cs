using NUnit.Framework;

namespace BorderRepair.RepairProps.VR.Tests
{
    public class VrSafetyPolicyTests
    {
        [TestCase(0f, true)]
        [TestCase(1.25f, true)]
        [TestCase(1.2501f, false)]
        [TestCase(-1f, false)]
        public void WorkDistance(float distance, bool expected) =>
            Assert.That(VrSafetyPolicy.CanWork(true, false, true, false, false, distance), Is.EqualTo(expected));

        [Test]
        public void EveryWorkGateIsRequired()
        {
            Assert.IsFalse(VrSafetyPolicy.CanWork(false, false, true, false, false, .5f));
            Assert.IsFalse(VrSafetyPolicy.CanWork(true, true, true, false, false, .5f));
            Assert.IsFalse(VrSafetyPolicy.CanWork(true, false, false, false, false, .5f));
            Assert.IsFalse(VrSafetyPolicy.CanWork(true, false, true, true, false, .5f));
            Assert.IsFalse(VrSafetyPolicy.CanWork(true, false, true, false, true, .5f));
        }

        [TestCase(.45f, -.12f, true)]
        [TestCase(.45f, .4f, true)]
        [TestCase(.451f, .1f, false)]
        [TestCase(.1f, .401f, false)]
        [TestCase(.1f, -.121f, false)]
        public void DockBounds(float distance, float bottom, bool expected) =>
            Assert.That(VrSafetyPolicy.CanDock(distance, bottom), Is.EqualTo(expected));

        [Test]
        public void NonFinitePoseCannotAuthorizeInteraction()
        {
            foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                Assert.IsFalse(VrSafetyPolicy.CanWork(true, false, true, false, false, value));
                Assert.IsFalse(VrSafetyPolicy.CanDock(value, 0));
                Assert.IsFalse(VrSafetyPolicy.CanDock(0, value));
            }
        }
    }
}
