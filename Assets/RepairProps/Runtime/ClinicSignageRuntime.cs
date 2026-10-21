using System.Collections.Generic;
using System.Linq;
using BorderRepair.TwoNight;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BorderRepair.RepairProps
{
    // Room lettering as projected light (Japanese + English) instead of painted / neon Chinese characters, placed where
    // nothing stands in front of it. Cheap ceiling projectors (a dark box, a lit lens, a faint additive beam) throw
    // the text onto real surfaces: the dark sign plates over the south door and on the SE wall (their neon letters are
    // switched off), a hanging scrim in front of the counter shelving (the tariff board it replaces sat behind the
    // racking), the counter's front skirt under each pad, and the bench wall above the shelf. The director's floating
    // zone captions (RECEIVE / SEND OUT / WORKBENCH TextMeshes) keep existing and keep their show / hide logic; only
    // their renderers are off, and each projection mirrors its caption's visibility. Play-time only, restored on destroy.
    [AddComponentMenu("")]
    public sealed class ClinicSignageRuntime : MonoBehaviour
    {
        public const string ScenePath = RepairBenchV2Runtime.ScenePath;
        static readonly Color Magenta = new Color(1f, .30f, .66f), Amber = new Color(1f, .64f, .22f), Pale = new Color(1f, .88f, .70f);

        public static ClinicSignageRuntime Current { get; private set; }

        sealed class Projection { public string name; public CanvasGroup group; public RectTransform glitch; public float seed; public GameObject mirror; public GameObject root; }
        readonly List<Projection> signs = new List<Projection>();
        readonly List<Renderer> hidden = new List<Renderer>();
        readonly List<Object> created = new List<Object>();
        Material beamAmber, beamMagenta, housing, lens;
        Font kana, mono;
        bool built;
        float started;

        public IReadOnlyList<string> SignNames => signs.Select(s => s.name).ToList();
        public int HiddenCount => hidden.Count;
        public GameObject SignRoot(string name) => signs.FirstOrDefault(s => s.name == name)?.root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHooks() { SceneManager.sceneLoaded -= OnSceneLoaded; Current = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() => SceneManager.sceneLoaded += OnSceneLoaded;

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!string.Equals(scene.path, ScenePath, System.StringComparison.OrdinalIgnoreCase)) return;
            var host = new GameObject("ClinicSignage (runtime only)");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<ClinicSignageRuntime>();
        }

        void Awake() { Current = this; started = Time.unscaledTime; }

        // The kits it projects onto are placed by their own runtimes on scene load; build once they are there.
        void Update()
        {
            if (!built)
            {
                bool ready = ClinicCyberRuntime.Current != null && ClinicCyberRuntime.Current.RoomKit != null && ClinicZonesV2Runtime.Current != null && ClinicZonesV2Runtime.Current.Zones != null;
                if (!ready && Time.unscaledTime - started < 3f) return;
                Build();
                built = true;
            }
            float t = Time.unscaledTime;
            foreach (var sign in signs)
            {
                if (sign.mirror != null && sign.root.activeSelf != sign.mirror.activeInHierarchy) sign.root.SetActive(sign.mirror.activeInHierarchy);
                if (!sign.root.activeSelf) continue;
                // Projector flicker: a soft shimmer and, every few seconds, a two-frame sideways tear.
                float n = HudMotion.Noise(Mathf.Floor(t * 24f) + sign.seed);
                sign.group.alpha = .82f + .18f * n;
                bool tear = Mathf.Repeat(t * .17f + sign.seed, 1f) < .012f;
                sign.glitch.anchoredPosition = new Vector2(tear ? (n - .5f) * 18f : 0, 0);
            }
        }

        void Build()
        {
            kana = RepairPropCyberStyle.SignKana != null ? RepairPropCyberStyle.SignKana : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mono = RepairPropCyberStyle.Mono != null ? RepairPropCyberStyle.Mono : kana;
            var shader = Resources.Load<Shader>("BorderRepairCyber/CyberBeam");
            if (shader != null)
            {
                beamAmber = Keep(new Material(shader) { name = "PJ_BeamAmber" }); beamAmber.SetColor("_Color", new Color(1f, .62f, .24f, .035f));
                beamMagenta = Keep(new Material(shader) { name = "PJ_BeamMagenta" }); beamMagenta.SetColor("_Color", new Color(1f, .30f, .66f, .04f));
            }
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            housing = Keep(new Material(lit) { name = "PJ_Housing" }); housing.SetColor("_BaseColor", new Color(.10f, .10f, .11f)); housing.SetFloat("_Smoothness", .3f);
            lens = Keep(new Material(lit) { name = "PJ_Lens" }); lens.SetColor("_BaseColor", Color.black);
            lens.EnableKeyword("_EMISSION"); lens.SetColor("_EmissionColor", new Color(2.2f, 1.4f, .7f));

            var cyber = ClinicCyberRuntime.Current;
            var room = cyber != null ? cyber.RoomKit : null;
            if (room != null)
            {
                foreach (var r in room.GetComponentsInChildren<Renderer>(true))
                    if (r.name.StartsWith("CY_Sign_Main_") || r.name == "CY_Sign_24H" || r.name.StartsWith("CY_Sign_Receive_")) Hide(r);
            }
            // Over the south door, on the old neon's dark plate (face +Z, into the room).
            var door = Sign("PJ_Door", new Vector3(0, 2.58f, -3.368f), Vector3.forward, 1.56f, .42f, Magenta, beamMagenta, new Vector3(0, 2.96f, -2.55f));
            Line(door, "義体修理", kana, 170, new Color(1f, .80f, .92f), 0, -6, 1.0f, TextAnchor.UpperCenter);
            Line(door, "CYBERWARE  REPAIR  //  修理承ります", mono, 52, Pale, 0, -262, 1.0f, TextAnchor.UpperCenter);
            var hours = Sign("PJ_Hours", new Vector3(-1.06f, 2.55f, -3.368f), Vector3.forward, .32f, .22f, Amber, beamAmber, new Vector3(-1.06f, 2.96f, -2.9f));
            Line(hours, "24H", mono, 110, Amber, 0, -6, 1.0f, TextAnchor.UpperCenter);
            Line(hours, "営業中 OPEN", kana, 40, Pale, 0, -150, 1.0f, TextAnchor.UpperCenter);
            // SE wall plate, vertical Japanese.
            var se = new Vector3(2.006f, 1.92f, -2.802f);
            var seNormal = new Vector3(-1, 0, 1).normalized;
            var receive = Sign("PJ_Receive", se + seNormal * .035f, seNormal, .26f, .98f, Amber, beamAmber, se + seNormal * .9f + Vector3.up * 1.0f);
            Line(receive, "受\n付\n窓\n口", kana, 150, Amber, 0, -20, 1.0f, TextAnchor.UpperCenter, 1.02f);
            Line(receive, "RCV", mono, 44, Pale, 0, -880, 1.0f, TextAnchor.UpperCenter);

            // Price list on a hanging scrim in front of the counter shelving (the old board sat behind the racking).
            var zones = ClinicZonesV2Runtime.Current != null ? ClinicZonesV2Runtime.Current.Zones : null;
            if (zones != null)
                foreach (var r in zones.GetComponentsInChildren<Renderer>(true)) if (r.name == "ZC_TariffBoard") Hide(r);
            var scrimAt = new Vector3(2.27f, 2.06f, 0f);
            Scrim(scrimAt, 1.50f, .56f);
            var prices = Sign("PJ_PriceList", scrimAt + Vector3.left * .012f, Vector3.left, 1.44f, .52f, Amber, beamAmber, new Vector3(1.55f, 2.95f, 0));
            Line(prices, "料金表 // PRICE LIST", kana, 64, Amber, 30, -14, .6f, TextAnchor.UpperLeft);
            Line(prices, "NSP-03", mono, 40, Pale, -30, -24, .3f, TextAnchor.UpperRight);
            var rows = new[] { ("清掃  CLEAN", "80"), ("特急  RUSH", "+50%"), ("部品  PARTS", "実費+工賃"), ("深夜  NIGHT", "+20") };
            for (int i = 0; i < rows.Length; i++)
            {
                Line(prices, rows[i].Item1, kana, 54, Pale, 40, -110 - i * 72, .55f, TextAnchor.UpperLeft);
                Line(prices, rows[i].Item2, kana, 54, Amber, -40, -110 - i * 72, .4f, TextAnchor.UpperRight);
            }
            Line(prices, "現金のみ  CASH ONLY   掛売お断り  NO CREDIT", kana, 30, new Color(1f, .64f, .22f, .7f), 0, -420, 1f, TextAnchor.UpperCenter);

            // The director's zone captions (one TextMesh per trade zone): renderer off, projected at their zones instead.
            foreach (var zone in FindObjectsByType<ClinicTradeZone>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (zone.gameObject.scene != gameObject.scene) continue;
                var caption = zone.GetComponentInChildren<TextMesh>(true);
                if (caption == null) continue;
                var renderer = caption.GetComponent<Renderer>();
                if (renderer != null) Hide(renderer);
                var p = caption.transform.position;
                if (zone.Action == ClinicTradeAction.Console)
                {
                    var bench = Sign("PJ_Workbench", new Vector3(0, 2.50f, 3.37f), Vector3.back, 1.10f, .26f, Amber, beamAmber, new Vector3(0, 2.96f, 2.7f));
                    Line(bench, "作業台 // WORKBENCH", kana, 96, Amber, 0, -20, 1f, TextAnchor.UpperCenter);
                    Line(bench, "修理受付中 · OPEN FOR REPAIRS", mono, 36, Pale, 0, -150, 1f, TextAnchor.UpperCenter);
                    Mirror("PJ_Workbench", caption.gameObject);
                }
                else if (zone.Action == ClinicTradeAction.Receive || zone.Action == ClinicTradeAction.Deliver)
                {
                    bool deliver = zone.Action == ClinicTradeAction.Deliver;
                    // Counter front skirt (x 1.62, facing the room) under the pad's side of the counter.
                    float z = Mathf.Clamp(p.z, -.9f, .9f);
                    var pad = Sign(deliver ? "PJ_Pad_Deliver" : "PJ_Pad_Receive", new Vector3(1.598f, .80f, z), Vector3.left, .40f, .12f, Amber, null, new Vector3(1.50f, .93f, z));
                    Line(pad, deliver ? "引渡 ▲ SEND OUT" : "受取 ▲ RECEIVE", kana, 64, deliver ? Pale : Amber, 0, -18, 1f, TextAnchor.UpperCenter);
                    Mirror(pad.name, caption.gameObject);
                }
            }
        }

        T Keep<T>(T o) where T : Object { created.Add(o); return o; }

        void Hide(Renderer renderer)
        {
            if (renderer == null || renderer.forceRenderingOff) return;
            renderer.forceRenderingOff = true;
            hidden.Add(renderer);
        }

        void Mirror(string name, GameObject caption)
        {
            var sign = signs.FirstOrDefault(s => s.name == name);
            if (sign != null) sign.mirror = caption;
        }

        // A projection: world canvas on the surface (1 px = 1 mm), glow backing, scanlines, a projector with its beam.
        RectTransform Sign(string name, Vector3 centre, Vector3 normal, float width, float height, Color color, Material beam, Vector3 projector)
        {
            var root = new GameObject(name);
            root.transform.SetParent(transform, false);
            var canvasObject = new GameObject("Canvas", typeof(RectTransform));
            canvasObject.transform.SetParent(root.transform, false);
            var canvas = canvasObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            canvasObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3;
            var rect = (RectTransform)canvasObject.transform;
            rect.sizeDelta = new Vector2(width * 1000, height * 1000);
            rect.position = centre + normal * .004f;
            // Readable from the room: the canvas's front (-forward) faces along the surface normal.
            rect.rotation = Quaternion.LookRotation(-normal, Vector3.up);
            rect.localScale = Vector3.one * .001f;
            var group = canvasObject.AddComponent<CanvasGroup>(); group.interactable = false; group.blocksRaycasts = false;
            var glow = new GameObject("Glow", typeof(RectTransform)).AddComponent<Image>();
            glow.transform.SetParent(rect, false); Stretch(glow.rectTransform, -20);
            glow.sprite = RepairPropCyberStyle.Fill; glow.color = new Color(color.r, color.g, color.b, .07f); glow.raycastTarget = false;
            var tear = (RectTransform)new GameObject("Content", typeof(RectTransform)).transform;
            tear.SetParent(rect, false); Stretch(tear, 0);
            var scan = new GameObject("Scanlines", typeof(RectTransform)).AddComponent<Image>();
            scan.transform.SetParent(rect, false); Stretch(scan.rectTransform, -20);
            scan.sprite = RepairPropCyberStyle.Scan; scan.type = Image.Type.Tiled; scan.pixelsPerUnitMultiplier = .25f;
            scan.color = new Color(0, 0, 0, .45f); scan.raycastTarget = false;
            // Projector on the ceiling: dark box, lit lens, and the beam to the surface's corners.
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(box.GetComponent<Collider>());
            box.name = "Projector"; box.transform.SetParent(root.transform, false);
            box.transform.position = projector; box.transform.localScale = new Vector3(.09f, .06f, .12f);
            box.transform.rotation = Quaternion.LookRotation(centre - projector, Vector3.up);
            box.GetComponent<Renderer>().sharedMaterial = housing;
            var lensObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(lensObject.GetComponent<Collider>());
            lensObject.name = "Lens"; lensObject.transform.SetParent(box.transform, false);
            lensObject.transform.localPosition = new Vector3(0, 0, .51f); lensObject.transform.localRotation = Quaternion.Euler(0, 180, 0);
            lensObject.transform.localScale = new Vector3(.5f, .5f, 1);
            lensObject.GetComponent<Renderer>().sharedMaterial = lens;
            if (beam != null) Beam(root.transform, projector + (centre - projector).normalized * .07f, rect, beam);
            signs.Add(new Projection { name = name, group = group, glitch = tear, seed = signs.Count * 3.7f, root = root });
            return tear;
        }

        void Beam(Transform parent, Vector3 from, RectTransform target, Material material)
        {
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            var mesh = Keep(new Mesh { name = "PJ_Beam" });
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                int b = verts.Count;
                verts.Add(from); verts.Add(corners[i]); verts.Add(corners[(i + 1) % 4]);
                uvs.Add(new Vector2(.5f, 0)); uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1));
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
            }
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0); mesh.RecalculateBounds();
            var go = new GameObject("Beam");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        }

        // Thin acrylic scrim on two wires, the price list's projection surface.
        void Scrim(Vector3 centre, float width, float height)
        {
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(panel.GetComponent<Collider>());
            panel.name = "PJ_Scrim"; panel.transform.SetParent(transform, false);
            panel.transform.position = centre; panel.transform.localScale = new Vector3(.008f, height, width);
            var mat = Keep(new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "PJ_ScrimMat" });
            mat.SetColor("_BaseColor", new Color(.06f, .05f, .05f)); mat.SetFloat("_Smoothness", .7f);
            panel.GetComponent<Renderer>().sharedMaterial = mat;
            foreach (float z in new[] { -width * .42f, width * .42f })
            {
                var wire = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(wire.GetComponent<Collider>());
                wire.name = "PJ_ScrimWire"; wire.transform.SetParent(panel.transform.parent, false);
                float top = 2.98f, bottom = centre.y + height / 2;
                wire.transform.position = new Vector3(centre.x, (top + bottom) / 2, centre.z + z);
                wire.transform.localScale = new Vector3(.004f, top - bottom, .004f);
                wire.GetComponent<Renderer>().sharedMaterial = housing;
            }
        }

        void Line(RectTransform content, string value, Font font, int size, Color color, float x, float y, float widthFraction, TextAnchor anchor, float lineSpacing = 1f)
        {
            var text = new GameObject("Text", typeof(RectTransform)).AddComponent<Text>();
            text.transform.SetParent(content, false);
            var r = text.rectTransform;
            float w = content.rect.width * widthFraction;
            bool right = anchor == TextAnchor.UpperRight, left = anchor == TextAnchor.UpperLeft;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(right ? 1 : left ? 0 : .5f, 1);
            r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, content.rect.height);
            text.font = font; text.fontSize = size; text.color = color; text.text = value; text.alignment = anchor; text.lineSpacing = lineSpacing;
            text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Overflow; text.raycastTarget = false;
            var shadow = text.gameObject.AddComponent<Outline>(); shadow.effectColor = new Color(color.r, color.g, color.b, .25f); shadow.effectDistance = new Vector2(3, -3);
        }

        static void Stretch(RectTransform r, float pad)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = new Vector2(pad, pad); r.offsetMax = new Vector2(-pad, -pad);
        }

        void OnDestroy()
        {
            foreach (var r in hidden) if (r != null) r.forceRenderingOff = false;
            foreach (var o in created) if (o != null) Destroy(o);
            if (Current == this) Current = null;
        }
    }
}
