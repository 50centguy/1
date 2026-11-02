using System.Linq;
using BorderRepair.FirstOrder;
using BorderRepair.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BorderRepair.RepairProps
{
    // Temporary title page for player builds of the clinic. Drawn entirely from code, over everything else; the game keeps
    // running behind it. Never installed in the editor: PlayMode tests load the clinic scene directly and must not be blocked.
    [AddComponentMenu("")]
    public sealed class ClinicStartScreenRuntime : MonoBehaviour
    {
        const string GameplayScene = "RepairProps_Clinic";
        const int SortingOrder = 600;   // over the help sheet's 500
        const int StartIndex = 0, QuitIndex = 1;

        public static string Title => "地下义体维修处";
        public static string Subtitle => "BORDER REPAIR STATION";
        public static readonly string[] MenuItems = { "开始营业", "退出" };

        // FirstPersonWalker takes the cursor back every frame unless a request is open, so this is what keeps it free.
        readonly System.Func<bool> holdCursor = () => true;
        Text[] rows;
        Image[] blocks;
        GameObject host;
        FirstPersonWalker walker;
        bool finished;

        public int SelectedIndex { get; private set; }

        /// <summary>Every string the page draws.</summary>
        public string[] RenderedTexts => GetComponentsInChildren<Text>(true).Select(t => t.text).ToArray();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Application.isEditor) return;
            // This assembly also ships in the other slice players, which have their own menus.
            if (UnityEngine.XR.XRSettings.isDeviceActive || SceneManager.GetActiveScene().name != GameplayScene) return;
            var canvas = ClinicHelpRuntime.OverlayCanvas("StartScreenCanvas", null);
            canvas.sortingOrder = SortingOrder;
            canvas.gameObject.AddComponent<GraphicRaycaster>();
            DontDestroyOnLoad(canvas.gameObject);
            Create(canvas.transform).host = canvas.gameObject;
        }

        /// <summary>Builds the page under the given canvas (or any of its children) and returns it. Does not check isEditor.</summary>
        public static ClinicStartScreenRuntime Create(Transform parent)
        {
            if (parent == null) throw new System.ArgumentNullException(nameof(parent));
            var root = new GameObject("ClinicStartScreen", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            Stretch((RectTransform)root.transform);
            var screen = root.AddComponent<ClinicStartScreenRuntime>();
            screen.host = root;
            screen.Build();
            return screen;
        }

        void Build()
        {
            var font = RepairPropCyberStyle.UiFontOr(UIFontProvider.GetCjkFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));
            const float left = 120;

            var backdrop = ClinicHelpRuntime.Box("Backdrop", transform, new Color(.018f, .014f, .010f, 1f));
            Stretch(backdrop);
            backdrop.GetComponent<Image>().raycastTarget = true;   // swallows clicks that miss the menu

            AddLine("Title", font, 48, RepairPropCyberStyle.Hot, left, -170, 800, 64, Title);
            AddLine("Subtitle", font, 24, RepairPropCyberStyle.Amber, left, -236, 800, 30, Subtitle);
            Place(ClinicHelpRuntime.Box("Rule", transform, RepairPropCyberStyle.AmberDim), left, -276, 420, 2);

            rows = new Text[MenuItems.Length];
            blocks = new Image[MenuItems.Length];
            const float menuTop = -304, rowHeight = 44;
            for (int i = 0; i < MenuItems.Length; i++) BuildRow(i, font, left, menuTop - i * rowHeight, rowHeight);

            AddLine("Hint", font, 16, RepairPropCyberStyle.Dim, left, menuTop - MenuItems.Length * rowHeight - 18, 800, 24,
                "上 / 下 选择    Enter 或 Space 确认    Esc 退出    也可以用鼠标点");

            var footer = ClinicHelpRuntime.Label("Footer", transform, font, 14, RepairPropCyberStyle.Dim, TextAnchor.MiddleLeft);
            footer.text = "北坡港务 NSP · 夜班    临时标题页    v" + Application.version;
            var footerRect = footer.rectTransform;
            footerRect.anchorMin = footerRect.anchorMax = footerRect.pivot = Vector2.zero;
            footerRect.anchoredPosition = new Vector2(left, 28);
            footerRect.sizeDelta = new Vector2(1000, 22);

            RefreshRows();
        }

        void BuildRow(int index, Font font, float x, float y, float height)
        {
            var hit = ClinicHelpRuntime.Box("Item" + index, transform, RepairPropCyberStyle.Clear);
            Place(hit, x, y, 300, height);
            var graphic = hit.GetComponent<Image>();
            graphic.raycastTarget = true;

            var button = hit.gameObject.AddComponent<Button>();
            button.targetGraphic = graphic;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };   // the arrows are read below; no double handling
            int captured = index;
            button.onClick.AddListener(() => Activate(captured));

            var hover = hit.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => Select(captured));
            hover.triggers.Add(enter);

            var block = ClinicHelpRuntime.Box("Cursor", hit, RepairPropCyberStyle.Hot);
            block.anchorMin = block.anchorMax = block.pivot = new Vector2(0, .5f);
            block.anchoredPosition = new Vector2(0, 0);
            block.sizeDelta = new Vector2(12, 24);
            blocks[index] = block.GetComponent<Image>();

            var label = ClinicHelpRuntime.Label("Label", hit, font, 24, RepairPropCyberStyle.Amber, TextAnchor.MiddleLeft);
            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(26, 0); labelRect.offsetMax = Vector2.zero;
            label.text = MenuItems[index];
            rows[index] = label;
        }

        void AddLine(string name, Font font, int size, Color color, float x, float y, float width, float height, string content)
        {
            var label = ClinicHelpRuntime.Label(name, transform, font, size, color, TextAnchor.MiddleLeft);
            Place(label.rectTransform, x, y, width, height);
            label.text = content;
        }

        void OnEnable() => HoldCursor();

        void Update()
        {
            if (finished) return;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.upArrowKey.wasPressedThisFrame) Select(SelectedIndex - 1);
                if (keyboard.downArrowKey.wasPressedThisFrame) Select(SelectedIndex + 1);
                if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame) Activate(SelectedIndex);
                else if (keyboard.escapeKey.wasPressedThisFrame) Quit();
            }
            if (finished) return;
            RefreshRows();
        }

        void LateUpdate()
        {
            if (!finished) HoldCursor();
        }

        void HoldCursor()
        {
            if (walker == null)
            {
                walker = FindFirstObjectByType<FirstPersonWalker>();
                if (walker != null) walker.CursorRequests.Add(holdCursor);
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void ReleaseWalker()
        {
            if (walker != null) walker.CursorRequests.Remove(holdCursor);
            walker = null;
        }

        /// <summary>Moves the selection, wrapping at the ends.</summary>
        public void Select(int index)
        {
            int count = MenuItems.Length;
            SelectedIndex = ((index % count) + count) % count;
        }

        void Activate(int index)
        {
            if (index == StartIndex) Begin();
            else if (index == QuitIndex) Quit();
        }

        /// <summary>Closes the page and hands the cursor back to the first-person controls. Play mode only.</summary>
        public void Begin()
        {
            if (finished) return;
            finished = true;
            Destroy(host);
            ReleaseWalker();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        /// <summary>Quits a player build; does nothing in the editor.</summary>
        public void Quit()
        {
            if (Application.isEditor) return;
            Application.Quit();
        }

        void RefreshRows()
        {
            bool blinkOn = Mathf.Repeat(Time.unscaledTime * 2f, 1f) < .5f;   // one full on/off cycle at 2 Hz
            for (int i = 0; i < rows.Length; i++)
            {
                bool selected = i == SelectedIndex;
                rows[i].color = selected ? RepairPropCyberStyle.Hot : RepairPropCyberStyle.AmberDim;
                blocks[i].enabled = selected && blinkOn;
            }
        }

        void OnDestroy() => ReleaseWalker();

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        // x from the page's left edge, y (negative) from its top edge.
        static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
