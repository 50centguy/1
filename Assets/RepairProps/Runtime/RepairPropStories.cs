namespace BorderRepair.RepairProps
{
    // What each of the night's four props carries besides its fault, told only through what a repairer may see:
    // marks on parts (inspection findings), metadata a read-only check reports, and what the owner says. Private
    // contents are never opened. Story notes: Docs/Integration/RepairPropsCyberBench/STORIES.md.
    // One thread runs through all four: someone, or something, answering in place of a person who is gone.
    // The text itself is in Assets/RepairProps/Story/Resources/RepairPropsStory/props.json (RepairPropStoryText).
    public static class RepairPropStories
    {
        public static string Backstory(RepairPropKind kind)
        {
            var prop = RepairPropStoryText.GetProp(kind);
            return prop != null ? RepairPropStoryText.NullIfEmpty(prop.backstory) : null;
        }

        /// <summary>The story mark on a part (variants such as _Connected / _Released share their stem's), or null.</summary>
        public static string Finding(RepairPropKind kind, string objectName)
        {
            var prop = RepairPropStoryText.GetProp(kind);
            if (prop == null || prop.findings == null || string.IsNullOrEmpty(objectName)) return null;
            foreach (var finding in prop.findings)
                if (objectName == finding.node || objectName.StartsWith(finding.node + "_", System.StringComparison.Ordinal))
                    return RepairPropStoryText.NullIfEmpty(finding.text);
            return null;
        }

        /// <summary>A line the test adds to its result (read-only metadata, or what the bench measures), or null.</summary>
        public static string TestNote(RepairPropKind kind, string testId, bool pass)
        {
            var prop = RepairPropStoryText.GetProp(kind);
            if (prop == null || prop.testNotes == null) return null;
            foreach (var note in prop.testNotes)
                if (note.test == testId) return RepairPropStoryText.NullIfEmpty(pass ? note.pass : note.fail);
            return null;
        }
    }
}
