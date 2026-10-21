using System.Collections.Generic;
using System.Linq;
using BorderRepair.UI;
using UnityEngine;

namespace BorderRepair.RepairProps
{
    // Visually retires prototype HUD panels that the repair workspace replaces, for as long as a workspace owns them.
    // RepairUIView, its GameObject and the scanner/station stay enabled: CanInteract and VR ownership read them.
    // Scan overlay, result/summary panels and the counter's dialogue/ledger/retry exits are left untouched.
    public static class RepairPropLegacyHud
    {
        public static readonly string[] Panels = { "TopBar", "CustomerPanel", "FindingsPanel", "Hint", "ActionRow", "Feedback" };

        sealed class Saved
        {
            public CanvasGroup group;
            public bool added, interactable, blocksRaycasts;
            public float alpha;
        }
        sealed class Entry
        {
            public readonly HashSet<object> owners = new HashSet<object>();
            public readonly List<Saved> saved = new List<Saved>();
        }
        static readonly Dictionary<RepairUIView, Entry> entries = new Dictionary<RepairUIView, Entry>();

        public static bool IsSuppressed(RepairUIView view) => view != null && entries.ContainsKey(view);

        public static void Acquire(RepairUIView view, object owner)
        {
            foreach (var dead in entries.Keys.Where(v => v == null).ToList()) entries.Remove(dead);
            if (view == null || owner == null) return;
            if (!entries.TryGetValue(view, out var entry))
            {
                entry = new Entry();
                foreach (string name in Panels)
                {
                    var panel = view.transform.Find(name);
                    if (panel == null) continue;
                    var group = panel.GetComponent<CanvasGroup>();
                    var saved = new Saved { group = group, added = group == null };
                    if (saved.added) saved.group = group = panel.gameObject.AddComponent<CanvasGroup>();
                    saved.alpha = group.alpha; saved.interactable = group.interactable; saved.blocksRaycasts = group.blocksRaycasts;
                    group.alpha = 0; group.interactable = false; group.blocksRaycasts = false;
                    entry.saved.Add(saved);
                }
                entries.Add(view, entry);
            }
            entry.owners.Add(owner);
        }

        public static void Release(RepairUIView view, object owner)
        {
            if (ReferenceEquals(view, null) || !entries.TryGetValue(view, out var entry)) return;
            entry.owners.Remove(owner);
            if (entry.owners.Count > 0) return;
            entries.Remove(view);
            foreach (var saved in entry.saved)
            {
                if (saved.group == null) continue;
                saved.group.alpha = saved.alpha; saved.group.interactable = saved.interactable; saved.group.blocksRaycasts = saved.blocksRaycasts;
                if (!saved.added) continue;
                if (Application.isPlaying) Object.Destroy(saved.group); else Object.DestroyImmediate(saved.group);
            }
        }
    }
}
