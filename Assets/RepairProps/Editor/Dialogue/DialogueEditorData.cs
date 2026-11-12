using UnityEngine;
using static BorderRepair.RepairProps.RepairPropStoryText;

namespace BorderRepair.RepairProps.Editor
{
    /// <summary>The dialogue editor's working copy. A ScriptableObject so Unity's Undo can snapshot every edit.</summary>
    public sealed class DialogueEditorData : ScriptableObject
    {
        public CallFile calls = new CallFile();
        public AssistantFile assistant = new AssistantFile();
        public PropFile props = new PropFile();
    }
}
