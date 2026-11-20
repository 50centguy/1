using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BorderRepair.RepairProps
{
    // The visor is a curved CRT glass (SIGNALIS / amber system monitor references): the room image and every screen-space
    // HUD element go through the same barrel. The scene half lives in the PS2 pass (global _VisorBarrel); the HUD half
    // warps vertices (subdividing big quads so edges bend). Hit tests stay on the logical rects: at this strength the
    // drawn offset is under 10 px even at the screen edge, less than half of any button. AR marks are projected from the undistorted camera and then warped like the
    // room, so they stay on their objects. Desktop clinic only, play time only.
    public static class HudLens
    {
        /// <summary>Barrel strength: a point at normalized screen position p (each axis -1..1) is drawn at p (1 - k |p|^2).</summary>
        public static float K = .009f;   // a hint of glass at the corners; stronger and the panels read as crooked, the type as soft
        static readonly int Barrel = Shader.PropertyToID("_VisorBarrel");
        public static bool On { get; private set; }

        public static void Enable(bool on)
        {
            On = on;
            Shader.SetGlobalFloat(Barrel, on ? K : 0f);
        }

        public static Vector2 Warp(Vector2 n) => On ? n * (1f - K * n.sqrMagnitude) : n;

        /// <summary>Inverse of Warp (fixed-point; converges in a few steps for small K).</summary>
        public static Vector2 Unwarp(Vector2 q)
        {
            if (!On) return q;
            var p = q;
            for (int i = 0; i < 4; i++) p = q / Mathf.Max(.5f, 1f - K * p.sqrMagnitude);
            return p;
        }

        public static Vector2 UnwarpScreen(Vector2 pixels)
        {
            var size = new Vector2(Screen.width, Screen.height);
            var n = (pixels / size) * 2f - Vector2.one;
            return (Unwarp(n) + Vector2.one) * .5f * size;
        }
    }

    /// <summary>Marks a canvas that is not seen through the visor glass (a program on a physical screen).</summary>
    [AddComponentMenu("")]
    public sealed class HudLensExempt : MonoBehaviour { }

    /// <summary>Bends a Graphic through the visor lens; re-bends whenever it moves on screen.</summary>
    [AddComponentMenu("")]
    public sealed class HudLensWarp : BaseMeshEffect
    {
        const float MaxEdge = .06f;   // subdivide triangles longer than this in normalized screen units
        readonly List<UIVertex> stream = new List<UIVertex>();
        readonly List<UIVertex> output = new List<UIVertex>();
        RectTransform root;
        Matrix4x4 last;
        Vector2 lastSize;

        static readonly List<HudLensWarp> live = new List<HudLensWarp>();
        static bool hooked;
        int slot = -1;

        internal static void ResetAll()
        {
            live.Clear();
            if (hooked) { Canvas.preWillRenderCanvases -= CheckAll; hooked = false; }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (slot < 0) { slot = live.Count; live.Add(this); }
            if (!hooked) { hooked = true; Canvas.preWillRenderCanvases += CheckAll; }
        }

        protected override void OnDisable()
        {
            if (slot >= 0 && slot < live.Count && live[slot] == this)
            {
                var moved = live[live.Count - 1];
                live[slot] = moved; moved.slot = slot;
                live.RemoveAt(live.Count - 1);
            }
            slot = -1;
            base.OnDisable();
        }

        static readonly Dictionary<RectTransform, Vector2> rootSizes = new Dictionary<RectTransform, Vector2>();

        static void CheckAll()
        {
            if (!HudLens.On) return;
            rootSizes.Clear();
            for (int i = 0; i < live.Count; i++) live[i].Check();
            // (hasChanged is cleared after everyone looked: a moved parent marks all its children)
            for (int i = 0; i < live.Count; i++) live[i].cached.hasChanged = false;
        }

        Transform cached;

        void Check()
        {
            if (cached == null) cached = transform;
            var g = graphic;
            if (g == null || !g.enabled) return;   // (re-enabling rebuilds it anyway)
            if (root == null) { var c = g.canvas; root = c != null ? c.rootCanvas.transform as RectTransform : null; if (root == null) return; }
            if (!rootSizes.TryGetValue(root, out var size)) rootSizes[root] = size = root.rect.size;
            if (!cached.hasChanged && size == lastSize) return;
            var m = root.worldToLocalMatrix * cached.localToWorldMatrix;
            if (m != last || size != lastSize) { last = m; lastSize = size; g.SetVerticesDirty(); }
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || !HudLens.On) return;
            var canvas = graphic.canvas;
            if (canvas == null || canvas.rootCanvas.renderMode == RenderMode.WorldSpace || canvas.rootCanvas.TryGetComponent<HudLensExempt>(out _)) return;
            root = canvas.rootCanvas.transform as RectTransform;
            var toRoot = root.worldToLocalMatrix * transform.localToWorldMatrix;
            var toLocal = toRoot.inverse;
            var half = root.rect.size * .5f;
            if (half.x < 1 || half.y < 1) return;
            stream.Clear(); output.Clear();
            vh.GetUIVertexStream(stream);
            for (int i = 0; i + 2 < stream.Count; i += 3) Split(stream[i], stream[i + 1], stream[i + 2], toRoot, half, 0, 0);
            for (int i = 0; i < output.Count; i++)
            {
                var v = output[i];
                Vector2 r = toRoot.MultiplyPoint3x4(v.position);
                var n = new Vector2(r.x / half.x, r.y / half.y);
                var w = HudLens.Warp(n);
                v.position = toLocal.MultiplyPoint3x4(new Vector3(w.x * half.x, w.y * half.y, 0));
                output[i] = v;
            }
            vh.Clear();
            vh.AddUIVertexTriangleStream(output);
        }

        // Four ways while the triangle is roughly even; a thin one (a line, a rule) is halved across its longest edge, so
        // it bends along its length without being diced across its width.
        void Split(UIVertex a, UIVertex b, UIVertex c, Matrix4x4 toRoot, Vector2 half, int depth, int cuts)
        {
            Vector2 pa = Norm(a, toRoot, half), pb = Norm(b, toRoot, half), pc = Norm(c, toRoot, half);
            float lab = (pa - pb).magnitude, lbc = (pb - pc).magnitude, lca = (pc - pa).magnitude;
            float longest = Mathf.Max(lab, Mathf.Max(lbc, lca));
            // thin: its height over the longest edge is under a quarter of that edge (a sliver, whatever its sides)
            float twiceArea = Mathf.Abs((pb.x - pa.x) * (pc.y - pa.y) - (pc.x - pa.x) * (pb.y - pa.y));
            bool thin = twiceArea < longest * longest * .25f;
            if (longest <= MaxEdge || (thin ? cuts >= 10 : depth >= 4)) { output.Add(a); output.Add(b); output.Add(c); return; }
            if (thin)
            {
                if (lab >= lbc && lab >= lca) { var m = Mid(a, b); Split(a, m, c, toRoot, half, depth, cuts + 1); Split(m, b, c, toRoot, half, depth, cuts + 1); }
                else if (lbc >= lca) { var m = Mid(b, c); Split(a, b, m, toRoot, half, depth, cuts + 1); Split(a, m, c, toRoot, half, depth, cuts + 1); }
                else { var m = Mid(c, a); Split(a, b, m, toRoot, half, depth, cuts + 1); Split(m, b, c, toRoot, half, depth, cuts + 1); }
                return;
            }
            var ab = Mid(a, b); var bc = Mid(b, c); var ca = Mid(c, a);
            Split(a, ab, ca, toRoot, half, depth + 1, cuts);
            Split(ab, b, bc, toRoot, half, depth + 1, cuts);
            Split(ca, bc, c, toRoot, half, depth + 1, cuts);
            Split(ab, bc, ca, toRoot, half, depth + 1, cuts);
        }

        static Vector2 Norm(UIVertex v, Matrix4x4 toRoot, Vector2 half)
        {
            Vector2 r = toRoot.MultiplyPoint3x4(v.position);
            return new Vector2(r.x / half.x, r.y / half.y);
        }

        static UIVertex Mid(UIVertex a, UIVertex b)
        {
            var v = a;
            v.position = (a.position + b.position) * .5f;
            v.color = Color32.Lerp(a.color, b.color, .5f);
            v.uv0 = (a.uv0 + b.uv0) * .5f;
            v.uv1 = (a.uv1 + b.uv1) * .5f;
            v.normal = (a.normal + b.normal) * .5f;
            v.tangent = (a.tangent + b.tangent) * .5f;
            return v;
        }
    }

    // Installs the lens on the desktop clinic: global barrel for the PS2 pass, warp effects on every
    // screen-space canvas (scanned twice a second, as panels come and go), and the visor glass overlay on top.
    [AddComponentMenu("")]
    public sealed class HudLensRuntime : MonoBehaviour
    {
        public const string ScenePath = RepairBenchV2Runtime.ScenePath;
        public static HudLensRuntime Current { get; private set; }
        readonly HashSet<int> done = new HashSet<int>();
        readonly List<Canvas> canvases = new List<Canvas>();
        readonly List<Graphic> graphics = new List<Graphic>();
        float nextScan;
        Canvas glass;

        public int WarpedGraphics => done.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHooks() { SceneManager.sceneLoaded -= OnSceneLoaded; Current = null; HudLens.Enable(false); HudLensWarp.ResetAll(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() => SceneManager.sceneLoaded += OnSceneLoaded;

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!RepairBenchV2Runtime.IsClinicScene(scene.path)) return;
            if (UnityEngine.XR.XRSettings.isDeviceActive) return;
            var host = new GameObject("VisorLens (runtime only)");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<HudLensRuntime>();
        }

        void Awake()
        {
            Current = this;
            HudLens.Enable(true);
            BuildGlass();
        }

        void BuildGlass()
        {
            var host = new GameObject("VisorGlass", typeof(RectTransform));
            host.transform.SetParent(transform, false);
            glass = host.AddComponent<Canvas>(); glass.renderMode = RenderMode.ScreenSpaceOverlay; glass.sortingOrder = 400;
            var scaler = host.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f;
            // Rounded tube mask and vignette (one stretched texture), then faint scanlines over everything.
            var mask = new GameObject("TubeMask", typeof(RectTransform)).AddComponent<RawImage>();
            mask.transform.SetParent(host.transform, false);
            // Oversized, then warped like everything else: the tube outline bends with the image and the HUD.
            Stretch(mask.rectTransform); mask.rectTransform.offsetMin = new Vector2(-40, -24); mask.rectTransform.offsetMax = new Vector2(40, 24);
            mask.texture = TubeTexture(); mask.raycastTarget = false;
            var scan = new GameObject("Scanlines", typeof(RectTransform)).AddComponent<Image>();
            scan.transform.SetParent(host.transform, false);
            Stretch(scan.rectTransform);
            scan.sprite = RepairPropCyberStyle.Scan; scan.type = Image.Type.Tiled; scan.color = new Color(0, 0, 0, .10f); scan.raycastTarget = false;
        }

        static Texture2D TubeTexture()
        {
            const int w = 256, h = 144;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + .5f) / w * 2 - 1, v = (y + .5f) / h * 2 - 1;
                    // Rounded-rectangle distance (superellipse) for the tube corners, plus a soft vignette.
                    float d = Mathf.Pow(Mathf.Pow(Mathf.Abs(u), 9) + Mathf.Pow(Mathf.Abs(v), 9), 1f / 9);
                    float edge = Mathf.SmoothStep(0, 1, (d - .975f) / .025f);
                    float vignette = Mathf.Clamp01((u * u + v * v - .5f) * .22f);
                    float a = Mathf.Clamp01(Mathf.Max(edge, vignette));
                    px[y * w + x] = new Color32(0, 0, 0, (byte)(a * 255));
                }
            t.SetPixels32(px); t.Apply(false, true);
            return t;
        }

        static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

        void Update()
        {
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + .5f;
            canvases.Clear();
            canvases.AddRange(FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            foreach (var canvas in canvases)
            {
                if (canvas.rootCanvas.renderMode == RenderMode.WorldSpace) continue;
                if (!canvas.isRootCanvas) continue;
                graphics.Clear();
                canvas.GetComponentsInChildren(true, graphics);
                foreach (var g in graphics)
                    if (done.Add(g.GetInstanceID()) && g.GetComponent<HudLensWarp>() == null) g.gameObject.AddComponent<HudLensWarp>();
            }
        }

        void OnDestroy()
        {
            HudLens.Enable(false);
            if (Current == this) Current = null;
        }
    }
}
