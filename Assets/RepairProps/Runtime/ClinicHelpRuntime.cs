using System.Collections.Generic;
using System.Linq;
using BorderRepair.Core;
using BorderRepair.FirstOrder;
using BorderRepair.TwoNight;
using BorderRepair.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BorderRepair.RepairProps
{
    // F1: a readable sheet of the controls, in every build, so someone trying the game can find out how to play it.
    // Hidden until asked for; it takes no pointer input. Playtest keys are ClinicDevKeysRuntime's and only listed where they exist.
    [AddComponentMenu("")]
    public sealed class ClinicHelpRuntime : MonoBehaviour
    {
        const float TagSeconds = 14f;

        public static ClinicHelpRuntime Current { get; private set; }

        /// <summary>The sheet's rows: key column, then what it does. A null key starts a group.</summary>
        public static string[][] RowsFor(bool devKeys)
        {
            var rows = new List<string[]>
            {
                new[] { null, "走动" },
                new[] { "W A S D / 方向键", "移动" },
                new[] { "鼠标", "转头（屏幕中央的小点是准星）" },
                new[] { "按住 Shift", "跑" },
                new[] { "C", "蹲下 / 站起（按住 Ctrl 也能蹲）" },
                new[] { "Tab", "松开光标去点界面；再点一下画面回到准星" },
                new[] { null, "动手" },
                new[] { "鼠标左键", "操作准星对着的东西" },
                new[] { "右键拖动 / 滚轮", "在维修台和手术近景里转动、缩放" },
                new[] { "Esc", "打开手册；维修时是取消动作、退出近景" },
                new[] { "1 – 4", "切换固定机位（按移动键回到行走）" },
                new[] { "C（在维修台）", "拿起 / 放下清洁工具（物品有污渍时）" },
                new[] { "T", "接听 / 跳过来电" },
                new[] { "-", "叫病人罗亚进诊所" },
                new[] { "F1", "打开 / 关闭这张帮助" },
            };
            if (devKeys)
            {
                rows.Add(new[] { null, "试玩快捷键" });
                rows.Add(new[] { "F4", "立刻清除所有污渍和灰尘" });
                rows.Add(new[] { "F5", "快捷完成当前这一步（做不了会说明原因）" });
                rows.Add(new[] { "=", "展开 / 收起 PS2 画面设置" });
                rows.Add(new[] { "F3", "调试信息（七号维修夜）" });
            }
            return rows.ToArray();
        }

        Canvas canvas;
        GameObject help, tagBox;
        Text nowText;
        CanvasGroup tagGroup;
        float tagUntil, nextNowRefresh;

        public bool HelpVisible => help != null && help.activeSelf;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHooks() { Current = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (UnityEngine.XR.XRSettings.isDeviceActive || Current != null) return;
            var host = new GameObject("ClinicHelp (runtime only)");
            DontDestroyOnLoad(host);
            Current = host.AddComponent<ClinicHelpRuntime>();
        }

        void Awake()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            tagUntil = Time.unscaledTime + TagSeconds;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => tagUntil = Time.unscaledTime + TagSeconds;

        void Update()
        {
            UpdateUi();
            var keyboard = Keyboard.current;
            if (keyboard == null || FirstOrderInput.TextInputFocused) return;
            if (keyboard.f1Key.wasPressedThisFrame) ToggleHelp();
        }

        public void ToggleHelp()
        {
            EnsureUi();
            help.SetActive(!help.activeSelf);
            nextNowRefresh = 0;
            tagUntil = 0;   // they found it
        }

        // What to do now: the sheet's top line.
        static string NowText()
        {
            var clinic = FindFirstObjectByType<UnifiedClinicDirector>();
            bool ready = clinic != null && clinic.Initialized;
            if (ready && clinic.ConsoleOpen)
            {
                var item = clinic.Station.Inspector.CurrentItem;
                var bridge = item != null ? item.GetComponentInChildren<RepairPropStationBridge>(true) : null;
                return bridge != null && bridge.Flow != null && bridge.Flow.Session != null ? RepairPropWorkflowStatus.NextStep(bridge.Flow) : "检查物品，按界面提示操作。";
            }
            var patient = SurgeryPatientRuntime.Current;
            if (patient != null && patient.Busy)
            {
                var op = SurgeryProcedureRuntime.Current;
                var closeup = SurgeryCloseupRuntime.Current;
                return closeup != null && closeup.Active && op != null ? op.NextStepText : patient.Hint ?? "罗亚正在走动，等他停下。";
            }
            if (ready && clinic.State != null && clinic.State.phase == TwoNightPhase.Night1Counter)
            {
                if (clinic.Carrying) return "走到维修台，对准台面按左键放下。";
                if (clinic.DeliverLampState != UnifiedClinicDirector.PadLamp.Off) return "物品修好了：走到闪灯的交件台，按左键交付。";
                if (clinic.ReceiveLampState == UnifiedClinicDirector.PadLamp.On) return "走到亮灯的收件台，对准物品按左键接收。";
                if (clinic.TradeState == ClinicTradeState.InRepair) return "走到维修台，对准台上的物品按左键，继续维修。";
            }
            return "在房间里走走看看，准星对准东西时会有提示。";
        }

        void UpdateUi()
        {
            float now = Time.unscaledTime;
            bool tagVisible = now < tagUntil && !HelpVisible;
            if (canvas == null)
            {
                if (!tagVisible) return;
                EnsureUi();
            }
            if (tagBox.activeSelf != tagVisible) tagBox.SetActive(tagVisible);
            if (tagVisible) tagGroup.alpha = Mathf.Clamp01((tagUntil - now) / 1.5f);
            if (HelpVisible && now >= nextNowRefresh)
            {
                nextNowRefresh = now + .5f;
                nowText.text = "现在：" + NowText();
            }
            // Nothing on screen: the canvas costs nothing and stays out of the frame.
            canvas.enabled = tagVisible || HelpVisible;
        }

        void EnsureUi()
        {
            if (canvas != null) return;
            var font = RepairPropCyberStyle.UiFontOr(UIFontProvider.GetCjkFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));
            canvas = OverlayCanvas("HelpCanvas", transform);
            BuildHelp(canvas.transform, font);
            BuildTag(canvas.transform, font);
        }

        void BuildHelp(Transform parent, Font font)
        {
            var rows = RowsFor(Debug.isDebugBuild);
            const float width = 680, rowHeight = 22, groupGap = 8;
            int groups = rows.Count(r => r[0] == null);
            float height = 92 + rows.Length * rowHeight + groups * groupGap + 24;
            var panel = Box("Help", parent, new Color(.03f, .04f, .045f, .86f));
            help = panel.gameObject;
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
            panel.sizeDelta = new Vector2(width, height);
            float y = -12;
            var title = Label("Title", panel, font, 22, new Color(1f, .84f, .5f), TextAnchor.MiddleLeft);
            Place(title.rectTransform, 22, y, width - 44, 30); title.text = "操作帮助";
            var close = Label("Close", panel, font, 14, new Color(.72f, .74f, .7f), TextAnchor.MiddleRight);
            Place(close.rectTransform, 22, y, width - 44, 30); close.text = "按 F1 关闭";
            y -= 32;
            nowText = Label("Now", panel, font, 15, new Color(.72f, .97f, .66f), TextAnchor.UpperLeft);
            nowText.horizontalOverflow = HorizontalWrapMode.Wrap;
            Place(nowText.rectTransform, 22, y, width - 44, 40);
            y -= 44;
            foreach (var row in rows)
            {
                if (row[0] == null)
                {
                    y -= groupGap;
                    var group = Label("Group", panel, font, 15, new Color(1f, .72f, .36f), TextAnchor.MiddleLeft);
                    Place(group.rectTransform, 22, y, width - 44, rowHeight); group.text = row[1];
                }
                else
                {
                    var key = Label("Key", panel, font, 16, new Color(1f, .96f, .88f), TextAnchor.MiddleLeft);
                    Place(key.rectTransform, 34, y, 170, rowHeight); key.text = row[0];
                    var what = Label("What", panel, font, 16, new Color(.86f, .88f, .84f), TextAnchor.MiddleLeft);
                    Place(what.rectTransform, 210, y, width - 232, rowHeight); what.text = row[1];
                }
                y -= rowHeight;
            }
            help.SetActive(false);
        }

        void BuildTag(Transform parent, Font font)
        {
            var box = Box("F1Tag", parent, new Color(0, 0, 0, .55f));
            tagBox = box.gameObject;
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(1, 0);
            box.anchoredPosition = new Vector2(-14, 14); box.sizeDelta = new Vector2(132, 28);
            tagGroup = tagBox.AddComponent<CanvasGroup>();
            tagGroup.blocksRaycasts = false; tagGroup.interactable = false;
            var text = Label("Text", box, font, 15, new Color(1f, .9f, .68f), TextAnchor.MiddleCenter);
            var r = text.rectTransform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            text.text = "按 F1 看操作";
            tagBox.SetActive(false);
        }

        // Shared with the playtest keys' toast: no GraphicRaycaster, so these canvases never take the pointer.
        internal static Canvas OverlayCanvas(string name, Transform parent)
        {
            var host = new GameObject(name, typeof(RectTransform));
            host.transform.SetParent(parent, false);
            var canvas = host.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;   // over the workspace
            var scaler = host.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f;
            return canvas;
        }

        internal static RectTransform Box(string name, Transform parent, Color color)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(parent, false);
            var image = obj.GetComponent<Image>();
            image.color = color; image.raycastTarget = false;
            return (RectTransform)obj.transform;
        }

        internal static Text Label(string name, Transform parent, Font font, int size, Color color, TextAnchor anchor)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(parent, false);
            var text = obj.GetComponent<Text>();
            text.font = font; text.fontSize = size; text.color = color; text.alignment = anchor;
            text.raycastTarget = false; text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        // x from the panel's left edge, y (negative) from its top edge.
        static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Current == this) Current = null;
        }
    }
}
