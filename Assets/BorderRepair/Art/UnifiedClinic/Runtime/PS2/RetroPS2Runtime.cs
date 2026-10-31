using System;
using System.Collections.Generic;
using BorderRepair.FirstOrder;
using BorderRepair.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

namespace BorderRepair.Art.UnifiedClinic.PS2
{
    /// <summary>
    /// Play-mode-only hookup for the two desktop clinic scenes: marks their RetroClinicCameras for the PS2 pass
    /// and shows a small collapsible "画面" panel ("=" key). Nothing is written to scenes, prefabs or saves; the object
    /// lives in the loaded desktop scene and everything it added is removed when it is destroyed.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class RetroPS2Runtime : MonoBehaviour
    {
        static readonly Color Background = new Color(.10f, .11f, .11f, .94f);
        static readonly Color Normal = new Color(.23f, .25f, .25f, 1);
        static readonly Color Active = new Color(.12f, .48f, .40f, 1);
        static readonly string[] PresetNames = { "默认", "维修更清楚", "强像素(可选)" };

        public static RetroPS2Runtime Current { get; private set; }

        readonly List<RetroPS2CameraEffect> owned = new List<RetroPS2CameraEffect>();
        readonly List<FirstPersonWalker> cursorOwners = new List<FirstPersonWalker>();
        readonly List<GameObject> roots = new List<GameObject>();
        readonly List<RetroClinicCamera> looks = new List<RetroClinicCamera>();
        readonly List<FirstPersonWalker> walkers = new List<FirstPersonWalker>();
        readonly Button[] presets = new Button[3];
        Func<bool> cursorRequest;
        float nextScan;
        bool expanded, editInspection, refreshing, lastReadability;
        Font font;
        GameObject canvasObject, body;
        Image rootImage;
        Button enable, lowTextures, roomTab, inspectionTab;
        Text tab, status;
        Slider strength, lines, sharpness, levels, dither, softness;
        Text strengthValue, linesValue, sharpnessValue, levelsValue, ditherValue, softnessValue;

        public bool Expanded => expanded;
        public int MarkedCameraCount { get { Prune(); return owned.Count; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            Current = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (RetroPS2CameraEffect.IsDesktopScene(scene)) Ensure(scene);
        }

        static void OnSceneUnloaded(Scene scene)
        {
            if (Current != null) return;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var other = SceneManager.GetSceneAt(i);
                if (other.isLoaded && RetroPS2CameraEffect.IsDesktopScene(other)) { Ensure(other); return; }
            }
        }

        static void Ensure(Scene scene)
        {
            if (Current != null) { Current.Scan(); return; }
            if (XRSettings.isDeviceActive) return; // a VR session never gets the desktop look or panel
            var host = new GameObject("RetroPS2 Look (runtime only)");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<RetroPS2Runtime>();
        }

        void Awake()
        {
            if (Current != null && Current != this) { Destroy(gameObject); return; }
            Current = this;
            cursorRequest = () => expanded && isActiveAndEnabled;
            BuildPanel();
            RetroPS2Settings.Changed += Refresh;
            RetroPS2Settings.Changed += ApplyTextureResolution;
            Scan();
            Refresh();
            ApplyTextureResolution();
        }

        bool texturesLowered;
        int savedMipmapLimit;
        AnisotropicFiltering savedAniso;

        // PS2-era texture detail: one mip level down and no anisotropic filtering while the look is on. Runtime
        // quality settings only (restored on destroy); no texture asset or import setting is touched.
        void ApplyTextureResolution()
        {
            bool want = RetroPS2Settings.Enabled && RetroPS2Settings.LowResTextures && Current == this;
            if (want == texturesLowered) return;
            if (want)
            {
                savedMipmapLimit = QualitySettings.globalTextureMipmapLimit;
                savedAniso = QualitySettings.anisotropicFiltering;
                QualitySettings.globalTextureMipmapLimit = savedMipmapLimit + 1;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
            }
            else
            {
                QualitySettings.globalTextureMipmapLimit = savedMipmapLimit;
                QualitySettings.anisotropicFiltering = savedAniso;
            }
            texturesLowered = want;
        }

        void OnDestroy()
        {
            RetroPS2Settings.Changed -= Refresh;
            RetroPS2Settings.Changed -= ApplyTextureResolution;
            if (texturesLowered)
            {
                QualitySettings.globalTextureMipmapLimit = savedMipmapLimit;
                QualitySettings.anisotropicFiltering = savedAniso;
                texturesLowered = false;
            }
            foreach (var walker in cursorOwners)
                if (walker != null) walker.CursorRequests.Remove(cursorRequest);
            cursorOwners.Clear();
            foreach (var effect in owned)
                if (effect != null) Destroy(effect);
            owned.Clear();
            if (canvasObject != null) Destroy(canvasObject);
            if (Current == this) Current = null;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.equalsKey.wasPressedThisFrame && !FirstOrderInput.TextInputFocused) SetExpanded(!expanded);
            if (Time.unscaledTime >= nextScan) Scan();
            if (expanded && Readability() != lastReadability) Refresh();
        }

        public void SetExpanded(bool value)
        {
            expanded = value;
            if (expanded) editInspection = Readability();
            Refresh();
        }

        /// <summary>Marks every RetroClinicCamera in loaded desktop scenes and registers the cursor request.</summary>
        public void Scan()
        {
            nextScan = Time.unscaledTime + 1f;
            Prune();
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded || !RetroPS2CameraEffect.IsDesktopScene(scene)) continue;
                scene.GetRootGameObjects(roots);
                foreach (var root in roots)
                {
                    root.GetComponentsInChildren(true, looks);
                    foreach (var look in looks)
                        if (look.GetComponent<RetroPS2CameraEffect>() == null)
                            owned.Add(look.gameObject.AddComponent<RetroPS2CameraEffect>());
                    root.GetComponentsInChildren(true, walkers);
                    foreach (var walker in walkers)
                        if (!cursorOwners.Contains(walker)) { walker.CursorRequests.Add(cursorRequest); cursorOwners.Add(walker); }
                }
            }
            roots.Clear(); looks.Clear(); walkers.Clear();
        }

        void Prune()
        {
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] == null) owned.RemoveAt(i);
            for (int i = cursorOwners.Count - 1; i >= 0; i--) if (cursorOwners[i] == null) cursorOwners.RemoveAt(i);
        }

        bool Readability()
        {
            Prune();
            foreach (var effect in owned)
            {
                var look = effect.Look;
                if (look == null || !look.isActiveAndEnabled) continue;
                var camera = look.GetComponent<Camera>();
                if (camera != null && camera.isActiveAndEnabled) return look.ReadabilityMode;
            }
            return false;
        }

        RetroPS2Profile Editing => editInspection ? RetroPS2Settings.Inspection : RetroPS2Settings.Room;

        void Change(Action<RetroPS2Profile> edit)
        {
            if (refreshing) return;
            edit(Editing);
            RetroPS2Settings.NotifyChanged();
        }

        // ---------- panel ----------

        void BuildPanel()
        {
            font = UIFontProvider.GetCjkFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            canvasObject = new GameObject("RetroPS2Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 95;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;

            var root = Rect("画面设置", canvasObject.transform);
            root.anchorMin = root.anchorMax = root.pivot = Vector2.one;
            root.anchoredPosition = new Vector2(-12, -12);
            rootImage = root.gameObject.AddComponent<Image>();
            rootImage.color = Background;
            var layout = root.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 4;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            root.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var header = Row(root, 24);
            var toggle = Command(header, "", () => SetExpanded(!expanded));
            tab = toggle.GetComponentInChildren<Text>();

            body = Rect("内容", root).gameObject;
            var column = body.AddComponent<VerticalLayoutGroup>();
            column.spacing = 4;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;

            var power = Row(body.transform, 24);
            enable = Command(power, "", () => { RetroPS2Settings.Enabled = !RetroPS2Settings.Enabled; RetroPS2Settings.NotifyChanged(); });
            Command(power, "恢复默认", RetroPS2Settings.ResetToDefaults, 84);
            lowTextures = Command(power, "", () => { RetroPS2Settings.LowResTextures = !RetroPS2Settings.LowResTextures; RetroPS2Settings.NotifyChanged(); }, 96);

            var presetRow = Row(body.transform, 24);
            Label(presetRow, "预设", 40);
            for (int i = 0; i < presets.Length; i++)
            {
                var preset = (RetroPS2Preset)i;
                presets[i] = Command(presetRow, PresetNames[i], () => RetroPS2Settings.ApplyPreset(preset));
            }

            var target = Row(body.transform, 24);
            Label(target, "调整", 40);
            roomTab = Command(target, "房间行走", () => { editInspection = false; Refresh(); });
            inspectionTab = Command(target, "维修近看", () => { editInspection = true; Refresh(); });

            strength = SliderRow(body.transform, "强度", 0, 1, false, out strengthValue, v => Change(p => p.strength = v));
            lines = SliderRow(body.transform, "低分辨率", 120, 1080, true, out linesValue, v => Change(p => p.virtualLines = (int)v));
            sharpness = SliderRow(body.transform, "像素边缘", 0, 1, false, out sharpnessValue, v => Change(p => p.pixelSharpness = v));
            levels = SliderRow(body.transform, "色阶", 4, 256, true, out levelsValue, v => Change(p => p.colorLevels = (int)v));
            dither = SliderRow(body.transform, "抖色", 0, 1, false, out ditherValue, v => Change(p => p.dither = v));
            softness = SliderRow(body.transform, "柔光", 0, 2, false, out softnessValue, v => Change(p => p.softness = v));

            status = Label(body.transform, "", 0);
            status.fontSize = 12;
            status.resizeTextMaxSize = 12;
            var statusSize = status.GetComponent<LayoutElement>();
            statusSize.minHeight = statusSize.preferredHeight = 34;
        }

        void Refresh()
        {
            if (body == null || refreshing) return;
            refreshing = true;
            try
            {
                bool readability = lastReadability = Readability();
                body.SetActive(expanded);
                rootImage.enabled = expanded; // collapsed: only the small tab receives pointer hits
                var root = (RectTransform)rootImage.transform;
                root.sizeDelta = new Vector2(expanded ? 320 : 92, root.sizeDelta.y);
                tab.text = expanded ? "画面 · 收起 (=)" : "画面 =";
                if (!expanded) return;

                bool on = RetroPS2Settings.Enabled;
                enable.GetComponentInChildren<Text>().text = on ? "PS2 效果：开" : "PS2 效果：关（原画面）";
                enable.GetComponent<Image>().color = on ? Active : Normal;
                lowTextures.GetComponentInChildren<Text>().text = RetroPS2Settings.LowResTextures ? "低清贴图：开" : "低清贴图：关";
                lowTextures.GetComponent<Image>().color = RetroPS2Settings.LowResTextures && on ? Active : Normal;
                for (int i = 0; i < presets.Length; i++)
                    presets[i].GetComponent<Image>().color = (int)RetroPS2Settings.Preset == i ? Active : Normal;
                roomTab.GetComponent<Image>().color = editInspection ? Normal : Active;
                inspectionTab.GetComponent<Image>().color = editInspection ? Active : Normal;

                var p = Editing;
                strength.SetValueWithoutNotify(p.strength);
                lines.SetValueWithoutNotify(p.virtualLines);
                sharpness.SetValueWithoutNotify(p.pixelSharpness);
                levels.SetValueWithoutNotify(p.colorLevels);
                dither.SetValueWithoutNotify(p.dither);
                softness.SetValueWithoutNotify(p.softness);
                strengthValue.text = (p.strength * 100).ToString("0") + "%";
                linesValue.text = p.virtualLines + " 行";
                sharpnessValue.text = p.pixelSharpness < .5f ? "柔 " + (p.pixelSharpness * 100).ToString("0") : "硬 " + (p.pixelSharpness * 100).ToString("0");
                levelsValue.text = p.colorLevels >= 256 ? "不量化" : p.colorLevels + " 级";
                ditherValue.text = (p.dither * 100).ToString("0") + "%";
                softnessValue.text = "×" + p.softness.ToString("0.00");
                foreach (var slider in new[] { strength, lines, sharpness, levels, dither, softness }) slider.interactable = on;
                status.text = "当前画面：" + (readability ? "维修近看" : "房间行走") + "。关 = 恢复原画面（保留原柔光）。\n界面文字不受影响；鼠标在面板上不会点进场景。";
            }
            finally { refreshing = false; }
        }

        // ---------- small uGUI helpers (same idiom as CosmeticPanel) ----------

        static RectTransform Rect(string name, Transform parent)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            return rect;
        }

        static void Size(RectTransform rect, float height, float width = 0)
        {
            var size = rect.gameObject.AddComponent<LayoutElement>();
            size.minHeight = size.preferredHeight = height;
            if (width > 0) size.minWidth = size.preferredWidth = width;
            else { size.minWidth = 0; size.flexibleWidth = 1; }
        }

        static RectTransform Row(Transform parent, float height)
        {
            var row = Rect("行", parent);
            Size(row, height);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            return row;
        }

        static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        Text Label(Transform parent, string text, float width)
        {
            var rect = Rect("文字", parent);
            var label = rect.gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = 14;
            label.color = Color.white;
            label.text = text;
            label.supportRichText = false;
            label.raycastTarget = false;
            label.alignment = TextAnchor.MiddleLeft;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 10;
            label.resizeTextMaxSize = label.fontSize;
            Size(rect, 22, width);
            return label;
        }

        Button Command(Transform parent, string text, UnityEngine.Events.UnityAction action, float width = 0)
        {
            var rect = Rect(string.IsNullOrEmpty(text) ? "按钮" : text, parent);
            Size(rect, 22, width);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = Normal;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(action);
            button.onClick.AddListener(Deselect);
            var label = Label(rect, text, 0);
            label.alignment = TextAnchor.MiddleCenter;
            Stretch((RectTransform)label.transform, 2);
            return button;
        }

        // Keeps keyboard focus off the panel so Space/R/Tab shortcuts keep reaching gameplay.
        static void Deselect()
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        Slider SliderRow(Transform parent, string name, float min, float max, bool whole, out Text value,
            UnityEngine.Events.UnityAction<float> changed)
        {
            var row = Row(parent, 22);
            Label(row, name, 64);
            var rect = Rect(name, row);
            Size(rect, 22);
            rect.gameObject.AddComponent<Image>().color = Color.clear;
            var slider = rect.gameObject.AddComponent<Slider>();
            var track = Rect("轨道", rect);
            Stretch(track, 0);
            track.offsetMin = new Vector2(0, 9);
            track.offsetMax = new Vector2(0, -9);
            track.gameObject.AddComponent<Image>().color = Normal;
            var area = Rect("滑动区", rect);
            Stretch(area, 8);
            var thumb = Rect("滑块", area);
            thumb.sizeDelta = new Vector2(16, 14);
            var graphic = thumb.gameObject.AddComponent<Image>();
            graphic.color = new Color(.72f, .83f, .78f);
            slider.handleRect = thumb;
            slider.targetGraphic = graphic;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = whole;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.onValueChanged.AddListener(v => { if (!refreshing) changed(v); });
            value = Label(row, "", 62);
            value.alignment = TextAnchor.MiddleRight;
            return slider;
        }
    }
}
