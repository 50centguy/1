using System;
using System.Collections.Generic;
using System.Linq;
using BorderRepair.Controls;
using BorderRepair.TwoNight;
using BorderRepair.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BorderRepair.RepairProps
{
    // Puts the V2 surgery table (Resources/SurgeryTableV2, built by SurgeryTableV2Builder) into the four-prop desktop
    // scene at play time in place of the primitive Bed_* meshes, the same way RepairBenchV2Runtime replaces the bench:
    // old renderers get forceRenderingOff (COL_Bed and the aisles stay as they are; the new body fits inside the old
    // bed), and everything is restored on destroy. It also runs the table's room interactions: the hand pendant
    // toggles flat / chair pose, each restraint buckles or opens, the instrument tray swings out or in.
    [AddComponentMenu("")]
    public sealed class SurgeryTableV2Runtime : MonoBehaviour
    {
        public const string ScenePath = RepairBenchV2Runtime.ScenePath;
        public const string PrefabResource = "SurgeryTableV2";
        const float BackChair = 38f, HeadRelative = -12f;
        static readonly string[] OldBed = { "Bed_Base", "Bed_Pads", "Bed_Rails", "Bed_Shell", "Bed_Straps" };

        public static SurgeryTableV2Runtime Current { get; private set; }

        readonly List<Renderer> hidden = new List<Renderer>();
        readonly List<Collider> disabled = new List<Collider>();
        readonly Dictionary<SurgeryTableControl, (GameObject closed, GameObject open)> straps = new Dictionary<SurgeryTableControl, (GameObject, GameObject)>();
        GameObject table;
        Transform back, head, tray;
        Quaternion backRest, headRest, trayRest;
        Vector3 headDir;
        float pose, trayAmount, traySign = 1;
        UnifiedClinicDirector clinic;
        GameObject promptCanvas;
        RectTransform prompt;
        Text promptText;

        public GameObject Table => table;
        public int StaticRenderers { get; private set; }
        public bool ChairPose { get; private set; }
        public bool TrayOut { get; private set; }
        public float PoseAmount => pose;
        public int HiddenCount => hidden.Count;
        public SurgeryTableControl Aimed { get; private set; }
        public IEnumerable<SurgeryTableControl> Controls => table != null ? table.GetComponentsInChildren<SurgeryTableControl>(true) : Enumerable.Empty<SurgeryTableControl>();
        public bool StrapClosed(SurgeryTableControl strap) => straps.TryGetValue(strap, out var s) && s.closed.activeSelf;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHooks() { SceneManager.sceneLoaded -= OnSceneLoaded; Current = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() => SceneManager.sceneLoaded += OnSceneLoaded;

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!string.Equals(scene.path, ScenePath, StringComparison.OrdinalIgnoreCase)) return;
            var host = new GameObject("SurgeryTableV2 (runtime only)");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<SurgeryTableV2Runtime>();
        }

        void Awake()
        {
            Current = this;
            var all = gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
            var prefab = Resources.Load<GameObject>(PrefabResource);
            if (prefab == null || !TryBedFrame(gameObject.scene, out var floor, out headDir))
            {
                Debug.LogWarning("[SurgeryTableV2] " + (prefab == null ? "Resources/" + PrefabResource + " missing (run Border Repair > Repair Props > Build Surgery Table V2)" : "no Bed_Base in scene") + "; keeping the old bed.");
                return;
            }
            table = Instantiate(prefab);
            table.name = "SurgeryTableV2";
            SceneManager.MoveGameObjectToScene(table, gameObject.scene);
            AlignToBed(table, floor, headDir);

            foreach (var name in OldBed)
                foreach (var t in all.Where(t => t.name == name))
                    foreach (var renderer in t.GetComponents<Renderer>())
                    {
                        if (renderer.forceRenderingOff) continue;
                        renderer.forceRenderingOff = true;
                        hidden.Add(renderer);
                        foreach (var collider in renderer.GetComponents<Collider>())
                            if (collider.enabled && !collider.isTrigger) { collider.enabled = false; disabled.Add(collider); }
                    }

            back = Find("BST_Pad_Back"); head = Find("BST_Pad_Head"); tray = Find("BST_Tray");
            backRest = back.rotation; headRest = head.rotation; trayRest = tray.rotation;
            // Swing the tray away from the table, whichever way it is turned.
            var trayCentre = tray.GetComponent<Renderer>().bounds.center;
            var swung = Quaternion.AngleAxis(90, Vector3.up) * (trayCentre - tray.position);
            var outward = Vector3.ProjectOnPlane(tray.position - floor, headDir);
            traySign = Vector3.Dot(swung, outward) >= 0 ? 1 : -1;
            foreach (var control in Controls)
            {
                foreach (var filter in control.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.GetComponent<Collider>() == null && filter.sharedMesh != null)
                        filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                if (control.Kind == SurgeryControlKind.Strap)
                {
                    var closed = control.transform.Cast<Transform>().First(c => c.name.EndsWith("_Closed")).gameObject;
                    var open = control.transform.Cast<Transform>().First(c => c.name.EndsWith("_Open")).gameObject;
                    open.SetActive(false);
                    straps[control] = (closed, open);
                }
            }
            StaticRenderers = V2StaticBatch.Combine(table, t => t == back || t == tray);
            BuildPrompt();
        }

        Transform Find(string name) => table.GetComponentsInChildren<Transform>(true).First(t => t.name == name);

        /// <summary>
        /// The old bed's floor centre and its head direction: the long axis of Bed_Base, pointed at the end where the
        /// gantry's surgical lamp boom hangs. Shared by the table and the gantry so both land in the same frame.
        /// </summary>
        public static bool TryBedFrame(Scene scene, out Vector3 floor, out Vector3 headDir)
        {
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
            var bedBase = all.FirstOrDefault(t => t.name == "Bed_Base");
            floor = Vector3.zero; headDir = Vector3.forward;
            if (bedBase == null) return false;
            var bed = bedBase.GetComponent<Renderer>();
            var size = bed != null ? bed.bounds.size : Vector3.forward;
            var axis = size.z >= size.x ? Vector3.forward : Vector3.right;
            var lamp = all.FirstOrDefault(t => t.name == "Gantry_Boom_SurgicalLamp");
            var lampPoint = lamp != null ? (lamp.GetComponent<Renderer>() != null ? lamp.GetComponent<Renderer>().bounds.center : lamp.position) : bedBase.position + Vector3.forward;
            headDir = Vector3.Dot(lampPoint - bedBase.position, axis) >= 0 ? axis : -axis;
            floor = bed != null ? new Vector3(bed.bounds.center.x, bed.bounds.min.y, bed.bounds.center.z) : bedBase.position;
            return true;
        }

        /// <summary>Lands an instance authored with Ref_Origin / Ref_Head / Ref_Up on the bed frame (whole frame: the
        /// imported root carries the FBX axis conversion).</summary>
        public static void AlignToBed(GameObject instance, Vector3 floor, Vector3 headDir)
        {
            // Blender suffixes repeated names in one file (Ref_Origin.001 ...), so accept that form too.
            Transform Ref(string name) => instance.GetComponentsInChildren<Transform>(true).First(t => t.name == name || t.name.StartsWith(name + ".", StringComparison.Ordinal));
            var origin = Ref("Ref_Origin");
            var authored = Quaternion.LookRotation(Ref("Ref_Head").position - origin.position, Ref("Ref_Up").position - origin.position);
            instance.transform.rotation = Quaternion.LookRotation(headDir, Vector3.up) * Quaternion.Inverse(authored) * instance.transform.rotation;
            instance.transform.position += floor - origin.position;
        }

        public void Activate(SurgeryTableControl control)
        {
            switch (control.Kind)
            {
                case SurgeryControlKind.Pendant: ChairPose = !ChairPose; break;
                case SurgeryControlKind.Tray: TrayOut = !TrayOut; break;
                case SurgeryControlKind.Arms:
                case SurgeryControlKind.Lamp:
                    if (SurgeryGantryV2Runtime.Current != null) SurgeryGantryV2Runtime.Current.Activate(control.Kind);
                    break;
                default:
                    if (straps.TryGetValue(control, out var s))
                    {
                        bool close = !s.closed.activeSelf;
                        s.closed.SetActive(close); s.open.SetActive(!close);
                    }
                    break;
            }
        }

        void Update()
        {
            if (table == null) return;
            float t = 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime);
            pose = Mathf.Lerp(pose, ChairPose ? 1 : 0, t);
            trayAmount = Mathf.Lerp(trayAmount, TrayOut ? 1 : 0, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
            back.rotation = Tilt(BackChair * pose) * backRest;
            head.rotation = Tilt((BackChair + HeadRelative) * pose) * headRest;
            tray.rotation = Quaternion.AngleAxis(90 * traySign * trayAmount, Vector3.up) * trayRest;
            HandleRoomClick();
        }

        // Tilting the head end up by `degrees`, about the table's cross axis.
        Quaternion Tilt(float degrees) => Quaternion.FromToRotation(headDir, Vector3.RotateTowards(headDir, Vector3.up, degrees * Mathf.Deg2Rad, 0));

        void HandleRoomClick()
        {
            Aimed = null;
            if (clinic == null) clinic = FindFirstObjectByType<UnifiedClinicDirector>();
            var rig = clinic != null && clinic.Initialized && clinic.Robot != null ? clinic.Robot.Flow.Rig : null;
            var walker = rig != null ? rig.Walker : null;
            bool walking = rig != null && rig.Walking && walker != null && walker.Aiming && !clinic.ConsoleOpen;
            if (walking)
            {
                var camera = rig.Cam;
                var ray = camera.ScreenPointToRay(new Vector2(Screen.width * .5f, Screen.height * .5f));
                foreach (var hit in Physics.RaycastAll(ray, walker.Reach, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
                {
                    if (hit.collider is CharacterController || hit.collider.name.StartsWith("COL_", StringComparison.Ordinal)) continue;
                    Aimed = hit.collider.GetComponentInParent<SurgeryTableControl>();
                    break;
                }
                if (Aimed != null && RepairInput.LeftPressed && !walker.ClickConsumedThisFrame) Activate(Aimed);
            }
            if (promptCanvas != null)
            {
                promptCanvas.SetActive(Aimed != null);
                if (Aimed != null)
                {
                    promptText.text = Aimed.Hint;
                    prompt.sizeDelta = new Vector2(promptText.preferredWidth + 24, 30);
                }
            }
        }

        void BuildPrompt()
        {
            promptCanvas = new GameObject("SurgeryTablePrompt", typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(promptCanvas, gameObject.scene);
            var canvas = promptCanvas.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 48;
            var scaler = promptCanvas.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f;
            prompt = (RectTransform)new GameObject("Tag", typeof(RectTransform)).transform;
            prompt.SetParent(promptCanvas.transform, false);
            prompt.anchorMin = prompt.anchorMax = new Vector2(.5f, .5f); prompt.pivot = new Vector2(.5f, 1); prompt.anchoredPosition = new Vector2(0, -26);
            var bg = prompt.gameObject.AddComponent<Image>(); bg.raycastTarget = false;
            RepairPropCyberStyle.Framed(bg, RepairPropCyberStyle.Field, RepairPropCyberStyle.Hot, true);
            var label = (RectTransform)new GameObject("Text", typeof(RectTransform)).transform;
            label.SetParent(prompt, false);
            label.anchorMin = Vector2.zero; label.anchorMax = Vector2.one; label.offsetMin = new Vector2(10, 2); label.offsetMax = new Vector2(-10, -2);
            promptText = label.gameObject.AddComponent<Text>();
            promptText.font = RepairPropCyberStyle.UiFontOr(UIFontProvider.GetCjkFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));
            promptText.fontSize = 14; promptText.color = RepairPropCyberStyle.Hot; promptText.alignment = TextAnchor.MiddleCenter; promptText.raycastTarget = false;
            promptText.horizontalOverflow = HorizontalWrapMode.Overflow;
            promptCanvas.SetActive(false);
        }

        void OnDestroy()
        {
            foreach (var renderer in hidden) if (renderer != null) renderer.forceRenderingOff = false;
            foreach (var collider in disabled) if (collider != null) collider.enabled = true;
            if (table != null) Destroy(table);
            if (promptCanvas != null) Destroy(promptCanvas);
            if (Current == this) Current = null;
        }
    }
}
