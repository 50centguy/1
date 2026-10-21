using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BorderRepair.RepairProps
{
    // Gives the older overlay UI in the four-prop desktop scene (room HUD, leave/menu buttons, shop, picture panel,
    // appearance editor) the same visor look as the repair workspace: line frames on a see-through field. Purely
    // visual and runtime-only: it adds a "Frame" child and recolours fills/text once per element, never touches
    // handlers, layout or scene assets. Code that repaints a fill later still works; the frame stays.
    [AddComponentMenu("")]
    public sealed class RepairPropCyberSkin : MonoBehaviour
    {
        public const string ScenePath = "Assets/RepairProps/Scenes/RepairProps_Clinic.unity";
        readonly HashSet<int> skinned = new HashSet<int>();
        readonly List<Canvas> canvases = new List<Canvas>();
        float nextScan;

        public int SkinnedCount => skinned.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHooks() => SceneManager.sceneLoaded -= OnSceneLoaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() => SceneManager.sceneLoaded += OnSceneLoaded;

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!string.Equals(scene.path, ScenePath, System.StringComparison.OrdinalIgnoreCase)) return;
            if (UnityEngine.XR.XRSettings.isDeviceActive) return;
            var host = new GameObject("CyberHudSkin (runtime only)");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<RepairPropCyberSkin>();
        }

        void Update()
        {
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + .5f;
            canvases.Clear();
            canvases.AddRange(FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            foreach (var canvas in canvases)
            {
                if (!canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace) continue;
                // The workspace and the AR layer style themselves.
                if (canvas.GetComponentInParent<RepairPropContextPanel>(true) != null || canvas.GetComponentInParent<RepairPropArOverlay>(true) != null) continue;
                Skin(canvas.transform);
            }
        }

        void Skin(Transform root)
        {
            foreach (var button in root.GetComponentsInChildren<Button>(true))
            {
                if (!skinned.Add(button.GetInstanceID())) continue;
                if (button.targetGraphic is Image fill && fill.sprite == null || button.targetGraphic is Image f2 && f2.sprite != null && f2.sprite.name == "UISprite")
                {
                    var image = (Image)button.targetGraphic;
                    var color = image.color;
                    // One phosphor: keep the brightness a repainting script chose, drop its hue, push it see-through.
                    float lum = (color.r + color.g + color.b) / 3f;
                    image.color = new Color(lum * .42f, lum * .26f, lum * .09f, Mathf.Min(color.a, .62f));
                    RepairPropCyberStyle.Framed(image, image.color, RepairPropCyberStyle.AmberDim);
                }
                foreach (var text in button.GetComponentsInChildren<Text>(true))
                {
                    if (text.color.r > .8f && text.color.g > .8f && text.color.b > .8f) text.color = RepairPropCyberStyle.Ink;
                    // Left column buttons read as one list: labels start on the same inset instead of each centring.
                    var rect = (RectTransform)button.transform;
                    if (rect.anchorMin.x < .01f && rect.anchoredPosition.x < 40 && text.alignment == TextAnchor.MiddleCenter)
                    {
                        text.alignment = TextAnchor.MiddleLeft;
                        var tr = text.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = new Vector2(16, 0); tr.offsetMax = new Vector2(-8, 0);
                    }
                }
            }
            var face = RepairPropCyberStyle.UiFont;
            if (face != null)
                foreach (var text in root.GetComponentsInChildren<Text>(true))
                    if (skinned.Add(text.GetInstanceID())) text.font = face;
            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                if (image.name == "Frame" || image.GetComponent<Button>() != null || image.GetComponent<Selectable>() != null) continue;
                if (!skinned.Add(image.GetInstanceID())) continue;
                var rect = image.rectTransform.rect;
                var c = image.color;
                // Large dark backgrounds become the visor field; small images (icons, swatches, bars) are left alone.
                bool panel = image.sprite == null && rect.width > 120 && rect.height > 40 && c.a > .5f && c.r + c.g + c.b < 1.2f;
                if (!panel) continue;
                float l = (c.r + c.g + c.b) / 3f;
                RepairPropCyberStyle.Framed(image, new Color(l * .45f + .01f, l * .30f + .007f, l * .12f + .004f, Mathf.Min(c.a, .82f)), RepairPropCyberStyle.AmberDim);
                RepairPropCyberStyle.Texture(image.transform, RepairPropCyberStyle.Dots, new Color(1f, .62f, .16f, .08f), "DotGrid");
                // Panels open with the visor boot, except the repair view's own elements (their CanvasGroups belong to RepairPropLegacyHud).
                if (image.GetComponentInParent<BorderRepair.UI.RepairUIView>(true) == null) HudMotion.Boot(image.gameObject);
            }
        }
    }
}
