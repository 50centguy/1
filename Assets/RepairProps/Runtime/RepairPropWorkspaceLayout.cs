using System;
using UnityEngine;

namespace BorderRepair.RepairProps
{
    // The desktop appearance editor compiles after this assembly. This hook lets it occupy the repair workspace's
    // slot and lets the workspace switch it off, without a reference cycle. VR panels never use it.
    public static class RepairPropWorkspaceLayout
    {
        static object appearanceOwner;
        static Func<bool> appearanceEditing;
        static Action closeAppearance;

        public static RepairPropContextPanel Host { get; private set; }

        /// <summary>
        /// The short bench HUD: the case, the tools, what can be done now, the last message. The phase strip, the
        /// test read-outs, the part picker, the order/records pages and the visor's decorative read-outs fold away
        /// until the player asks for them. Off by default so every caller that never asked sees the full panel; a
        /// game started in the clinic scene switches it on (ClinicTutorialRuntime).
        /// </summary>
        public static bool Compact
        {
            get => compact;
            set { compact = value; if (value) CompactOffered = true; }
        }
        static bool compact;
        /// <summary>True once the short HUD has been on in this game: only then does the panel carry its details switch,
        /// so a game that never asked for it keeps the full panel exactly as it was.</summary>
        public static bool CompactOffered { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetCompact() { compact = false; CompactOffered = false; }
        public static bool Hosting => Host != null && Host.Visible;
        public static bool AppearanceOpen => Hosting && Host.AppearanceOpen;
        // Null unless a visible workspace currently shows the appearance page.
        public static RectTransform AppearanceSlot => AppearanceOpen ? Host.AppearanceSlot : null;
        public static bool AppearanceEditing => appearanceEditing != null && appearanceEditing();

        public static void BindAppearance(object owner, Func<bool> editing, Action close)
        {
            appearanceOwner = owner; appearanceEditing = editing; closeAppearance = close;
        }

        public static void UnbindAppearance(object owner)
        {
            if (!ReferenceEquals(owner, appearanceOwner)) return;
            appearanceOwner = null; appearanceEditing = null; closeAppearance = null;
        }

        public static void CloseAppearance() => closeAppearance?.Invoke();

        internal static void Register(RepairPropContextPanel panel) => Host = panel;
        internal static void Unregister(RepairPropContextPanel panel) { if (Host == panel) Host = null; }
    }
}
