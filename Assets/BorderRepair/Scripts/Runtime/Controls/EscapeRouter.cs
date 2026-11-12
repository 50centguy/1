using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BorderRepair.Controls
{
    /// <summary>
    /// One press of Esc closes one thing: the topmost that is open. Everything that listens for Esc registers what it
    /// is (its layer) and whether it is open, and asks Pressed(layer) instead of reading the key. The open layers are
    /// sampled once per frame, before any handler acts, so closing the top one cannot let the next one through in the
    /// same frame whatever order the Updates run in.
    /// </summary>
    public static class EscapeRouter
    {
        /// <summary>Higher closes first.</summary>
        public enum Layer
        {
            Cursor = 0,     // walking: let go of the mouse
            Closeup = 10,   // the surgery close-up
            Gesture = 20,   // a hand motion in progress on the bench
            Panel = 30,     // the manual
            Menu = 40,      // the SYS menu
            Shop = 50,      // the parts terminal
        }

        sealed class Entry { public Layer layer; public UnityEngine.Object owner; public Func<bool> open; }
        static readonly List<Entry> entries = new List<Entry>();
        static int sampledFrame = -1, top = -1;
        static bool pressed;

        /// <summary>Tests replace these; null means the real keyboard and frame counter.</summary>
        public static Func<bool> KeySource;
        public static Func<int> FrameSource;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset() { entries.Clear(); sampledFrame = -1; top = -1; pressed = false; KeySource = null; FrameSource = null; }

        /// <summary>Says "owner is something Esc closes, on this layer, while open() is true". Forgotten when owner is destroyed.</summary>
        public static void Register(Layer layer, UnityEngine.Object owner, Func<bool> open)
        {
            if (owner == null || open == null) return;
            entries.RemoveAll(e => ReferenceEquals(e.owner, owner) && e.layer == layer);
            entries.Add(new Entry { layer = layer, owner = owner, open = open });
        }

        /// <summary>True when Esc went down this frame and nothing on a higher layer was open when the frame began.</summary>
        public static bool Pressed(Layer layer)
        {
            Sample();
            return pressed && (int)layer >= top;
        }

        static void Sample()
        {
            int frame = FrameSource != null ? FrameSource() : Time.frameCount;
            if (frame == sampledFrame) return;
            sampledFrame = frame;
            pressed = KeySource != null ? KeySource() : Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            top = -1;
            if (!pressed) return;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i];
                if (e.owner == null) { entries.RemoveAt(i); continue; }   // destroyed
                bool open;
                try { open = e.open(); } catch (Exception) { open = false; }
                if (open && (int)e.layer > top) top = (int)e.layer;
            }
        }
    }
}
