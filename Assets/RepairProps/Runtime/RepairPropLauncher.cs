using System.Collections.Generic;
using System.Linq;
using BorderRepair.TwoNight;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BorderRepair.RepairProps
{
    // The left-hand system launcher of the desktop clinic: one small icon (three bars and a corner tick, "SYS") under
    // the director's leave / return-to-bench button. Clicking it opens a small menu window with the rarely used
    // entries — the parts terminal and the main menu — instead of keeping them as permanent buttons. The director's
    // own main-menu button stays in the scene (hidden, not removed) and the menu entry clicks it, so its behaviour is
    // unchanged.
    [AddComponentMenu("")]
    public sealed class RepairPropLauncher : MonoBehaviour
    {
        public const string ScenePath = RepairBenchV2Runtime.ScenePath;
        public static RepairPropLauncher Current { get; private set; }

        Canvas canvas;
        RectTransform menu;
        Button icon, shopEntry, menuEntry;
        Text shopNote;
        RepairPropEconomyController shop;
        Button directorMenu, nightEnd;
        UnifiedClinicDirector director;
        CanvasGroup directorMenuGroup;
        bool open;

        public Button IconButton => icon;
        public Button ShopEntry => shopEntry;
        public Button MenuEntry => menuEntry;
        public bool MenuOpen => open;

        /// <summary>The desktop clinic the player left for the main menu; the menu's "back to the clinic" returns here.</summary>
        public static string ReturnScenePath { get; private set; }

        /// <summary>True when the menu's "continue" into night 2 is bringing the player back to this clinic: that run
        /// is kept instead of the scene's usual fresh start.</summary>
        public static bool Resuming(Scene scene) =>
            ReturnScenePath != null && string.Equals(scene.path, ReturnScenePath, System.StringComparison.OrdinalIgnoreCase) &&
            TwoNightRun.Current != null && TwoNightRun.Current.phase == TwoNightPhase.Night2Open;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHooks() { SceneManager.sceneLoaded -= OnSceneLoaded; Current = null; ReturnScenePath = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() => SceneManager.sceneLoaded += OnSceneLoaded;

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // The clinic menu always loads the main game's clinic scene by name. Coming from this desktop clinic, that
            // load is switched off before any Start runs and this clinic is loaded instead (same run state).
            if (ReturnScenePath != null && scene.name == TwoNightScenes.Clinic)
            {
                foreach (var root in scene.GetRootGameObjects()) root.SetActive(false);
                Return(ReturnScenePath);
                return;
            }
            if (ReturnScenePath != null && scene.name != TwoNightScenes.ClinicMenu && !string.Equals(scene.path, ReturnScenePath, System.StringComparison.OrdinalIgnoreCase))
                ReturnScenePath = null;   // the player went somewhere else on purpose
            if (!string.Equals(scene.path, ScenePath, System.StringComparison.OrdinalIgnoreCase)) return;
            if (UnityEngine.XR.XRSettings.isDeviceActive) return;
            var host = new GameObject("SystemLauncher (runtime only)");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<RepairPropLauncher>();
        }

        void Awake()
        {
            Current = this;
            var font = RepairPropCyberStyle.UiFontOr(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            var host = new GameObject("LauncherCanvas", typeof(RectTransform));
            host.transform.SetParent(transform, false);
            canvas = host.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 85;
            var scaler = host.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = 0;   // the director HUD scaling (TwoNightUi.Canvas), so the left column lines up
            host.AddComponent<GraphicRaycaster>();

            // Icon: same left edge as the director's buttons (x 16), below the leave / return button.
            var iconRect = Rect("SystemIcon", host.transform, 16, -70, 44, 44);
            var iconImage = iconRect.gameObject.AddComponent<Image>();
            RepairPropCyberStyle.Framed(iconImage, new Color(.02f, .015f, .01f, .7f), RepairPropCyberStyle.Amber);
            icon = iconRect.gameObject.AddComponent<Button>(); icon.targetGraphic = iconImage;
            icon.onClick.AddListener(() => Toggle(!open));
            for (int i = 0; i < 3; i++) Bar(iconRect, 11, -11 - i * 7, i == 1 ? 14 : 22, 3);
            Bar(iconRect, 30, -25, 3, 8);
            var sys = RepairPropCyberStyle.Caption(iconRect, "SYS", font, 12, RepairPropCyberStyle.Amber, TextAnchor.MiddleLeft);
            var sysRect = sys.rectTransform; sysRect.anchorMin = sysRect.anchorMax = new Vector2(1, .5f); sysRect.pivot = new Vector2(0, .5f);
            sysRect.anchoredPosition = new Vector2(8, 0); sysRect.sizeDelta = new Vector2(60, 16);

            // Menu window: title bar, two entries, the shop entry's availability note.
            menu = Rect("SystemMenu", host.transform, 16, -124, 220, 116);
            RepairPropCyberStyle.Framed(menu.gameObject.AddComponent<Image>(), RepairPropCyberStyle.FieldDeep, RepairPropCyberStyle.Amber);
            RepairPropCyberStyle.Texture(menu, RepairPropCyberStyle.Dither, new Color(1f, .42f, .10f, .05f), "Dither");
            HudWindowDrag.Attach(RepairPropCyberStyle.TitleBar(menu, "SYS // 系统菜单", font), menu, "launcher");
            HudMotion.Boot(menu.gameObject);
            shopEntry = Entry(menu, "01  配件采购", -8, font, OpenShop); shopEntry.name = "配件采购";
            shopNote = RepairPropCyberStyle.Caption(shopEntry.transform, "", font, 12, RepairPropCyberStyle.Dim, TextAnchor.MiddleRight);
            var noteRect = shopNote.rectTransform; noteRect.anchorMin = Vector2.zero; noteRect.anchorMax = Vector2.one; noteRect.offsetMin = Vector2.zero; noteRect.offsetMax = new Vector2(-10, 0);
            menuEntry = Entry(menu, "02  主菜单", -44, font, OpenMainMenu); menuEntry.name = "主菜单";
            RepairPropCyberStyle.Caption(menu, "ESC / 再点图标收起", font, 12, RepairPropCyberStyle.AmberDim, TextAnchor.MiddleLeft).rectTransform.anchoredPosition = new Vector2(0, 0);
            var hint = menu.GetChild(menu.childCount - 1) as RectTransform;
            hint.anchorMin = hint.anchorMax = new Vector2(0, 0); hint.pivot = new Vector2(0, 0); hint.anchoredPosition = new Vector2(10, 6); hint.sizeDelta = new Vector2(200, 16);
            menu.gameObject.SetActive(false);
        }

        void Start()
        {
            shop = FindObjectsByType<RepairPropEconomyController>(FindObjectsSortMode.None).FirstOrDefault(c => c.gameObject.scene == gameObject.scene);
            director = FindObjectsByType<UnifiedClinicDirector>(FindObjectsSortMode.None).FirstOrDefault(c => c.gameObject.scene == gameObject.scene);
            if (director != null)
            {
                directorMenu = director.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "Menu");
                if (directorMenu != null)
                {
                    directorMenuGroup = directorMenu.GetComponent<CanvasGroup>();
                    if (directorMenuGroup == null) directorMenuGroup = directorMenu.gameObject.AddComponent<CanvasGroup>();
                    directorMenuGroup.alpha = 0; directorMenuGroup.blocksRaycasts = false; directorMenuGroup.interactable = false;
                }
            }
        }

        public void Toggle(bool value)
        {
            open = value;
            menu.gameObject.SetActive(open);
            if (open) menu.SetAsLastSibling();
        }

        void OpenShop()
        {
            Toggle(false);
            if (shop != null) shop.Open();
        }

        static void Return(string path)
        {
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            if (Application.CanStreamedLevelBeLoaded(path)) SceneManager.LoadScene(path);
            else Debug.LogWarning("[Launcher] " + path + " is not in the build; staying in the main clinic.");
#endif
        }

        void OpenMainMenu()
        {
            Toggle(false);
            ReturnScenePath = gameObject.scene.path;
            if (directorMenu != null) directorMenu.onClick.Invoke();
        }

        void Update()
        {
            // Night 1 ends with "back to the main menu" on the robot director's end panel (built only once the robot
            // part of the night starts); its "continue" then returns here for night 2 instead of the main game's clinic.
            if (nightEnd == null && director != null && director.Robot != null && director.Robot.MenuButton != null)
            {
                nightEnd = director.Robot.MenuButton;
                nightEnd.onClick.AddListener(() => ReturnScenePath = gameObject.scene.path);
            }
            // The launcher follows the director's HUD: shown while that HUD is (the trade phases), hidden in the shop.
            bool hud = directorMenu == null || directorMenu.gameObject.activeInHierarchy;
            bool show = hud && (shop == null || !shop.IsOpen);
            if (canvas.enabled != show) { canvas.enabled = show; if (!show) Toggle(false); }
            if (directorMenuGroup != null && directorMenuGroup.alpha > 0) directorMenuGroup.alpha = 0;
            if (!open) return;
            bool can = shop != null && shop.CanOpen;
            shopEntry.interactable = can;
            shopNote.text = can ? "▶" : "暂不可用";
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Toggle(false);
        }

        Button Entry(RectTransform parent, string label, float y, Font font, UnityEngine.Events.UnityAction action)
        {
            var r = Rect(label, parent, 8, y, 204, 32);
            var img = r.gameObject.AddComponent<Image>();
            RepairPropCyberStyle.Framed(img, new Color(1f, .62f, .16f, .06f), RepairPropCyberStyle.AmberDim);
            var button = r.gameObject.AddComponent<Button>(); button.targetGraphic = img;
            var colors = button.colors; colors.highlightedColor = new Color(1.6f, 1.4f, 1.1f, 1); colors.disabledColor = new Color(.6f, .6f, .6f, .6f); button.colors = colors;
            button.onClick.AddListener(action);
            var text = RepairPropCyberStyle.Caption(r, label, font, 12, RepairPropCyberStyle.Ink, TextAnchor.MiddleLeft);
            var tr = text.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = new Vector2(16, 0); tr.offsetMax = Vector2.zero;
            return button;
        }

        static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
        {
            var r = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h);
            return r;
        }

        static void Bar(RectTransform parent, float x, float y, float w, float h)
        {
            var r = Rect("Glyph", parent, x, y, w, h);
            var img = r.gameObject.AddComponent<Image>(); img.color = RepairPropCyberStyle.Amber; img.raycastTarget = false;
        }

        void OnDestroy() { if (Current == this) Current = null; }
    }
}
