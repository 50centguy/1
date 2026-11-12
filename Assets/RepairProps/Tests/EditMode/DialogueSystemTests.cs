using System.Collections.Generic;
using System.IO;
using System.Linq;
using BorderRepair.RepairProps;
using BorderRepair.RepairProps.Dialogue;
using NUnit.Framework;
using UnityEngine;
using static BorderRepair.RepairProps.RepairPropStoryText;

namespace BorderRepair.RepairProps.Tests
{
    public sealed class DialogueSystemTests
    {
        const string Folder = "Assets/RepairProps/Story/Resources/RepairPropsStory/";

        sealed class FakeWorld : IDialogueWorld
        {
            public readonly HashSet<string> facts = new HashSet<string>();
            public readonly Dictionary<string, float> numbers = new Dictionary<string, float>();
            public FakeWorld(params string[] facts) { foreach (var f in facts) this.facts.Add(f); }
            public bool Has(string kind, string arg) => facts.Contains(kind + ":" + arg);
            public bool TryNumber(string name, out float value) => numbers.TryGetValue(name, out value);
        }

        [SetUp] public void Fresh() => Reload();

        // ---- conditions ----

        [Test]
        public void Conditions_ParseAndEvaluate()
        {
            var w = new FakeWorld("flag:a", "played:rent-n1");
            w.numbers["time"] = 30; w.numbers["cash"] = 1200;
            Assert.IsTrue(DialogueCondition.Evaluate("", w), "empty is true");
            Assert.IsTrue(DialogueCondition.Evaluate("flag:a", w));
            Assert.IsFalse(DialogueCondition.Evaluate("flag:b", w));
            Assert.IsTrue(DialogueCondition.Evaluate("!flag:b", w));
            Assert.IsTrue(DialogueCondition.Evaluate("flag:b | flag:a & played:rent-n1", w), "& binds tighter than |");
            Assert.IsFalse(DialogueCondition.Evaluate("(flag:b | flag:a) & played:x", w));
            Assert.IsTrue(DialogueCondition.Evaluate("time>25 & cash >= 1200", w));
            Assert.IsFalse(DialogueCondition.Evaluate("time > 30", w));
            Assert.IsTrue(DialogueCondition.Evaluate("time >= 30 & cash != 0", w));
            Assert.IsFalse(DialogueCondition.Evaluate("delivered >= 1", w), "an unknown number is false");
        }

        [Test]
        public void Conditions_ReportMistakes_AndEvaluateFalse()
        {
            foreach (var bad in new[] { "flag:", "wat:x", "kept", "time >", "money > 3", "(flag:a", "flag:a &", "flag:a flag:b" })
            {
                var c = DialogueCondition.Parse(bad);
                Assert.IsFalse(c.Valid, bad);
                Assert.IsFalse(c.Evaluate(new FakeWorld("flag:a", "flag:b")), bad);
            }
            CollectionAssert.AreEqual(new[] { ("flag", "a"), ("delivered", "receiver") }, DialogueCondition.Parse("flag:a & !delivered:receiver | time>1").Facts.ToArray());
        }

        [Test]
        public void SetText_RaisesAndClears()
        {
            CollectionAssert.AreEqual(new[] { ("a", true), ("b", true), ("c", false) }, DialogueCondition.ParseSet("a, flag:b !c").ToArray());
        }

        // ---- runner ----

        static Call Branching() => new Call
        {
            id = "t", name = "霞",
            lines = new[]
            {
                new CallLine { text = "开场" },
                new CallLine { text = "只在有标记时说", when = "flag:met" },
                new CallLine { text = "问你", choices = new[]
                {
                    new Choice { text = "好", next = "yes", set = "agreed" },
                    new Choice { text = "不", next = "no" },
                    new Choice { text = "藏起来", when = "flag:never" },
                } },
                new CallLine { id = "yes", text = "那就好", next = "end" },
                new CallLine { id = "no", text = "随你", set = "!agreed" },
                new CallLine { text = "（只在拒绝时）", when = "!flag:agreed" },
            },
        };

        static List<string> Walk(Call call, FakeWorld world, int pick)
        {
            var said = new List<string>();
            var r = new DialogueRunner(call, world, (f, on) => { if (on) world.facts.Add("flag:" + f); else world.facts.Remove("flag:" + f); });
            int guard = 0;
            while (r.Advance() && guard++ < 50)
            {
                said.Add(r.Line.text);
                if (r.AwaitingChoice) said.Add("> " + r.Choose(pick).text);
            }
            Assert.IsTrue(r.Ended);
            return said;
        }

        [Test]
        public void Runner_FollowsChoicesJumpsConditionsAndFlags()
        {
            var w = new FakeWorld();
            CollectionAssert.AreEqual(new[] { "开场", "问你", "> 好", "那就好" }, Walk(Branching(), w, 0));
            Assert.IsTrue(w.facts.Contains("flag:agreed"), "the choice set its flag");

            w = new FakeWorld("flag:met", "flag:agreed");
            CollectionAssert.AreEqual(new[] { "开场", "只在有标记时说", "问你", "> 不", "随你", "（只在拒绝时）" }, Walk(Branching(), w, 1));
            Assert.IsFalse(w.facts.Contains("flag:agreed"), "the line cleared the flag before the next condition read it");
        }

        [Test]
        public void Runner_HidesChoicesWhoseConditionFails_AndWaitsForAPick()
        {
            var r = new DialogueRunner(Branching(), new FakeWorld(), null);
            r.Advance(); r.Advance();
            Assert.IsTrue(r.AwaitingChoice);
            CollectionAssert.AreEqual(new[] { "好", "不" }, r.Choices.Select(c => c.text).ToArray());
            Assert.IsTrue(r.Advance(), "Advance does nothing while a choice is pending");
            Assert.AreEqual("问你", r.Line.text);
            Assert.IsNull(r.Choose(5));
        }

        [Test]
        public void Runner_PlaysAPlainCallStraightThrough()
        {
            var call = new Call { id = "p", lines = new[] { new CallLine { text = "一" }, new CallLine { text = "二", player = true }, new CallLine { text = "三" } } };
            CollectionAssert.AreEqual(new[] { "一", "二", "三" }, Walk(call, new FakeWorld(), 0));
        }

        // ---- shipped files ----

        [Test]
        public void ShippedFiles_HaveNoErrors()
        {
            var errors = DialogueValidator.Check(new CallFile { calls = Calls.ToArray() }, Load<AssistantFile>("assistant"), Load<PropFile>("props"), ClinicNpcRuntime.Names)
                .Where(i => i.Error).Select(i => i.ToString()).ToArray();
            Assert.That(errors, Is.Empty);
        }

        static T Load<T>(string name) => JsonUtility.FromJson<T>(File.ReadAllText(Folder + name + ".json"));

        [TestCase("calls", typeof(CallFile))]
        [TestCase("assistant", typeof(AssistantFile))]
        [TestCase("props", typeof(PropFile))]
        public void ShippedFiles_AreInTheEditorsFormat(string name, System.Type type)
        {
            // The editor writes with DialogueJson; files already in that format save without a spurious diff.
            string text = File.ReadAllText(Folder + name + ".json").Replace("\r\n", "\n");
            Assert.AreEqual(text, DialogueJson.Write(JsonUtility.FromJson(text, type)));
        }

        [Test]
        public void Validator_CatchesBrokenJumpsAndConditions()
        {
            var calls = new CallFile { calls = new[] { new Call { id = "x", name = "x", caller = "Nobody", trigger = "phase:Night9",
                lines = new[] { new CallLine { text = "a", next = "missing", when = "flag:" }, new CallLine { text = "" } } } } };
            var all = DialogueValidator.Check(calls, new AssistantFile(), new PropFile(), ClinicNpcRuntime.Names).Select(i => i.ToString()).ToArray();
            Assert.That(all, Has.Some.Contains("missing"));
            Assert.That(all, Has.Some.Contains("Night9"));
            Assert.That(all, Has.Some.Contains("条件写错了"));
            Assert.That(all, Has.Some.Contains("台词是空的"));
            Assert.That(all, Has.Some.Contains("Nobody"));
        }

        // ---- triggers: the same moments the old hard-coded Trigger() used ----

        static bool Rings(string id, FakeWorld w) => DialogueCondition.Evaluate(Calls.First(c => c.id == id).trigger, w);

        static FakeWorld Night(string phase, float time, int delivered, params string[] facts)
        {
            var w = new FakeWorld(facts);
            w.facts.Add("phase:" + phase);
            w.numbers["time"] = time; w.numbers["delivered"] = delivered;
            return w;
        }

        [Test]
        public void Triggers_MatchTheOldSchedule()
        {
            Assert.IsFalse(Rings("rent-n1", Night("Night1Counter", 25, 0)), "rent-n1 needs more than 25 s");
            Assert.IsTrue(Rings("rent-n1", Night("Night1Counter", 25.1f, 0)));
            Assert.IsFalse(Rings("rent-n1", Night("Night2Open", 99, 0)));

            Assert.IsFalse(Rings("dealer-intro", Night("Night1Counter", 200, 3)), "dealer-intro waits for rent-n1");
            Assert.IsFalse(Rings("dealer-intro", Night("Night1Counter", 150, 0, "played:rent-n1")));
            Assert.IsTrue(Rings("dealer-intro", Night("Night1Counter", 150.1f, 0, "played:rent-n1")));
            Assert.IsTrue(Rings("dealer-intro", Night("Night1Counter", 30, 1, "played:rent-n1")));

            Assert.IsTrue(Rings("patrol-receiver", Night("Night1Counter", 0, 1, "delivered:receiver")));
            Assert.IsFalse(Rings("patrol-receiver", Night("Night1Counter", 0, 1, "delivered:power")));
            Assert.IsTrue(Rings("care-omamori", Night("Night1Counter", 0, 1, "delivered:omamori")));
            Assert.IsFalse(Rings("dealer-cartridge", Night("Night1Counter", 0, 1, "delivered:cartridge")), "the dealer calls back only after his intro");
            Assert.IsTrue(Rings("dealer-cartridge", Night("Night1Counter", 0, 1, "delivered:cartridge", "played:dealer-intro")));

            Assert.IsFalse(Rings("rent-n2", Night("Night2Open", 8, 0)));
            Assert.IsTrue(Rings("rent-n2", Night("Night2Open", 8.1f, 0)));
            Assert.IsFalse(Rings("rent-n2", Night("Night1Counter", 99, 0)));
            CollectionAssert.AreEqual(new[] { "rent-n1", "dealer-intro", "patrol-receiver", "care-omamori", "dealer-cartridge", "rent-n2" },
                Calls.Select(c => c.id).ToArray(), "file order is ring priority, as the old if-chain was");
        }
    }
}
