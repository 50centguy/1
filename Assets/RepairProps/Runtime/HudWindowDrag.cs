using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BorderRepair.RepairProps
{
    // Drag handle for a HUD window: put it on the title bar. Dragging lifts the window out of its column (the layout
    // element starts ignoring layout, the others close up) and moves it freely inside the root canvas; a double click
    // on the bar drops it back into the column. Positions are kept per window name for the session, so a window opened
    // again (next item, shop reopened) comes back where the player left it.
    [AddComponentMenu("")]
    public sealed class HudWindowDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IPointerClickHandler
    {
        static readonly Dictionary<string, Vector2> placed = new Dictionary<string, Vector2>();
        public RectTransform window;
        public string key;
        bool restored;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetPlaces() => placed.Clear();

        public static void Attach(Graphic bar, RectTransform window, string key)
        {
            bar.raycastTarget = true;
            var drag = bar.gameObject.GetComponent<HudWindowDrag>();
            if (drag == null) drag = bar.gameObject.AddComponent<HudWindowDrag>();
            drag.window = window; drag.key = key;
        }

        public bool Floating
        {
            get { var e = window != null ? window.GetComponent<LayoutElement>() : null; return e == null || e.ignoreLayout; }
        }

        void LateUpdate()
        {
            // Bring a remembered placement back once the layout has sized the window.
            if (restored || window == null) return;
            restored = true;
            if (placed.TryGetValue(key, out var at)) { Lift(); window.position = at; Clamp(); }
        }

        void Lift()
        {
            var element = window.GetComponent<LayoutElement>();
            if (element != null && !element.ignoreLayout)
            {
                var size = window.rect.size;
                var position = window.position;
                element.ignoreLayout = true;
                window.anchorMin = window.anchorMax = new Vector2(.5f, .5f);
                window.sizeDelta = size;
                window.position = position;
            }
            window.SetAsLastSibling();
        }

        public void OnBeginDrag(PointerEventData eventData) { if (window != null) Lift(); }

        public void OnDrag(PointerEventData eventData)
        {
            if (window == null) return;
            window.position += (Vector3)eventData.delta;   // overlay canvases: world units are screen pixels
            Clamp();
            placed[key] = window.position;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.clickCount < 2 || window == null) return;
            var element = window.GetComponent<LayoutElement>();
            if (element == null) return;
            element.ignoreLayout = false;
            placed.Remove(key);
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)window.parent);
        }

        // Keep the whole window on screen.
        void Clamp()
        {
            var canvas = window.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var root = (RectTransform)canvas.rootCanvas.transform;
            var corners = new Vector3[4];
            window.GetWorldCorners(corners);
            var bounds = new Vector3[4];
            root.GetWorldCorners(bounds);
            var shift = Vector3.zero;
            if (corners[0].x < bounds[0].x) shift.x = bounds[0].x - corners[0].x;
            if (corners[2].x > bounds[2].x) shift.x = bounds[2].x - corners[2].x;
            if (corners[0].y < bounds[0].y) shift.y = bounds[0].y - corners[0].y;
            if (corners[2].y > bounds[2].y) shift.y = bounds[2].y - corners[2].y;
            window.position += shift;
        }
    }
}
