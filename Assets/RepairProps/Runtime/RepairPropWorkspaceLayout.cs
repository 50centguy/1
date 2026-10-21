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
