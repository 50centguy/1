using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BorderRepair.RepairProps.Tests
{
    // Each kind of bench action has its own hand motion (RepairPropFeel), and the motions are forgiving: a slip resets
    // the motion and says why, it never breaks anything.
    public sealed class RepairPropFeelTests
    {
        const float Frame = 1f / 60f;

        [Test]
        public void EveryPropsCommands_GetTheMotionOfWhatTheyDo()
        {
            foreach (RepairPropKind kind in System.Enum.GetValues(typeof(RepairPropKind)))
                using (var rig = new PropFlowTestRig(kind))
                    foreach (var c in rig.definition.commands)
                    {
                        var feel = RepairPropFeel.For(c);
                        string id = c.id, where = kind + " " + id;
                        if (id.StartsWith("remove.")) Assert.That(feel, Is.EqualTo(PropFeel.Unscrew), where);
                        else if (id.StartsWith("install.")) Assert.That(feel, Is.EqualTo(PropFeel.Screw), where);
                        else if (id == "cover.remove") Assert.That(feel, Is.EqualTo(PropFeel.Pry), where);
                        else if (id == "cover.install") Assert.That(feel, Is.EqualTo(PropFeel.Seat), where);
                        else if (id == "isolate" || id == "harness.release") Assert.That(feel, Is.EqualTo(PropFeel.Unplug), where);
                        else if (id == "supply.connect" || id == "harness.connect") Assert.That(feel, Is.EqualTo(PropFeel.Plug), where);
                        else if (id.StartsWith("module.remove.") || id == "tray.remove") Assert.That(feel, Is.EqualTo(PropFeel.SlideOut), where);
                        else if (id.StartsWith("module.install.") || id == "tray.install") Assert.That(feel, Is.EqualTo(PropFeel.SlideIn), where);
                        else if (c.kind == PropCommandKind.Measure) Assert.That(feel, Is.EqualTo(PropFeel.Probe), where);
                        else if (id == "fix.coax") Assert.That(feel, Is.EqualTo(PropFeel.Scrub), where);
                        else if (id == "fix.lead") Assert.That(feel, Is.EqualTo(PropFeel.Crimp), where);
                        else Assert.That(feel, Is.EqualTo(PropFeel.Tap), where + ": power, tests, poses and delivery stay one press.");
                        if (feel != PropFeel.Tap) Assert.That(RepairPropFeel.Prompt(feel), Is.Not.Empty, where);
                    }
        }

        [Test]
        public void ReceiverProbes_ShowAReadingOnTheMeter()
        {
            using (var rig = new PropFlowTestRig(RepairPropKind.Receiver))
                foreach (var probe in rig.definition.probes)
                {
                    Assert.That(probe.normalDisplay, Is.Not.Empty, probe.id);
                    Assert.That(probe.abnormalDisplay, Is.Not.Empty.And.Not.EqualTo(probe.normalDisplay), probe.id);
                }
        }

        static void Circle(RepairPropGestureLogic g, float degrees, float sign, float radius = 50f)
        {
            for (float a = 0; a <= degrees && !g.Done; a += 10f)
            {
                float r = sign * a * Mathf.Deg2Rad;
                g.Step(new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * radius, true, Frame);
            }
        }

        [Test]
        public void Screws_TurnOneWayOut_AndTheOtherWayIn()
        {
            var unscrew = new RepairPropGestureLogic(PropFeel.Unscrew, Vector2.up);
            Circle(unscrew, 200f, -1f);
            Assert.That(unscrew.Progress, Is.Zero, "Clockwise does not loosen it.");
            Assert.That(unscrew.Message, Does.Contain("逆时针"));
            Circle(unscrew, 380f, 1f);
            Assert.That(unscrew.Done, Is.True, "One turn counter-clockwise backs it out.");
            Assert.That(unscrew.Ticks, Is.GreaterThanOrEqualTo(12), "The ratchet clicks along the way.");

            var screw = new RepairPropGestureLogic(PropFeel.Screw, Vector2.up);
            Circle(screw, 380f, 1f);
            Assert.That(screw.Done, Is.False);
            Circle(screw, 380f, -1f);
            Assert.That(screw.Done, Is.True, "One turn clockwise drives it home.");
        }

        [Test]
        public void Pry_LetGoEarlySpringsBack_AFullPullPopsIt()
        {
            var pry = new RepairPropGestureLogic(PropFeel.Pry, Vector2.right);
            pry.Step(Vector2.zero, true, Frame);
            for (int i = 1; i <= 6; i++) pry.Step(new Vector2(i * 10f, 0), true, Frame);
            Assert.That(pry.Progress, Is.GreaterThan(.4f).And.LessThan(1f));
            for (int i = 0; i < 30; i++) pry.Step(new Vector2(60f, 0), false, Frame);
            Assert.That(pry.Progress, Is.Zero, "Released early, the cover settles back.");
            Assert.That(pry.Done, Is.False);
            pry.Step(Vector2.zero, true, Frame);
            for (int i = 1; i <= 12; i++) pry.Step(new Vector2(i * 10f, 4f), true, Frame);
            Assert.That(pry.Done, Is.True);

            var far = new RepairPropGestureLogic(PropFeel.Pry, Vector2.right);
            far.Step(new Vector2(400f, 0), true, Frame);
            far.Step(new Vector2(600f, 0), true, Frame);
            Assert.That(far.Progress, Is.Zero, "A press away from the part grabs nothing.");
            Assert.That(far.Message, Is.Not.Null);
        }

        [Test]
        public void Seat_PushesAgainstTheWayThePartCameOut()
        {
            var seat = new RepairPropGestureLogic(PropFeel.Seat, Vector2.up);
            Assert.That(seat.direction, Is.EqualTo(Vector2.down));
            seat.Step(Vector2.zero, true, Frame);
            for (int i = 1; i <= 8; i++) seat.Step(new Vector2(0, -i * 10f), true, Frame);
            Assert.That(seat.Done, Is.True);
        }

        [Test]
        public void Unplug_NeedsTheLatchHeldBeforeItPulls()
        {
            var plug = new RepairPropGestureLogic(PropFeel.Unplug, Vector2.up);
            plug.Step(Vector2.zero, true, Frame);
            for (int i = 1; i <= 8; i++) plug.Step(new Vector2(0, i * 10f), true, Frame);
            Assert.That(plug.Progress, Is.Zero, "Yanking with the latch closed does nothing.");
            Assert.That(plug.Message, Does.Contain("卡扣"));
            var at = new Vector2(0, 80f);
            for (int i = 0; i < 30 && !plug.Latched; i++) plug.Step(at, true, Frame);
            Assert.That(plug.Latched, Is.True, "Held still for a moment, the latch lets go.");
            for (int i = 1; i <= 8; i++) plug.Step(at + new Vector2(0, i * 10f), true, Frame);
            Assert.That(plug.Done, Is.True);
        }

        [Test]
        public void Scrub_CleansOnlyWhereTheCrustIs()
        {
            var scrub = new RepairPropGestureLogic(PropFeel.Scrub, Vector2.up);
            for (int i = 0; i < 80; i++) scrub.Step(new Vector2(200f + (i % 2) * 40f, 0), true, Frame);
            Assert.That(scrub.Progress, Is.Zero);
            Assert.That(scrub.Message, Is.Not.Null);
            int strokes = 0;
            while (!scrub.Done && strokes < 200) scrub.Step(new Vector2(strokes++ % 2 == 0 ? -40f : 40f, 0), true, Frame);
            Assert.That(scrub.Done, Is.True);
            Assert.That(strokes, Is.InRange(10, 30), "About a dozen back-and-forth strokes.");
        }

        [Test]
        public void Crimp_LetGoInTheGreenBand()
        {
            var crimp = new RepairPropGestureLogic(PropFeel.Crimp, Vector2.up);
            Squeeze(crimp, .3f);
            Assert.That(crimp.Done, Is.False);
            Assert.That(crimp.Message, Does.Contain("不够紧"));
            Squeeze(crimp, 1.5f);
            Assert.That(crimp.Done, Is.False);
            Assert.That(crimp.Message, Does.Contain("过头"));
            Squeeze(crimp, (RepairPropGestureLogic.CrimpLow + RepairPropGestureLogic.CrimpHigh) * .5f);
            Assert.That(crimp.Done, Is.True);
        }

        static void Squeeze(RepairPropGestureLogic g, float pressure)
        {
            g.Step(Vector2.zero, true, Frame);
            float seconds = pressure / RepairPropGestureLogic.CrimpRate;
            for (float t = Frame; t < seconds; t += Frame) g.Step(Vector2.zero, true, Frame);
            g.Step(Vector2.zero, false, Frame);
        }

        [Test]
        public void Probe_ReadsOnlyOnTheMarkedPoint()
        {
            var probe = new RepairPropGestureLogic(PropFeel.Probe, Vector2.up, new Vector2(-150f, -40f));
            Assert.That(probe.ProbeTip, Is.EqualTo(probe.Holster));
            probe.Step(probe.Holster, true, Frame);
            probe.Step(new Vector2(-100f, 0), true, Frame);
            probe.Step(new Vector2(-100f, 0), false, Frame);
            Assert.That(probe.Contact, Is.False);
            Assert.That(probe.Message, Does.Contain("没碰到"));
            for (int i = 0; i < 30; i++) probe.Step(Vector2.zero, false, Frame);
            Assert.That(probe.ProbeTip, Is.EqualTo(probe.Holster), "The probe goes back to its holster.");
            probe.Step(probe.Holster, true, Frame);
            probe.Step(new Vector2(10f, -8f), true, Frame);
            probe.Step(new Vector2(10f, -8f), false, Frame);
            Assert.That(probe.Contact, Is.True);
            for (int i = 0; i < 60 && !probe.Done; i++) probe.Step(Vector2.zero, false, Frame);
            Assert.That(probe.Done, Is.True, "The reading settles in under a second.");
        }

        [Test]
        public void ProbeReading_MatchesWhatTheMeasureCommandRecords()
        {
            RepairPropFlow.FaultOverride = 2;
            try
            {
                using (var rig = new PropFlowTestRig(RepairPropKind.Receiver))
                {
                    rig.flow.RollFault();
                    var probes = rig.definition.probes.ToDictionary(p => p.id);
                    Assert.That(rig.flow.ProbeReadsAbnormal(probes["coax"]), Is.True);
                    Assert.That(rig.flow.ProbeReadsAbnormal(probes["lead"]), Is.False);
                    Assert.That(rig.flow.ProbeReadsAbnormal(probes["board"]), Is.False);
                }
            }
            finally { RepairPropFlow.FaultOverride = 0; }
        }
    }
}
