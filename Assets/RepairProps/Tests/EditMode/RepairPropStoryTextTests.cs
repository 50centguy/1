using System;
using System.Linq;
using BorderRepair.RepairProps;
using NUnit.Framework;

namespace BorderRepair.RepairProps.Tests
{
    // The story text moved from code to Resources/RepairPropsStory/*.json. These pin that every lookup still finds its
    // text, so a rewrite that drops or renames an entry fails here instead of going silent in the game.
    public sealed class RepairPropStoryTextTests
    {
        [SetUp] public void Fresh() => RepairPropStoryText.Reload();

        [Test]
        public void EveryProp_HasBackstory_Findings_AndItsTestNote()
        {
            var tests = new[] { (RepairPropKind.Receiver, "signal"), (RepairPropKind.Cartridge, "read"), (RepairPropKind.Power, "load"), (RepairPropKind.Omamori, "identity") };
            foreach (var (kind, test) in tests)
            {
                Assert.That(RepairPropStories.Backstory(kind), Is.Not.Null.And.Not.Empty, kind + " backstory");
                Assert.That(RepairPropStoryText.GetProp(kind).findings, Is.Not.Empty, kind + " findings");
                Assert.That(RepairPropStories.TestNote(kind, test, true), Is.Not.Null.And.Not.Empty, kind + " pass note");
            }
        }

        [Test]
        public void Lookups_KeepTheirOldMeaning()
        {
            StringAssert.Contains("K.MORITA", RepairPropStories.Finding(RepairPropKind.Cartridge, "MC_DataCore"));
            StringAssert.Contains("别格式化", RepairPropStories.Finding(RepairPropKind.Cartridge, "MC_BoardJumper_Released"), "variants share their stem's mark");
            Assert.IsNull(RepairPropStories.Finding(RepairPropKind.Cartridge, "MC_BoardJumperX"), "a stem only matches whole or with _suffix");
            Assert.IsNull(RepairPropStories.TestNote(RepairPropKind.Omamori, "identity", false), "an empty fail note stays null");
            Assert.IsNull(RepairPropStories.TestNote(RepairPropKind.Receiver, "read", true), "an unknown test has no note");
            StringAssert.Contains("属主标记", RepairPropStories.TestNote(RepairPropKind.Cartridge, "read", false));
        }

        [Test]
        public void Calls_AllPresent_WithCallerAndLines()
        {
            string[] ids = { "rent-n1", "dealer-intro", "patrol-receiver", "care-omamori", "dealer-cartridge", "rent-n2" };
            CollectionAssert.AreEquivalent(ids, ClinicCallRuntime.Calls.Select(c => c.id).ToArray());
            foreach (var id in ids)
            {
                var call = ClinicCallRuntime.Find(id);
                Assert.That(call.name, Is.Not.Empty, id);
                Assert.That(call.lines, Is.Not.Empty, id);
                Assert.That(call.lines.All(l => !string.IsNullOrEmpty(l.text)), id + " has an empty line");
            }
            Assert.IsTrue(ClinicCallRuntime.Find("dealer-cartridge").lines.Any(l => l.player), "player lines keep their flag");
        }

        [Test]
        public void Assistant_GroupsHintsAndBanter_Resolve()
        {
            foreach (var key in new[] { "purchase-received", "greet", "first-item", "retest-failed", "all-done", "parts-delivered",
                         "after-call:rent-n1", "after-call:dealer-intro", "after-call:patrol-receiver", "after-call:dealer-cartridge", "after-call:care-omamori" })
            {
                var lines = RepairPropStoryText.AssistantGroup(key);
                Assert.That(lines, Is.Not.Empty, key);
                foreach (var line in lines)
                    Assert.IsTrue(Enum.TryParse(line.face, out ClinicAssistantRuntime.Face _), key + ": unknown face " + line.face);
            }
            foreach (var key in new[] { "hint-busy", "hint-carrying", "hint-pending-case", "hint-in-repair", "hint-ready-for-delivery" })
                Assert.That(RepairPropStoryText.AssistantHint(key), Is.Not.Null.And.Not.Empty, key);
            Assert.That(RepairPropStoryText.Banter.Count, Is.EqualTo(4));
            CollectionAssert.AreEqual(new[] { "patrol-receiver", "care-omamori" }, RepairPropStoryText.BanterAfterCall.Select(b => b.afterCall).ToArray());
        }
    }
}
