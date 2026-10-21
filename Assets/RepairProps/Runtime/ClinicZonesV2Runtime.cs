using System;
using System.Collections.Generic;
using System.Linq;
using BorderRepair.TwoNight;
using BorderRepair.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps
{
    // Setting pass on the trade counter (east) and the UNIT07 dock corner (west) of the four-prop desktop scene
    // (SETTING_PROPS.md, README section 11): places Resources/ClinicZonesV2 (built by ClinicZonesV2Builder) in the room
    // frame, switches the primitive counter, its shelving and the dock wall's paper off with forceRenderingOff and
    // tints the dock's grey paint to the bench's steel green. Old colliders stay as they were (the new bodies keep the
    // old footprint), and the two trade status lamps the director drives are left visible. Also writes the intake
    // terminal and runs the desk fan, the CCTV and charge lamps. Play-time only; everything is restored on destroy.
    [AddComponentMenu("")]
    public sealed class ClinicZonesV2Runtime : MonoBehaviour
    {
        public const string ScenePath = RepairBenchV2Runtime.ScenePath;
        public const string PrefabResource = "ClinicZonesV2";
        static readonly string[] OldCounter =
        {
            "Trade_Counter", "Trade_CounterTop", "Trade_CounterPanels", "Trade_CounterHazard", "Trade_CounterShelf",
            "Trade_CRT_Body", "Trade_CRT_Base", "Trade_CRT_BackVent", "Trade_CRT_Screen", "Trade_CRT_Keyboard",
            "Trade_Ledger", "Trade_Note", "Trade_Sign", "Trade_Stool", "Trade_Stool_Seat", "Trade_ShelfLabel", "Trade_CargoLabel",
            "Trade_Pad_Receive", "Trade_Pad_Receive_Frame", "Trade_Pad_Receive_Label",
            "Trade_Pad_Deliver", "Trade_Pad_Deliver_Frame", "Trade_Pad_Deliver_Label", "Storage_EastShelves",
        };
        static readonly string[] OldDockWall = { "Unit07_Schematic", "Unit07_Label", "Unit07_CageLamp", "Unit07_CageLampFrame", "Wall_W_Tag" };
        static readonly Color DockPaint = new Color(.29f, .357f, .325f);   // bench M_BV2_SteelGreen's paint

        public static ClinicZonesV2Runtime Current { get; private set; }

        readonly List<Renderer> hidden = new List<Renderer>();
        readonly List<(Renderer renderer, Material[] shared)> tinted = new List<(Renderer, Material[])>();
        readonly List<Material> created = new List<Material>();
        GameObject zones;
        Text terminal;
        float nextTerminal;
        UnifiedClinicDirector clinic;
        Transform fanHead, fanBlades;
        Quaternion headRest, bladesRest;
        Vector3 headAxis, bladesAxis;
        float bladesAngle;
        Renderer cctvLed, chargeLamp;

        public GameObject Zones => zones;
        public int HiddenCount => hidden.Count;
        public int TintedDockRenderers => tinted.Count;
        public int StaticRenderers { get; private set; }
        public string TerminalText => terminal != null ? terminal.text : "";
        /// <summary>The intake terminal's screen surface (its readout canvas): where device programs such as the parts shop run.</summary>
        public RectTransform TerminalScreen => terminal != null ? (RectTransform)terminal.transform.parent : null;
        public Transform FanBlades => fanBlades;
        public Transform FanHead => fanHead;
        public bool CctvLedOn => cctvLed != null && cctvLed.enabled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHooks() { SceneManager.sceneLoaded -= OnSceneLoaded; Current = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() => SceneManager.sceneLoaded += OnSceneLoaded;

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!string.Equals(scene.path, ScenePath, StringComparison.OrdinalIgnoreCase)) return;
            var host = new GameObject("ClinicZonesV2 (runtime only)");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<ClinicZonesV2Runtime>();
        }

        void Awake()
        {
            Current = this;
            var prefab = Resources.Load<GameObject>(PrefabResource);
            if (prefab == null) { Debug.LogWarning("[ClinicZonesV2] Resources/" + PrefabResource + " missing (Border Repair > Repair Props > Build Clinic Zones V2); keeping the old counter and dock wall."); return; }
            zones = Instantiate(prefab);
            zones.name = PrefabResource;
            SceneManager.MoveGameObjectToScene(zones, gameObject.scene);
            SurgeryTableV2Runtime.AlignToBed(zones, Vector3.zero, Vector3.forward);   // room frame = world
            var roots = gameObject.scene.GetRootGameObjects();
            var environment = roots.Where(r => r.name.StartsWith("UnifiedClinicEnvironment", StringComparison.Ordinal))
                .SelectMany(r => r.GetComponentsInChildren<Renderer>(true));
            foreach (var renderer in environment)
            {
                string n = renderer.name;
                if (OldCounter.Contains(n) || n.StartsWith("Clutter_East_", StringComparison.Ordinal) || OldDockWall.Contains(n)) Hide(renderer);
            }
            TintDock(roots);
            SetupTerminal();
            fanHead = Find("ZC_Fan_Head"); fanBlades = Find("ZC_Fan_Blades");
            if (fanHead != null && fanBlades != null)
            {
                headRest = fanHead.localRotation; bladesRest = fanBlades.localRotation;
                headAxis = Quaternion.Inverse(fanHead.rotation) * Vector3.up;
                bladesAxis = Quaternion.Inverse(fanBlades.rotation) * (fanBlades.position - fanHead.position).normalized;
            }
            cctvLed = Find("ZC_Cctv_Led")?.GetComponent<Renderer>();
            chargeLamp = Find("ZD_ChargeLamp")?.GetComponent<Renderer>();
            StaticRenderers = V2StaticBatch.Combine(zones, t => t.name.StartsWith("ZC_Fan_", StringComparison.Ordinal)
                || t.name == "ZC_Cctv_Led" || t.name == "ZD_ChargeLamp");
        }

        void Hide(Renderer renderer)
        {
            if (renderer.forceRenderingOff) return;
            renderer.forceRenderingOff = true;
            hidden.Add(renderer);
        }

        // The dock's grey paint becomes the bench / table / gantry steel green; its own grime texture and UVs stay.
        void TintDock(GameObject[] roots)
        {
            var dock = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == "Unit07ServiceDock");
            if (dock == null) return;
            var copies = new Dictionary<Material, Material>();
            foreach (var renderer in dock.GetComponentsInChildren<Renderer>(true))
            {
                var shared = renderer.sharedMaterials;
                if (!shared.Any(m => m != null && m.name.StartsWith("M_Dock_Gray", StringComparison.Ordinal))) continue;
                tinted.Add((renderer, shared));
                renderer.sharedMaterials = shared.Select(m =>
                {
                    if (m == null || !m.name.StartsWith("M_Dock_Gray", StringComparison.Ordinal)) return m;
                    if (!copies.TryGetValue(m, out var copy))
                    {
                        copy = new Material(m) { name = m.name + " (steel green)" };
                        if (copy.HasProperty("_BaseColor")) copy.SetColor("_BaseColor", DockPaint);
                        if (copy.HasProperty("_Color")) copy.SetColor("_Color", DockPaint);
                        copies[m] = copy; created.Add(copy);
                    }
                    return copy;
                }).ToArray();
            }
        }

        // Amber canvas on the terminal glass, facing the room (same construction as the bench CRT readout).
        void SetupTerminal()
        {
            var glass = Find("ZC_Terminal_Glass");
            if (glass == null) return;
            var mesh = glass.GetComponent<MeshFilter>().sharedMesh;
            var size = Vector3.Scale(mesh.bounds.size, glass.lossyScale);
            int thin = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
            var axes = new[] { glass.right, glass.up, glass.forward };
            var center = glass.TransformPoint(mesh.bounds.center);
            var normal = axes[thin];
            if (Vector3.Dot(normal, Vector3.ProjectOnPlane(-center, Vector3.up)) < 0) normal = -normal;   // toward the room centre
            int a = (thin + 1) % 3, b = (thin + 2) % 3;
            int upAxis = Mathf.Abs(Vector3.Dot(axes[a], Vector3.up)) > Mathf.Abs(Vector3.Dot(axes[b], Vector3.up)) ? a : b;
            float height = size[upAxis], width = size[upAxis == a ? b : a];
            var canvasObject = new GameObject("ZC_TerminalReadout", typeof(RectTransform));
            canvasObject.transform.SetParent(zones.transform, false);
            var canvas = canvasObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)canvasObject.transform;
            const float unit = .0005f;
            rect.sizeDelta = new Vector2(width / unit, height / unit);
            rect.localScale = Vector3.one * unit;
            rect.SetPositionAndRotation(center + normal * .003f, Quaternion.LookRotation(-normal, Vector3.up));
            var textObject = new GameObject("Text", typeof(RectTransform));
            textObject.transform.SetParent(rect, false);
            var textRect = (RectTransform)textObject.transform;
            textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one; textRect.offsetMin = new Vector2(16, 12); textRect.offsetMax = new Vector2(-16, -12);
            terminal = textObject.AddComponent<Text>();
            terminal.font = RepairPropCyberStyle.UiFontOr(UIFontProvider.GetCjkFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));
            terminal.fontSize = 26; terminal.lineSpacing = 1f; terminal.color = new Color(1f, .68f, .22f); terminal.alignment = TextAnchor.UpperLeft;
            terminal.horizontalOverflow = HorizontalWrapMode.Wrap; terminal.verticalOverflow = VerticalWrapMode.Truncate; terminal.raycastTarget = false;
            terminal.text = "NSP 收发终端 03\n> 开机";
        }

        void Update()
        {
            if (zones == null) return;
            float time = Time.unscaledTime;
            if (terminal != null && time >= nextTerminal)
            {
                nextTerminal = time + .5f;
                if (clinic == null) clinic = FindFirstObjectByType<UnifiedClinicDirector>();
                terminal.text = TerminalLines(clinic);
            }
            if (fanHead != null)
            {
                fanHead.localRotation = headRest * Quaternion.AngleAxis(32f * Mathf.Sin(time * .45f), headAxis);
                bladesAngle = (bladesAngle + 900f * Time.unscaledDeltaTime) % 360f;
                fanBlades.localRotation = bladesRest * Quaternion.AngleAxis(bladesAngle, bladesAxis);
            }
            if (cctvLed != null) cctvLed.enabled = time % 1.4f < .18f;
            if (chargeLamp != null) chargeLamp.enabled = time % 2.4f < 1.6f;
        }

        public static string TerminalLines(UnifiedClinicDirector clinic)
        {
            var state = clinic != null ? clinic.State : null;
            if (state == null) return "NSP 收发终端 03\n> 待机";
            var lines = new List<string>
            {
                "NSP 收发终端 03",
                "第" + Mathf.Max(1, state.night) + "晚  现金 " + state.Cash,
                "房租 " + state.rent + " · 第" + state.rentDueNight + "晚前" + (state.RentShortfall > 0 ? "  差 " + state.RentShortfall : ""),
                "收件 > " + (clinic.ReceiveLampState != UnifiedClinicDirector.PadLamp.Off ? "有件待收" : "空"),
                "取件 > " + (clinic.DeliverLampState != UnifiedClinicDirector.PadLamp.Off ? "待交付" : "空"),
            };
            return string.Join("\n", lines);
        }

        Transform Find(string name) => zones.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        void OnDestroy()
        {
            foreach (var renderer in hidden) if (renderer != null) renderer.forceRenderingOff = false;
            foreach (var (renderer, shared) in tinted) if (renderer != null) renderer.sharedMaterials = shared;
            foreach (var material in created) if (material != null) Destroy(material);
            hidden.Clear(); tinted.Clear(); created.Clear();
            if (zones != null) Destroy(zones);
            if (Current == this) Current = null;
        }
    }
}
