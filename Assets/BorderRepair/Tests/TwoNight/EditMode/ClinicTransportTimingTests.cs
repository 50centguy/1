using System;
using BorderRepair.FirstOrder;
using NUnit.Framework;
using UnityEngine;

namespace BorderRepair.TwoNight.Tests
{
    public class ClinicTransportTimingTests
    {
        GameObject root;
        FirstOrderFlow flow;

        [SetUp]
        public void SetUp() { root = new GameObject("TransportTiming"); flow = root.AddComponent<FirstOrderFlow>(); }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(root);

        [Test]
        public void DisabledSpeed_PreservesLegacyDurations()
        {
            Assert.AreEqual(0, flow.MaxCarrySpeed);
            Assert.AreEqual(.45f, flow.CarryDuration(.45f, 8));
            Assert.AreEqual(1.2f, flow.CarryDuration(1.2f, 8, 180, .3f));
            flow.ConfigureCarrySpeed(.75f);
            flow.ConfigureCarrySpeed(0);
            Assert.AreEqual(.45f, flow.CarryDuration(.45f, 8));
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidSpeed_IsRejectedWithoutChangingConfiguration(float speed)
        {
            flow.ConfigureCarrySpeed(.75f);
            Assert.Throws<ArgumentOutOfRangeException>(() => flow.ConfigureCarrySpeed(speed));
            Assert.AreEqual(.75f, flow.MaxCarrySpeed);
        }

        [TestCase(.05f, 0f, 0f)]
        [TestCase(4f, 0f, 0f)]
        [TestCase(4f, 180f, .3f)]
        [TestCase(0f, 180f, .3f)]
        public void OptInDuration_BoundsSmoothTranslationAndPivotTurnSpeed(float distance, float angle, float radius)
        {
            const float maxSpeed = .75f;
            flow.ConfigureCarrySpeed(maxSpeed);
            float seconds = flow.CarryDuration(.45f, distance, angle, radius);
            Assert.GreaterOrEqual(seconds, .45f);
            var pivot = Vector3.forward * radius;
            Vector3 Position(float k)
            {
                var center = Vector3.right * distance * Mathf.SmoothStep(0, 1, k);
                var rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(0, angle, 0),
                    Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.2f, .8f, k)));
                return center - rotation * pivot;
            }
            var previous = Position(0);
            for (int i = 1; i <= 2000; i++)
            {
                var current = Position(i / 2000f);
                Assert.LessOrEqual(Vector3.Distance(previous, current) / (seconds / 2000f), maxSpeed + .002f);
                previous = current;
            }
        }
    }
}
