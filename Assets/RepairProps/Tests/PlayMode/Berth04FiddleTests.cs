using System.Collections;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.Tests
{
    public sealed partial class Berth04PlayableTests
    {
        // Each of the B1's small things can be aimed at from where a player would stand, shows its hint, and does what it
        // says when used; none of them is on the generated solids' layer. Frames of each are kept.
        [UnityTest, Timeout(600000)]
        public IEnumerator B1_SmallThingsCanBeAimedAtAndUsed()
        {
            var fiddles = Object.FindObjectsByType<B1Fiddle>(FindObjectsSortMode.None).OrderBy(f => f.kind).ToArray();
            Assert.That(fiddles.Select(f => f.kind).Distinct().Count(), Is.EqualTo(System.Enum.GetValues(typeof(B1FiddleKind)).Length),
                "every kind is in the B1: " + string.Join(", ", fiddles.Select(f => f.kind)));
            var walker = Berth04Playable.Current.Join.Walker;
            var cc = walker.GetComponent<CharacterController>();
            var room = ClinicLayout.Active.room;
            var log = new StringBuilder("B1 fiddles:\n");
            var failures = new StringBuilder();
            foreach (var f in fiddles)
            {
                var box = f.GetComponent<BoxCollider>();
                Assert.That(f.gameObject.layer, Is.Not.EqualTo(2), f.name + " is pickable");
                Assert.That(room.Contains(box.bounds.center), Is.True, f.name + " in the B1");
                // Stand 0.65 m off it toward the middle of the room, look at it.
                var at = box.bounds.center;
                var away = new Vector3(room.center.x - at.x, 0, room.center.z - at.z).normalized;
                Vector3 feet = new Vector3(at.x, .05f, at.z) + away * .65f;
                cc.enabled = false; walker.PlaceAt(feet, 0); cc.enabled = true;
                walker.LookAt(at);
                for (float w = 0; w < .35f; w += Time.unscaledDeltaTime) yield return null;
                var rt = B1FiddleRuntime.Instance;
                Assert.That(rt, Is.Not.Null, "fiddle runtime");
                string hint = rt.HintText;
                log.Append("  ").Append(f.name).Append(" at ").Append(at.ToString("F2")).Append(": aimed ").Append(rt.Aimed == f).Append(", hint ").Append(hint ?? "-").Append('\n');
                if (rt.Aimed != f) { failures.Append(f.name).Append(" not aimed (got ").Append(rt.Aimed ? rt.Aimed.name : "nothing").Append("); "); continue; }
                Berth04Measure.Frame(Berth04Playable.Current.Join.Eye, "fiddle - " + f.kind + " - before.jpg");

                bool lampBefore = f.lamp != null && f.lamp.enabled;
                int stateBefore = f.State;
                var line = f.Use();
                for (float w = 0; w < 6 && f.Busy; w += Time.unscaledDeltaTime) yield return null;
                for (int i = 0; i < 5; i++) yield return null;
                Berth04Measure.Frame(Berth04Playable.Current.Join.Eye, "fiddle - " + f.kind + " - used.jpg");
                log.Append("    used: state ").Append(stateBefore).Append(" -> ").Append(f.State).Append(", line ").Append(line ?? "-").Append('\n');
                Assert.That(f.Uses, Is.EqualTo(1), f.name + " used once");
                switch (f.kind)
                {
                    case B1FiddleKind.CagedBulb: Assert.That(f.lamp.enabled, Is.Not.EqualTo(lampBefore), "the cord switches the bulb"); break;
                    case B1FiddleKind.Radio: Assert.That(f.speaker.isPlaying, Is.True, "the radio plays"); break;
                    case B1FiddleKind.Heater: Assert.That(f.speaker.isPlaying, Is.False, "the heater stops humming"); break;
                    case B1FiddleKind.Newspaper: case B1FiddleKind.WaterDispenser: Assert.That(line, Is.Not.Null, "a line to read"); break;
                    case B1FiddleKind.Thermos: Assert.That(f.second.Find("Tea") != null && f.second.Find("Tea").gameObject.activeSelf, Is.True, "tea in the cup"); break;
                }
                // Back as it was where it toggles, so the room is left as dressed.
                if (f.kind == B1FiddleKind.CagedBulb || f.kind == B1FiddleKind.Heater || f.kind == B1FiddleKind.Thermos)
                {
                    f.Use();
                    for (float w = 0; w < 6 && f.Busy; w += Time.unscaledDeltaTime) yield return null;
                }
                if (f.kind == B1FiddleKind.Radio) { f.Use(); f.Use(); }
                if (f.kind == B1FiddleKind.FestiveBulbs) { f.Use(); f.Use(); }
            }
            Berth04Measure.Append("fiddles.txt", log.ToString());
            Assert.That(failures.Length, Is.EqualTo(0), failures.ToString());

            // The far fill is there and stays cheap: one merged mesh, two mist rings.
            var mid = GameObject.Find("Middle ring blocks");
            Assert.That(mid, Is.Not.Null, "middle ring");
            Assert.That(mid.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan(400).And.LessThan(20000), "middle ring blocks");
            Assert.That(Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Count(r => r.name.StartsWith("Mist - ")), Is.EqualTo(2), "mist rings");
        }
    }
}
