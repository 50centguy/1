using System;
using System.Collections.Generic;
using Call = BorderRepair.RepairProps.RepairPropStoryText.Call;
using CallLine = BorderRepair.RepairProps.RepairPropStoryText.CallLine;
using Choice = BorderRepair.RepairProps.RepairPropStoryText.Choice;

namespace BorderRepair.RepairProps.Dialogue
{
    /// <summary>
    /// Walks one call's lines: skips lines whose condition fails, follows jumps, applies flag changes, and stops on a
    /// line with choices until one is picked. No UI and no timing, so the game's call card and the editor's preview
    /// drive the same rules.
    /// </summary>
    public sealed class DialogueRunner
    {
        public const string End = "end";

        readonly Call call;
        readonly IDialogueWorld world;
        readonly Action<string, bool> setFlag;
        readonly List<Choice> visible = new List<Choice>();
        int index = -1;
        string chosenNext;
        bool picked;

        public DialogueRunner(Call call, IDialogueWorld world, Action<string, bool> setFlag)
        {
            this.call = call ?? throw new ArgumentNullException(nameof(call));
            this.world = world;
            this.setFlag = setFlag;
        }

        public Call Call => call;
        CallLine[] Lines => call.lines ?? Array.Empty<CallLine>();
        /// <summary>The line on screen, or null before the first Advance and after the end.</summary>
        public CallLine Line => index >= 0 && index < Lines.Length ? Lines[index] : null;
        public int Index => index;
        public bool Ended { get; private set; }
        /// <summary>The replies on offer for the current line (only those whose condition holds); empty when there is no choice to make.</summary>
        public IReadOnlyList<Choice> Choices => visible;
        public bool AwaitingChoice => visible.Count > 0;

        /// <summary>Moves to the next line that plays. False once the call is over. Does nothing while a choice is pending.</summary>
        public bool Advance()
        {
            if (Ended) return false;
            if (AwaitingChoice) return true;
            var lines = Lines;
            string jump = picked ? chosenNext : Line?.next;
            picked = false; chosenNext = null;

            int i;
            if (string.IsNullOrWhiteSpace(jump)) i = index + 1;
            else if (string.Equals(jump.Trim(), End, StringComparison.OrdinalIgnoreCase)) i = lines.Length;
            else
            {
                i = IndexOf(jump.Trim());
                if (i < 0) { UnityEngine.Debug.LogWarning($"[Dialogue] {call.id}: jump to unknown line \"{jump}\"; the call ends."); i = lines.Length; }
            }
            while (i < lines.Length && !DialogueCondition.Evaluate(lines[i].when, world)) i++;
            index = i;
            visible.Clear();
            if (i >= lines.Length) { Ended = true; return false; }
            var line = lines[i];
            Apply(line.set);
            if (line.choices != null)
                foreach (var c in line.choices)
                    if (c != null && DialogueCondition.Evaluate(c.when, world)) visible.Add(c);
            return true;
        }

        /// <summary>Picks a reply: applies its flags and remembers where it leads. Returns it (its text is the player's line), or null if out of range.</summary>
        public Choice Choose(int i)
        {
            if (i < 0 || i >= visible.Count) return null;
            var choice = visible[i];
            visible.Clear();
            Apply(choice.set);
            picked = true;
            chosenNext = !string.IsNullOrWhiteSpace(choice.next) ? choice.next : Line?.next;
            return choice;
        }

        int IndexOf(string id)
        {
            var lines = Lines;
            for (int i = 0; i < lines.Length; i++) if (lines[i].id == id) return i;
            return -1;
        }

        void Apply(string set)
        {
            if (setFlag == null) return;
            foreach (var (flag, on) in DialogueCondition.ParseSet(set)) setFlag(flag, on);
        }
    }
}
