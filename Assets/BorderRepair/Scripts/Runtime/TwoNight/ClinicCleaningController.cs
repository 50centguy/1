using System;
using System.Linq;
using BorderRepair.Cleaning;
using BorderRepair.Controls;
using BorderRepair.Core;
using BorderRepair.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BorderRepair.TwoNight
{
    /// <summary>Optional customer-item cleaning. Adds no mechanical repair step, income or save-format changes.</summary>
    [DefaultExecutionOrder(-120)]
    public sealed class ClinicCleaningController : MonoBehaviour
    {
        public UnifiedClinicDirector Clinic { get; private set; }
        public bool Cleaning { get; private set; }
        [Range(.01f,.2f)] public float brushRadiusUV = .045f;
        [Range(.1f,8)] public float strengthPerSecond = 2.4f;
        CleanableSurface[] surfaces = Array.Empty<CleanableSurface>();
        CleaningOccluder[] occluders = Array.Empty<CleaningOccluder>();
        GameObject mounted, ui;
        Button toggle;
        Text text, buttonText;
        bool previousScan;
        bool previousScannedTint;
        CleanableSurface previousSurface;
        Vector2 previousUv;
        bool stroke;
        public float Progress => surfaces.Length == 0 ? 0 : surfaces.Average(s => s != null ? s.Progress : 0);
        public bool Available => Clinic != null && Clinic.Initialized && Clinic.ConsoleOpen &&
            Clinic.Station != null && Clinic.Station.Session != null && Clinic.Station.Session.Stage == RepairStage.Inspect &&
            Clinic.Station.Inspector.isActiveAndEnabled && Clinic.Station.Inspector.InteractionEnabled &&
            !Clinic.Station.InputLocked && !Clinic.Counter.DialogueVisible && surfaces.Length > 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneManager.sceneLoaded += SceneLoaded;
        }
        static void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.path != "Assets/BorderRepair/Scenes/UnifiedClinic/UnifiedClinic.unity") return;
            var clinic = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<UnifiedClinicDirector>(true)).FirstOrDefault();
            if (clinic == null || scene.GetRootGameObjects().Any(g => g.GetComponent<ClinicCleaningController>() != null)) return;
            var root = new GameObject("ClinicCleaning_Runtime"); SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<ClinicCleaningController>().Configure(clinic);
        }

        public void Configure(UnifiedClinicDirector clinic) { Clinic = clinic; }

        void Start()
        {
            if (Clinic == null) Clinic = FindFirstObjectByType<UnifiedClinicDirector>();
            BuildUi();
        }

        void Update()
        {
            if (Clinic == null || Clinic.Station == null) return;
            var item = Clinic.Station.Inspector.CurrentItem;
            if (item != mounted)
            {
                SetCleaning(false);
                mounted = item;
                surfaces = item != null ? item.GetComponentsInChildren<CleanableSurface>(true)
                    .Where(s => s.Ready && s.Target.gameObject.activeInHierarchy).ToArray() : Array.Empty<CleanableSurface>();
                occluders = item != null ? item.GetComponentsInChildren<CleaningOccluder>(true) : Array.Empty<CleaningOccluder>();
            }
            if (!Available && Cleaning) SetCleaning(false);
            bool textFocused = EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
                EventSystem.current.currentSelectedGameObject.GetComponent<InputField>() != null;
            if (!textFocused && Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame) SetCleaning(!Cleaning);
            if (Cleaning && RepairInput.LeftHeld && !RepairInput.RightHeld && !RepairInput.PointerOverUI)
                WipeAt(RepairInput.PointerPosition, Mathf.Min(Time.unscaledDeltaTime, .1f));
            else { stroke = false; previousSurface = null; }
            if (ui != null)
            {
                ui.SetActive(Clinic.ConsoleOpen && mounted != null && surfaces.Length > 0);
                toggle.interactable = Available;
                buttonText.text = Cleaning ? "退出清洁 [C]" : "清洁工具 [C]";
                text.text = $"污垢清除 {Progress:P1}" + (Cleaning ? "\n按住左键擦拭 · 右键转动 · 滚轮缩放" : "\n局部清洁，不改变维修步骤与费用");
            }
        }

        public bool SetCleaning(bool value)
        {
            if (value == Cleaning) return Cleaning;
            if (value && !Available) return false;
            if (Clinic == null || Clinic.Station == null) return false;
            var station = Clinic.Station;
            if (value)
            {
                previousScan = station.Session.ScanModeActive;
                previousScannedTint = station.Scanner.ShowScannedTint;
                station.SelectTool(null);
                station.Session.SetScanMode(false);
            }
            Cleaning = value; stroke = false; previousSurface = null;
            station.CleaningActive = value;
            station.Inspector.LeftPointerReserved = value;
            station.Scanner.InteractionSuppressed = value;
            station.Scanner.ShowScannedTint = value ? false : previousScannedTint;
            foreach (var surface in surfaces) if (surface != null) surface.SetProbeEnabled(value);
            foreach (var occluder in occluders) if (occluder != null) occluder.SetProbeEnabled(value);
            if (!value && station.Session != null && station.Session.Stage == RepairStage.Inspect)
                station.Session.SetScanMode(previousScan);
            return Cleaning;
        }

        /// <summary>Same path used by real input and acceptance tests. No cleaning through UI or room occluders.</summary>
        public bool WipeAt(Vector2 screen, float deltaSeconds)
        {
            if (!float.IsFinite(deltaSeconds) || deltaSeconds <= 0 || !TryGetCleaningHit(screen,out var closest,out var hit))
            { stroke = false; previousSurface = null; return false; }
            Vector2 uv = hit.textureCoord2;
            float amount = strengthPerSecond * Mathf.Min(deltaSeconds, .1f);
            bool changed = closest.Wipe(uv, brushRadiusUV, amount);
            // Fill fast mouse strokes, but do not bridge distant atlas islands or a change of surface.
            if (stroke && closest == previousSurface && closest.SameIsland(uv, previousUv) && Vector2.Distance(uv, previousUv) < .25f)
            {
                int steps = Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(uv, previousUv) / (brushRadiusUV * .4f)), 1, 24);
                for (int i = 1; i < steps; i++) changed |= closest.Wipe(Vector2.Lerp(previousUv, uv, i / (float)steps), brushRadiusUV, amount);
            }
            previousUv = uv; previousSurface = closest; stroke = true;
            return changed;
        }

        public bool TryGetCleaningHit(Vector2 screen,out CleanableSurface closest,out RaycastHit hit)
        {
            closest = null; hit = default;
            if (!Cleaning || !Available || !float.IsFinite(screen.x) || !float.IsFinite(screen.y) || IsOverUi(screen)) return false;
            var camera = Clinic.Station.Inspector.ViewCamera;
            if (!camera.pixelRect.Contains(screen)) return false;
            Physics.SyncTransforms();
            Ray ray = camera.ScreenPointToRay(screen);
            float distance = 10;
            foreach (var surface in surfaces)
                if (surface != null && surface.Raycast(ray, distance, out var candidate))
                { distance = candidate.distance; closest = surface; hit = candidate; }
            if (closest == null || !closest.AcceptsHit(hit)) return false;
            foreach (var occluder in occluders)
                if (occluder != null && occluder.Blocks(ray,Mathf.Max(0,distance-.0001f))) return false;
            if (Physics.Raycast(ray, out var blocker, Mathf.Max(0, distance - .002f), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && !blocker.transform.IsChildOf(mounted.transform)) return false;
            return true;
        }

        static bool IsOverUi(Vector2 screen)
        {
            if (EventSystem.current == null) return false;
            var hits = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screen }, hits);
            return hits.Count > 0;
        }

        void BuildUi()
        {
            ui = new GameObject("Cleaning UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            ui.transform.SetParent(transform, false);
            var canvas = ui.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 60;
            var scale = ui.GetComponent<CanvasScaler>(); scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scale.referenceResolution = new Vector2(1280,720); scale.matchWidthOrHeight = .5f;
            var panel = new GameObject("Cleaning controls", typeof(RectTransform), typeof(Image)); panel.transform.SetParent(ui.transform, false);
            var rect = panel.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(18,18); rect.sizeDelta = new Vector2(465,68);
            var background = panel.GetComponent<Image>(); background.color = new Color(.045f,.06f,.065f,.92f); background.raycastTarget = false;
            var button = new GameObject("Cleaning toggle", typeof(RectTransform), typeof(Image), typeof(Button)); button.transform.SetParent(panel.transform,false);
            var buttonRect = button.GetComponent<RectTransform>(); buttonRect.anchorMin = buttonRect.anchorMax = buttonRect.pivot = Vector2.zero;
            buttonRect.anchoredPosition = new Vector2(8,9); buttonRect.sizeDelta = new Vector2(145,50);
            button.GetComponent<Image>().color = new Color(.25f,.32f,.31f);
            toggle = button.GetComponent<Button>(); toggle.onClick.AddListener(() =>
            { SetCleaning(!Cleaning); EventSystem.current?.SetSelectedGameObject(null); });
            var font = UIFontProvider.GetCjkFont(Clinic.Station.View.GetComponentInChildren<Text>(true)?.font);
            buttonText = Label(button.transform,"Button label",font,new Vector2(0,0),new Vector2(145,50));
            buttonText.alignment=TextAnchor.MiddleCenter;
            text = Label(panel.transform,"Progress",font,new Vector2(163,9),new Vector2(298,50));
            ui.SetActive(false);
        }

        static Text Label(Transform parent,string name,Font font,Vector2 position,Vector2 size)
        {
            var obj=new GameObject(name,typeof(RectTransform),typeof(Text)); obj.transform.SetParent(parent,false);
            var r=obj.GetComponent<RectTransform>(); r.anchorMin=r.anchorMax=r.pivot=Vector2.zero; r.anchoredPosition=position; r.sizeDelta=size;
            var label=obj.GetComponent<Text>(); label.font=font; label.fontSize=16; label.color=new Color(.92f,.94f,.89f);
            label.raycastTarget=false; label.alignment=TextAnchor.MiddleLeft; return label;
        }

        void OnDisable() { SetCleaning(false); }
        void OnDestroy() { if (ui != null) Destroy(ui); }
    }
}
