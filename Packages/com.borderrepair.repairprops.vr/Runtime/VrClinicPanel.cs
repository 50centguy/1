using System.Collections.Generic;
using System.Linq;
using BorderRepair.Data;
using BorderRepair.RepairProps.Cosmetics;
using BorderRepair.TwoNight;
using BorderRepair.UI;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace BorderRepair.RepairProps.VR
{
    public sealed class VrClinicPanel : MonoBehaviour
    {
        public TeleportationAnchor benchTeleport;
        public TeleportationAnchor counterTeleport;
        ClinicVrSession session;
        RectTransform repairContent;
        Text status;
        Text title;
        Text customer;
        Button receive;
        Button deliver;
        Font font;
        CosmeticPanel cosmeticsPanel;
        readonly List<(Button button, string command)> commands = new List<(Button, string)>();

        public void Initialize(ClinicVrSession owner)
        {
            session = owner;
            font = UIFontProvider.GetCjkFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            var bench = Canvas("VR_Workbench", 800, 1000);
            var benchPosition = session.clinic.WorkAnchor.position + Vector3.right * .52f + Vector3.up * .5f;
            Pose(bench, benchPosition, session.clinic.BenchStandPoint + Vector3.up * 1.5f);
            title = Label(bench, "", 55);
            status = Label(bench, "", 170);
            var scroll = new GameObject("Commands", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
            scroll.transform.SetParent(bench, false);
            var rect = (RectTransform)scroll.transform;
            rect.sizeDelta = new Vector2(770, 650);
            scroll.GetComponent<Mask>().showMaskGraphic = false;
            scroll.GetComponent<Image>().color = new Color(.08f, .09f, .09f, 1f);
            repairContent = Column("Content", rect);
            repairContent.anchorMin = repairContent.anchorMax = new Vector2(.5f, 1);
            repairContent.pivot = new Vector2(.5f, 1);
            repairContent.sizeDelta = new Vector2(740, 0);
            var sr = scroll.GetComponent<ScrollRect>();
            sr.viewport = rect;
            sr.content = repairContent;
            sr.horizontal = false;
            sr.scrollSensitivity = 30;
            var element = scroll.AddComponent<LayoutElement>();
            element.preferredHeight = 650;
            element.minHeight = 650;
            AddButton(bench, "交易区", () => counterTeleport.RequestTeleport());
            var trade = Canvas("VR_Trade", 700, 660);
            var p = session.clinic.ReceiveAnchor.position + Vector3.up * .48f;
            Pose(trade, p, session.clinic.BenchStandPoint + Vector3.up * 1.5f);
            Label(trade, "收件 / 交件", 60);
            customer = Label(trade, "", 220);
            receive = AddButton(trade, "接收下一件", session.Receive);
            deliver = AddButton(trade, "交付维修物品", session.Deliver);
            AddButton(trade, "工作台", () => benchTeleport.RequestTeleport());
            if (session.Cosmetics != null)
            {
                var cosmeticCanvas = Canvas("VR_Cosmetics", 800, 760);
                Pose(cosmeticCanvas, session.clinic.WorkAnchor.position - Vector3.right * .55f + Vector3.up * .5f,
                    session.clinic.BenchStandPoint + Vector3.up * 1.5f);
                cosmeticsPanel = cosmeticCanvas.gameObject.AddComponent<CosmeticPanel>();
                cosmeticsPanel.Initialize(session.Cosmetics, true, session.origin.Camera);
            }
        }

        public void Rebuild()
        {
            if (repairContent == null) return;
            foreach (Transform child in repairContent) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            commands.Clear();
            var flow = session.ActiveFlow;
            if (flow == null) return;
            AddButton(repairContent, "整件器具", () => session.SelectTarget(flow.Definition.rootObjectName));
            AddButton(repairContent, "检查部件", session.Inspect);
            AddButton(repairContent, "空手", () => session.SelectTool(null));
            foreach (var tool in flow.Definition.commands.Select(c => c.tool).Distinct())
            {
                var value = tool;
                AddButton(repairContent, RepairActionText.Label(value), () => session.SelectTool(value));
            }
            Label(repairContent, "维修操作", 44);
            foreach (var command in flow.Definition.commands.Where(c => c.target == session.SelectedTarget))
            {
                var c = command;
                commands.Add((AddButton(repairContent, c.label, () => session.Execute(c.id)), c.id));
            }
            Label(repairContent, "已拆部件", 44);
            foreach (var node in flow.Definition.nodes.Where(n => n.movable && flow.GetState(n.outKey) == n.outValue))
            {
                var n = node;
                AddButton(repairContent, n.displayName, () => session.SelectTarget(n.objectName));
            }
        }

        public void Refresh()
        {
            if (session == null || title == null) return;
            cosmeticsPanel?.Refresh();
            var flow = session.ActiveFlow;
            // Same read-only phase and first-blocker wording as the desktop workspace.
            bool working = flow != null && session.CanWork;
            title.text = flow != null ? RepairPropText.Item(flow.Definition.kind)
                + (working ? "　阶段：" + RepairPropWorkflowStatus.Label(RepairPropWorkflowStatus.Phase(flow)) : "") : "维修台";
            var node = flow != null ? flow.Definition.FindNode(session.SelectedTarget) : null;
            status.text = (working ? RepairPropWorkflowStatus.NextStep(flow) + "\n" : "") + (node != null ? node.displayName : "") + "　" +
                (session.Tool.HasValue ? RepairActionText.Label(session.Tool.Value) : "空手") + "\n" + session.Feedback;
            receive.interactable = session.Ready && session.clinic.HasPendingCase && !session.clinic.Carrying;
            deliver.interactable = session.Ready && (session.clinic.DeliverLampState != UnifiedClinicDirector.PadLamp.Off);
            var data = session.clinic.Station.Session.CurrentCase;
            customer.text = session.clinic.State.phase == TwoNightPhase.Night1Ledger
                ? "四单验收结束\n现金 " + session.clinic.State.Cash
                : data.customerName + "\n" + data.customerStatement;
            foreach (var pair in commands)
                pair.button.interactable = session.CanWork && session.Tool.HasValue && flow != null &&
                    flow.CanExecute(pair.command, session.SelectedTarget, session.Tool.Value, out _);
        }

        public void SetVisible(bool visible)
        {
            foreach (var canvas in GetComponentsInChildren<Canvas>(true)) canvas.enabled = visible;
        }

        RectTransform Canvas(string name, int width, int height)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(Image), typeof(TrackedDeviceGraphicRaycaster));
            go.transform.SetParent(transform, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = Vector3.one * .00065f;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = session.origin.Camera;
            go.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 10;
            go.GetComponent<Image>().color = new Color(.035f, .045f, .045f, .97f);
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 16, 16);
            layout.spacing = 8;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            return rect;
        }

        static RectTransform Column(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            go.transform.SetParent(parent, false);
            var group = go.GetComponent<VerticalLayoutGroup>();
            group.spacing = 8;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandHeight = false;
            go.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return (RectTransform)go.transform;
        }

        static void Pose(Transform panel, Vector3 position, Vector3 standingEye)
        {
            panel.position = position;
            panel.rotation = Quaternion.LookRotation(position - standingEye, Vector3.up);
        }

        Text Label(Transform parent, string text, float height)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.fontSize = 26;
            t.color = Color.white;
            t.text = text;
            t.raycastTarget = false;
            go.GetComponent<LayoutElement>().preferredHeight = height;
            return t;
        }

        Button AddButton(Transform parent, string text, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject("Action", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(.16f, .24f, .23f, 1);
            go.GetComponent<LayoutElement>().preferredHeight = 52;
            go.GetComponent<LayoutElement>().minHeight = 52;
            var label = Label(go.transform, text, 52);
            var rect = (RectTransform)label.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(8, 0);
            rect.offsetMax = new Vector2(-8, 0);
            label.alignment = TextAnchor.MiddleCenter;
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(action);
            return button;
        }
    }
}
