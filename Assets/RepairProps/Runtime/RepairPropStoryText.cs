using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BorderRepair.RepairProps
{
    /// <summary>
    /// The night's story text, kept out of code so it can be rewritten without a programmer: each prop's backstory,
    /// the marks on its parts, the notes its tests add, the phone calls, and UNIT 07's lines. The files are
    /// Assets/RepairProps/Story/Resources/RepairPropsStory/{props,calls,assistant}.json; Resources ships them in every
    /// build. Each file is read once; a missing file or entry logs an error and comes back empty rather than throwing.
    /// </summary>
    public static class RepairPropStoryText
    {
        public const string ResourceFolder = "RepairPropsStory";

        [Serializable] public sealed class Finding { public string node, text; }
        [Serializable] public sealed class TestNote { public string test, pass, fail; }
        [Serializable] public sealed class Prop { public string kind, backstory; public Finding[] findings; public TestNote[] testNotes; }
        [Serializable] public sealed class PropFile { public Prop[] props; }

        /// <summary>
        /// One line of a call. Only player/text are needed for a straight call; the rest make it branch:
        /// id labels the line for a jump, when skips it unless the condition holds (DialogueCondition), set raises or
        /// clears flags as it plays ("a, !b"), next jumps after it (a line id, or "end"), choices make the player pick.
        /// </summary>
        [Serializable] public sealed class CallLine
        {
            public bool player;
            public string text;
            public string id, when, set, next;
            public Choice[] choices;
        }

        /// <summary>A reply the player can pick: shown only when its condition holds; said as the player's line, then jumps to next.</summary>
        [Serializable] public sealed class Choice { public string text, when, set, next; }

        /// <summary>A phone call. trigger is the condition that rings it (checked in file order, once per run); empty never rings by itself.</summary>
        [Serializable] public sealed class Call { public string id, caller, name, title, trigger; public CallLine[] lines; }
        [Serializable] public sealed class CallFile { public Call[] calls; }

        [Serializable] public sealed class Line { public string face, text; }
        [Serializable] public sealed class Group { public string key; public Line[] lines; }
        [Serializable] public sealed class Hint { public string key, text; }
        [Serializable] public sealed class CallBanter { public string afterCall, text; }
        [Serializable] public sealed class AssistantFile { public Group[] groups; public Hint[] hints; public string[] banter; public CallBanter[] banterAfterCall; }

        static PropFile props;
        static CallFile calls;
        static AssistantFile assistant;

        /// <summary>Drops the cached files; the next lookup reads them again.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reload() { props = null; calls = null; assistant = null; }

        static T Load<T>(string name) where T : class, new()
        {
            var asset = Resources.Load<TextAsset>(ResourceFolder + "/" + name);
            if (asset == null) { Debug.LogError($"[Story] Missing Resources/{ResourceFolder}/{name}.json"); return new T(); }
            try { return JsonUtility.FromJson<T>(asset.text) ?? new T(); }
            catch (Exception e) { Debug.LogError($"[Story] {name}.json does not parse: {e.Message}"); return new T(); }
        }

        static PropFile PropData => props ??= Load<PropFile>("props");
        static CallFile CallData => calls ??= Load<CallFile>("calls");
        static AssistantFile AssistantData => assistant ??= Load<AssistantFile>("assistant");

        static string Missing(string what) { Debug.LogError("[Story] No entry for " + what); return null; }

        public static Prop GetProp(RepairPropKind kind)
        {
            string name = kind.ToString();
            var prop = (PropData.props ?? Array.Empty<Prop>()).FirstOrDefault(p => p.kind == name);
            if (prop == null) Missing("prop " + name);
            return prop;
        }

        public static IReadOnlyList<Call> Calls => CallData.calls ?? Array.Empty<Call>();

        /// <summary>Tests only: replaces the calls until the next Reload.</summary>
        public static void UseCallsForTests(CallFile file) => calls = file ?? new CallFile();

        /// <summary>The lines UNIT 07 says for one moment (see the keys in assistant.json); empty if there are none.</summary>
        public static IReadOnlyList<Line> AssistantGroup(string key)
        {
            var group = (AssistantData.groups ?? Array.Empty<Group>()).FirstOrDefault(g => g.key == key);
            if (group == null) return Array.Empty<Line>();
            return group.lines ?? Array.Empty<Line>();
        }

        public static string AssistantHint(string key)
        {
            var hint = (AssistantData.hints ?? Array.Empty<Hint>()).FirstOrDefault(h => h.key == key);
            return hint != null ? hint.text : Missing("assistant hint " + key);
        }

        public static IReadOnlyList<string> Banter => AssistantData.banter ?? Array.Empty<string>();
        public static IReadOnlyList<CallBanter> BanterAfterCall => AssistantData.banterAfterCall ?? Array.Empty<CallBanter>();

        public static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;
    }
}
